using System;
using System.Collections.Generic;
using Embervale.Shared.Data;

namespace Embervale.Shared.Simulation
{
    // ----------------------------------------------------------------------
    // Quests
    // ----------------------------------------------------------------------
    public sealed class QuestProgress
    {
        public string QuestId;
        public int Stage;              // index into QuestDef.Stages; == Stages.Count when complete
        public int[] Counters = new int[0];
        public bool Completed;
    }

    /// <summary>Per-character quest state and objective tracking.</summary>
    public sealed class QuestLog
    {
        private readonly GameDatabase _db;
        public readonly Dictionary<string, QuestProgress> Quests = new Dictionary<string, QuestProgress>();

        public QuestLog(GameDatabase db) { _db = db; }

        public int QuestPoints
        {
            get
            {
                int qp = 0;
                foreach (var q in Quests.Values)
                    if (q.Completed && _db.Quests.TryGetValue(q.QuestId, out var def)) qp += def.QuestPoints;
                return qp;
            }
        }

        public bool IsStarted(string id) => Quests.ContainsKey(id);
        public bool IsComplete(string id) => Quests.TryGetValue(id, out var q) && q.Completed;

        public string CurrentStageId(string id)
        {
            if (!Quests.TryGetValue(id, out var q) || q.Completed) return null;
            var def = _db.Quests[id];
            return q.Stage < def.Stages.Count ? def.Stages[q.Stage].Id : null;
        }

        public bool CanStart(string id, SkillSet skills, out string reason)
        {
            reason = null;
            if (!_db.Quests.TryGetValue(id, out var def)) { reason = "Unknown quest."; return false; }
            if (IsStarted(id)) { reason = "You have already started this quest."; return false; }
            foreach (var r in def.Requirements.Skills)
                if (skills.Level(r.Skill) < r.Level) { reason = "You need level " + r.Level + " " + r.Skill + "."; return false; }
            foreach (var q in def.Requirements.Quests)
                if (!IsComplete(q)) { reason = "You must first complete " + (_db.Quests.TryGetValue(q, out var qd) ? qd.Name : q) + "."; return false; }
            return true;
        }

        public QuestProgress Start(string id)
        {
            var def = _db.Quests[id];
            var p = new QuestProgress { QuestId = id, Stage = 0, Counters = new int[def.Stages[0].Objectives.Count] };
            Quests[id] = p;
            return p;
        }

        public void SetStage(QuestProgress p, int stage)
        {
            var def = _db.Quests[p.QuestId];
            p.Stage = stage;
            if (stage >= def.Stages.Count)
            {
                p.Completed = true;
                p.Counters = new int[0];
            }
            else p.Counters = new int[def.Stages[stage].Objectives.Count];
        }

        /// <summary>Increments matching counters for active quests. Returns true if anything changed.</summary>
        public bool Record(ObjectiveType type, string target, int amount)
        {
            bool changed = false;
            foreach (var p in Quests.Values)
            {
                if (p.Completed) continue;
                var stage = _db.Quests[p.QuestId].Stages[p.Stage];
                for (int i = 0; i < stage.Objectives.Count; i++)
                {
                    var o = stage.Objectives[i];
                    if (o.Type != type || o.Target != target) continue;
                    if (p.Counters[i] >= o.Count) continue;
                    p.Counters[i] = Math.Min(o.Count, p.Counters[i] + amount);
                    changed = true;
                }
            }
            return changed;
        }

        public bool ObjectiveMet(QuestProgress p, int index, ItemContainer inventory)
        {
            var o = _db.Quests[p.QuestId].Stages[p.Stage].Objectives[index];
            switch (o.Type)
            {
                case ObjectiveType.Collect: return inventory.Count(o.Target) >= o.Count;
                case ObjectiveType.Talk: return false;   // completed via dialogue
                default: return p.Counters[index] >= o.Count;
            }
        }

        /// <summary>True when every non-Talk objective is satisfied and the stage has no Talk objective.</summary>
        public bool StageAutoComplete(QuestProgress p, ItemContainer inventory)
        {
            var stage = _db.Quests[p.QuestId].Stages[p.Stage];
            for (int i = 0; i < stage.Objectives.Count; i++)
            {
                if (stage.Objectives[i].Type == ObjectiveType.Talk) return false;
                if (!ObjectiveMet(p, i, inventory)) return false;
            }
            return stage.Objectives.Count > 0;
        }
    }

    // ----------------------------------------------------------------------
    // Farming: per-character patches, timed against wall-clock so crops grow offline
    // ----------------------------------------------------------------------
    public enum PatchStatus : byte { Weeds, Empty, Growing, Ready, Dead }

    public sealed class PatchState
    {
        public int PatchId;
        public PatchStatus Status = PatchStatus.Weeds;
        public string Crop;
        public long PlantedAtUnix;
        public int WateredMask;          // bit per stage
        public int HarvestsLeft;
    }

    public sealed class FarmingState
    {
        public readonly Dictionary<int, PatchState> Patches = new Dictionary<int, PatchState>();

        public PatchState Get(int patchId)
        {
            if (!Patches.TryGetValue(patchId, out var s)) Patches[patchId] = s = new PatchState { PatchId = patchId };
            return s;
        }
    }

    public static class FarmingRules
    {
        /// <summary>Seconds per growth stage at the standard tick length.</summary>
        public static long StageSeconds(CropDef c) => (long)Math.Max(1, c.TicksPerStage * GameTime.TickSeconds);

        /// <summary>
        /// Advances a growing patch to the current time. Each completed stage rolls for failure
        /// deterministically (seeded by patch + plant time + stage) using the watered chance if the
        /// stage was watered.
        /// </summary>
        public static int CurrentStage(PatchState s, CropDef crop, long nowUnix, out bool died)
        {
            died = false;
            if (s.Status != PatchStatus.Growing || crop == null) return 0;
            long elapsed = Math.Max(0, nowUnix - s.PlantedAtUnix);
            int stagesDone = (int)Math.Min(crop.Stages, elapsed / StageSeconds(crop));
            for (int st = 0; st < stagesDone; st++)
            {
                bool watered = (s.WateredMask & (1 << st)) != 0;
                double fail = watered ? crop.FailChanceWatered : crop.FailChanceUnwatered;
                var rng = new XorShiftRandom((ulong)(s.PatchId * 7919L + s.PlantedAtUnix * 31L + st));
                if (rng.Chance(fail)) { died = true; return st; }
            }
            return stagesDone;
        }

        /// <summary>Applies elapsed growth. Returns true if status changed.</summary>
        public static bool Update(PatchState s, GameDatabase db, long nowUnix)
        {
            if (s.Status != PatchStatus.Growing || s.Crop == null || !db.Crops.TryGetValue(s.Crop, out var crop)) return false;
            int stage = CurrentStage(s, crop, nowUnix, out bool died);
            if (died) { s.Status = PatchStatus.Dead; return true; }
            if (stage >= crop.Stages) { s.Status = PatchStatus.Ready; return true; }
            return false;
        }
    }

    // ----------------------------------------------------------------------
    // Trading
    // ----------------------------------------------------------------------
    public enum TradeStage : byte { Offer, Confirm }

    /// <summary>
    /// Two-party escrow trade. Offered items are removed from the owner's inventory into the
    /// session; on cancel they are returned, on completion swapped atomically.
    /// </summary>
    public sealed class TradeSession
    {
        public Player A, B;
        public ItemContainer OfferA, OfferB;
        public bool AcceptedA, AcceptedB;
        public TradeStage Stage = TradeStage.Offer;
        public int Version;

        public ItemContainer OfferOf(Player p) => p == A ? OfferA : OfferB;
        public Player Other(Player p) => p == A ? B : A;

        public void ResetAcceptance()
        {
            AcceptedA = AcceptedB = false;
            Stage = TradeStage.Offer;
            Version++;
        }
    }

    // ----------------------------------------------------------------------
    // Parties
    // ----------------------------------------------------------------------
    public sealed class Party
    {
        public int Id;
        public Player Leader;
        public readonly List<Player> Members = new List<Player>();
        public readonly HashSet<long> Invites = new HashSet<long>();
        public const int MaxSize = 5;
    }

    // ----------------------------------------------------------------------
    // Shops
    // ----------------------------------------------------------------------
    public sealed class ShopState
    {
        public ShopDef Def;
        public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();
        public readonly Dictionary<string, int> BaseStock = new Dictionary<string, int>();
        public readonly List<string> Order = new List<string>();
        public int NextRestockTick;
        public int Version;

        public ShopState(ShopDef def)
        {
            Def = def;
            foreach (var s in def.Stock)
            {
                Stock[s.Item] = s.Qty;
                BaseStock[s.Item] = s.Qty;
                Order.Add(s.Item);
            }
        }

        public int StockOf(string item) => Stock.TryGetValue(item, out var n) ? n : 0;

        /// <summary>Price to buy one unit from the shop. Scarcity raises the price.</summary>
        public int BuyPrice(ItemDef item)
        {
            int baseStock = BaseStock.TryGetValue(item.Id, out var b) ? Math.Max(1, b) : 1;
            double scarcity = 1.0 + 0.3 * Math.Max(0, 1.0 - (double)StockOf(item.Id) / baseStock);
            return Math.Max(1, (int)Math.Ceiling(item.Value * Def.BuyMultiplier * scarcity));
        }

        /// <summary>Price the shop pays for one unit. Oversupply lowers the price.</summary>
        public int SellPrice(ItemDef item)
        {
            int baseStock = BaseStock.TryGetValue(item.Id, out var b) ? b : 0;
            int over = Math.Max(0, StockOf(item.Id) - baseStock);
            double glut = Math.Max(0.3, 1.0 - over * 0.03);
            return Math.Max(0, (int)Math.Floor(item.Value * Def.SellMultiplier * glut));
        }

        public bool WillBuy(ItemDef item)
        {
            if (item == null || !item.Tradeable || item.Id == "coins") return false;
            return Def.GeneralStore || BaseStock.ContainsKey(item.Id);
        }

        /// <summary>Moves stock one step toward base levels. Returns true if changed.</summary>
        public bool Restock()
        {
            bool changed = false;
            var keys = new List<string>(Stock.Keys);
            foreach (var k in keys)
            {
                int baseQty = BaseStock.TryGetValue(k, out var b) ? b : 0;
                int cur = Stock[k];
                if (cur < baseQty) { Stock[k] = cur + Math.Max(1, baseQty / 10); if (Stock[k] > baseQty) Stock[k] = baseQty; changed = true; }
                else if (cur > baseQty) { Stock[k] = cur - 1; changed = true; }
                if (Stock[k] == 0 && baseQty == 0) { Stock.Remove(k); Order.Remove(k); }
            }
            if (changed) Version++;
            return changed;
        }
    }
}
