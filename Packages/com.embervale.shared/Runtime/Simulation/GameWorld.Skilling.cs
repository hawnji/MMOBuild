using System;
using System.Collections.Generic;
using Embervale.Shared.Data;

namespace Embervale.Shared.Simulation
{
    public sealed class GatherActivity : PlayerActivity
    {
        public ResourceNode Node;
        public int NextAttempt;

        public override bool Tick(GameWorld w, Player p) => w.TickGathering(p, this);
    }

    public sealed class CraftActivity : PlayerActivity
    {
        public RecipeDef Recipe;
        public int Remaining;
        public int StationId;
        public int NextCraft;

        public override bool Tick(GameWorld w, Player p) => w.TickCrafting(p, this);
    }

    public sealed class AgilityActivity : PlayerActivity
    {
        public Obstacle Obstacle;
        public int FinishTick;

        public override bool Tick(GameWorld w, Player p) => w.TickAgility(p, this);
    }

    public sealed partial class GameWorld
    {
        // ------------------------------------------------------------------
        // Gathering (woodcutting, mining, fishing)
        // ------------------------------------------------------------------
        public const int GatherIntervalTicks = 4;

        public bool CanGather(Player p, ResourceNode node, bool notify, out ItemDef tool)
        {
            tool = null;
            var def = node.Def;
            if (node.Depleted) { if (notify) Message(p, "There's nothing left to gather here right now."); return false; }
            int level = p.Effective(def.Skill);
            if (level < def.MinLevel) { if (notify) Message(p, "You need level " + def.MinLevel + " " + def.Skill + " to do that."); return false; }
            if (def.ToolType == "Spade" || def.RequiresItem != null)
            {
                if (def.RequiresItem != null && !p.Inventory.Contains(def.RequiresItem)) { if (notify) Message(p, "You need a " + (Db.Item(def.RequiresItem)?.Name ?? def.RequiresItem).ToLowerInvariant() + "."); return false; }
            }
            else
            {
                tool = BestTool(p, def.ToolType, def.Skill);
                if (tool == null) { if (notify) Message(p, "You need a " + ToolName(def.ToolType) + " that you can use."); return false; }
            }
            if (def.Bait != null && !p.Inventory.Contains(def.Bait)) { if (notify) Message(p, "You don't have any bait."); return false; }
            if (p.Inventory.FreeSlots == 0 && !AllYieldsStackInInventory(p, def)) { if (notify) Message(p, "Your inventory is too full to hold any more."); return false; }
            return true;
        }

        private bool AllYieldsStackInInventory(Player p, NodeDef def)
        {
            foreach (var y in def.Yields) if (!(Db.IsStackable(y.Item) && p.Inventory.Contains(y.Item))) return false;
            return true;
        }

        private static string ToolName(string toolType)
        {
            switch (toolType)
            {
                case "Pickaxe": return "pickaxe";
                case "Hatchet": return "hatchet";
                case "FishingRod": return "fishing rod";
                case "FishingNet": return "fishing net";
                case "Harpoon": return "harpoon";
                default: return toolType.ToLowerInvariant();
            }
        }

        private static ActionAnim GatherAnim(Skill s) =>
            s == Skill.Mining ? ActionAnim.Mine : s == Skill.Woodcutting ? ActionAnim.Chop : s == Skill.Fishing ? ActionAnim.Fish : ActionAnim.Farm;

        private void StartGathering(Player p, ResourceNode node)
        {
            if (!CanGather(p, node, true, out _)) return;
            Message(p, node.Def.Skill == Skill.Fishing ? "You attempt to catch a fish." :
                       node.Def.Skill == Skill.Woodcutting ? "You swing your hatchet at the tree." : "You swing your pickaxe at the rock.");
            StartActivity(p, new GatherActivity { Node = node, NextAttempt = Tick + GatherIntervalTicks });
            Animate(p, GatherAnim(node.Def.Skill), GatherIntervalTicks);
        }

        internal bool TickGathering(Player p, GatherActivity a)
        {
            var node = a.Node;
            if (node.Removed || Vec2.TileDistance(p.Position, node.Position) > node.InteractRange) return false;
            if (!CanGather(p, node, true, out var tool)) return false;
            if (p.Anim != GatherAnim(node.Def.Skill) || Tick >= p.AnimUntilTick) Animate(p, GatherAnim(node.Def.Skill), GatherIntervalTicks);
            if (Tick < a.NextAttempt) return true;
            a.NextAttempt = Tick + GatherIntervalTicks;

            var def = node.Def;
            int level = p.Effective(def.Skill);
            // choose the best yield the player can obtain, weighted
            var eligible = new List<NodeYield>();
            int total = 0;
            foreach (var y in def.Yields) if (level >= y.Level) { eligible.Add(y); total += Math.Max(1, y.Weight); }
            if (eligible.Count == 0) return false;
            int roll = Rng.Next(0, total - 1);
            NodeYield pick = eligible[0];
            foreach (var y in eligible) { roll -= Math.Max(1, y.Weight); if (roll < 0) { pick = y; break; } }

            double chance = GatheringFormulas.SuccessChance(def.BaseChance, level, pick.Level, tool?.ToolPower ?? 1f);
            if (!Rng.Chance(chance)) return true;

            if (!p.Inventory.Add(pick.Item, 1)) { Message(p, "Your inventory is too full to hold any more."); return false; }
            if (def.Bait != null) p.Inventory.Remove(def.Bait, 1);
            GiveXp(p, def.Skill, pick.Xp);
            var itemDef = Db.Item(pick.Item);
            Message(p, "You get some " + (itemDef?.Name ?? pick.Item).ToLowerInvariant() + ".");
            Emit(new WorldEvent { Type = WorldEventType.Sound, EntityId = p.Id, Text = def.Skill.ToString().ToLowerInvariant() + "_success" });
            if (p.Quests.Record(ObjectiveType.Gather, pick.Item, 1)) { p.QuestsDirty = true; }
            EvaluateQuests(p);

            if (def.GemChance > 0 && Rng.Chance(def.GemChance))
            {
                var gems = new[] { "uncut_amber", "uncut_jade", "uncut_garnet", "uncut_sapphire" };
                var gem = gems[Rng.Next(0, gems.Length - 1)];
                GiveOrDrop(p, gem, 1);
                Message(p, "You find a gem!");
            }

            if (def.DepleteChance > 0 && Rng.Chance(def.DepleteChance))
            {
                node.DepletedUntilTick = Tick + Math.Max(1, def.RespawnTicks);
                node.MarkStateChanged();
                // everyone gathering this node stops (server-side contention)
                foreach (var other in _players)
                    if (other.Activity is GatherActivity ga && ga.Node == node && other != p)
                    {
                        Message(other, "Someone else got the last of it.");
                        StopActivity(other);
                    }
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Crafting (smithing, cooking, fletching, crafting, herblore, runecrafting, firemaking)
        // ------------------------------------------------------------------
        public string StationTypeOf(Entity e)
        {
            switch (e)
            {
                case Station s: return s.StationType;
                case Fire _: return "Fire";
                default: return null;
            }
        }

        /// <summary>Checks every requirement of a recipe. Returns null if OK, else the failure message.</summary>
        public string CheckRecipe(Player p, RecipeDef r, Entity station)
        {
            if (p.Effective(r.Skill) < r.Level) return "You need level " + r.Level + " " + r.Skill + " to make that.";
            if (r.Station != null)
            {
                if (station == null || !r.StationMatches(StationTypeOf(station))) return "You need to be at a " + StationLabel(r.Station) + ".";
                if (Vec2.TileDistance(p.Position, station.Position) > 2) return "You're too far away.";
            }
            foreach (var t in r.Tools)
                if (!HasItemOrEquipped(p, t)) return "You need a " + (Db.Item(t)?.Name ?? t).ToLowerInvariant() + ".";
            foreach (var i in r.Inputs)
                if (!p.Inventory.Contains(i.Item, i.Qty)) return "You don't have the materials to make that.";
            var outputs = new List<ItemStack>();
            foreach (var o in r.Outputs) outputs.Add(new ItemStack(o.Item, o.Qty));
            var inputs = new List<ItemStack>();
            foreach (var i in r.Inputs) inputs.Add(new ItemStack(i.Item, i.Qty));
            if (!p.Inventory.CanAddAll(outputs, inputs)) return "You don't have enough inventory space.";
            if (r.SpawnsFire && (StationAt(p.Position.TileX, p.Position.TileZ) != null || FireAt(p.Position.TileX, p.Position.TileZ) != null))
                return "You can't light a fire here.";
            return null;
        }

        private static string StationLabel(string s) =>
            s.StartsWith("Altar_") ? s.Substring(6) + " altar" : s == "Range" ? "cooking range" : s.ToLowerInvariant();

        public void RequestCraft(Player p, string recipeId, int count, int stationEntityId)
        {
            if (!ValidateActor(p) || p.Trade != null) return;
            if (!Db.Recipes.TryGetValue(recipeId, out var r)) return;
            var station = Get(stationEntityId);
            var err = CheckRecipe(p, r, station);
            if (err != null) { Message(p, err); return; }
            count = Math.Max(1, Math.Min(count, 1000));
            p.Pending = null;
            p.Path.Clear();
            StartActivity(p, new CraftActivity { Recipe = r, Remaining = count, StationId = stationEntityId, NextCraft = Tick + 1 });
            Animate(p, CraftAnim(r), r.Ticks);
        }

        private static ActionAnim CraftAnim(RecipeDef r)
        {
            switch (r.Skill)
            {
                case Skill.Smithing: return r.Station == "Anvil" ? ActionAnim.Smith : ActionAnim.Cook;
                case Skill.Cooking: return ActionAnim.Cook;
                case Skill.Firemaking: return ActionAnim.Firemaking;
                case Skill.Runecrafting: return ActionAnim.CastSpell;
                default: return ActionAnim.Craft;
            }
        }

        internal bool TickCrafting(Player p, CraftActivity a)
        {
            if (Tick < a.NextCraft) return true;
            var r = a.Recipe;
            var station = Get(a.StationId);
            var err = CheckRecipe(p, r, station);
            if (err != null)
            {
                if (a.Remaining > 0 && a.StartedTick != Tick - 1) Message(p, err);
                return false;
            }
            int batches = 1;
            if (r.Bulk)
            {
                // e.g. runecrafting binds all essence at once
                batches = int.MaxValue;
                foreach (var i in r.Inputs) batches = Math.Min(batches, p.Inventory.Count(i.Item) / Math.Max(1, i.Qty));
                batches = Math.Max(1, batches);
            }
            for (int b = 0; b < batches; b++)
            {
                foreach (var i in r.Inputs) p.Inventory.Remove(i.Item, i.Qty);
                double fail = GatheringFormulas.FailChance(r, p.Effective(r.Skill));
                if (fail > 0 && Rng.Chance(fail))
                {
                    p.Inventory.Add(r.FailOutput, 1);
                    Message(p, "You accidentally burn it.");
                    continue;
                }
                foreach (var o in r.Outputs)
                {
                    p.Inventory.Add(o.Item, o.Qty);
                    if (p.Quests.Record(ObjectiveType.Craft, o.Item, o.Qty)) p.QuestsDirty = true;
                }
                GiveXp(p, r.Skill, r.Xp);
            }
            if (r.SpawnsFire) LightFire(p);
            else
            {
                var outDef = Db.Item(r.Outputs.Count > 0 ? r.Outputs[0].Item : null);
                if (outDef != null && !r.Bulk) Message(p, "You make " + Article(outDef.Name) + ".");
                else if (r.Bulk) Message(p, "You bind the essence into runes.");
            }
            Emit(new WorldEvent { Type = WorldEventType.Sound, EntityId = p.Id, Text = r.Skill.ToString().ToLowerInvariant() });
            EvaluateQuests(p);
            a.Remaining -= r.Bulk ? a.Remaining : 1;
            a.NextCraft = Tick + Math.Max(1, r.Ticks);
            if (a.Remaining > 0) Animate(p, CraftAnim(r), r.Ticks);
            return a.Remaining > 0 && !r.SpawnsFire;
        }

        public static string Article(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            char c = char.ToLowerInvariant(name[0]);
            return ("aeiou".IndexOf(c) >= 0 ? "an " : "a ") + name.ToLowerInvariant();
        }

        public Station StationAt(int x, int z)
        {
            var tmp = new List<Entity>();
            QueryArea(Vec2.TileCenter(x, z), 1f, tmp);
            foreach (var e in tmp) if (e is Station s && s.Position.TileX == x && s.Position.TileZ == z) return s;
            return null;
        }

        public Fire FireAt(int x, int z)
        {
            foreach (var f in _fires) if (f.Position.TileX == x && f.Position.TileZ == z) return f;
            return null;
        }

        private void LightFire(Player p)
        {
            var fire = Register(new Fire { DefId = "fire", Position = Vec2.TileCenter(p.Position.TileX, p.Position.TileZ), ExpiresTick = Tick + 120 });
            Grid.AddBlocker(fire.Position.TileX, fire.Position.TileZ);
            Message(p, "The fire catches and the logs begin to burn.");
            // step aside, preferring west, like stepping back from a campfire
            int[,] dirs = { { -1, 0 }, { 1, 0 }, { 0, -1 }, { 0, 1 } };
            for (int i = 0; i < 4; i++)
            {
                int nx = p.Position.TileX + dirs[i, 0], nz = p.Position.TileZ + dirs[i, 1];
                if (Grid.IsWalkable(nx, nz)) { p.Path.Clear(); p.Path.Add(Vec2.TileCenter(nx, nz)); break; }
            }
        }

        private void UseStation(Player p, Station st)
        {
            switch (st.StationType)
            {
                case "PrayerAltar":
                    p.PrayerPoints = p.Skills.Level(Skill.Prayer);
                    p.StatsDirty = true;
                    Animate(p, ActionAnim.Pray, 3);
                    Message(p, "You pray at the altar. Your prayer points are restored.");
                    return;
                case "Water":
                    if (p.Inventory.Contains("vial")) RequestCraft(p, "fill_vial", 28, st.Id);
                    else if (p.Inventory.Contains("bucket")) RequestCraft(p, "fill_bucket", 28, st.Id);
                    else Message(p, "Fresh, clean water. Useful for filling vials and buckets.");
                    return;
            }
            if (st.StationType.StartsWith("Altar_"))
            {
                var rune = st.StationType.Substring(6);
                if (Db.Recipes.TryGetValue("bind_" + rune, out var r)) { RequestCraft(p, r.Id, int.MaxValue, st.Id); return; }
            }
            Emit(new WorldEvent { Type = WorldEventType.CraftMenu, PlayerId = p.Id, EntityId = st.Id, Text = st.StationType });
        }

        // ------------------------------------------------------------------
        // Farming
        // ------------------------------------------------------------------
        private void FarmAction(Player p, FarmPatch patch, string option)
        {
            var s = p.Farming.Get(patch.Id);
            FarmingRules.Update(s, Db, Clock());
            switch (s.Status)
            {
                case PatchStatus.Weeds:
                    if (!p.Inventory.Contains("rake")) { Message(p, "You need a rake to clear the weeds."); return; }
                    s.Status = PatchStatus.Empty;
                    GiveXp(p, Skill.Farming, 4);
                    Animate(p, ActionAnim.Farm, 4);
                    Message(p, "You rake the weeds out of the patch.");
                    break;
                case PatchStatus.Empty:
                    Message(p, "The patch is ready for planting. Use a seed on it.");
                    break;
                case PatchStatus.Growing:
                    var crop = Db.Crops[s.Crop];
                    int stage = FarmingRules.CurrentStage(s, crop, Clock(), out _);
                    bool watered = (s.WateredMask & (1 << stage)) != 0;
                    Message(p, crop.Name + " is growing (stage " + (stage + 1) + " of " + crop.Stages + ")" + (watered ? " and is well watered." : ". It could use some water."));
                    break;
                case PatchStatus.Ready:
                    Harvest(p, s);
                    break;
                case PatchStatus.Dead:
                    if (!p.Inventory.Contains("spade")) { Message(p, "The crop has withered. You need a spade to clear it."); return; }
                    s.Status = PatchStatus.Empty; s.Crop = null;
                    Animate(p, ActionAnim.Farm, 3);
                    Message(p, "You dig up the withered crop.");
                    break;
            }
            p.FarmingDirty = true;
        }

        private void Harvest(Player p, PatchState s)
        {
            var crop = Db.Crops[s.Crop];
            if (!p.Inventory.Contains("spade")) { Message(p, "You need a spade to harvest."); return; }
            if (s.HarvestsLeft <= 0) s.HarvestsLeft = Rng.Next(crop.YieldMin, crop.YieldMax);
            int got = 0;
            while (s.HarvestsLeft > 0 && p.Inventory.Add(crop.Produce, 1))
            {
                s.HarvestsLeft--;
                got++;
                GiveXp(p, Skill.Farming, crop.HarvestXp);
            }
            if (got > 0)
            {
                Animate(p, ActionAnim.Farm, 3);
                if (p.Quests.Record(ObjectiveType.Gather, crop.Produce, got)) p.QuestsDirty = true;
                Message(p, "You harvest " + got + " " + crop.Name.ToLowerInvariant() + (got == 1 ? "." : "s."));
            }
            if (s.HarvestsLeft <= 0) { s.Status = PatchStatus.Empty; s.Crop = null; s.WateredMask = 0; }
            else Message(p, "Your inventory is full.");
            EvaluateQuests(p);
        }

        private void FarmUseItem(Player p, FarmPatch patch, int slot)
        {
            var stack = p.Inventory[slot];
            var s = p.Farming.Get(patch.Id);
            FarmingRules.Update(s, Db, Clock());
            if (stack.ItemId == "watering_can")
            {
                if (s.Status != PatchStatus.Growing) { Message(p, "There's nothing here to water."); return; }
                var crop = Db.Crops[s.Crop];
                int stage = FarmingRules.CurrentStage(s, crop, Clock(), out _);
                s.WateredMask |= 1 << stage;
                Animate(p, ActionAnim.Farm, 3);
                Message(p, "You water the crop.");
                p.FarmingDirty = true;
                return;
            }
            CropDef seedCrop = null;
            foreach (var c in Db.Crops.Values) if (c.Seed == stack.ItemId) seedCrop = c;
            if (seedCrop == null) { Message(p, "Nothing interesting happens."); return; }
            if (s.Status == PatchStatus.Weeds) { Message(p, "You need to rake the weeds first."); return; }
            if (s.Status != PatchStatus.Empty) { Message(p, "Something is already growing here."); return; }
            if (p.Effective(Skill.Farming) < seedCrop.Level) { Message(p, "You need level " + seedCrop.Level + " Farming to plant that."); return; }
            int qty = 1;
            if (!p.Inventory.Remove(stack.ItemId, qty)) return;
            s.Status = PatchStatus.Growing;
            s.Crop = seedCrop.Id;
            s.PlantedAtUnix = Clock();
            s.WateredMask = 0;
            s.HarvestsLeft = 0;
            GiveXp(p, Skill.Farming, seedCrop.PlantXp);
            Animate(p, ActionAnim.Farm, 3);
            Message(p, "You plant the " + seedCrop.Name.ToLowerInvariant() + " seed.");
            p.FarmingDirty = true;
        }

        // ------------------------------------------------------------------
        // Thieving
        // ------------------------------------------------------------------
        public static double ThievingChance(int level, int required) =>
            Math.Max(0.1, Math.Min(0.95, 0.55 + (level - required) * 0.015));

        private void Pickpocket(Player p, Npc npc)
        {
            ThievingDef def = null;
            foreach (var t in Db.Thieving.Values) if (t.Targets != null && t.Targets.Contains(npc.DefId)) def = t;
            if (def == null) { Message(p, "You can't pickpocket them."); return; }
            if (p.Effective(Skill.Thieving) < def.Level) { Message(p, "You need level " + def.Level + " Thieving to pickpocket them."); return; }
            Animate(p, ActionAnim.Pickpocket, 2);
            if (Rng.Chance(ThievingChance(p.Effective(Skill.Thieving), def.Level)))
            {
                var pick = LootRoller.Weighted(def.Loot, 0, Rng);
                if (pick != null) GiveOrDrop(p, pick.Item, Rng.Next(pick.Min, pick.Max));
                GiveXp(p, Skill.Thieving, def.Xp);
                Message(p, "You pick the " + npc.Def.Name.ToLowerInvariant() + "'s pocket.");
            }
            else
            {
                Emit(new WorldEvent { Type = WorldEventType.Chat, EntityId = npc.Id, Text = "Hey! Get your hands off me!", Channel = ChatChannel.Local, Text2 = npc.Def.Name });
                p.StunnedUntil = Tick + def.StunTicks;
                p.Path.Clear();
                DamageActor(p, def.Damage, npc, false, false);
                Message(p, "You've been stunned!");
            }
        }

        private void StealFromStall(Player p, Stall stall)
        {
            var def = stall.Def;
            if (def == null) return;
            if (stall.EmptyUntilTick > 0) { Message(p, "The stall is empty."); return; }
            if (p.Effective(Skill.Thieving) < def.Level) { Message(p, "You need level " + def.Level + " Thieving to steal from this stall."); return; }
            // guards nearby notice failed attempts
            Animate(p, ActionAnim.Pickpocket, 2);
            if (!Rng.Chance(ThievingChance(p.Effective(Skill.Thieving), def.Level) + 0.15))
            {
                Message(p, "A guard spots you and you back away.");
                p.StunnedUntil = Tick + 4;
                return;
            }
            var pick = LootRoller.Weighted(def.Loot, 0, Rng);
            if (pick != null && !p.Inventory.Add(pick.Item, Rng.Next(pick.Min, pick.Max))) { Message(p, "You don't have enough inventory space."); return; }
            GiveXp(p, Skill.Thieving, def.Xp);
            stall.EmptyUntilTick = Tick + Math.Max(1, def.RespawnTicks);
            stall.MarkStateChanged();
            Message(p, "You steal from the stall.");
        }

        // ------------------------------------------------------------------
        // Agility
        // ------------------------------------------------------------------
        private void StartAgility(Player p, Obstacle ob)
        {
            if (p.Effective(Skill.Agility) < ob.Def.Level) { Message(p, "You need level " + ob.Def.Level + " Agility to attempt this."); return; }
            Animate(p, ActionAnim.Climb, ob.Def.Ticks);
            StartActivity(p, new AgilityActivity { Obstacle = ob, FinishTick = Tick + Math.Max(1, ob.Def.Ticks) });
        }

        internal bool TickAgility(Player p, AgilityActivity a)
        {
            if (Tick < a.FinishTick) return true;
            var ob = a.Obstacle;
            double fail = Math.Max(0, ob.Def.FailChance - (p.Effective(Skill.Agility) - ob.Def.Level) * 0.01);
            if (Rng.Chance(fail))
            {
                Message(p, "You slip and hurt yourself.");
                DamageActor(p, ob.Def.FailDamage, null, false, false);
                p.LastAgilityObstacle = -1;
                return false;
            }
            p.Position = ob.End;
            Spatial.Update(p, p.Position);
            p.MarkStateChanged();
            Emit(new WorldEvent { Type = WorldEventType.Teleport, EntityId = p.Id });
            GiveXp(p, Skill.Agility, ob.Def.Xp);
            if (ob.Order == p.LastAgilityObstacle + 1) p.LastAgilityObstacle = ob.Order; else p.LastAgilityObstacle = ob.Order == 0 ? 0 : -1;
            int maxOrder = 0;
            foreach (var o in Db.World?.Agility ?? new List<ObstaclePlacement>()) maxOrder = Math.Max(maxOrder, o.Order);
            if (p.LastAgilityObstacle == maxOrder && Db.Agility.TryGetValue("course_lap", out var lap))
            {
                GiveXp(p, Skill.Agility, lap.Xp);
                Message(p, "You complete a lap of the Greenleaf course!");
                p.LastAgilityObstacle = -1;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Prayer
        // ------------------------------------------------------------------
        public void RequestTogglePrayer(Player p, string prayerId)
        {
            if (!ValidateActor(p) || !Db.Prayers.TryGetValue(prayerId, out var def)) return;
            if (p.ActivePrayers.Contains(prayerId)) { p.ActivePrayers.Remove(prayerId); p.StatsDirty = true; return; }
            if (p.Skills.Level(Skill.Prayer) < def.Level) { Message(p, "You need level " + def.Level + " Prayer to use " + def.Name + "."); return; }
            if (p.PrayerPoints <= 0) { Message(p, "You have no prayer points left. Recharge at an altar."); return; }
            p.ActivePrayers.Add(prayerId);
            p.StatsDirty = true;
        }

        private void TickPrayer(Player p)
        {
            if (p.ActivePrayers.Count == 0) return;
            double drain = 0;
            foreach (var id in p.ActivePrayers) if (Db.Prayers.TryGetValue(id, out var d)) drain += d.DrainPerMinute / GameTime.TicksPerMinute;
            p.PrayerPoints = Math.Max(0, p.PrayerPoints - drain);
            if (p.PrayerPoints <= 0)
            {
                p.ActivePrayers.Clear();
                Message(p, "You have run out of prayer points.");
            }
            if (Tick % 4 == 0) p.StatsDirty = true;
        }

        public double PrayerBonus(Player p, string effect)
        {
            double best = 0;
            foreach (var id in p.ActivePrayers)
                if (Db.Prayers.TryGetValue(id, out var d) && d.Effect.TryGetValue(effect, out var v)) best = Math.Max(best, v);
            return best;
        }
    }
}
