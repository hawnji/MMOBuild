using System;
using System.IO;
using Embervale.Shared;
using Embervale.Shared.Data;
using Embervale.Shared.Simulation;

namespace Embervale.Tests
{
    public static class TestUtil
    {
        private static readonly Lazy<GameDatabase> _db = new Lazy<GameDatabase>(() => GameDatabase.LoadFromDirectory(FindGameData()));
        public static GameDatabase Db => _db.Value;

        public static string FindGameData()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var p = Path.Combine(dir.FullName, "Assets", "StreamingAssets", "GameData");
                if (File.Exists(Path.Combine(p, "items.json"))) return p;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("GameData not found");
        }

        public static GameWorld NewWorld(ulong seed = 42) => new GameWorld(Db, seed) { Clock = () => 1_700_000_000 };

        /// <summary>Adds a fresh character standing at the Brookhollow square.</summary>
        public static Player AddPlayer(GameWorld w, string name = "Tester", long charId = 0)
        {
            var p = w.CreateNewPlayer(name, new Appearance());
            p.CharacterId = charId;
            w.AddPlayer(p);
            return p;
        }

        public static void Run(GameWorld w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        /// <summary>Steps until the condition holds or the tick budget runs out.</summary>
        public static bool RunUntil(GameWorld w, Func<bool> cond, int maxTicks = 400)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (cond()) return true;
                w.Step();
            }
            return cond();
        }

        public static void Teleport(GameWorld w, Player p, Vec2 pos)
        {
            p.Position = pos;
            w.Spatial.Update(p, pos);
        }

        public static T Nearest<T>(GameWorld w, Vec2 from, Func<T, bool> pred = null) where T : Entity
        {
            T best = null;
            float bd = float.MaxValue;
            foreach (var e in w.AllEntities)
            {
                if (!(e is T t) || (pred != null && !pred(t))) continue;
                float d = Vec2.Distance(from, e.Position);
                if (d < bd) { bd = d; best = t; }
            }
            return best;
        }

        /// <summary>A walkable tile next to an entity.</summary>
        public static Vec2 StandNextTo(GameWorld w, Entity e)
        {
            for (int r = 1; r <= 3; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = e.Position.TileX + dx, z = e.Position.TileZ + dz;
                        if (w.Grid.IsWalkable(x, z)) return Vec2.TileCenter(x, z);
                    }
            throw new InvalidOperationException("No walkable tile near " + e.DisplayName);
        }
    }
}
