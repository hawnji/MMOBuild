using System;
using Embervale.Shared.Data;

namespace Embervale.Shared
{
    /// <summary>
    /// Precomputed experience thresholds. Total XP required to reach level L:
    ///   floor(base * (growth^(L-1) - 1) / (growth - 1) + linear * (L-1))
    /// All parameters are data driven (experience.json).
    /// </summary>
    public sealed class ExperienceTable
    {
        private readonly long[] _thresholds; // index = level, value = total xp needed
        public int MaxLevel { get; }
        public long MaxXp => _thresholds[MaxLevel] * 2 + 1_000_000; // hard cap well past max level

        public ExperienceTable(ExperienceConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            MaxLevel = Math.Max(2, cfg.MaxLevel);
            _thresholds = new long[MaxLevel + 2];
            for (int level = 1; level <= MaxLevel + 1; level++)
                _thresholds[level] = Compute(cfg, level);
            for (int l = 2; l <= MaxLevel + 1; l++)
                if (_thresholds[l] <= _thresholds[l - 1])
                    throw new ArgumentException("Experience formula must be strictly increasing (level " + l + ")");
        }

        private static long Compute(ExperienceConfig cfg, int level)
        {
            if (level <= 1) return 0;
            int n = level - 1;
            double geometric = Math.Abs(cfg.Growth - 1.0) < 1e-9
                ? cfg.Base * n
                : cfg.Base * (Math.Pow(cfg.Growth, n) - 1.0) / (cfg.Growth - 1.0);
            return (long)Math.Floor(geometric + cfg.Linear * n);
        }

        /// <summary>Total XP needed to be at <paramref name="level"/>.</summary>
        public long XpForLevel(int level)
        {
            if (level <= 1) return 0;
            if (level > MaxLevel) level = MaxLevel;
            return _thresholds[level];
        }

        public int LevelForXp(long xp)
        {
            if (xp <= 0) return 1;
            // binary search over thresholds
            int lo = 1, hi = MaxLevel;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_thresholds[mid] <= xp) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        /// <summary>XP remaining until the next level, or 0 at max level.</summary>
        public long XpToNextLevel(long xp)
        {
            int level = LevelForXp(xp);
            if (level >= MaxLevel) return 0;
            return _thresholds[level + 1] - xp;
        }

        /// <summary>Progress through the current level in [0,1].</summary>
        public float Progress(long xp)
        {
            int level = LevelForXp(xp);
            if (level >= MaxLevel) return 1f;
            long start = _thresholds[level], end = _thresholds[level + 1];
            return (float)(xp - start) / (end - start);
        }
    }
}
