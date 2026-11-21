using System;
using System.Collections.Generic;
using Embervale.Shared.Data;
using Embervale.Shared.Simulation;

namespace Embervale.Shared.Persistence
{
    public sealed class SlotData
    {
        public int Slot;
        public string Item;
        public int Qty;
    }

    public sealed class QuestData
    {
        public string Id;
        public int Stage;
        public int[] Counters;
        public bool Completed;
    }

    public sealed class PatchData
    {
        public int Id;
        public PatchStatus Status;
        public string Crop;
        public long PlantedAt;
        public int Watered;
        public int HarvestsLeft;
    }

    /// <summary>
    /// Serializable snapshot of everything persistent about a character. Stored as JSON in the
    /// character table so new fields never need a schema migration.
    /// </summary>
    public sealed class CharacterData
    {
        public long Id;
        public long AccountId;
        public string Name;
        public Appearance Appearance = new Appearance();
        public float X, Z;
        public int Hp;
        public double PrayerPoints;
        public double RunEnergy = 100;
        public Dictionary<string, long> SkillXpTenths = new Dictionary<string, long>();
        public List<SlotData> Inventory = new List<SlotData>();
        public List<SlotData> Equipment = new List<SlotData>();
        public List<SlotData> Bank = new List<SlotData>();
        public List<QuestData> Quests = new List<QuestData>();
        public List<PatchData> Farming = new List<PatchData>();
        public Dictionary<string, string> Settings = new Dictionary<string, string>();
        public MeleeStance Stance;
        public bool AutoRetaliate = true;
        public string AutocastSpell;
        public long PlayTimeSeconds;
        public int BankCapacity = GameWorld.BankCapacity;
    }

    public static class CharacterMapper
    {
        /// <summary>Captures persistent state. Items held in an unfinished trade are saved back into the inventory.</summary>
        public static CharacterData Capture(Player p)
        {
            var d = new CharacterData
            {
                Id = p.CharacterId,
                AccountId = p.AccountId,
                Name = p.Name,
                Appearance = p.Appearance.Clone(),
                X = p.Position.X,
                Z = p.Position.Z,
                Hp = Math.Max(1, p.Hp),
                PrayerPoints = p.PrayerPoints,
                RunEnergy = p.RunEnergy,
                Stance = p.Stance,
                AutoRetaliate = p.AutoRetaliate,
                AutocastSpell = p.AutocastSpell,
                PlayTimeSeconds = p.PlayTimeSeconds,
                BankCapacity = p.Bank.Capacity,
            };
            foreach (var s in Skills.All) d.SkillXpTenths[s.ToString()] = p.Skills.XpTenths(s);

            var inv = p.Inventory.Clone();
            if (p.Trade != null)
                foreach (var esc in p.Trade.OfferOf(p).NonEmpty())
                    if (!inv.Add(esc.ItemId, esc.Quantity, false))
                    {
                        // can't happen in practice (escrow came from inventory) - fall back to the bank
                        p.Bank.Add(esc.ItemId, esc.Quantity, false);
                    }
            for (int i = 0; i < inv.Capacity; i++)
                if (!inv[i].IsEmpty) d.Inventory.Add(new SlotData { Slot = i, Item = inv[i].ItemId, Qty = inv[i].Quantity });
            for (int i = 0; i < EquipSlots.Count; i++)
            {
                var s = p.Equipment[(EquipSlot)i];
                if (!s.IsEmpty) d.Equipment.Add(new SlotData { Slot = i, Item = s.ItemId, Qty = s.Quantity });
            }
            for (int i = 0; i < p.Bank.Capacity; i++)
                if (!p.Bank[i].IsEmpty) d.Bank.Add(new SlotData { Slot = i, Item = p.Bank[i].ItemId, Qty = p.Bank[i].Quantity });
            foreach (var q in p.Quests.Quests.Values)
                d.Quests.Add(new QuestData { Id = q.QuestId, Stage = q.Stage, Counters = (int[])q.Counters.Clone(), Completed = q.Completed });
            foreach (var f in p.Farming.Patches.Values)
                d.Farming.Add(new PatchData { Id = f.PatchId, Status = f.Status, Crop = f.Crop, PlantedAt = f.PlantedAtUnix, Watered = f.WateredMask, HarvestsLeft = f.HarvestsLeft });
            foreach (var kv in p.Settings)
                if (!kv.Key.StartsWith("_")) d.Settings[kv.Key] = kv.Value;
            return d;
        }

        /// <summary>Rebuilds a player from saved data. Unknown items (removed from the game) are dropped safely.</summary>
        public static Player Restore(GameWorld world, CharacterData d)
        {
            var db = world.Db;
            var p = world.NewPlayerShell(d.Name);
            p.CharacterId = d.Id;
            p.AccountId = d.AccountId;
            p.Appearance = d.Appearance ?? new Appearance();
            p.Appearance.Sanitize();
            p.Position = new Vec2(d.X, d.Z);
            p.PrayerPoints = d.PrayerPoints;
            p.RunEnergy = d.RunEnergy;
            p.Stance = d.Stance;
            p.AutoRetaliate = d.AutoRetaliate;
            p.AutocastSpell = d.AutocastSpell;
            p.PlayTimeSeconds = d.PlayTimeSeconds;
            if (d.BankCapacity > 0 && d.BankCapacity != p.Bank.Capacity) p.Bank.Resize(Math.Max(d.BankCapacity, d.Bank.Count));
            if (d.SkillXpTenths != null)
                foreach (var kv in d.SkillXpTenths)
                    if (Enum.TryParse(kv.Key, out Skill s)) p.Skills.SetXpTenths(s, kv.Value);
            foreach (var s in d.Inventory)
                if (db.Item(s.Item) != null && s.Slot >= 0 && s.Slot < p.Inventory.Capacity && s.Qty > 0)
                    p.Inventory.SetSlot(s.Slot, new ItemStack(s.Item, s.Qty), false);
            foreach (var s in d.Equipment)
                if (db.Item(s.Item) != null && s.Slot >= 0 && s.Slot < EquipSlots.Count && s.Qty > 0)
                    p.Equipment.SetSlot((EquipSlot)s.Slot, new ItemStack(s.Item, s.Qty), false);
            foreach (var s in d.Bank)
                if (db.Item(s.Item) != null && s.Slot >= 0 && s.Slot < p.Bank.Capacity && s.Qty > 0)
                    p.Bank.SetSlot(s.Slot, new ItemStack(s.Item, s.Qty), false);
            foreach (var q in d.Quests)
            {
                if (!db.Quests.TryGetValue(q.Id, out var qd)) continue;
                var prog = new QuestProgress { QuestId = q.Id, Stage = Math.Min(q.Stage, qd.Stages.Count), Completed = q.Completed };
                int n = prog.Completed || prog.Stage >= qd.Stages.Count ? 0 : qd.Stages[prog.Stage].Objectives.Count;
                prog.Counters = new int[n];
                if (q.Counters != null) Array.Copy(q.Counters, prog.Counters, Math.Min(n, q.Counters.Length));
                if (prog.Stage >= qd.Stages.Count) prog.Completed = true;
                p.Quests.Quests[q.Id] = prog;
            }
            foreach (var f in d.Farming)
                p.Farming.Patches[f.Id] = new PatchState { PatchId = f.Id, Status = f.Status, Crop = f.Crop, PlantedAtUnix = f.PlantedAt, WateredMask = f.Watered, HarvestsLeft = f.HarvestsLeft };
            if (d.Settings != null) foreach (var kv in d.Settings) p.Settings[kv.Key] = kv.Value;
            p.Hp = Math.Max(1, Math.Min(d.Hp <= 0 ? p.MaxHp : d.Hp, p.MaxHp));
            return p;
        }
    }

    /// <summary>Persistent world state: resource nodes, shop stock, stalls, doors.</summary>
    public sealed class WorldStateData
    {
        public long SavedAtUnix;
        public Dictionary<int, int> DepletedNodes = new Dictionary<int, int>();      // node id -> ticks remaining
        public Dictionary<string, Dictionary<string, int>> ShopStock = new Dictionary<string, Dictionary<string, int>>();
        public Dictionary<int, int> EmptyStalls = new Dictionary<int, int>();
        public List<int> OpenDoors = new List<int>();

        public static WorldStateData Capture(GameWorld w)
        {
            var d = new WorldStateData { SavedAtUnix = w.Clock() };
            foreach (var n in w.Nodes) if (n.Depleted) d.DepletedNodes[n.Id] = Math.Max(1, n.DepletedUntilTick - w.Tick);
            foreach (var s in w.Shops) d.ShopStock[s.Key] = new Dictionary<string, int>(s.Value.Stock);
            foreach (var e in w.AllEntities)
                if (e is Stall st && st.EmptyUntilTick > 0) d.EmptyStalls[st.Id] = st.EmptyUntilTick - w.Tick;
            foreach (var door in w.Doors) if (door.Open) d.OpenDoors.Add(door.Id);
            return d;
        }

        public void Apply(GameWorld w)
        {
            long elapsedTicks = Math.Max(0, (w.Clock() - SavedAtUnix) * 2);
            foreach (var kv in DepletedNodes)
            {
                var n = w.Get<ResourceNode>(kv.Key);
                if (n == null) continue;
                long remain = kv.Value - elapsedTicks;
                if (remain > 0) { n.DepletedUntilTick = w.Tick + (int)remain; n.MarkStateChanged(); }
            }
            foreach (var kv in ShopStock)
            {
                if (!w.Shops.TryGetValue(kv.Key, out var shop)) continue;
                foreach (var st in kv.Value)
                {
                    if (w.Db.Item(st.Key) == null) continue;
                    if (!shop.Stock.ContainsKey(st.Key)) shop.Order.Add(st.Key);
                    shop.Stock[st.Key] = Math.Max(0, st.Value);
                }
            }
            foreach (var kv in EmptyStalls)
            {
                var s = w.Get<Stall>(kv.Key);
                if (s != null && kv.Value - elapsedTicks > 0) s.EmptyUntilTick = w.Tick + (int)(kv.Value - elapsedTicks);
            }
        }
    }

    /// <summary>Storage abstraction implemented by the server (SQLite) and the local host (JSON files).</summary>
    public interface IGameStore
    {
        /// <summary>Returns account id, or 0 if the name is taken.</summary>
        long CreateAccount(string username, string passwordHash, string salt);
        bool TryGetAccount(string username, out long accountId, out string passwordHash, out string salt);
        List<CharacterData> ListCharacters(long accountId);
        /// <summary>Returns the new character id, or 0 if the name is taken.</summary>
        long CreateCharacter(long accountId, CharacterData data);
        CharacterData LoadCharacter(long characterId);
        /// <summary>Atomically saves several characters (used after trades to prevent duplication).</summary>
        void SaveCharacters(IEnumerable<CharacterData> characters);
        WorldStateData LoadWorldState();
        void SaveWorldState(WorldStateData state);
        void AppendTradeLog(string fromName, string toName, string itemsJson);
    }
}
