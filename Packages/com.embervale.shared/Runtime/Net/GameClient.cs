using System;
using System.Collections.Generic;
using Embervale.Shared.Data;
using Embervale.Shared.Simulation;

namespace Embervale.Shared.Net
{
    public interface IClientTransport
    {
        void Send(byte[] data, bool reliable);
        void Disconnect();
    }

    public sealed class ClientSlot
    {
        public string ItemId;
        public int Quantity;
        public bool IsEmpty => string.IsNullOrEmpty(ItemId) || Quantity <= 0;
    }

    public sealed class ClientQuest
    {
        public string Id;
        public int Stage;
        public bool Completed;
        public int[] Counters;
    }

    public sealed class TradeView
    {
        public string OtherName;
        public TradeStage Stage;
        public bool MeAccepted, ThemAccepted;
        public ClientSlot[] MyOffer = new ClientSlot[0];
        public ClientSlot[] TheirOffer = new ClientSlot[0];
        public int TheirFreeSlots;
    }

    public sealed class DialogueView
    {
        public int NpcId;
        public string Speaker;
        public string Text;
        public List<string> Options = new List<string>();
    }

    public sealed class ShopView
    {
        public string Id;
        public string Name;
        public List<ShopEntry> Entries = new List<ShopEntry>();
    }

    public sealed class MarketOrderView
    {
        public long Id;
        public bool Buy;
        public string ItemId;
        public int Quantity, Filled, Price, CollectItems;
        public long CollectCoins;
        public bool Cancelled;
        public bool Active => !Cancelled && Filled < Quantity;
    }

    public sealed class MarketQuoteView
    {
        public string ItemId;
        public int BestBuy, BuyQty, BestSell, SellQty, LastPrice;
    }

    /// <summary>
    /// Engine-independent client: decodes server packets into a local mirror of the world and
    /// exposes events for the presentation layer (Unity). Contains no game rules: everything it
    /// shows was decided by the server.
    /// </summary>
    public sealed class GameClient
    {
        private IClientTransport _transport;
        public GameDatabase Db { get; }

        // connection / account
        public bool HandshakeDone { get; private set; }
        public bool Authenticated { get; private set; }
        public bool InWorld { get; private set; }
        public string ServerName { get; private set; }
        public string Motd { get; private set; }
        public int SelfId { get; private set; }
        public string SelfName { get; private set; }
        public float TickSeconds { get; private set; } = GameTime.TickSeconds;
        public List<CharacterSummary> Characters { get; } = new List<CharacterSummary>();

        // world mirror
        public readonly Dictionary<int, EntitySnapshot> Entities = new Dictionary<int, EntitySnapshot>();
        public readonly ClientSlot[] Inventory = NewSlots(GameWorld.InventorySize);
        public readonly ClientSlot[] Equipment = NewSlots(EquipSlots.Count);
        public ClientSlot[] Bank = NewSlots(0);
        public readonly long[] SkillXpTenths = new long[Skills.Count];
        public readonly int[] SkillBoosts = new int[Skills.Count];
        public readonly Dictionary<string, ClientQuest> Quests = new Dictionary<string, ClientQuest>();
        public readonly Dictionary<int, PatchState> FarmPatches = new Dictionary<int, PatchState>();
        public readonly List<PartyMemberInfo> Party = new List<PartyMemberInfo>();
        public int Hp, MaxHp, CombatLevel, QuestPoints, Poison;
        public float PrayerPoints, RunEnergy;
        public bool Running, AutoRetaliate;
        public MeleeStance Stance;
        public string Autocast;
        public readonly HashSet<string> ActivePrayers = new HashSet<string>();
        public DialogueView Dialogue;
        public ShopView Shop;
        public bool BankOpen;
        public TradeView Trade;
        public bool MarketOpen;
        public readonly List<MarketOrderView> MarketOrders = new List<MarketOrderView>();
        public MarketQuoteView MarketQuote;
        public int LastPingMs;

        // events for the presentation layer
        public event Action<bool, string> OnAuthResult;
        public event Action<bool, string> OnCharacterResult;
        public event Action OnCharacterList;
        public event Action OnEnterWorld;
        public event Action<string> OnKicked;
        public event Action<EntitySnapshot> OnEntitySpawn;
        public event Action<EntitySnapshot> OnEntityUpdate;
        public event Action<int> OnEntityDespawn;
        public event Action<int, float, float> OnTeleport;
        public event Action OnInventoryChanged, OnEquipmentChanged, OnBankChanged, OnSkillsChanged, OnStatsChanged, OnQuestsChanged, OnFarmingChanged, OnPartyChanged;
        public event Action<string> OnGameMessage;
        public event Action<ChatChannel, int, string, string> OnChat;
        public event Action<int, int, int, bool, string> OnHitSplat;
        public event Action<int, int, string> OnProjectile;
        public event Action<int, ActionAnim> OnAnimation;
        public event Action<Skill, int> OnLevelUp;
        public event Action<Skill, int> OnXpDrop;
        public event Action<int, int> OnDeath;
        public event Action<int, float, float, int, string> OnTelegraph;
        public event Action<int, string> OnSound;
        public event Action<DialogueView> OnDialogue;
        public event Action OnDialogueClosed;
        public event Action<ShopView> OnShop;
        public event Action OnShopClosed;
        public event Action OnBankOpened, OnBankClosed;
        public event Action<int, string> OnCraftMenu;
        public event Action<int, string> OnTradeRequest;
        public event Action<TradeView> OnTrade;
        public event Action<string> OnTradeClosed;
        public event Action OnMarketOpened, OnMarketClosed, OnMarketOrders;
        public event Action<MarketQuoteView> OnMarketQuote;

        public GameClient(GameDatabase db, IClientTransport transport = null)
        {
            Db = db;
            _transport = transport;
        }

        public void SetTransport(IClientTransport t) => _transport = t;

        private static ClientSlot[] NewSlots(int n)
        {
            var a = new ClientSlot[n];
            for (int i = 0; i < n; i++) a[i] = new ClientSlot();
            return a;
        }

        public EntitySnapshot Self => Entities.TryGetValue(SelfId, out var e) ? e : null;
        public int SkillLevel(Skill s) => Db.XpTable.LevelForXp(SkillXpTenths[(int)s] / SkillSet.XpScale);
        public long SkillXp(Skill s) => SkillXpTenths[(int)s] / SkillSet.XpScale;

        // ------------------------------------------------------------------
        // Outgoing
        // ------------------------------------------------------------------
        public void Send(byte[] packet, bool reliable = true) => _transport?.Send(packet, reliable);

        public void Connect() { Send(ClientPackets.Hello()); }
        public void Login(string user, string pass) => Send(ClientPackets.Login(user, pass));
        public void Register(string user, string pass) => Send(ClientPackets.Register(user, pass));
        public void CreateCharacter(string name, Appearance a) => Send(ClientPackets.CreateCharacter(name, a));
        public void SelectCharacter(long id) => Send(ClientPackets.SelectCharacter(id));
        public void MoveTo(int x, int z, bool run) => Send(ClientPackets.MoveTo(x, z, run));
        public void Interact(int id, string option) => Send(ClientPackets.Interact(id, option));

        // ------------------------------------------------------------------
        // Incoming
        // ------------------------------------------------------------------
        public void Receive(byte[] data, int offset, int length)
        {
            var r = new PacketReader(data, offset, length);
            var op = (S2C)r.U8();
            switch (op)
            {
                case S2C.HelloAck: r.I32(); ServerName = r.Str(); Motd = r.Str(); HandshakeDone = true; break;
                case S2C.AuthResult:
                    {
                        bool ok = r.Bool(); var msg = r.Str();
                        Authenticated = ok;
                        OnAuthResult?.Invoke(ok, msg);
                        break;
                    }
                case S2C.CharacterList:
                    {
                        Characters.Clear();
                        int n = r.U8();
                        for (int i = 0; i < n; i++)
                            Characters.Add(new CharacterSummary
                            {
                                Id = r.I64(), Name = r.Str(), CombatLevel = r.I16(), TotalLevel = r.I16(),
                                Appearance = new Appearance { Gender = r.U8(), SkinTone = r.U8(), HairStyle = r.U8(), HairColor = r.U8(), BodyColor = r.U8(), LegColor = r.U8() },
                            });
                        InWorld = false;
                        OnCharacterList?.Invoke();
                        break;
                    }
                case S2C.CharacterResult: { bool ok = r.Bool(); OnCharacterResult?.Invoke(ok, r.Str()); break; }
                case S2C.EnterWorld:
                    SelfId = r.I32(); r.I32(); TickSeconds = r.F32(); SelfName = r.Str();
                    Entities.Clear();
                    InWorld = true;
                    OnEnterWorld?.Invoke();
                    break;
                case S2C.Pong: LastPingMs = Environment.TickCount - r.I32(); break;
                case S2C.Kicked: { var reason = r.Str(); InWorld = false; OnKicked?.Invoke(reason); break; }
                case S2C.EntitySpawn:
                    {
                        var s = EntitySnapshot.Read(r);
                        Entities[s.Id] = s;
                        OnEntitySpawn?.Invoke(s);
                        break;
                    }
                case S2C.EntityUpdate:
                    {
                        var s = EntitySnapshot.Read(r);
                        if (!Entities.ContainsKey(s.Id)) break;
                        Entities[s.Id] = s;
                        OnEntityUpdate?.Invoke(s);
                        break;
                    }
                case S2C.EntityDespawn: { int id = r.I32(); if (Entities.Remove(id)) OnEntityDespawn?.Invoke(id); break; }
                case S2C.Teleport:
                    {
                        int id = r.I32(); float x = r.Pos(), z = r.Pos();
                        if (Entities.TryGetValue(id, out var e)) { e.X = x; e.Z = z; }
                        OnTeleport?.Invoke(id, x, z);
                        break;
                    }
                case S2C.Inventory: ReadContainer(r, Inventory); OnInventoryChanged?.Invoke(); break;
                case S2C.Equipment:
                    {
                        int n = r.U8();
                        for (int i = 0; i < n; i++) { var id = r.Str(); var q = r.I32(); if (i < Equipment.Length) { Equipment[i].ItemId = id; Equipment[i].Quantity = q; } }
                        OnEquipmentChanged?.Invoke();
                        break;
                    }
                case S2C.Bank:
                    {
                        int cap = r.U16();
                        if (Bank.Length != cap) Bank = NewSlots(cap);
                        ReadContainerBody(r, Bank);
                        OnBankChanged?.Invoke();
                        break;
                    }
                case S2C.Skills:
                    {
                        int n = r.U8();
                        for (int i = 0; i < n; i++) { long xp = r.I64(); int boost = r.I16(); if (i < SkillXpTenths.Length) { SkillXpTenths[i] = xp; SkillBoosts[i] = boost; } }
                        OnSkillsChanged?.Invoke();
                        break;
                    }
                case S2C.PlayerStats:
                    {
                        Hp = r.I32(); MaxHp = r.I32(); PrayerPoints = r.F32(); RunEnergy = r.F32(); Running = r.Bool(); Stance = (MeleeStance)r.U8();
                        AutoRetaliate = r.Bool(); Autocast = r.Str(); CombatLevel = r.I16(); QuestPoints = r.I16(); Poison = r.I32();
                        ActivePrayers.Clear();
                        int n = r.U8();
                        for (int i = 0; i < n; i++) ActivePrayers.Add(r.Str());
                        OnStatsChanged?.Invoke();
                        break;
                    }
                case S2C.Quests:
                    {
                        Quests.Clear();
                        int n = r.U8();
                        for (int i = 0; i < n; i++)
                        {
                            var q = new ClientQuest { Id = r.Str(), Stage = r.U8(), Completed = r.Bool() };
                            int c = r.U8();
                            q.Counters = new int[c];
                            for (int k = 0; k < c; k++) q.Counters[k] = r.I32();
                            Quests[q.Id] = q;
                        }
                        OnQuestsChanged?.Invoke();
                        break;
                    }
                case S2C.Farming:
                    {
                        FarmPatches.Clear();
                        int n = r.U8();
                        for (int i = 0; i < n; i++)
                        {
                            var ps = new PatchState { PatchId = r.I32(), Status = (PatchStatus)r.U8(), Crop = r.Str(), PlantedAtUnix = r.I64(), WateredMask = r.I32() };
                            FarmPatches[ps.PatchId] = ps;
                        }
                        OnFarmingChanged?.Invoke();
                        break;
                    }
                case S2C.GameMessage: OnGameMessage?.Invoke(r.Str()); break;
                case S2C.Chat: { var ch = (ChatChannel)r.U8(); int id = r.I32(); var name = r.Str(); var text = r.Str(); OnChat?.Invoke(ch, id, name, text); break; }
                case S2C.HitSplat: { int v = r.I32(), a = r.I32(), d = r.I32(); bool crit = r.Bool(); var kind = r.Str(); OnHitSplat?.Invoke(v, a, d, crit, kind); break; }
                case S2C.Projectile: { int f = r.I32(), t = r.I32(); OnProjectile?.Invoke(f, t, r.Str()); break; }
                case S2C.Animation: { int id = r.I32(); var anim = (ActionAnim)r.U8(); if (Entities.TryGetValue(id, out var e)) e.Anim = anim; OnAnimation?.Invoke(id, anim); break; }
                case S2C.LevelUp: { r.I32(); var sk = (Skill)r.U8(); OnLevelUp?.Invoke(sk, r.I16()); break; }
                case S2C.XpDrop: { var sk = (Skill)r.U8(); OnXpDrop?.Invoke(sk, r.I32()); break; }
                case S2C.Death: { int id = r.I32(); OnDeath?.Invoke(id, r.I32()); break; }
                case S2C.Telegraph: { int id = r.I32(); float x = r.Pos(), z = r.Pos(); int rad = r.U8(); OnTelegraph?.Invoke(id, x, z, rad, r.Str()); break; }
                case S2C.Sound: { int id = r.I32(); OnSound?.Invoke(id, r.Str()); break; }
                case S2C.Dialogue:
                    {
                        var d = new DialogueView { NpcId = r.I32(), Speaker = r.Str(), Text = r.Str() };
                        int n = r.U8();
                        for (int i = 0; i < n; i++) d.Options.Add(r.Str());
                        Dialogue = d;
                        OnDialogue?.Invoke(d);
                        break;
                    }
                case S2C.DialogueClose: Dialogue = null; OnDialogueClosed?.Invoke(); break;
                case S2C.Shop:
                    {
                        var sv = new ShopView { Id = r.Str(), Name = r.Str() };
                        int n = r.U16();
                        for (int i = 0; i < n; i++) sv.Entries.Add(new ShopEntry { ItemId = r.Str(), Stock = r.I32(), BuyPrice = r.I32(), SellPrice = r.I32() });
                        Shop = sv;
                        OnShop?.Invoke(sv);
                        break;
                    }
                case S2C.ShopClose: Shop = null; OnShopClosed?.Invoke(); break;
                case S2C.BankOpen: BankOpen = true; OnBankOpened?.Invoke(); break;
                case S2C.BankClose: BankOpen = false; OnBankClosed?.Invoke(); break;
                case S2C.CraftMenu: { int id = r.I32(); OnCraftMenu?.Invoke(id, r.Str()); break; }
                case S2C.TradeRequest: { int id = r.I32(); OnTradeRequest?.Invoke(id, r.Str()); break; }
                case S2C.Trade:
                    {
                        var t = new TradeView { OtherName = r.Str(), Stage = (TradeStage)r.U8(), MeAccepted = r.Bool(), ThemAccepted = r.Bool() };
                        t.MyOffer = ReadContainerNew(r);
                        t.TheirOffer = ReadContainerNew(r);
                        t.TheirFreeSlots = r.I32();
                        Trade = t;
                        OnTrade?.Invoke(t);
                        break;
                    }
                case S2C.TradeClose: Trade = null; OnTradeClosed?.Invoke(r.Str()); break;
                case S2C.MarketOpen: r.I32(); MarketOpen = true; OnMarketOpened?.Invoke(); break;
                case S2C.MarketClose: MarketOpen = false; OnMarketClosed?.Invoke(); break;
                case S2C.MarketOrders:
                    {
                        MarketOrders.Clear();
                        int n = r.U8();
                        for (int i = 0; i < n; i++)
                            MarketOrders.Add(new MarketOrderView
                            {
                                Id = r.I64(), Buy = r.Bool(), ItemId = r.Str(), Quantity = r.I32(), Filled = r.I32(), Price = r.I32(),
                                Cancelled = r.Bool(), CollectItems = r.I32(), CollectCoins = r.I64(),
                            });
                        OnMarketOrders?.Invoke();
                        break;
                    }
                case S2C.MarketQuote:
                    MarketQuote = new MarketQuoteView { ItemId = r.Str(), BestBuy = r.I32(), BuyQty = r.I32(), BestSell = r.I32(), SellQty = r.I32(), LastPrice = r.I32() };
                    OnMarketQuote?.Invoke(MarketQuote);
                    break;
                case S2C.Party:
                    {
                        Party.Clear();
                        int n = r.U8();
                        for (int i = 0; i < n; i++)
                            Party.Add(new PartyMemberInfo { EntityId = r.I32(), Name = r.Str(), Hp = r.I32(), MaxHp = r.I32(), CombatLevel = r.I16(), Leader = r.Bool() });
                        OnPartyChanged?.Invoke();
                        break;
                    }
            }
        }

        private static void ReadContainer(PacketReader r, ClientSlot[] slots)
        {
            r.U16(); // capacity
            ReadContainerBody(r, slots);
        }

        private static void ReadContainerBody(PacketReader r, ClientSlot[] slots)
        {
            foreach (var s in slots) { s.ItemId = null; s.Quantity = 0; }
            int count = r.U16();
            for (int i = 0; i < count; i++)
            {
                int slot = r.U16(); var id = r.Str(); int qty = r.I32();
                if (slot < slots.Length) { slots[slot].ItemId = id; slots[slot].Quantity = qty; }
            }
        }

        private static ClientSlot[] ReadContainerNew(PacketReader r)
        {
            int cap = r.U16();
            var slots = NewSlots(cap);
            ReadContainerBody(r, slots);
            return slots;
        }

        public int InventoryCount(string itemId)
        {
            int n = 0;
            foreach (var s in Inventory) if (s.ItemId == itemId) n += s.Quantity;
            return n;
        }
    }
}
