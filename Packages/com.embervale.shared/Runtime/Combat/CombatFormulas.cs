using System;
using System.Collections.Generic;
using Embervale.Shared.Data;

namespace Embervale.Shared
{
    /// <summary>Everything needed to resolve one attack. Built by the server from authoritative state.</summary>
    public struct AttackProfile
    {
        public CombatStyle Style;
        public int AccuracyLevel;     // effective Attack / Ranged / Magic level
        public int PowerLevel;        // effective Strength / Ranged / Magic level
        public int AccuracyBonus;     // equipment accuracy for the style
        public int PowerBonus;        // equipment strength for the style
        public int SpellMaxHit;       // magic only: base max hit from the spell
        public double Multiplier;     // prayers / buffs (1.0 = none)
    }

    public struct DefenseProfile
    {
        public int DefenseLevel;
        public int MagicLevel;
        public int DefenseBonus;      // equipment defence vs the incoming style
        public double DamageReduction; // 0..1 (protective blessings)
    }

    public struct HitResult
    {
        public bool Hit;
        public bool Critical;
        public int Damage;
    }

    /// <summary>
    /// Embervale combat maths. Pure functions so the server, tests and client tooltips agree.
    /// Accuracy: rating = (level + 8) * (bonus + 48). Hit chance compares attacker and defender
    /// ratings on a smooth curve clamped to [5%, 95%].
    /// </summary>
    public static class CombatFormulas
    {
        public const double CritChance = 0.05;
        public const double CritMultiplier = 1.5;

        public static long AttackRating(in AttackProfile a) =>
            (long)((a.AccuracyLevel + 8) * Math.Max(1, a.AccuracyBonus + 48) * Math.Max(0.1, a.Multiplier));

        public static long DefenseRating(in DefenseProfile d, CombatStyle incoming)
        {
            int lvl = incoming == CombatStyle.Magic ? (int)Math.Round(d.MagicLevel * 0.7 + d.DefenseLevel * 0.3) : d.DefenseLevel;
            return (long)(lvl + 8) * Math.Max(1, d.DefenseBonus + 48);
        }

        public static double HitChance(long attackRating, long defenseRating)
        {
            if (attackRating <= 0) return 0.05;
            double ratio = (double)attackRating / Math.Max(1, defenseRating);
            // ratio 1 => 55%, ratio 2 => ~80%, ratio 0.5 => ~30%
            double p = 0.55 + 0.35 * Math.Tanh(Math.Log(ratio) * 1.1);
            return Math.Max(0.05, Math.Min(0.95, p));
        }

        public static int MaxHit(in AttackProfile a)
        {
            double mult = a.Multiplier <= 0 ? 1.0 : a.Multiplier;
            if (a.Style == CombatStyle.Magic)
            {
                double boosted = a.SpellMaxHit * (1.0 + a.PowerBonus / 100.0) * (1.0 + (a.PowerLevel - 1) / 200.0);
                return Math.Max(1, (int)Math.Floor(boosted * mult));
            }
            double baseHit = 1.3 + a.PowerLevel / 10.0 + a.PowerBonus / 80.0 + a.PowerLevel * (a.PowerBonus + 64) / 640.0;
            return Math.Max(1, (int)Math.Floor(baseHit * mult));
        }

        public static HitResult Roll(IRandom rng, in AttackProfile atk, in DefenseProfile def)
        {
            var result = new HitResult();
            double chance = HitChance(AttackRating(atk), DefenseRating(def, atk.Style));
            if (!rng.Chance(chance)) return result;
            int max = MaxHit(atk);
            int dmg = rng.Next(0, max);
            if (rng.Chance(CritChance))
            {
                result.Critical = true;
                dmg = Math.Max(1, (int)Math.Ceiling(Math.Max(dmg, max * 0.5) * CritMultiplier));
            }
            if (def.DamageReduction > 0) dmg = (int)Math.Floor(dmg * (1.0 - Math.Min(0.9, def.DamageReduction)));
            result.Hit = true;
            result.Damage = Math.Max(0, dmg);
            return result;
        }

        /// <summary>Combat XP per point of damage dealt.</summary>
        public const double XpPerDamage = 4.0;
        public const double VitalityXpPerDamage = 1.33;

        public static AttackProfile MonsterAttack(MonsterDef m) => new AttackProfile
        {
            Style = m.Style,
            AccuracyLevel = m.Attack,
            PowerLevel = m.Strength,
            AccuracyBonus = m.Level / 2,
            PowerBonus = m.Level / 3,
            SpellMaxHit = Math.Max(2, m.Strength / 4),
            Multiplier = 1.0,
        };

        public static DefenseProfile MonsterDefense(MonsterDef m) => new DefenseProfile
        {
            DefenseLevel = m.Defense,
            MagicLevel = m.Defense,
            DefenseBonus = m.Level / 2,
        };
    }

    /// <summary>Rolls loot tables.</summary>
    public static class LootRoller
    {
        public static List<ItemStack> Roll(LootTableDef table, IRandom rng)
        {
            var drops = new List<ItemStack>();
            if (table == null) return drops;
            foreach (var e in table.Always) drops.Add(new ItemStack(e.Item, rng.Next(e.Min, e.Max)));
            for (int r = 0; r < Math.Max(0, table.Rolls); r++)
            {
                var pick = Weighted(table.Entries, table.NothingWeight, rng);
                if (pick != null) drops.Add(new ItemStack(pick.Item, rng.Next(pick.Min, pick.Max)));
            }
            if (table.Rare.Count > 0 && rng.Chance(table.RareChance))
            {
                var pick = Weighted(table.Rare, 0, rng);
                if (pick != null) drops.Add(new ItemStack(pick.Item, rng.Next(pick.Min, pick.Max)));
            }
            return Merge(drops);
        }

        public static LootEntry Weighted(List<LootEntry> entries, int nothingWeight, IRandom rng)
        {
            int total = Math.Max(0, nothingWeight);
            foreach (var e in entries) total += Math.Max(0, e.Weight);
            if (total <= 0) return null;
            int roll = rng.Next(0, total - 1);
            foreach (var e in entries)
            {
                roll -= Math.Max(0, e.Weight);
                if (roll < 0) return e;
            }
            return null; // landed on "nothing"
        }

        private static List<ItemStack> Merge(List<ItemStack> drops)
        {
            var result = new List<ItemStack>();
            foreach (var d in drops)
            {
                if (d.IsEmpty) continue;
                int idx = result.FindIndex(x => x.ItemId == d.ItemId);
                if (idx >= 0) result[idx] = new ItemStack(d.ItemId, result[idx].Quantity + d.Quantity);
                else result.Add(d);
            }
            return result;
        }
    }

    /// <summary>Gathering success maths.</summary>
    public static class GatheringFormulas
    {
        /// <summary>Chance per attempt (one attempt every 4 ticks) of obtaining a resource.</summary>
        public static double SuccessChance(double baseChance, int skillLevel, int requiredLevel, float toolPower)
        {
            double levelBonus = (skillLevel - requiredLevel) * 0.008;
            double tool = 1.0 + Math.Max(0, toolPower - 1.0) * 0.25;
            return Math.Max(0.02, Math.Min(0.95, (baseChance + levelBonus) * tool));
        }

        /// <summary>Chance a crafting attempt fails (e.g. burning food). Linearly falls to 0 at noFailLevel.</summary>
        public static double FailChance(RecipeDef r, int level)
        {
            if (r.FailOutput == null || r.FailChanceAtLevel <= 0) return 0;
            if (level >= r.NoFailLevel) return 0;
            double span = Math.Max(1, r.NoFailLevel - r.Level);
            double t = Math.Max(0, Math.Min(1, (level - r.Level) / span));
            return r.FailChanceAtLevel * (1 - t);
        }
    }
}
