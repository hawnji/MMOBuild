using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Embervale.Shared.Data;
using Embervale.Shared.Persistence;
using Embervale.Shared.Simulation;

namespace Embervale.Shared.Net
{
    /// <summary>Transport-independent connection sink (LiteNetLib on the server, loopback for local play/tests).</summary>
    public interface IServerTransport
    {
        void Send(int connectionId, byte[] data, int length, bool reliable);
        void Disconnect(int connectionId, string reason);
    }

    public static class PasswordHasher
    {
        public const int Iterations = 120_000;

        public static string NewSalt()
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            return Convert.ToBase64String(salt);
        }

        public static string Hash(string password, string salt)
        {
            using (var kdf = new Rfc2898DeriveBytes(password, Convert.FromBase64String(salt), Iterations, HashAlgorithmName.SHA256))
                return Convert.ToBase64String(kdf.GetBytes(32));
        }

        public static bool Verify(string password, string salt, string expectedHash)
        {
            var actual = Convert.FromBase64String(Hash(password, salt));
            var expected = Convert.FromBase64String(expectedHash);
            if (actual.Length != expected.Length) return false;
            int diff = 0;
            for (int i = 0; i < actual.Length; i++) diff |= actual[i] ^ expected[i];
            return diff == 0;
        }
    }

    public enum SessionState { Connected, Authenticated, InWorld, Closed }

    public sealed class ClientSession
    {
        public int ConnectionId;
        public SessionState State;
        public long AccountId;
        public string Username;
        public Player Player;
        public bool HelloReceived;
        public int LastPacketTick;
        public int PacketsThisTick;
        public int BadPackets;
        public readonly Dictionary<int, KnownEntity> Known = new Dictionary<int, KnownEntity>();
        public int LastShopVersion = -1;
        public int LastTradeVersion = -1;
        public int LastStatsTick;
    }

    public struct KnownEntity
    {
        public int StateVersion;
        public float X, Z;
    }

    /// <summary>
    /// The authoritative server: owns the GameWorld, authenticates clients, decodes their intents,
    /// runs interest management and sends state deltas. The dedicated .NET server and the Unity
    /// "local host" mode both wrap this class with their own transport and storage.
    /// </summary>
    public sealed class ServerCore
    {
        public readonly GameWorld World;
        private readonly IGameStore _store;
        private IServerTransport _transport;
        private readonly Dictionary<int, ClientSession> _sessions = new Dictionary<int, ClientSession>();
        private readonly Dictionary<long, ClientSession> _byCharacter = new Dictionary<long, ClientSession>();
        private readonly PacketWriter _w = new PacketWriter(4096);
        private readonly List<Entity> _visible = new List<Entity>();
        private readonly HashSet<int> _visibleIds = new HashSet<int>();

        public string ServerName = "Embervale";
        public string Motd = "Welcome to Embervale!";
        public int MaxCharactersPerAccount = 3;
        public int MaxPlayers = 200;
        public int AutosaveIntervalTicks = 120;     // one minute
        public int MaxPacketsPerTick = 40;
        public Action<string> Log = _ => { };

        public IReadOnlyCollection<ClientSession> Sessions => _sessions.Values;

        private static readonly Regex NameRegex = new Regex("^[A-Za-z][A-Za-z0-9 ]{1,10}[A-Za-z0-9]$");
        private static readonly Regex UserRegex = new Regex("^[A-Za-z0-9_]{3,20}$");

        public ServerCore(GameWorld world, IGameStore store, IServerTransport transport = null)
        {
            World = world;
            _store = store;
            _transport = transport;
            World.TradeCompleted += OnTradeCompleted;
            World.MarketChanged += OnMarketChanged;
            var ws = store.LoadWorldState();
            ws?.Apply(world);
        }

        public void SetTransport(IServerTransport t) => _transport = t;

        // ------------------------------------------------------------------
        // Transport callbacks
        // ------------------------------------------------------------------
        public void OnConnected(int connId)
        {
            _sessions[connId] = new ClientSession { ConnectionId = connId, State = SessionState.Connected };
        }

        public void OnDisconnected(int connId)
        {
            if (!_sessions.TryGetValue(connId, out var s)) return;
            _sessions.Remove(connId);
            LeaveWorld(s);
            s.State = SessionState.Closed;
        }

        public void OnData(int connId, byte[] data, int offset, int length)
        {
            if (!_sessions.TryGetValue(connId, out var s)) return;
            if (length <= 0 || length > Protocol.MaxPacketBytes) { Kick(s, "Invalid packet."); return; }
            if (s.LastPacketTick != World.Tick) { s.LastPacketTick = World.Tick; s.PacketsThisTick = 0; }
            if (++s.PacketsThisTick > MaxPacketsPerTick) return;   // flood protection: drop excess
            try
            {
                var r = new PacketReader(data, offset, length);
                Handle(s, r);
            }
            catch (PacketException e)
            {
                if (++s.BadPackets > 5) Kick(s, "Malformed packets.");
                Log("Bad packet from " + connId + ": " + e.Message);
            }
            catch (Exception e)
            {
                Log("Error handling packet from " + connId + ": " + e);
            }
        }

        // ------------------------------------------------------------------
        // Packet handling
        // ------------------------------------------------------------------
        private void Handle(ClientSession s, PacketReader r)
        {
            var op = (C2S)r.U8();
            if (op == C2S.Ping) { Send(s, S2C.Pong, w => w.I32(r.I32())); return; }
            if (op == C2S.Hello)
            {
                int ver = r.I32();
                if (ver != Protocol.Version) { Kick(s, "Client version mismatch (server " + Protocol.Version + ", client " + ver + ")."); return; }
                s.HelloReceived = true;
                Send(s, S2C.HelloAck, w => w.I32(Protocol.Version).Str(ServerName).Str(Motd));
                return;
            }
            if (!s.HelloReceived) { Kick(s, "Handshake required."); return; }

            switch (s.State)
            {
                case SessionState.Connected:
                    if (op == C2S.Register) HandleRegister(s, r.Str(64), r.Str(128));
                    else if (op == C2S.Login) HandleLogin(s, r.Str(64), r.Str(128));
                    return;
                case SessionState.Authenticated:
                    if (op == C2S.CreateCharacter) HandleCreateCharacter(s, r);
                    else if (op == C2S.SelectCharacter) HandleSelectCharacter(s, r.I64());
                    else if (op == C2S.Logout) { s.State = SessionState.Connected; s.AccountId = 0; }
                    return;
                case SessionState.InWorld:
                    HandleInWorld(s, op, r);
                    return;
            }
        }

        private void HandleRegister(ClientSession s, string user, string pass)
        {
            if (user == null || pass == null || !UserRegex.IsMatch(user)) { AuthResult(s, false, "Usernames are 3-20 letters, digits or underscores."); return; }
            if (pass.Length < 6) { AuthResult(s, false, "Passwords must be at least 6 characters."); return; }
            var salt = PasswordHasher.NewSalt();
            long id = _store.CreateAccount(user.ToLowerInvariant(), PasswordHasher.Hash(pass, salt), salt);
            if (id == 0) { AuthResult(s, false, "That username is taken."); return; }
            Log("Registered account " + user);
            HandleLogin(s, user, pass);
        }

        private void HandleLogin(ClientSession s, string user, string pass)
        {
            if (user == null || pass == null || !_store.TryGetAccount(user.ToLowerInvariant(), out var id, out var hash, out var salt) || !PasswordHasher.Verify(pass, salt, hash))
            {
                AuthResult(s, false, "Invalid username or password.");
                return;
            }
            foreach (var other in _sessions.Values)
                if (other != s && other.AccountId == id && other.State != SessionState.Connected)
                {
                    Kick(other, "Logged in from another location.");
                    break;
                }
            s.AccountId = id;
            s.Username = user;
            s.State = SessionState.Authenticated;
            AuthResult(s, true, "Welcome back, " + user + ".");
            SendCharacterList(s);
        }

        private void AuthResult(ClientSession s, bool ok, string msg) => Send(s, S2C.AuthResult, w => w.Bool(ok).Str(msg));

        private void SendCharacterList(ClientSession s)
        {
            var chars = _store.ListCharacters(s.AccountId);
            Send(s, S2C.CharacterList, w =>
            {
                w.U8((byte)chars.Count);
                foreach (var c in chars)
                {
                    var tmp = CharacterMapper.Restore(World, c);
                    w.I64(c.Id).Str(c.Name).I16((short)tmp.CombatLevel).I16((short)tmp.Skills.TotalLevel);
                    var a = c.Appearance ?? new Appearance();
                    w.U8(a.Gender).U8(a.SkinTone).U8(a.HairStyle).U8(a.HairColor).U8(a.BodyColor).U8(a.LegColor);
                }
            });
        }

        private void HandleCreateCharacter(ClientSession s, PacketReader r)
        {
            var name = (r.Str(32) ?? "").Trim();
            var a = new Appearance { Gender = r.U8(), SkinTone = r.U8(), HairStyle = r.U8(), HairColor = r.U8(), BodyColor = r.U8(), LegColor = r.U8() };
            a.Sanitize();
            if (!NameRegex.IsMatch(name) || name.Contains("  ")) { CharResult(s, false, "Names are 3-12 letters, digits or spaces."); return; }
            if (_store.ListCharacters(s.AccountId).Count >= MaxCharactersPerAccount) { CharResult(s, false, "You already have the maximum number of characters."); return; }
            var p = World.CreateNewPlayer(name, a);
            var data = CharacterMapper.Capture(p);
            data.AccountId = s.AccountId;
            long id = _store.CreateCharacter(s.AccountId, data);
            if (id == 0) { CharResult(s, false, "That name is taken."); return; }
            Log("Created character " + name);
            CharResult(s, true, "Character created.");
            SendCharacterList(s);
        }

        private void CharResult(ClientSession s, bool ok, string msg) => Send(s, S2C.CharacterResult, w => w.Bool(ok).Str(msg));

        private void HandleSelectCharacter(ClientSession s, long charId)
        {
            var data = _store.LoadCharacter(charId);
            if (data == null || data.AccountId != s.AccountId) { CharResult(s, false, "Character not found."); return; }
            if (_byCharacter.TryGetValue(charId, out var existing)) Kick(existing, "Logged in from another location.");
            if (World.Players.Count >= MaxPlayers) { CharResult(s, false, "The world is full. Please try again later."); return; }
            var p = CharacterMapper.Restore(World, data);
            World.AddPlayer(p);
            s.Player = p;
            s.State = SessionState.InWorld;
            _byCharacter[charId] = s;
            s.Known.Clear();
            Send(s, S2C.EnterWorld, w => w.I32(p.Id).I32(World.Tick).F32(GameTime.TickSeconds).Str(p.Name));
            p.InventoryDirty = p.EquipmentDirty = p.BankDirty = p.SkillsDirty = p.QuestsDirty = p.StatsDirty = p.FarmingDirty = true;
            SyncSession(s);
            World.Message(p, Motd);
            Log(p.Name + " entered the world (" + World.Players.Count + " online)");
        }

        private void HandleInWorld(ClientSession s, C2S op, PacketReader r)
        {
            var p = s.Player;
            var w = World;
            switch (op)
            {
                case C2S.Logout: LeaveWorld(s); s.State = SessionState.Authenticated; SendCharacterList(s); break;
                case C2S.MoveTo: w.RequestMove(p, r.I16(), r.I16(), r.Bool()); break;
                case C2S.Interact:
                    {
                        int id = r.I32(); var opt = r.Str(32);
                        var target = w.Get(id);
                        if (target is Player other && opt == "Trade") { w.CancelActions(p); w.RequestTrade(p, other); }
                        else w.RequestInteract(p, id, opt);
                        break;
                    }
                case C2S.ItemOption: w.RequestItemOption(p, r.U8(), r.Str(32)); break;
                case C2S.UseItemOnItem: w.RequestUseItemOnItem(p, r.U8(), r.U8()); break;
                case C2S.UseItemOnEntity: w.RequestUseItemOnEntity(p, r.U8(), r.I32()); break;
                case C2S.MoveItem: w.RequestMoveItem(p, r.U8(), r.U8()); break;
                case C2S.Unequip: { int slot = r.U8(); if (slot < EquipSlots.Count) w.RequestUnequip(p, (EquipSlot)slot); break; }
                case C2S.Craft: w.RequestCraft(p, r.Str(64), r.I32(), r.I32()); break;
                case C2S.DialogueOption: w.RequestDialogueOption(p, r.U8()); break;
                case C2S.CloseInterface: w.RequestCloseInterface(p); break;
                case C2S.BankDeposit: w.RequestBankDeposit(p, r.U16(), r.I32()); break;
                case C2S.BankWithdraw: w.RequestBankWithdraw(p, r.U16(), r.I32()); break;
                case C2S.BankDepositAll: w.RequestBankDepositAll(p); break;
                case C2S.BankSwap: w.RequestBankSwap(p, r.U16(), r.U16()); break;
                case C2S.ShopBuy: w.RequestShopBuy(p, r.Str(64), r.I32()); break;
                case C2S.ShopSell: w.RequestShopSell(p, r.U8(), r.I32()); break;
                case C2S.TradeOffer: w.RequestTradeOffer(p, r.U8(), r.I32()); break;
                case C2S.TradeRemove: w.RequestTradeRemove(p, r.U8(), r.I32()); break;
                case C2S.TradeAccept: w.RequestTradeAccept(p); break;
                case C2S.Chat: { var ch = (ChatChannel)r.U8(); var text = r.Str(512); var target = r.Str(32); if (ch != ChatChannel.System) w.RequestChat(p, ch, text, target); break; }
                case C2S.PartyInvite: w.RequestPartyInvite(p, w.FindPlayerByName(r.Str(32) ?? "")); break;
                case C2S.PartyAccept: w.RequestPartyAccept(p); break;
                case C2S.PartyLeave: w.LeaveParty(p); break;
                case C2S.CastSpell: w.RequestCastSpell(p, r.Str(64), r.I32()); break;
                case C2S.SetAutocast: w.RequestSetAutocast(p, r.Str(64)); break;
                case C2S.SetStance: { int st = r.U8(); if (st <= 2) w.RequestSetStance(p, (MeleeStance)st); break; }
                case C2S.SetAutoRetaliate: w.RequestSetAutoRetaliate(p, r.Bool()); break;
                case C2S.TogglePrayer: w.RequestTogglePrayer(p, r.Str(64)); break;
                case C2S.ToggleRun: w.RequestToggleRun(p, r.Bool()); break;
                case C2S.MarketPlace: { bool buy = r.Bool(); var item = r.Str(64); int qty = r.I32(); int price = r.I32(); w.RequestMarketPlace(p, buy, item, qty, price); break; }
                case C2S.MarketCancel: w.RequestMarketCancel(p, r.I64()); break;
                case C2S.MarketCollect: w.RequestMarketCollect(p, r.I64()); break;
                case C2S.MarketQuote: w.RequestMarketQuote(p, r.Str(64)); break;
            }
        }

        private void LeaveWorld(ClientSession s)
        {
            if (s.Player == null) return;
            var p = s.Player;
            World.CancelTrade(p, "The other player has logged out.");
            SaveNow(new[] { p });
            World.RemovePlayer(p);
            _byCharacter.Remove(p.CharacterId);
            s.Player = null;
            s.Known.Clear();
            Log(p.Name + " left the world (" + World.Players.Count + " online)");
        }

        public void Kick(ClientSession s, string reason)
        {
            Send(s, S2C.Kicked, w => w.Str(reason));
            LeaveWorld(s);
            _sessions.Remove(s.ConnectionId);
            s.State = SessionState.Closed;
            _transport?.Disconnect(s.ConnectionId, reason);
        }

        // ------------------------------------------------------------------
        // Simulation tick + replication
        // ------------------------------------------------------------------
        public void Tick()
        {
            World.Step();
            foreach (var s in _sessions.Values)
                if (s.State == SessionState.InWorld && s.Player != null) SyncSession(s);
            DispatchEvents();
            if (World.Tick % AutosaveIntervalTicks == 0) SaveAll();
        }

        private void SyncSession(ClientSession s)
        {
            var p = s.Player;
            _visible.Clear();
            _visibleIds.Clear();
            World.QueryArea(p.Position, GameWorld.ViewRadius, _visible);
            bool farmRefresh = p.FarmingDirty || World.Tick % 20 == 0;
            foreach (var e in _visible)
            {
                if (!World.CanSee(p, e)) continue;
                _visibleIds.Add(e.Id);
                if (!s.Known.TryGetValue(e.Id, out var k))
                {
                    var snap = EntitySnapshot.From(World, e, p);
                    Send(s, S2C.EntitySpawn, w => snap.Write(w));
                    s.Known[e.Id] = new KnownEntity { StateVersion = e.StateVersion, X = e.Position.X, Z = e.Position.Z };
                }
                else if (k.StateVersion != e.StateVersion || k.X != e.Position.X || k.Z != e.Position.Z || (farmRefresh && e is FarmPatch))
                {
                    var snap = EntitySnapshot.From(World, e, p);
                    Send(s, S2C.EntityUpdate, w => snap.Write(w), reliable: !(e is Actor) || k.StateVersion != e.StateVersion);
                    s.Known[e.Id] = new KnownEntity { StateVersion = e.StateVersion, X = e.Position.X, Z = e.Position.Z };
                }
            }
            if (s.Known.Count != _visibleIds.Count)
            {
                var gone = new List<int>();
                foreach (var id in s.Known.Keys) if (!_visibleIds.Contains(id)) gone.Add(id);
                foreach (var id in gone)
                {
                    s.Known.Remove(id);
                    Send(s, S2C.EntityDespawn, w => w.I32(id));
                }
            }
            SendDirtyState(s);
        }

        private void SendDirtyState(ClientSession s)
        {
            var p = s.Player;
            if (p.InventoryDirty) { p.InventoryDirty = false; Send(s, S2C.Inventory, w => WriteContainer(w, p.Inventory)); }
            if (p.EquipmentDirty)
            {
                p.EquipmentDirty = false;
                Send(s, S2C.Equipment, w =>
                {
                    w.U8(EquipSlots.Count);
                    for (int i = 0; i < EquipSlots.Count; i++) { var st = p.Equipment[(EquipSlot)i]; w.Str(st.ItemId).I32(st.Quantity); }
                });
            }
            if (p.BankDirty && p.OpenBankEntityId != 0) { p.BankDirty = false; Send(s, S2C.Bank, w => WriteContainer(w, p.Bank)); }
            if (p.SkillsDirty)
            {
                p.SkillsDirty = false;
                Send(s, S2C.Skills, w =>
                {
                    w.U8((byte)Skills.Count);
                    foreach (var sk in Skills.All) w.I64(p.Skills.XpTenths(sk)).I16((short)p.BuffAmount(sk));
                });
            }
            if (p.StatsDirty || World.Tick - s.LastStatsTick >= 10)
            {
                p.StatsDirty = false;
                s.LastStatsTick = World.Tick;
                Send(s, S2C.PlayerStats, w =>
                {
                    w.I32(p.Hp).I32(p.MaxHp).F32((float)p.PrayerPoints).F32((float)p.RunEnergy).Bool(p.Running).U8((byte)p.Stance)
                     .Bool(p.AutoRetaliate).Str(p.AutocastSpell).I16((short)p.CombatLevel).I16((short)p.Quests.QuestPoints).I32(p.PoisonDamage);
                    w.U8((byte)p.ActivePrayers.Count);
                    foreach (var pr in p.ActivePrayers) w.Str(pr);
                });
            }
            if (p.QuestsDirty)
            {
                p.QuestsDirty = false;
                Send(s, S2C.Quests, w =>
                {
                    w.U8((byte)p.Quests.Quests.Count);
                    foreach (var q in p.Quests.Quests.Values)
                    {
                        w.Str(q.QuestId).U8((byte)q.Stage).Bool(q.Completed).U8((byte)q.Counters.Length);
                        foreach (var c in q.Counters) w.I32(c);
                    }
                });
            }
            if (p.FarmingDirty)
            {
                p.FarmingDirty = false;
                Send(s, S2C.Farming, w =>
                {
                    w.U8((byte)p.Farming.Patches.Count);
                    foreach (var f in p.Farming.Patches.Values) w.I32(f.PatchId).U8((byte)f.Status).Str(f.Crop).I64(f.PlantedAtUnix).I32(f.WateredMask);
                });
            }
            if (p.OpenShopId != null && World.Shops.TryGetValue(p.OpenShopId, out var shop) && shop.Version != s.LastShopVersion)
            {
                s.LastShopVersion = shop.Version;
                SendShop(s, shop);
            }
            if (p.Trade != null && p.Trade.Version != s.LastTradeVersion)
            {
                s.LastTradeVersion = p.Trade.Version;
                SendTrade(s);
            }
        }

        private static void WriteContainer(PacketWriter w, ItemContainer c)
        {
            w.U16((ushort)c.Capacity);
            int count = 0;
            for (int i = 0; i < c.Capacity; i++) if (!c[i].IsEmpty) count++;
            w.U16((ushort)count);
            for (int i = 0; i < c.Capacity; i++)
                if (!c[i].IsEmpty) w.U16((ushort)i).Str(c[i].ItemId).I32(c[i].Quantity);
        }

        private void SendShop(ClientSession s, ShopState shop)
        {
            Send(s, S2C.Shop, w =>
            {
                w.Str(shop.Def.Id).Str(shop.Def.Name).U16((ushort)shop.Order.Count);
                foreach (var id in shop.Order)
                {
                    var def = World.Db.Item(id);
                    w.Str(id).I32(shop.StockOf(id)).I32(def != null ? shop.BuyPrice(def) : 0).I32(def != null ? shop.SellPrice(def) : 0);
                }
            });
        }

        private void SendTrade(ClientSession s)
        {
            var p = s.Player;
            var t = p.Trade;
            var other = t.Other(p);
            Send(s, S2C.Trade, w =>
            {
                w.Str(other.Name).U8((byte)t.Stage).Bool(p == t.A ? t.AcceptedA : t.AcceptedB).Bool(p == t.A ? t.AcceptedB : t.AcceptedA);
                WriteContainer(w, t.OfferOf(p));
                WriteContainer(w, t.OfferOf(other));
                w.I32(other.Inventory.FreeSlots);
            });
        }

        private void DispatchEvents()
        {
            foreach (var e in World.Events)
            {
                if (e.PlayerId != 0)
                {
                    var target = SessionFor(e.PlayerId);
                    if (target != null) SendEvent(target, e);
                }
                else
                {
                    var src = World.Get(e.EntityId);
                    if (src == null && e.Type != WorldEventType.Death) continue;
                    foreach (var s in _sessions.Values)
                    {
                        if (s.State != SessionState.InWorld || s.Player == null) continue;
                        if (src != null ? s.Known.ContainsKey(src.Id) : true) SendEvent(s, e);
                    }
                }
            }
            World.Events.Clear();
        }

        private ClientSession SessionFor(int playerEntityId)
        {
            var p = World.Get<Player>(playerEntityId);
            if (p == null) return null;
            return _byCharacter.TryGetValue(p.CharacterId, out var s) ? s : null;
        }

        private void SendEvent(ClientSession s, WorldEvent e)
        {
            switch (e.Type)
            {
                case WorldEventType.GameMessage: Send(s, S2C.GameMessage, w => w.Str(e.Text)); break;
                case WorldEventType.Chat: Send(s, S2C.Chat, w => w.U8((byte)e.Channel).I32(e.EntityId).Str(e.Text2).Str(e.Text)); break;
                case WorldEventType.HitSplat: Send(s, S2C.HitSplat, w => w.I32(e.EntityId).I32(e.TargetId).I32(e.Value).Bool(e.Flag).Str(e.Text)); break;
                case WorldEventType.Projectile: Send(s, S2C.Projectile, w => w.I32(e.EntityId).I32(e.TargetId).Str(e.Text)); break;
                case WorldEventType.Animation: Send(s, S2C.Animation, w => w.I32(e.EntityId).U8((byte)e.Value)); break;
                case WorldEventType.LevelUp: if (e.PlayerId != 0) Send(s, S2C.LevelUp, w => w.I32(e.EntityId).U8((byte)e.Skill).I16((short)e.Value)); break;
                case WorldEventType.XpDrop: Send(s, S2C.XpDrop, w => w.U8((byte)e.Skill).I32(e.Value)); break;
                case WorldEventType.Death: Send(s, S2C.Death, w => w.I32(e.EntityId).I32(e.TargetId)); break;
                case WorldEventType.Telegraph: Send(s, S2C.Telegraph, w => w.I32(e.EntityId).Pos(e.X).Pos(e.Z).U8((byte)e.Value).Str(e.Text)); break;
                case WorldEventType.Sound: Send(s, S2C.Sound, w => w.I32(e.EntityId).Str(e.Text), reliable: false); break;
                case WorldEventType.Teleport:
                    {
                        var ent = World.Get(e.EntityId);
                        if (ent != null) Send(s, S2C.Teleport, w => w.I32(ent.Id).Pos(ent.Position.X).Pos(ent.Position.Z));
                        break;
                    }
                case WorldEventType.DialogueOpen:
                    {
                        var opts = World.DialogueOptions(s.Player, out var speaker, out var text);
                        Send(s, S2C.Dialogue, w =>
                        {
                            w.I32(e.EntityId).Str(speaker).Str(text).U8((byte)opts.Count);
                            foreach (var o in opts) w.Str(o);
                        });
                        break;
                    }
                case WorldEventType.DialogueClose: Send(s, S2C.DialogueClose); break;
                case WorldEventType.ShopOpen:
                    s.LastShopVersion = -1;
                    if (World.Shops.TryGetValue(e.Text, out var shop)) { SendShop(s, shop); s.LastShopVersion = shop.Version; }
                    break;
                case WorldEventType.ShopClose: Send(s, S2C.ShopClose); break;
                case WorldEventType.BankOpen:
                    Send(s, S2C.BankOpen);
                    Send(s, S2C.Bank, w => WriteContainer(w, s.Player.Bank));
                    s.Player.BankDirty = false;
                    break;
                case WorldEventType.BankClose: Send(s, S2C.BankClose); break;
                case WorldEventType.CraftMenu: Send(s, S2C.CraftMenu, w => w.I32(e.EntityId).Str(e.Text)); break;
                case WorldEventType.MarketOpen: Send(s, S2C.MarketOpen, w => w.I32(e.EntityId)); break;
                case WorldEventType.MarketClose: Send(s, S2C.MarketClose); break;
                case WorldEventType.MarketUpdate: SendMarketOrders(s); break;
                case WorldEventType.MarketQuote:
                    {
                        var q = World.Market.Quote(e.Text);
                        Send(s, S2C.MarketQuote, w => w.Str(e.Text).I32(q.bestBuy).I32(q.buyQty).I32(q.bestSell).I32(q.sellQty).I32(q.last));
                        break;
                    }
                case WorldEventType.TradeRequest: Send(s, S2C.TradeRequest, w => w.I32(e.EntityId).Str(e.Text)); break;
                case WorldEventType.TradeUpdate: if (s.Player.Trade != null) { SendTrade(s); s.LastTradeVersion = s.Player.Trade.Version; } break;
                case WorldEventType.TradeClose: Send(s, S2C.TradeClose, w => w.Str(e.Text)); break;
                case WorldEventType.QuestUpdate: s.Player.QuestsDirty = true; break;
                case WorldEventType.PartyUpdate:
                    {
                        var party = s.Player.Party;
                        Send(s, S2C.Party, w =>
                        {
                            int n = party?.Members.Count ?? 0;
                            w.U8((byte)n);
                            if (party != null)
                                foreach (var m in party.Members)
                                    w.I32(m.Id).Str(m.Name).I32(m.Hp).I32(m.MaxHp).I16((short)m.CombatLevel).Bool(party.Leader == m);
                        });
                        break;
                    }
            }
        }

        private void Send(ClientSession s, S2C op, Action<PacketWriter> body = null, bool reliable = true)
        {
            if (_transport == null) return;
            _w.Reset();
            _w.U8((byte)op);
            body?.Invoke(_w);
            _transport.Send(s.ConnectionId, _w.Buffer, _w.Length, reliable);
        }

        // ------------------------------------------------------------------
        // Persistence
        // ------------------------------------------------------------------
        private void OnTradeCompleted(TradeSession t)
        {
            // persist both sides in one transaction immediately so a crash can't duplicate items
            SaveNow(new[] { t.A, t.B });
            _store.AppendTradeLog(t.A.Name, t.B.Name, "completed");
        }

        private void OnMarketChanged(Player p, MarketPersist order)
        {
            // Persist in the order that can lose value on a crash but never duplicate it.
            switch (order)
            {
                case MarketPersist.CharacterThenWorld: SaveNow(new[] { p }); _store.SaveWorldState(WorldStateData.Capture(World)); break;
                case MarketPersist.WorldOnly: _store.SaveWorldState(WorldStateData.Capture(World)); break;
                case MarketPersist.WorldThenCharacter: _store.SaveWorldState(WorldStateData.Capture(World)); SaveNow(new[] { p }); break;
            }
        }

        private void SendMarketOrders(ClientSession s)
        {
            if (s.Player == null) return;
            var mine = new List<MarketOrder>(World.Market.For(s.Player.CharacterId));
            Send(s, S2C.MarketOrders, w =>
            {
                w.U8((byte)mine.Count);
                foreach (var o in mine)
                    w.I64(o.Id).Bool(o.Buy).Str(o.ItemId).I32(o.Quantity).I32(o.Filled).I32(o.Price).Bool(o.Cancelled).I32(o.CollectItems).I64(o.CollectCoins);
            });
        }

        public void SaveNow(IEnumerable<Player> players)
        {
            var list = new List<CharacterData>();
            foreach (var p in players) if (p.CharacterId != 0) list.Add(CharacterMapper.Capture(p));
            if (list.Count > 0) _store.SaveCharacters(list);
        }

        public void SaveAll()
        {
            SaveNow(World.Players);
            _store.SaveWorldState(WorldStateData.Capture(World));
        }

        public void Shutdown(string reason)
        {
            foreach (var s in new List<ClientSession>(_sessions.Values)) Kick(s, reason);
            SaveAll();
        }
    }
}
