using System;
using System.Collections.Generic;
using Embervale.Shared.Data;

namespace Embervale.Shared.Simulation
{
    public sealed class CombatActivity : PlayerActivity
    {
        public Actor Target;
        public string Spell;          // one-off spell cast (null = use weapon / autocast)
        public override bool IsCombat => true;
        public override bool Tick(GameWorld w, Player p) => w.TickPlayerCombat(p, this);
        public override void OnStop(GameWorld w, Player p) { p.TargetId = 0; }
    }

    public sealed partial class GameWorld
    {
        public const int RespawnDelayTicks = 8;

        // ------------------------------------------------------------------
        // Player offence
        // ------------------------------------------------------------------
        public void StartCombat(Player p, Actor target, string spell = null)
        {
            if (target == null || target.IsDead || target == p) return;
            if (target is Npc) { Message(p, "You can't attack them."); return; }
            if (target is Player) { Message(p, "The people of Embervale do not fight each other here."); return; }
            if (target is Monster m && m.State == MonsterState.Dead) return;
            p.TargetId = target.Id;
            StartActivity(p, new CombatActivity { Target = target, Spell = spell });
            p.TargetId = target.Id;
            TickPlayerCombat(p, (CombatActivity)p.Activity);
        }

        public void RequestCastSpell(Player p, string spellId, int targetEntityId)
        {
            if (!ValidateActor(p) || !Db.Spells.TryGetValue(spellId, out var spell)) return;
            if (p.Effective(Skill.Magic) < spell.Level) { Message(p, "You need level " + spell.Level + " Magic to cast that."); return; }
            switch (spell.Type)
            {
                case SpellType.Attack:
                    var t = Get<Actor>(targetEntityId);
                    if (t == null) return;
                    CloseInterfaces(p);
                    CancelActions(p);
                    StartCombat(p, t, spellId);
                    break;
                case SpellType.Heal:
                    var target = Get<Player>(targetEntityId) ?? p;
                    if (Vec2.TileDistance(target.Position, p.Position) > Math.Max(1, spell.Range)) { Message(p, "You're too far away."); return; }
                    if (!ConsumeRunes(p, spell)) return;
                    target.Hp = Math.Min(target.MaxHp, target.Hp + spell.Heal);
                    target.MarkStateChanged();
                    target.StatsDirty = true;
                    GiveXp(p, Skill.Magic, spell.Xp);
                    Animate(p, ActionAnim.CastSpell, 3);
                    Message(p, "You cast " + spell.Name + ".");
                    break;
                case SpellType.Buff:
                    if (!ConsumeRunes(p, spell)) return;
                    if (Enum.TryParse(spell.Buff, out Skill s))
                    {
                        p.Buffs.RemoveAll(b => b.Skill == s);
                        p.Buffs.Add(new Buff { Skill = s, Amount = spell.Amount, ExpiresTick = Tick + spell.DurationTicks });
                    }
                    GiveXp(p, Skill.Magic, spell.Xp);
                    Animate(p, ActionAnim.CastSpell, 3);
                    p.SkillsDirty = true;
                    Message(p, "You feel protected.");
                    break;
                case SpellType.Teleport:
                    if (p.Activity != null && p.Activity.IsCombat && Tick - p.LastHitByTick < 10) { Message(p, "You can't teleport during combat."); return; }
                    if (!ConsumeRunes(p, spell)) return;
                    CancelActions(p);
                    CloseInterfaces(p);
                    if (p.Trade != null) CancelTrade(p, "Trade cancelled.");
                    p.Position = Location(spell.Destination);
                    Spatial.Update(p, p.Position);
                    p.MarkStateChanged();
                    GiveXp(p, Skill.Magic, spell.Xp);
                    Emit(new WorldEvent { Type = WorldEventType.Teleport, EntityId = p.Id });
                    Message(p, "You follow the path.");
                    break;
            }
        }

        private void CastSpellOn(Player p, string spellId, Entity target)
        {
            if (target is Actor a) StartCombat(p, a, spellId);
        }

        public void RequestSetAutocast(Player p, string spellId)
        {
            if (spellId == null || spellId == "") { p.AutocastSpell = null; p.StatsDirty = true; return; }
            if (!Db.Spells.TryGetValue(spellId, out var s) || s.Type != SpellType.Attack) return;
            if (p.Effective(Skill.Magic) < s.Level) { Message(p, "You need level " + s.Level + " Magic to autocast that."); return; }
            p.AutocastSpell = spellId;
            p.StatsDirty = true;
        }

        public void RequestSetStance(Player p, MeleeStance stance)
        {
            p.Stance = stance;
            p.StatsDirty = true;
        }

        public void RequestSetAutoRetaliate(Player p, bool on)
        {
            p.AutoRetaliate = on;
            p.StatsDirty = true;
        }

        private bool ConsumeRunes(Player p, SpellDef spell)
        {
            foreach (var r in spell.Runes)
                if (!p.Inventory.Contains(r.Item, r.Qty)) { Message(p, "You don't have enough runes to cast " + spell.Name + "."); return false; }
            foreach (var r in spell.Runes) p.Inventory.Remove(r.Item, r.Qty);
            return true;
        }

        /// <summary>What style and range the player will attack with right now.</summary>
        public CombatStyle PlayerStyle(Player p, string spell, out int range, out SpellDef spellDef)
        {
            spellDef = null;
            var sid = spell ?? p.AutocastSpell;
            if (sid != null && Db.Spells.TryGetValue(sid, out var sd) && sd.Type == SpellType.Attack)
            {
                spellDef = sd;
                range = sd.Range > 0 ? sd.Range : 8;
                return CombatStyle.Magic;
            }
            var w = p.Equipment.Weapon;
            if (w != null && w.WeaponStyle == CombatStyle.Ranged)
            {
                range = w.Stats != null && w.Stats.AttackRange > 0 ? w.Stats.AttackRange : 7;
                return CombatStyle.Ranged;
            }
            range = 1;
            return CombatStyle.Melee;
        }

        internal bool TickPlayerCombat(Player p, CombatActivity a)
        {
            var t = a.Target;
            if (t == null || t.Removed || t.IsDead || (t is Monster tm && tm.State == MonsterState.Dead)) return false;
            if (Vec2.TileDistance(p.Position, t.Position) > 24) { Message(p, "Your target got away."); return false; }
            var style = PlayerStyle(p, a.Spell, out int range, out var spell);
            int dist = Vec2.TileDistance(p.Position, t.Position);
            bool los = range <= 1 || Grid.HasLineOfSight(p.Position.TileX, p.Position.TileZ, t.Position.TileX, t.Position.TileZ);
            if (dist > range || dist == 0 || !los)
            {
                if (p.Path.Count == 0 || Tick % 2 == 0)
                {
                    var path = PlanPath(p.Position, t.Position.TileX, t.Position.TileZ, range, false, 32);
                    if (path == null) { Message(p, "I can't reach that."); return false; }
                    p.Path.Clear();
                    p.Path.AddRange(path);
                }
                return true;
            }
            p.Path.Clear();
            p.FaceTowards(t.Position);
            if (Tick < p.NextAttackTick) return true;

            var stats = p.Equipment.TotalStats();
            var atk = new AttackProfile { Style = style, Multiplier = 1.0 };
            switch (style)
            {
                case CombatStyle.Melee:
                    atk.AccuracyLevel = p.Effective(Skill.Attack) + (p.Stance == MeleeStance.Accurate ? 3 : 0);
                    atk.PowerLevel = p.Effective(Skill.Strength) + (p.Stance == MeleeStance.Aggressive ? 3 : 0);
                    atk.AccuracyBonus = stats.AttackMelee;
                    atk.PowerBonus = stats.StrengthMelee;
                    atk.Multiplier = 1.0 + Math.Max(PrayerBonus(p, "Attack"), PrayerBonus(p, "Strength"));
                    break;
                case CombatStyle.Ranged:
                    var weapon = p.Equipment.Weapon;
                    var ammo = p.Equipment.DefIn(EquipSlot.Ammo);
                    string needed = weapon?.WeaponType == "Crossbow" ? "Bolt" : "Arrow";
                    if (ammo == null || ammo.AmmoType != needed) { Message(p, "You have no " + needed.ToLowerInvariant() + "s equipped."); return false; }
                    atk.AccuracyLevel = p.Effective(Skill.Ranged);
                    atk.PowerLevel = p.Effective(Skill.Ranged);
                    atk.AccuracyBonus = stats.AttackRanged;
                    atk.PowerBonus = stats.StrengthRanged;
                    atk.Multiplier = 1.0 + PrayerBonus(p, "Ranged");
                    p.Equipment.ConsumeAmmo(1);
                    break;
                case CombatStyle.Magic:
                    if (!ConsumeRunes(p, spell)) { if (p.AutocastSpell == spell.Id) p.AutocastSpell = null; return false; }
                    atk.AccuracyLevel = p.Effective(Skill.Magic);
                    atk.PowerLevel = p.Effective(Skill.Magic);
                    atk.AccuracyBonus = stats.AttackMagic;
                    atk.PowerBonus = stats.MagicPower;
                    atk.SpellMaxHit = spell.MaxHit;
                    atk.Multiplier = 1.0 + PrayerBonus(p, "Magic");
                    break;
            }
            var def = DefenseOf(t, style);
            var hit = CombatFormulas.Roll(Rng, atk, def);
            int speed = style == CombatStyle.Magic ? 5 : stats.AttackSpeed;
            p.NextAttackTick = Tick + Math.Max(2, speed);
            Animate(p, style == CombatStyle.Melee ? ActionAnim.AttackMelee : style == CombatStyle.Ranged ? ActionAnim.AttackRanged : ActionAnim.CastSpell, 2);
            if (style != CombatStyle.Melee)
                Emit(new WorldEvent { Type = WorldEventType.Projectile, EntityId = p.Id, TargetId = t.Id, Text = style == CombatStyle.Magic ? spell.Element.ToLowerInvariant() : "arrow" });

            int dealt = DamageActor(t, hit.Hit ? hit.Damage : 0, p, hit.Critical, false, !hit.Hit);
            // experience
            if (style == CombatStyle.Magic) GiveXp(p, Skill.Magic, spell.Xp + dealt * 2.0);
            else if (dealt > 0)
            {
                Skill s = style == CombatStyle.Ranged ? Skill.Ranged :
                          p.Stance == MeleeStance.Aggressive ? Skill.Strength :
                          p.Stance == MeleeStance.Defensive ? Skill.Defense : Skill.Attack;
                GiveXp(p, s, dealt * CombatFormulas.XpPerDamage);
            }
            if (dealt > 0) GiveXp(p, Skill.Vitality, dealt * CombatFormulas.VitalityXpPerDamage);
            if (a.Spell != null) return false;  // one-off cast ends combat activity
            return !t.IsDead;
        }

        public DefenseProfile DefenseOf(Actor a, CombatStyle incoming)
        {
            if (a is Monster m) return CombatFormulas.MonsterDefense(m.Def);
            if (a is Player p)
            {
                var st = p.Equipment.TotalStats();
                int bonus = incoming == CombatStyle.Melee ? st.DefenseMelee : incoming == CombatStyle.Ranged ? st.DefenseRanged : st.DefenseMagic;
                return new DefenseProfile
                {
                    DefenseLevel = (int)Math.Round((p.Effective(Skill.Defense) + (p.Stance == MeleeStance.Defensive ? 3 : 0)) * (1.0 + PrayerBonus(p, "Defense"))),
                    MagicLevel = p.Effective(Skill.Magic),
                    DefenseBonus = bonus,
                    DamageReduction = PrayerBonus(p, "DamageReduction"),
                };
            }
            return new DefenseProfile { DefenseLevel = 1, MagicLevel = 1 };
        }

        // ------------------------------------------------------------------
        // Damage + death
        // ------------------------------------------------------------------
        /// <summary>Applies damage (server only). Returns damage actually dealt.</summary>
        public int DamageActor(Actor victim, int damage, Actor attacker, bool critical, bool poison, bool miss = false)
        {
            if (victim.IsDead) return 0;
            damage = Math.Max(0, Math.Min(damage, victim.Hp));
            victim.Hp -= damage;
            victim.MarkStateChanged();
            if (victim is Player vp) vp.StatsDirty = true;
            Emit(new WorldEvent { Type = WorldEventType.HitSplat, EntityId = victim.Id, TargetId = attacker?.Id ?? 0, Value = damage, Flag = critical, Text = poison ? "poison" : miss ? "miss" : null });
            if (attacker != null)
            {
                victim.LastHitByTick = Tick;
                victim.LastAttackerId = attacker.Id;
                if (victim is Monster vm && attacker is Player ap)
                {
                    vm.DamageBy.TryGetValue(ap.Id, out var prev);
                    vm.DamageBy[ap.Id] = prev + damage;
                    if (vm.State != MonsterState.Dead && (vm.TargetId == 0 || vm.State == MonsterState.Idle))
                    {
                        vm.TargetId = ap.Id;
                        vm.State = MonsterState.Chasing;
                    }
                }
                if (victim is Player pv && pv.Activity == null && pv.Path.Count == 0 && pv.AutoRetaliate && !victim.IsDead)
                    StartCombat(pv, attacker);
            }
            if (damage > 0 && victim.Anim != ActionAnim.AttackMelee) Animate(victim, ActionAnim.Hurt, 1);
            if (victim.Hp <= 0) Kill(victim, attacker);
            return damage;
        }

        private void Kill(Actor victim, Actor killer)
        {
            victim.Hp = 0;
            victim.Path.Clear();
            Animate(victim, ActionAnim.Death, 4);
            Emit(new WorldEvent { Type = WorldEventType.Death, EntityId = victim.Id, TargetId = killer?.Id ?? 0 });
            if (victim is Monster m) KillMonster(m);
            else if (victim is Player p) KillPlayer(p);
        }

        private void KillMonster(Monster m)
        {
            m.State = MonsterState.Dead;
            m.TargetId = 0;
            m.RespawnAtTick = Tick + Math.Max(RespawnDelayTicks, m.Def.RespawnTicks);
            // loot goes to whoever did the most damage
            Player owner = null;
            int best = -1;
            foreach (var kv in m.DamageBy)
            {
                var pl = Get<Player>(kv.Key);
                if (pl != null && kv.Value > best) { best = kv.Value; owner = pl; }
            }
            if (Db.Loot.TryGetValue(m.Def.LootTable, out var table))
                foreach (var drop in LootRoller.Roll(table, Rng))
                {
                    if (drop.Quantity <= 0) continue;
                    if (Db.IsStackable(drop.ItemId)) DropToGround(drop, m.Position, owner);
                    else for (int i = 0; i < drop.Quantity; i++) DropToGround(new ItemStack(drop.ItemId, 1), m.Position, owner);
                }
            // kill credit for every player in the owner's party nearby, else only owner
            var credited = new HashSet<Player>();
            if (owner != null)
            {
                credited.Add(owner);
                if (owner.Party != null)
                    foreach (var mem in owner.Party.Members)
                        if (m.DamageBy.ContainsKey(mem.Id) && Vec2.TileDistance(mem.Position, m.Position) < 20) credited.Add(mem);
            }
            foreach (var pl in credited)
            {
                if (pl.Quests.Record(ObjectiveType.Kill, m.Def.Id, 1)) pl.QuestsDirty = true;
                EvaluateQuests(pl);
                if (pl.Activity is CombatActivity ca && ca.Target == m) StopActivity(pl);
            }
            m.DamageBy.Clear();
            if (m.IsSummon) _toRemove.Add(m);
        }

        private void KillPlayer(Player p)
        {
            CancelActions(p);
            CloseInterfaces(p);
            if (p.Trade != null) CancelTrade(p, "Trade cancelled.");
            p.RespawnAtTick = Tick + 6;
            // inventory drops as a pile that only the owner can see for 3 minutes
            foreach (var s in p.Inventory.NonEmpty())
            {
                if (Db.IsStackable(s.ItemId)) DropToGround(s, p.Position, p, 360, 720);
                else for (int i = 0; i < s.Quantity; i++) DropToGround(new ItemStack(s.ItemId, 1), p.Position, p, 360, 720);
            }
            p.Inventory.Clear();
            foreach (var m in _monsters) if (m.TargetId == p.Id) { m.TargetId = 0; m.State = MonsterState.Returning; }
            Message(p, "Oh dear, you have been defeated.");
        }

        // ------------------------------------------------------------------
        // Monster AI
        // ------------------------------------------------------------------
        private void TickMonster(Monster m)
        {
            if (m.State == MonsterState.Dead)
            {
                if (!m.IsSummon && Tick >= m.RespawnAtTick) RespawnMonster(m);
                return;
            }
            if (m.PoisonDamage > 0 && Tick >= m.PoisonNextTick)
            {
                m.PoisonNextTick = Tick + 30;
                DamageActor(m, m.PoisonDamage, null, false, true);
                m.PoisonDamage--;
                if (m.IsDead) return;
            }
            if (m.PendingAbility != null && Tick >= m.PendingAbilityTick) ResolveAbility(m);

            var target = Get<Player>(m.TargetId);
            switch (m.State)
            {
                case MonsterState.Idle:
                    if (m.Def.Aggressive && Tick % 2 == 0)
                    {
                        var victim = FindAggroTarget(m);
                        if (victim != null) { m.TargetId = victim.Id; m.State = MonsterState.Chasing; break; }
                    }
                    if (m.Hp < m.MaxHp && Tick % 10 == 0) { m.Hp++; m.MarkStateChanged(); }
                    if (m.Path.Count == 0 && Tick >= m.NextWanderTick && m.Def.WanderRange > 0)
                    {
                        m.NextWanderTick = Tick + Rng.Next(6, 24);
                        var dest = RandomWalkableNear(m.SpawnPoint, Math.Min(m.Def.WanderRange, m.SpawnRadius + 2));
                        if (dest.HasValue)
                        {
                            var path = Pathfinder.FindPath(m.Position.TileX, m.Position.TileZ, dest.Value.TileX, dest.Value.TileZ, 0, false, 20);
                            if (path != null && path.Count < 14) { m.Path.Clear(); m.Path.AddRange(path); m.Running = false; }
                        }
                    }
                    break;

                case MonsterState.Chasing:
                    if (target == null || target.IsDead || target.Removed
                        || Vec2.Distance(m.Position, m.SpawnPoint) > m.Def.ChaseRange + m.SpawnRadius
                        || Vec2.TileDistance(m.Position, target.Position) > m.Def.ChaseRange + 4)
                    {
                        m.TargetId = 0;
                        m.State = MonsterState.Returning;
                        break;
                    }
                    MonsterCombat(m, target);
                    break;

                case MonsterState.Returning:
                    if (m.Path.Count == 0)
                    {
                        if (Vec2.TileDistance(m.Position, m.SpawnPoint) <= Math.Max(1, (int)m.SpawnRadius))
                        {
                            m.State = MonsterState.Idle;
                            m.Hp = m.MaxHp;
                            m.DamageBy.Clear();
                            m.MarkStateChanged();
                        }
                        else
                        {
                            var path = Pathfinder.FindPath(m.Position.TileX, m.Position.TileZ, (int)m.SpawnPoint.X, (int)m.SpawnPoint.Z, (int)Math.Max(1, m.SpawnRadius - 1), true, 48);
                            if (path == null || path.Count == 0)
                            {
                                // stuck: snap home
                                m.Position = RandomWalkableNear(m.SpawnPoint, m.SpawnRadius) ?? m.Position;
                                Spatial.Update(m, m.Position);
                                m.State = MonsterState.Idle;
                                Emit(new WorldEvent { Type = WorldEventType.Teleport, EntityId = m.Id });
                            }
                            else { m.Path.Clear(); m.Path.AddRange(path); m.Running = true; }
                        }
                    }
                    break;
            }
            if (AdvanceMovement(m)) m.MarkStateChanged();
        }

        private Player FindAggroTarget(Monster m)
        {
            var nearby = new List<Entity>();
            QueryArea(m.Position, m.Def.AggroRange + 0.5f, nearby);
            Player best = null;
            float bestD = float.MaxValue;
            foreach (var e in nearby)
            {
                if (!(e is Player p) || p.IsDead) continue;
                // seasoned adventurers are ignored by weak monsters
                if (!m.Def.Boss && p.CombatLevel > m.Def.Level * 2 + 4) continue;
                if (Vec2.Distance(p.Position, m.SpawnPoint) > m.Def.ChaseRange + m.SpawnRadius) continue;
                float d = Vec2.Distance(p.Position, m.Position);
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        private void MonsterCombat(Monster m, Player target)
        {
            int range = Math.Max(1, m.Def.AttackRange);
            int dist = Vec2.TileDistance(m.Position, target.Position);
            bool los = range <= 1 || Grid.HasLineOfSight(m.Position.TileX, m.Position.TileZ, target.Position.TileX, target.Position.TileZ);
            if (dist > range || dist == 0 || !los)
            {
                if (Tick >= m.NextRepathTick || m.Path.Count == 0)
                {
                    m.NextRepathTick = Tick + 2;
                    var path = Pathfinder.FindPath(m.Position.TileX, m.Position.TileZ, target.Position.TileX, target.Position.TileZ, range, false, 24);
                    m.Path.Clear();
                    if (path != null) { m.Path.AddRange(path); m.Running = false; }
                    else { m.TargetId = 0; m.State = MonsterState.Returning; }
                }
                return;
            }
            m.Path.Clear();
            m.FaceTowards(target.Position);
            if (Tick < m.NextAttackTick) return;
            m.NextAttackTick = Tick + Math.Max(2, m.Def.AttackSpeed);
            m.AttacksMade++;

            // boss abilities
            foreach (var ab in m.Def.Abilities)
            {
                if (ab.AtHpFraction > 0 && !m.SummonUsed && m.Hp <= m.MaxHp * ab.AtHpFraction && ab.Monster != null && Db.Monsters.TryGetValue(ab.Monster, out var sdef))
                {
                    m.SummonUsed = true;
                    for (int i = 0; i < ab.Count; i++)
                    {
                        var pos = RandomWalkableNear(m.Position, 3) ?? m.Position;
                        var s = SpawnMonster(sdef, pos, m.SpawnPoint, m.SpawnRadius + 4);
                        s.IsSummon = true;
                        s.TargetId = target.Id;
                        s.State = MonsterState.Chasing;
                    }
                    Emit(new WorldEvent { Type = WorldEventType.Chat, EntityId = m.Id, Text = "Rise, my servants!", Channel = ChatChannel.Local, Text2 = m.Def.Name });
                    return;
                }
                if (ab.EveryAttacks > 0 && m.AttacksMade % ab.EveryAttacks == 0 && m.PendingAbility == null)
                {
                    m.PendingAbility = ab;
                    m.PendingAbilityTick = Tick + Math.Max(1, ab.TelegraphTicks);
                    m.PendingAbilityCenter = target.Position;
                    Animate(m, ActionAnim.CastSpell, ab.TelegraphTicks);
                    Emit(new WorldEvent { Type = WorldEventType.Telegraph, EntityId = m.Id, Value = ab.Radius, Text = ab.Id, X = target.Position.X, Z = target.Position.Z });
                    return;
                }
            }

            var atk = CombatFormulas.MonsterAttack(m.Def);
            var hit = CombatFormulas.Roll(Rng, atk, DefenseOf(target, m.Def.Style));
            Animate(m, m.Def.Style == CombatStyle.Melee ? ActionAnim.AttackMelee : m.Def.Style == CombatStyle.Ranged ? ActionAnim.AttackRanged : ActionAnim.CastSpell, 2);
            if (m.Def.Style != CombatStyle.Melee)
                Emit(new WorldEvent { Type = WorldEventType.Projectile, EntityId = m.Id, TargetId = target.Id, Text = m.Def.Style == CombatStyle.Magic ? "spirit" : "arrow" });
            int dealt = DamageActor(target, hit.Hit ? hit.Damage : 0, m, hit.Critical, false, !hit.Hit);
            if (dealt > 0 && m.Def.Poison > 0 && Rng.Chance(0.2) && target.PoisonDamage == 0)
            {
                target.PoisonDamage = m.Def.Poison;
                target.PoisonNextTick = Tick + 10;
                Message(target, "You have been poisoned!");
            }
        }

        private void ResolveAbility(Monster m)
        {
            var ab = m.PendingAbility;
            m.PendingAbility = null;
            if (m.IsDead) return;
            var hits = new List<Entity>();
            QueryArea(m.PendingAbilityCenter, ab.Radius + 0.5f, hits);
            foreach (var e in hits)
            {
                if (!(e is Player p) || p.IsDead) continue;
                int dmg = Rng.Next(ab.MaxHit / 3, ab.MaxHit);
                dmg = (int)Math.Floor(dmg * (1.0 - PrayerBonus(p, "DamageReduction")));
                DamageActor(p, dmg, m, false, false);
                if (ab.Poison > 0 && p.PoisonDamage == 0) { p.PoisonDamage = ab.Poison; p.PoisonNextTick = Tick + 10; }
            }
        }

        private void RespawnMonster(Monster m)
        {
            m.Position = RandomWalkableNear(m.SpawnPoint, m.SpawnRadius) ?? m.SpawnPoint;
            m.Hp = m.MaxHp;
            m.State = MonsterState.Idle;
            m.TargetId = 0;
            m.SummonUsed = false;
            m.AttacksMade = 0;
            m.PendingAbility = null;
            m.PoisonDamage = 0;
            m.Anim = ActionAnim.Idle;
            m.Path.Clear();
            m.DamageBy.Clear();
            Spatial.Update(m, m.Position);
            m.MarkStateChanged();
            Emit(new WorldEvent { Type = WorldEventType.Teleport, EntityId = m.Id });
        }
    }
}
