using System;
using System.Collections.Generic;
using Embervale.Shared.Data;

namespace Embervale.Shared.Simulation
{
    /// <summary>A queued "walk to X then do Y" request.</summary>
    public sealed class PendingInteraction
    {
        public int TargetId;
        public string Option;
        public int ItemSlot = -1;        // for use-item-on-entity
        public string SpellId;           // for cast-on-entity
        public int Range = 1;
        public int RepathTick;
        public int FailedPaths;
    }

    /// <summary>A continuing activity (gathering, crafting, fighting...). Return false from Tick to stop.</summary>
    public abstract class PlayerActivity
    {
        public int StartedTick;
        public abstract bool Tick(GameWorld w, Player p);
        public virtual void OnStop(GameWorld w, Player p) { }
        public virtual bool IsCombat => false;
    }

    public sealed partial class GameWorld
    {
        // ------------------------------------------------------------------
        // Per-tick player update
        // ------------------------------------------------------------------
        private void TickPlayer(Player p)
        {
            if (p.IsDead)
            {
                if (Tick >= p.RespawnAtTick) RespawnPlayer(p);
                return;
            }

            // timers
            if (Tick % 2 == 0) p.PlayTimeSeconds++;
            if (p.PoisonDamage > 0 && Tick >= p.PoisonNextTick)
            {
                p.PoisonNextTick = Tick + 30;
                DamageActor(p, p.PoisonDamage, null, false, true);
                p.PoisonDamage = Math.Max(0, p.PoisonDamage - 1);
                if (p.IsDead) return;
            }
            if (Tick % 100 == 0 && p.Hp < p.MaxHp) { p.Hp++; p.MarkStateChanged(); }
            if (!p.Running && p.RunEnergy < 100 && Tick % 2 == 0) p.RunEnergy = Math.Min(100, p.RunEnergy + 0.5 + p.Skills.Level(Skill.Agility) / 100.0);
            for (int i = p.Buffs.Count - 1; i >= 0; i--)
                if (Tick >= p.Buffs[i].ExpiresTick) { p.Buffs.RemoveAt(i); p.StatsDirty = true; p.SkillsDirty = true; }
            TickPrayer(p);

            // queued interaction: walk until in range, then perform
            if (p.Pending != null) ProcessPending(p);

            bool moved = AdvanceMovement(p);
            if (moved)
            {
                p.MarkStateChanged();
                CheckVisitLocations(p);
                // walking away from an open interface closes it
                if (p.OpenBankEntityId != 0 || p.OpenShopId != null || p.DialogueId != null) CheckInterfaceDistance(p);
                if (p.Trade != null && Vec2.TileDistance(p.Position, p.Trade.Other(p).Position) > 12) CancelTrade(p, "You walked too far away.");
            }

            // continuing activity
            if (p.Activity != null && p.Path.Count == 0 && p.Pending == null)
            {
                bool keep;
                try { keep = p.Activity.Tick(this, p); }
                catch (Exception) { keep = false; }
                if (!keep) StopActivity(p);
            }
            else if (p.Activity != null && p.Activity.IsCombat)
            {
                // combat keeps chasing while moving
                if (!p.Activity.Tick(this, p)) StopActivity(p);
            }
        }

        private void CheckInterfaceDistance(Player p)
        {
            var anchor = Get(p.OpenBankEntityId != 0 ? p.OpenBankEntityId : (p.OpenShopNpcId != 0 ? p.OpenShopNpcId : p.DialogueNpcId));
            if (anchor == null || Vec2.TileDistance(anchor.Position, p.Position) > 4) CloseInterfaces(p);
        }

        public void StopActivity(Player p)
        {
            if (p.Activity == null) return;
            var a = p.Activity;
            p.Activity = null;
            a.OnStop(this, p);
            if (p.Anim != ActionAnim.Idle && p.Anim != ActionAnim.Death) { p.Anim = ActionAnim.Idle; p.MarkStateChanged(); }
        }

        public void StartActivity(Player p, PlayerActivity a)
        {
            StopActivity(p);
            a.StartedTick = Tick;
            p.Activity = a;
        }

        /// <summary>Cancels movement, queued interactions and activities (e.g. new click).</summary>
        public void CancelActions(Player p)
        {
            p.Pending = null;
            p.Path.Clear();
            StopActivity(p);
            p.TargetId = 0;
        }

        private void RespawnPlayer(Player p)
        {
            p.Hp = p.MaxHp;
            p.PoisonDamage = 0;
            p.Buffs.Clear();
            p.ActivePrayers.Clear();
            p.PrayerPoints = p.Skills.Level(Skill.Prayer);
            p.Position = Location(Db.Starter.SpawnPoint);
            p.Anim = ActionAnim.Idle;
            Spatial.Update(p, p.Position);
            p.MarkStateChanged();
            p.StatsDirty = true;
            Emit(new WorldEvent { Type = WorldEventType.Teleport, EntityId = p.Id });
            Message(p, "You wake up in Brookhollow. Your belongings remain where you fell for a short while.");
        }

        // ------------------------------------------------------------------
        // Intents
        // ------------------------------------------------------------------
        public bool ValidateActor(Player p) => p != null && !p.Removed && !p.IsDead && p.StunnedUntil <= Tick;

        public void RequestMove(Player p, int x, int z, bool run = false)
        {
            if (!ValidateActor(p)) return;
            if (p.Trade != null) CancelTrade(p, "Trade cancelled.");
            CloseInterfaces(p);
            CancelActions(p);
            if (!Grid.InBounds(x, z)) return;
            var path = PlanPath(p.Position, x, z, 0, allowClosest: true, maxDistance: 64);
            if (path == null) { Message(p, "You can't reach that."); return; }
            p.Running = run && p.RunEnergy > 1;
            p.Path.AddRange(path);
        }

        public void RequestToggleRun(Player p, bool run)
        {
            p.Running = run && p.RunEnergy > 1;
            p.StatsDirty = true;
        }

        /// <summary>Click on an entity with a named option (Attack, Talk-to, Mine, Open...).</summary>
        public void RequestInteract(Player p, int entityId, string option)
        {
            if (!ValidateActor(p)) return;
            var e = Get(entityId);
            if (e == null || (e is GroundItem && !CanSee(p, e))) return;
            if (Vec2.TileDistance(e.Position, p.Position) > 40) return;
            if (p.Trade != null) CancelTrade(p, "Trade cancelled.");
            CloseInterfaces(p);
            CancelActions(p);
            if (option == "Examine") { Examine(p, e); return; }
            int range = e.InteractRange;
            if (option == "Attack")
            {
                StartCombat(p, e as Actor);
                return;
            }
            Queue(p, new PendingInteraction { TargetId = entityId, Option = option, Range = range });
        }

        public void RequestUseItemOnEntity(Player p, int slot, int entityId)
        {
            if (!ValidateActor(p) || p.Inventory[slot].IsEmpty) return;
            var e = Get(entityId);
            if (e == null) return;
            CloseInterfaces(p);
            CancelActions(p);
            Queue(p, new PendingInteraction { TargetId = entityId, Option = "UseItem", ItemSlot = slot, Range = e.InteractRange });
        }

        private void Queue(Player p, PendingInteraction pi)
        {
            p.Pending = pi;
            ProcessPending(p);
        }

        private void ProcessPending(Player p)
        {
            var pi = p.Pending;
            var target = Get(pi.TargetId);
            if (target == null || target.Removed) { p.Pending = null; p.Path.Clear(); return; }
            int dist = Vec2.TileDistance(p.Position, target.Position);
            bool inRange = dist <= pi.Range && (dist > 0 || !(target is ResourceNode || target is Station || target is Stall));
            bool canAct = inRange && (pi.Range <= 1 || Grid.HasLineOfSight(p.Position.TileX, p.Position.TileZ, target.Position.TileX, target.Position.TileZ));
            if (canAct)
            {
                p.Pending = null;
                p.Path.Clear();
                p.FaceTowards(target.Position);
                p.MarkStateChanged();
                PerformInteraction(p, target, pi);
                return;
            }
            // (re)path towards the target
            if (p.Path.Count == 0 || (target is Actor && Tick >= pi.RepathTick))
            {
                pi.RepathTick = Tick + 3;
                var path = PlanPath(p.Position, target.Position.TileX, target.Position.TileZ, Math.Max(1, pi.Range), false, 64);
                if (path == null || (path.Count == 0 && !inRange))
                {
                    if (++pi.FailedPaths > 2 || path == null)
                    {
                        p.Pending = null;
                        Message(p, "I can't reach that.");
                    }
                    return;
                }
                p.Path.Clear();
                p.Path.AddRange(path);
            }
        }

        private void PerformInteraction(Player p, Entity target, PendingInteraction pi)
        {
            if (pi.Option == "UseItem") { UseItemOnEntity(p, pi.ItemSlot, target); return; }
            if (pi.SpellId != null) { CastSpellOn(p, pi.SpellId, target); return; }
            switch (target)
            {
                case ResourceNode node: StartGathering(p, node); break;
                case Npc npc:
                    if (pi.Option == "Pickpocket") Pickpocket(p, npc);
                    else if (pi.Option == "Trade" && npc.Def.Role == "Merchant") OpenShopFor(p, npc);
                    else if (pi.Option == "Bank" && npc.Def.Role == "Banker") OpenBank(p, npc);
                    else StartDialogue(p, npc);
                    break;
                case Door door: SetDoor(door, !door.Open); break;
                case GroundItem gi: PickUp(p, gi); break;
                case Station st: UseStation(p, st); break;
                case Fire fire:
                    Emit(new WorldEvent { Type = WorldEventType.CraftMenu, PlayerId = p.Id, EntityId = fire.Id, Text = "Fire" });
                    break;
                case FarmPatch patch: FarmAction(p, patch, pi.Option); break;
                case Stall stall: StealFromStall(p, stall); break;
                case Obstacle ob: StartAgility(p, ob); break;
                case Player other:
                    if (pi.Option == "Trade") RequestTrade(p, other);
                    else if (pi.Option == "Follow") { p.Path.Clear(); }
                    break;
            }
        }

        private void Examine(Player p, Entity e)
        {
            string text;
            switch (e)
            {
                case Monster m: text = m.Def.Name + " (level " + m.Def.Level + ")."; break;
                case Npc n: text = n.Def.Name + "."; break;
                case ResourceNode r: text = r.Depleted ? "It's been exhausted for now." : "A " + r.Def.Name.ToLowerInvariant() + ". Requires level " + r.Def.MinLevel + " " + r.Def.Skill + "."; break;
                case GroundItem g: text = Db.Item(g.Stack.ItemId)?.Description ?? "Something."; break;
                case Player o: text = o.Name + ", combat level " + o.CombatLevel + "."; break;
                default: text = "It's a " + e.DisplayName.ToLowerInvariant() + "."; break;
            }
            Message(p, text);
        }

        // ------------------------------------------------------------------
        // Inventory intents
        // ------------------------------------------------------------------
        public void RequestMoveItem(Player p, int from, int to)
        {
            if (p == null || p.Trade != null) return;
            p.Inventory.Swap(from, to);
        }

        public void RequestEquip(Player p, int slot)
        {
            if (!ValidateActor(p) || p.Trade != null) return;
            var res = p.Equipment.Equip(p.Inventory, slot, p.Skills, out var failed);
            switch (res)
            {
                case EquipResult.RequirementNotMet: Message(p, "You need level " + failed.Level + " " + failed.Skill + " to wear that."); break;
                case EquipResult.InventoryFull: Message(p, "You don't have enough inventory space."); break;
                case EquipResult.NotEquippable: Message(p, "You can't wear that."); break;
                case EquipResult.Ok:
                    if (p.Activity != null && p.Activity.IsCombat) { /* weapon swap keeps fighting */ }
                    break;
            }
        }

        public void RequestUnequip(Player p, EquipSlot slot)
        {
            if (p == null || p.IsDead || p.Trade != null) return;
            if (p.Equipment.Unequip(slot, p.Inventory) == EquipResult.InventoryFull)
                Message(p, "You don't have enough inventory space.");
        }

        public void RequestDropItem(Player p, int slot, int qty = int.MaxValue)
        {
            if (!ValidateActor(p) || p.Trade != null) return;
            var s = p.Inventory[slot];
            if (s.IsEmpty) return;
            var taken = p.Inventory.TakeFromSlot(slot, Math.Max(1, qty));
            DropToGround(taken, p.Position, p);
            Emit(new WorldEvent { Type = WorldEventType.Sound, EntityId = p.Id, Text = "drop" });
        }

        /// <summary>Inventory item option: Eat, Drink, Bury, Equip, Drop, Examine, Light.</summary>
        public void RequestItemOption(Player p, int slot, string option)
        {
            if (p == null || p.IsDead || p.Trade != null) return;
            var stack = p.Inventory[slot];
            if (stack.IsEmpty) return;
            var def = Db.Item(stack.ItemId);
            if (def == null) return;
            switch (option)
            {
                case "Examine": Message(p, def.Description); return;
                case "Drop": RequestDropItem(p, slot); return;
                case "Equip": case "Wield": case "Wear": RequestEquip(p, slot); return;
                case "Eat": case "Drink": Consume(p, slot, def); return;
                case "Bury": Bury(p, slot, def); return;
            }
        }

        private void PickUp(Player p, GroundItem gi)
        {
            if (gi.Removed || !CanSee(p, gi)) return;
            if (!p.Inventory.Add(gi.Stack.ItemId, gi.Stack.Quantity))
            {
                Message(p, "You don't have enough inventory space.");
                return;
            }
            Unregister(gi);
            Emit(new WorldEvent { Type = WorldEventType.Sound, EntityId = p.Id, Text = "pickup" });
            EvaluateQuests(p);
        }

        private void Consume(Player p, int slot, ItemDef def)
        {
            if (!def.IsFood && def.ConsumeEffect == null) return;
            if ((p.Anim == ActionAnim.Eat || p.Anim == ActionAnim.Drink) && Tick < p.AnimUntilTick) return;   // one bite at a time
            p.Inventory.TakeFromSlot(slot, 1);
            int heal = def.Heal + (def.ConsumeEffect?.Heal ?? 0);
            if (heal > 0)
            {
                int before = p.Hp;
                p.Hp = Math.Min(p.MaxHp, p.Hp + heal);
                Message(p, "You " + (def.ConsumeEffect != null ? "drink" : "eat") + " the " + def.Name.ToLowerInvariant() + ". It heals " + (p.Hp - before) + " health.");
            }
            var fx = def.ConsumeEffect;
            if (fx != null && fx.Buff != null && Enum.TryParse(fx.Buff, out Skill s))
            {
                p.Buffs.RemoveAll(b => b.Skill == s);
                p.Buffs.Add(new Buff { Skill = s, Amount = fx.Amount, ExpiresTick = Tick + fx.DurationTicks });
                Message(p, "You feel your " + s + " increase.");
                p.SkillsDirty = true;
            }
            if (def.ConsumeEffect != null && def.Id != "vial") p.Inventory.Add("vial", 1);
            p.NextAttackTick = Math.Max(p.NextAttackTick, Tick + 2);   // eating delays your next attack
            Animate(p, def.ConsumeEffect != null ? ActionAnim.Drink : ActionAnim.Eat, 2);
            p.StatsDirty = true;
            p.MarkStateChanged();
        }

        private void Bury(Player p, int slot, ItemDef def)
        {
            if (def.BuryXp <= 0) return;
            p.Inventory.TakeFromSlot(slot, 1);
            GiveXp(p, Skill.Prayer, def.BuryXp);
            Animate(p, ActionAnim.Pray, 2);
            Message(p, "You bury the bones.");
        }

        /// <summary>Use one inventory item on another (knife on logs, tinderbox on logs, herb on vial...).</summary>
        public void RequestUseItemOnItem(Player p, int slotA, int slotB)
        {
            if (!ValidateActor(p) || p.Trade != null) return;
            var a = p.Inventory[slotA]; var b = p.Inventory[slotB];
            if (a.IsEmpty || b.IsEmpty || slotA == slotB) return;
            // find recipes without a station that use both items (as input or tool)
            var matches = new List<RecipeDef>();
            foreach (var r in Db.Recipes.Values)
            {
                if (r.Station != null) continue;
                if (Uses(r, a.ItemId) && Uses(r, b.ItemId)) matches.Add(r);
            }
            if (matches.Count == 0) { Message(p, "Nothing interesting happens."); return; }
            if (matches.Count == 1) { RequestCraft(p, matches[0].Id, 1, 0); return; }
            Emit(new WorldEvent { Type = WorldEventType.CraftMenu, PlayerId = p.Id, EntityId = 0, Text = "item:" + a.ItemId + ":" + b.ItemId });
        }

        private static bool Uses(RecipeDef r, string item)
        {
            foreach (var i in r.Inputs) if (i.Item == item) return true;
            foreach (var t in r.Tools) if (t == item) return true;
            return false;
        }

        private void UseItemOnEntity(Player p, int slot, Entity target)
        {
            var stack = p.Inventory[slot];
            if (stack.IsEmpty) return;
            switch (target)
            {
                case Station st:
                    foreach (var r in Db.Recipes.Values)
                        if (r.StationMatches(st.StationType) && Uses(r, stack.ItemId))
                        {
                            Emit(new WorldEvent { Type = WorldEventType.CraftMenu, PlayerId = p.Id, EntityId = st.Id, Text = st.StationType });
                            return;
                        }
                    break;
                case Fire fire:
                    foreach (var r in Db.Recipes.Values)
                        if (r.StationMatches("Fire") && Uses(r, stack.ItemId)) { RequestCraft(p, r.Id, 28, fire.Id); return; }
                    break;
                case FarmPatch patch:
                    FarmUseItem(p, patch, slot);
                    return;
                case Player other:
                    Message(p, "Use the trade option to give items to " + other.Name + ".");
                    return;
            }
            Message(p, "Nothing interesting happens.");
        }

        // ------------------------------------------------------------------
        // Location visits (quests / region tracking)
        // ------------------------------------------------------------------
        private void CheckVisitLocations(Player p)
        {
            if (Db.World == null || Tick % 2 != 0) return;
            foreach (var kv in Db.World.Locations)
            {
                if (kv.Value.Radius <= 0) continue;
                if (Vec2.Distance(p.Position, new Vec2(kv.Value.X, kv.Value.Z)) <= kv.Value.Radius)
                {
                    if (p.LastVisitedLocation == kv.Key) return;
                    p.LastVisitedLocation = kv.Key;
                    if (p.Quests.Record(ObjectiveType.Visit, kv.Key, 1)) { p.QuestsDirty = true; EvaluateQuests(p); }
                    return;
                }
            }
            p.LastVisitedLocation = null;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------
        /// <summary>Best tool of a type from equipment or inventory that the player can use.</summary>
        public ItemDef BestTool(Player p, string toolType, Skill skill)
        {
            ItemDef best = null;
            void Consider(ItemDef d)
            {
                if (d == null || d.ToolType != toolType) return;
                if (!p.Skills.MeetsAll(d.Requirements, out _)) return;
                if (best == null || d.ToolPower > best.ToolPower) best = d;
            }
            Consider(p.Equipment.Weapon);
            foreach (var s in p.Inventory.NonEmpty()) Consider(Db.Item(s.ItemId));
            return best;
        }

        public bool HasItemOrEquipped(Player p, string itemId)
        {
            if (p.Inventory.Contains(itemId)) return true;
            for (int i = 0; i < EquipSlots.Count; i++) if (p.Equipment[(EquipSlot)i].ItemId == itemId) return true;
            return false;
        }

        /// <summary>Gives items, dropping overflow at the player's feet. Returns true if all fit.</summary>
        public bool GiveOrDrop(Player p, string item, int qty)
        {
            if (qty <= 0) return true;
            if (p.Inventory.Add(item, qty)) return true;
            if (Db.IsStackable(item)) { DropToGround(new ItemStack(item, qty), p.Position, p); return false; }
            int added = 0;
            while (added < qty && p.Inventory.Add(item, 1)) added++;
            if (qty - added > 0)
            {
                for (int i = 0; i < qty - added; i++) DropToGround(new ItemStack(item, 1), p.Position, p);
                Message(p, "Your inventory is full, so some items were placed on the ground.");
            }
            return added == qty;
        }
    }
}
