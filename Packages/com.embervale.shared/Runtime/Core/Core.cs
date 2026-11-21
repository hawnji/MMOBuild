using System;

namespace Embervale.Shared
{
    /// <summary>2D position on the world ground plane (X east, Z north), in metres/tiles.</summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Z;

        public Vec2(float x, float z) { X = x; Z = z; }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Z + b.Z);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Z - b.Z);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Z * s);

        public float Length => (float)Math.Sqrt(X * X + Z * Z);
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;

        /// <summary>Chebyshev (king-move) distance in whole tiles.</summary>
        public static int TileDistance(Vec2 a, Vec2 b) =>
            Math.Max(Math.Abs(a.TileX - b.TileX), Math.Abs(a.TileZ - b.TileZ));

        public int TileX => (int)Math.Floor(X);
        public int TileZ => (int)Math.Floor(Z);
        public static Vec2 TileCenter(int x, int z) => new Vec2(x + 0.5f, z + 0.5f);

        public Vec2 MoveTowards(Vec2 target, float maxStep)
        {
            var d = target - this;
            float len = d.Length;
            if (len <= maxStep || len < 1e-5f) return target;
            return this + d * (maxStep / len);
        }

        public bool Equals(Vec2 o) => X == o.X && Z == o.Z;
        public override bool Equals(object obj) => obj is Vec2 o && Equals(o);
        public override int GetHashCode() => X.GetHashCode() * 397 ^ Z.GetHashCode();
        public override string ToString() => "(" + X.ToString("0.0") + ", " + Z.ToString("0.0") + ")";
    }

    /// <summary>Deterministic, seedable random source (xorshift128+), safe to use from the simulation.</summary>
    public interface IRandom
    {
        double NextDouble();
        int Next(int minInclusive, int maxInclusive);
    }

    public sealed class XorShiftRandom : IRandom
    {
        private ulong _s0, _s1;

        public XorShiftRandom(ulong seed)
        {
            _s0 = SplitMix(ref seed);
            _s1 = SplitMix(ref seed);
            if (_s0 == 0 && _s1 == 0) _s1 = 1;
        }

        private static ulong SplitMix(ref ulong x)
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            ulong x = _s0, y = _s1;
            _s0 = y;
            x ^= x << 23;
            _s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
            return _s1 + y;
        }

        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        public int Next(int minInclusive, int maxInclusive)
        {
            if (maxInclusive <= minInclusive) return minInclusive;
            ulong range = (ulong)((long)maxInclusive - minInclusive + 1);
            return (int)(minInclusive + (long)(NextULong() % range));
        }
    }

    public static class RandomExtensions
    {
        public static bool Chance(this IRandom r, double p) => p > 0 && r.NextDouble() < p;
    }

    /// <summary>Server tick timing constants.</summary>
    public static class GameTime
    {
        /// <summary>Simulation tick length in seconds. Actions, combat and movement advance per tick.</summary>
        public const float TickSeconds = 0.5f;
        public const int TicksPerMinute = 120;
        public const float WalkTilesPerTick = 1f;
        public const float RunTilesPerTick = 2f;
    }
}
