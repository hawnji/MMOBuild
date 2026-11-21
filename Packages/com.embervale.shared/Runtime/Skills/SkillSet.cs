using System;
using System.Collections.Generic;
using Embervale.Shared.Data;

namespace Embervale.Shared
{
    public struct LevelUp
    {
        public Skill Skill;
        public int OldLevel;
        public int NewLevel;
    }

    /// <summary>
    /// A character's skills. XP is stored in tenths (fixed point) so fractional rewards
    /// accumulate exactly and persist without floating point drift.
    /// </summary>
    public sealed class SkillSet
    {
        public const int XpScale = 10;
        private readonly long[] _xpTenths = new long[Skills.Count];
        private readonly int[] _boost = new int[Skills.Count];   // temporary +/- from potions/spells
        private readonly ExperienceTable _table;

        public event Action<LevelUp> LevelledUp;

        public SkillSet(ExperienceTable table, IDictionary<string, int> startingLevels = null)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
            if (startingLevels != null)
            {
                foreach (var kv in startingLevels)
                    if (Enum.TryParse(kv.Key, out Skill s))
                        _xpTenths[(int)s] = table.XpForLevel(kv.Value) * XpScale;
            }
        }

        public ExperienceTable Table => _table;

        public long Xp(Skill s) => _xpTenths[(int)s] / XpScale;
        public long XpTenths(Skill s) => _xpTenths[(int)s];
        public int Level(Skill s) => _table.LevelForXp(Xp(s));

        /// <summary>Level including temporary boosts (never below 1).</summary>
        public int EffectiveLevel(Skill s) => Math.Max(1, Level(s) + _boost[(int)s]);
        public int Boost(Skill s) => _boost[(int)s];

        public void SetBoost(Skill s, int amount) => _boost[(int)s] = amount;

        public int TotalLevel
        {
            get
            {
                int t = 0;
                foreach (var s in Skills.All) t += Level(s);
                return t;
            }
        }

        /// <summary>
        /// Adds experience. Amount must be non-negative. Returns the number of levels gained.
        /// Only the server should call this.
        /// </summary>
        public int AddXp(Skill s, double amount)
        {
            if (double.IsNaN(amount) || amount <= 0) return 0;
            int i = (int)s;
            int before = Level(s);
            long add = (long)Math.Round(amount * XpScale);
            long cap = _table.MaxXp * XpScale;
            _xpTenths[i] = Math.Min(cap, _xpTenths[i] + add);
            int after = Level(s);
            if (after > before)
                LevelledUp?.Invoke(new LevelUp { Skill = s, OldLevel = before, NewLevel = after });
            return after - before;
        }

        /// <summary>Directly sets stored XP (persistence load only).</summary>
        public void SetXpTenths(Skill s, long tenths)
        {
            _xpTenths[(int)s] = Math.Max(0, Math.Min(_table.MaxXp * XpScale, tenths));
        }

        public bool Meets(Requirement r) => r == null || Level(r.Skill) >= r.Level;

        public bool MeetsAll(IEnumerable<Requirement> reqs, out Requirement failed)
        {
            failed = null;
            if (reqs == null) return true;
            foreach (var r in reqs)
                if (!Meets(r)) { failed = r; return false; }
            return true;
        }

        /// <summary>Overall combat level shown above characters (3..~100).</summary>
        public int CombatLevel
        {
            get
            {
                double defensive = (Level(Skill.Defense) + Level(Skill.Vitality)) * 0.25;
                double offensive = Math.Max((Level(Skill.Attack) + Level(Skill.Strength)) / 2.0,
                                            Math.Max(Level(Skill.Ranged), Level(Skill.Magic))) * 0.5;
                return (int)Math.Floor(defensive + offensive + Level(Skill.Prayer) / 10.0);
            }
        }

        public int MaxHitpoints => EffectiveLevel(Skill.Vitality) * 2;
    }
}
