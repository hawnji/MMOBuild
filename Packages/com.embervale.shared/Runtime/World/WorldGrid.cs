using System;
using System.Collections.Generic;
using Embervale.Shared.Data;

namespace Embervale.Shared.World
{
    /// <summary>
    /// Server-side collision grid (1 tile = 1 m). Static blocking comes from world.json; dynamic
    /// blocking (closed doors, resource nodes, stations) is layered on top.
    /// </summary>
    public sealed class WorldGrid
    {
        public int Width { get; }
        public int Height { get; }
        private readonly byte[] _static;
        private readonly byte[] _tiles;
        private readonly int[] _dynamic;   // reference-counted dynamic blockers

        public WorldGrid(int width, int height, byte[] blocked = null, byte[] tiles = null)
        {
            Width = width;
            Height = height;
            _static = blocked ?? new byte[width * height];
            _tiles = tiles ?? new byte[width * height];
            if (_static.Length != width * height) throw new ArgumentException("blocked grid size mismatch");
            _dynamic = new int[width * height];
        }

        public static WorldGrid FromWorld(WorldDef world)
        {
            return new WorldGrid(world.Width, world.Height, world.DecodeBlocked(), world.DecodeTiles());
        }

        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Height;

        public bool IsWalkable(int x, int z) =>
            InBounds(x, z) && _static[z * Width + x] == 0 && _dynamic[z * Width + x] == 0;

        public bool IsStaticBlocked(int x, int z) => !InBounds(x, z) || _static[z * Width + x] != 0;

        public TileType TileAt(int x, int z) => InBounds(x, z) ? (TileType)_tiles[z * Width + x] : TileType.Rock;

        public void AddBlocker(int x, int z) { if (InBounds(x, z)) _dynamic[z * Width + x]++; }
        public void RemoveBlocker(int x, int z) { if (InBounds(x, z) && _dynamic[z * Width + x] > 0) _dynamic[z * Width + x]--; }

        /// <summary>Diagonal moves may not cut corners past blocked tiles.</summary>
        public bool CanStep(int fx, int fz, int tx, int tz)
        {
            if (!IsWalkable(tx, tz)) return false;
            int dx = tx - fx, dz = tz - fz;
            if (Math.Abs(dx) > 1 || Math.Abs(dz) > 1) return false;
            if (dx != 0 && dz != 0)
                return IsWalkable(fx + dx, fz) && IsWalkable(fx, fz + dz);
            return true;
        }

        /// <summary>Line-of-sight for ranged attacks and spells (walls block, water does not).</summary>
        public bool HasLineOfSight(int x0, int z0, int x1, int z1)
        {
            int dx = Math.Abs(x1 - x0), dz = Math.Abs(z1 - z0);
            int sx = x0 < x1 ? 1 : -1, sz = z0 < z1 ? 1 : -1;
            int err = dx - dz;
            int x = x0, z = z0;
            while (x != x1 || z != z1)
            {
                int e2 = 2 * err;
                if (e2 > -dz) { err -= dz; x += sx; }
                if (e2 < dx) { err += dx; z += sz; }
                if (x == x1 && z == z1) break;
                if (IsStaticBlocked(x, z))
                {
                    var t = TileAt(x, z);
                    if (t != TileType.Water && t != TileType.DeepWater) return false;
                }
            }
            return true;
        }
    }

    /// <summary>A* pathfinding over the WorldGrid with 8-way movement.</summary>
    public sealed class Pathfinder
    {
        private readonly WorldGrid _grid;
        private readonly int[] _gScore;
        private readonly int[] _cameFrom;
        private readonly int[] _visitStamp;
        private int _stamp;

        /// <summary>Hard cap on explored nodes so a bad request can't stall the server tick.</summary>
        public int MaxExpanded = 12000;

        public Pathfinder(WorldGrid grid)
        {
            _grid = grid;
            int n = grid.Width * grid.Height;
            _gScore = new int[n];
            _cameFrom = new int[n];
            _visitStamp = new int[n];
        }

        private static int Heuristic(int x, int z, int tx, int tz)
        {
            int dx = Math.Abs(x - tx), dz = Math.Abs(z - tz);
            return 10 * (dx + dz) - 6 * Math.Min(dx, dz);
        }

        /// <summary>
        /// Finds a path from start to any tile within <paramref name="range"/> (Chebyshev) of the goal.
        /// range 0 = exact tile. Returns tile centres excluding the start, or null if unreachable.
        /// If the goal is unreachable and <paramref name="allowClosest"/> is set, returns a path to the
        /// closest reachable tile instead.
        /// </summary>
        public List<Vec2> FindPath(int sx, int sz, int gx, int gz, int range = 0, bool allowClosest = false, int maxDistance = 96)
        {
            if (!_grid.InBounds(sx, sz) || !_grid.InBounds(gx, gz)) return null;
            if (Math.Max(Math.Abs(sx - gx), Math.Abs(sz - gz)) > maxDistance) return null;
            if (Math.Max(Math.Abs(sx - gx), Math.Abs(sz - gz)) <= range && (range > 0 || (sx == gx && sz == gz)))
                return new List<Vec2>();

            _stamp++;
            if (_stamp == int.MaxValue) { Array.Clear(_visitStamp, 0, _visitStamp.Length); _stamp = 1; }
            int w = _grid.Width;
            var open = new MinHeap();
            int start = sz * w + sx;
            _visitStamp[start] = _stamp;
            _gScore[start] = 0;
            _cameFrom[start] = -1;
            open.Push(start, Heuristic(sx, sz, gx, gz));

            int best = start, bestH = Heuristic(sx, sz, gx, gz);
            int expanded = 0;
            int found = -1;
            var closed = new HashSet<int>();
            while (open.Count > 0 && expanded < MaxExpanded)
            {
                int cur = open.Pop();
                if (!closed.Add(cur)) continue;
                expanded++;
                int cx = cur % w, cz = cur / w;
                if (Math.Max(Math.Abs(cx - gx), Math.Abs(cz - gz)) <= range && (range > 0 || cur == gz * w + gx))
                {
                    // ranged/adjacent goals additionally need to not stand inside the target
                    found = cur;
                    break;
                }
                int h = Heuristic(cx, cz, gx, gz);
                if (h < bestH) { bestH = h; best = cur; }
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx, nz = cz + dz;
                    if (!_grid.CanStep(cx, cz, nx, nz)) continue;
                    if (Math.Abs(nx - sx) > maxDistance || Math.Abs(nz - sz) > maxDistance) continue;
                    int ni = nz * w + nx;
                    int cost = _gScore[cur] + (dx != 0 && dz != 0 ? 14 : 10);
                    if (_visitStamp[ni] == _stamp && cost >= _gScore[ni]) continue;
                    _visitStamp[ni] = _stamp;
                    _gScore[ni] = cost;
                    _cameFrom[ni] = cur;
                    open.Push(ni, cost + Heuristic(nx, nz, gx, gz));
                }
            }
            if (found < 0)
            {
                if (!allowClosest || best == start) return null;
                found = best;
            }
            var path = new List<Vec2>();
            for (int c = found; c != start && c >= 0; c = _cameFrom[c])
                path.Add(Vec2.TileCenter(c % w, c / w));
            path.Reverse();
            return path;
        }

        private sealed class MinHeap
        {
            private readonly List<(int node, int pri)> _h = new List<(int, int)>();
            public int Count => _h.Count;

            public void Push(int node, int pri)
            {
                _h.Add((node, pri));
                int i = _h.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (_h[p].pri <= _h[i].pri) break;
                    var t = _h[p]; _h[p] = _h[i]; _h[i] = t;
                    i = p;
                }
            }

            public int Pop()
            {
                var top = _h[0].node;
                var last = _h[_h.Count - 1];
                _h.RemoveAt(_h.Count - 1);
                if (_h.Count > 0)
                {
                    _h[0] = last;
                    int i = 0;
                    while (true)
                    {
                        int l = i * 2 + 1, r = l + 1, m = i;
                        if (l < _h.Count && _h[l].pri < _h[m].pri) m = l;
                        if (r < _h.Count && _h[r].pri < _h[m].pri) m = r;
                        if (m == i) break;
                        var t = _h[m]; _h[m] = _h[i]; _h[i] = t;
                        i = m;
                    }
                }
                return top;
            }
        }
    }

    /// <summary>Uniform-grid spatial hash used for interest management and proximity queries.</summary>
    public sealed class SpatialHash<T> where T : class
    {
        private readonly int _cell;
        private readonly Dictionary<long, List<T>> _cells = new Dictionary<long, List<T>>();
        private readonly Dictionary<T, long> _where = new Dictionary<T, long>();

        public SpatialHash(int cellSize) { _cell = Math.Max(1, cellSize); }

        private long Key(Vec2 p) => ((long)(int)Math.Floor(p.X / _cell) << 32) ^ (uint)(int)Math.Floor(p.Z / _cell);

        public void Update(T item, Vec2 pos)
        {
            long k = Key(pos);
            if (_where.TryGetValue(item, out var old))
            {
                if (old == k) return;
                _cells[old].Remove(item);
            }
            if (!_cells.TryGetValue(k, out var list)) _cells[k] = list = new List<T>();
            list.Add(item);
            _where[item] = k;
        }

        public void Remove(T item)
        {
            if (_where.TryGetValue(item, out var old))
            {
                _cells[old].Remove(item);
                _where.Remove(item);
            }
        }

        public void Query(Vec2 center, float radius, List<T> results)
        {
            int x0 = (int)Math.Floor((center.X - radius) / _cell), x1 = (int)Math.Floor((center.X + radius) / _cell);
            int z0 = (int)Math.Floor((center.Z - radius) / _cell), z1 = (int)Math.Floor((center.Z + radius) / _cell);
            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                long k = ((long)x << 32) ^ (uint)z;
                if (_cells.TryGetValue(k, out var list)) results.AddRange(list);
            }
        }
    }
}
