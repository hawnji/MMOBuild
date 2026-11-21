using System.Collections.Generic;
using Embervale.Shared.Simulation;

namespace Embervale.Shared.Net
{
    public static class Protocol
    {
        public const int Version = 3;
        public const int DefaultPort = 7777;
        public const string ConnectionKey = "embervale";
        public const int MaxPacketBytes = 16 * 1024;
    }

    /// <summary>Client to server opcodes.</summary>
    public enum C2S : byte
    {
        Hello = 1, Register, Login, CreateCharacter, SelectCharacter, Logout, Ping,
        MoveTo = 20, Interact, ItemOption, UseItemOnItem, UseItemOnEntity, MoveItem, Unequip, Craft,
        DialogueOption = 40, CloseInterface,
        BankDeposit = 50, BankWithdraw, BankDepositAll, BankSwap,
        ShopBuy = 60, ShopSell,
        TradeOffer = 70, TradeRemove, TradeAccept,
        Chat = 80, PartyInvite, PartyAccept, PartyLeave,
        CastSpell = 90, SetAutocast, SetStance, SetAutoRetaliate, TogglePrayer, ToggleRun,
        MarketPlace = 100, MarketCancel, MarketCollect, MarketQuote,
    }

    /// <summary>Server to client opcodes.</summary>
    public enum S2C : byte
    {
        HelloAck = 1, AuthResult, CharacterList, CharacterResult, EnterWorld, Pong, Kicked,
        EntitySpawn = 20, EntityUpdate, EntityDespawn, Teleport,
        Inventory = 30, Equipment, Bank, Skills, PlayerStats, Quests, Farming,
        GameMessage = 50, Chat, HitSplat, Projectile, Animation, LevelUp, XpDrop, Death, Telegraph, Sound,
        Dialogue = 70, DialogueClose, Shop, ShopClose, BankOpen, BankClose, CraftMenu,
        TradeRequest = 80, Trade, TradeClose, Party,
        MarketOpen = 90, MarketOrders, MarketQuote, MarketClose,
    }

    /// <summary>Networked view of any entity. The same struct is used for spawn and update.</summary>
    public sealed class EntitySnapshot
    {
        public int Id;
        public EntityKind Kind;
        public string DefId;
        public string Name;
        public float X, Z, Facing;
        public int Hp, MaxHp;
        public ActionAnim Anim;
        public byte Flags;
        public int Level;
        public int Quantity;
        public Appearance Appearance;
        public string[] Equipped;   // players only: item ids per EquipSlot

        public const byte FlagDepleted = 1, FlagOpen = 2, FlagDead = 4, FlagRunning = 8, FlagEmpty = 16, FlagInCombat = 32, FlagBoss = 64;

        public bool Has(byte f) => (Flags & f) != 0;

        public void Write(PacketWriter w)
        {
            w.I32(Id).U8((byte)Kind).Str(DefId).Str(Name).Pos(X).Pos(Z).I16((short)Facing)
             .I32(Hp).I32(MaxHp).U8((byte)Anim).U8(Flags).I16((short)Level).I32(Quantity);
            if (Kind == EntityKind.Player)
            {
                var a = Appearance ?? new Appearance();
                w.U8(a.Gender).U8(a.SkinTone).U8(a.HairStyle).U8(a.HairColor).U8(a.BodyColor).U8(a.LegColor);
                int n = Equipped?.Length ?? 0;
                w.U8((byte)n);
                for (int i = 0; i < n; i++) w.Str(Equipped[i]);
            }
        }

        public static EntitySnapshot Read(PacketReader r)
        {
            var s = new EntitySnapshot
            {
                Id = r.I32(), Kind = (EntityKind)r.U8(), DefId = r.Str(), Name = r.Str(), X = r.Pos(), Z = r.Pos(), Facing = r.I16(),
                Hp = r.I32(), MaxHp = r.I32(), Anim = (ActionAnim)r.U8(), Flags = r.U8(), Level = r.I16(), Quantity = r.I32(),
            };
            if (s.Kind == EntityKind.Player)
            {
                s.Appearance = new Appearance { Gender = r.U8(), SkinTone = r.U8(), HairStyle = r.U8(), HairColor = r.U8(), BodyColor = r.U8(), LegColor = r.U8() };
                int n = r.U8();
                s.Equipped = new string[n];
                for (int i = 0; i < n; i++) s.Equipped[i] = r.Str();
            }
            return s;
        }

        /// <summary>Builds the snapshot of an entity as seen by <paramref name="viewer"/>.</summary>
        public static EntitySnapshot From(GameWorld w, Entity e, Player viewer)
        {
            var s = new EntitySnapshot
            {
                Id = e.Id, Kind = e.Kind, DefId = e.DefId, Name = e.DisplayName, X = e.Position.X, Z = e.Position.Z, Facing = e.Facing,
            };
            switch (e)
            {
                case Player p:
                    s.Hp = p.Hp; s.MaxHp = p.MaxHp; s.Anim = p.Anim; s.Level = p.CombatLevel; s.Appearance = p.Appearance;
                    if (p.IsDead) s.Flags |= FlagDead;
                    if (p.Running) s.Flags |= FlagRunning;
                    if (p.Activity is CombatActivity) s.Flags |= FlagInCombat;
                    s.Equipped = new string[Data.EquipSlots.Count];
                    for (int i = 0; i < s.Equipped.Length; i++) s.Equipped[i] = p.Equipment[(Data.EquipSlot)i].ItemId;
                    break;
                case Monster m:
                    s.Hp = m.Hp; s.MaxHp = m.MaxHp; s.Anim = m.Anim; s.Level = m.Def.Level;
                    if (m.State == MonsterState.Dead) s.Flags |= FlagDead;
                    if (m.Running) s.Flags |= FlagRunning;
                    if (m.TargetId != 0) s.Flags |= FlagInCombat;
                    if (m.Def.Boss) s.Flags |= FlagBoss;
                    break;
                case Npc n:
                    s.Hp = n.Hp; s.MaxHp = n.MaxHp; s.Anim = n.Anim;
                    break;
                case ResourceNode r:
                    if (r.Depleted) s.Flags |= FlagDepleted;
                    break;
                case Door d:
                    if (d.Open) s.Flags |= FlagOpen;
                    break;
                case GroundItem g:
                    s.Quantity = g.Stack.Quantity;
                    s.Name = w.Db.Item(g.Stack.ItemId)?.Name ?? g.Stack.ItemId;
                    break;
                case Stall st:
                    if (st.EmptyUntilTick > 0) s.Flags |= FlagEmpty;
                    break;
                case Station st2:
                    s.DefId = st2.Model ?? st2.StationType;
                    s.Name = st2.StationType;
                    break;
                case FarmPatch fp:
                    if (viewer != null && viewer.Farming.Patches.TryGetValue(fp.Id, out var ps))
                    {
                        s.Level = (int)ps.Status;
                        s.Name = ps.Crop ?? "Farming patch";
                        if (ps.Status == PatchStatus.Growing && ps.Crop != null && w.Db.Crops.TryGetValue(ps.Crop, out var crop))
                            s.Quantity = FarmingRules.CurrentStage(ps, crop, w.Clock(), out _);
                    }
                    break;
            }
            return s;
        }
    }

    public sealed class CharacterSummary
    {
        public long Id;
        public string Name;
        public int CombatLevel;
        public int TotalLevel;
        public Appearance Appearance;
    }

    public sealed class ShopEntry
    {
        public string ItemId;
        public int Stock;
        public int BuyPrice;
        public int SellPrice;
    }

    public sealed class PartyMemberInfo
    {
        public int EntityId;
        public string Name;
        public int Hp, MaxHp, CombatLevel;
        public bool Leader;
    }

    /// <summary>Static helpers that build client-to-server packets (used by the Unity client and bots/tests).</summary>
    public static class ClientPackets
    {
        public static byte[] Build(C2S op, System.Action<PacketWriter> body = null)
        {
            var w = new PacketWriter(64);
            w.U8((byte)op);
            body?.Invoke(w);
            return w.ToArray();
        }

        public static byte[] Hello() => Build(C2S.Hello, w => w.I32(Protocol.Version));
        public static byte[] Register(string user, string pass) => Build(C2S.Register, w => w.Str(user).Str(pass));
        public static byte[] Login(string user, string pass) => Build(C2S.Login, w => w.Str(user).Str(pass));
        public static byte[] CreateCharacter(string name, Appearance a) => Build(C2S.CreateCharacter, w => w.Str(name).U8(a.Gender).U8(a.SkinTone).U8(a.HairStyle).U8(a.HairColor).U8(a.BodyColor).U8(a.LegColor));
        public static byte[] SelectCharacter(long id) => Build(C2S.SelectCharacter, w => w.I64(id));
        public static byte[] Logout() => Build(C2S.Logout);
        public static byte[] Ping(int t) => Build(C2S.Ping, w => w.I32(t));
        public static byte[] MoveTo(int x, int z, bool run) => Build(C2S.MoveTo, w => w.I16((short)x).I16((short)z).Bool(run));
        public static byte[] Interact(int id, string option) => Build(C2S.Interact, w => w.I32(id).Str(option));
        public static byte[] ItemOption(int slot, string option) => Build(C2S.ItemOption, w => w.U8((byte)slot).Str(option));
        public static byte[] UseItemOnItem(int a, int b) => Build(C2S.UseItemOnItem, w => w.U8((byte)a).U8((byte)b));
        public static byte[] UseItemOnEntity(int slot, int id) => Build(C2S.UseItemOnEntity, w => w.U8((byte)slot).I32(id));
        public static byte[] MoveItem(int from, int to) => Build(C2S.MoveItem, w => w.U8((byte)from).U8((byte)to));
        public static byte[] Unequip(int slot) => Build(C2S.Unequip, w => w.U8((byte)slot));
        public static byte[] Craft(string recipe, int count, int station) => Build(C2S.Craft, w => w.Str(recipe).I32(count).I32(station));
        public static byte[] DialogueOption(int i) => Build(C2S.DialogueOption, w => w.U8((byte)i));
        public static byte[] CloseInterface() => Build(C2S.CloseInterface);
        public static byte[] BankDeposit(int slot, int qty) => Build(C2S.BankDeposit, w => w.U16((ushort)slot).I32(qty));
        public static byte[] BankWithdraw(int slot, int qty) => Build(C2S.BankWithdraw, w => w.U16((ushort)slot).I32(qty));
        public static byte[] BankDepositAll() => Build(C2S.BankDepositAll);
        public static byte[] BankSwap(int a, int b) => Build(C2S.BankSwap, w => w.U16((ushort)a).U16((ushort)b));
        public static byte[] ShopBuy(string item, int qty) => Build(C2S.ShopBuy, w => w.Str(item).I32(qty));
        public static byte[] ShopSell(int slot, int qty) => Build(C2S.ShopSell, w => w.U8((byte)slot).I32(qty));
        public static byte[] TradeOffer(int slot, int qty) => Build(C2S.TradeOffer, w => w.U8((byte)slot).I32(qty));
        public static byte[] TradeRemove(int slot, int qty) => Build(C2S.TradeRemove, w => w.U8((byte)slot).I32(qty));
        public static byte[] TradeAccept() => Build(C2S.TradeAccept);
        public static byte[] Chat(ChatChannel ch, string text, string target = null) => Build(C2S.Chat, w => w.U8((byte)ch).Str(text).Str(target));
        public static byte[] PartyInvite(string name) => Build(C2S.PartyInvite, w => w.Str(name));
        public static byte[] PartyAccept() => Build(C2S.PartyAccept);
        public static byte[] PartyLeave() => Build(C2S.PartyLeave);
        public static byte[] CastSpell(string spell, int target) => Build(C2S.CastSpell, w => w.Str(spell).I32(target));
        public static byte[] SetAutocast(string spell) => Build(C2S.SetAutocast, w => w.Str(spell));
        public static byte[] SetStance(Data.MeleeStance s) => Build(C2S.SetStance, w => w.U8((byte)s));
        public static byte[] SetAutoRetaliate(bool on) => Build(C2S.SetAutoRetaliate, w => w.Bool(on));
        public static byte[] TogglePrayer(string id) => Build(C2S.TogglePrayer, w => w.Str(id));
        public static byte[] ToggleRun(bool on) => Build(C2S.ToggleRun, w => w.Bool(on));
        public static byte[] MarketPlace(bool buy, string item, int qty, int price) => Build(C2S.MarketPlace, w => w.Bool(buy).Str(item).I32(qty).I32(price));
        public static byte[] MarketCancel(long orderId) => Build(C2S.MarketCancel, w => w.I64(orderId));
        public static byte[] MarketCollect(long orderId) => Build(C2S.MarketCollect, w => w.I64(orderId));
        public static byte[] MarketQuote(string item) => Build(C2S.MarketQuote, w => w.Str(item));
    }
}
