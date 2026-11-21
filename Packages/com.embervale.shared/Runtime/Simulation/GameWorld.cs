using System;
using System.Collections.Generic;
using Embervale.Shared.Data;
using Embervale.Shared.World;

namespace Embervale.Shared.Simulation
{
    /// <summary>
    /// The authoritative world simulation. Runs on the dedicated server (or in-process for local
    /// play). Clients never mutate this directly; they submit intents through the Request* methods,
    /// which validate everything against server state.
    /// </summary>
    public sealed partial class GameWorld
    {
        public readonly GameDatabase Db;
        public readonly WorldGrid Grid;
        public readonly Pathfinder Pathfinder;
        public readonly IRandom Rng;
        public int Tick { get; private set; }

        /// <summary>Wall clock (unix seconds) provider; injectable for tests.</summary>
        public Func<long> Clock = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private readonly Dictionary<int, Entity> _entities = new Dictionary<int, Entity>();
        private readonly List<Player> _players = new List<Player>();
        private readonly List<Monster> _monsters = new List<Monster>();
        private readonly List<Npc> _npcs = new List<Npc>();
        private readonly List<ResourceNode> _nodes = new List<ResourceNode>();
        private readonly List<GroundItem> _groundItems = new List<GroundItem>();
        private readonly List<Door> _doors = new List<Door>();
        private readonly List<Fire> _fires = new List<Fire>();
        private readonly List<Stall> _stalls = new List<Stall>();
        private readonly List<Entity> _toRemove = new List<Entity>();
        public readonly SpatialHash<Entity> Spatial = new SpatialHash<Entity>(16);
        public readonly Dictionary<string, ShopState> Shops = new Dictionary<string, ShopState>();
        public readonly List<WorldEvent> Events = new List<WorldEvent>();
        private int _nextDynamicId = 1_000_000;
        private int _nextPartyId = 1;

        public IReadOnlyList<Player> Players => _players;
        public IReadOnlyList<Monster> Monsters => _monsters;
        public IReadOnlyList<ResourceNode> Nodes => _nodes;
        public IReadOnlyList<GroundItem> GroundItems => _groundItems;
        public IReadOnlyList<Door> Doors => _doors;
        public IEnumerable<Entity> AllEntities => _entities.Values;

        public const int BankCapacity = 400;
        public const int InventorySize = 28;
        public const float ViewRadius = 48f;

        public GameWorld(GameDatabase db, ulong seed = 12345, WorldGrid grid = null)
        {
            Db = db ?? throw new ArgumentNullException(nameof(db));
            Rng = new XorShiftRandom(seed);
            Grid = grid ?? (db.World != null ? WorldGrid.FromWorld(db.World) : new WorldGrid(128, 128));
            Pathfinder = new Pathfinder(Grid);
            foreach (var s in db.Shops.Values) Shops[s.Id] = new ShopState(s);
            if (db.World != null) PopulateFromWorld(db.World);
        }

        // ------------------------------------------------------------------
        // Entity registry
        // ------------------------------------------------------------------
        public T Register<T>(T e, int id = 0) where T : Entity
        {
            if (id == 0) id = _nextDynamicId++;
            if (_entities.ContainsKey(id)) throw new InvalidOperationException("Duplicate entity id " + id);
            e.Id = id;
            _entities[id] = e;
            Spatial.Update(e, e.Position);
            switch (e)
            {
                case Player p: _players.Add(p); break;
                case Monster m: _monsters.Add(m); break;
                case Npc n: _npcs.Add(n); break;
                case ResourceNode r: _nodes.Add(r); break;
                case GroundItem g: _groundItems.Add(g); break;
                case Door d: _doors.Add(d); break;
                case Fire f: _fires.Add(f); break;
                case Stall s: _stalls.Add(s); break;
            }
            return e;
        }

        public void Unregister(Entity e)
        {
            if (e == null || e.Removed) return;
            e.Removed = true;
            _entities.Remove(e.Id);
            Spatial.Remove(e);
            switch (e)
            {
                case Player p: _players.Remove(p); break;
                case Monster m: _monsters.Remove(m); break;
                case Npc n: _npcs.Remove(n); break;
                case ResourceNode r: _nodes.Remove(r); break;
                case GroundItem g: _groundItems.Remove(g); break;
                case Door d: _doors.Remove(d); break;
                case Fire f: _fires.Remove(f); Grid.RemoveBlocker(f.Position.TileX, f.Position.TileZ); break;
                case Stall s: _stalls.Remove(s); break;
            }
        }

        public Entity Get(int id) => id != 0 && _entities.TryGetValue(id, out var e) ? e : null;
        public T Get<T>(int id) where T : Entity => Get(id) as T;

        public Player FindPlayerByName(string name)
        {
            foreach (var p in _players)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        public void QueryArea(Vec2 center, float radius, List<Entity> results)
        {
            var tmp = new List<Entity>();
            Spatial.Query(center, radius, tmp);
            float r2 = radius * radius;
            foreach (var e in tmp)
            {
                var d = e.Position - center;
                if (d.X * d.X + d.Z * d.Z <= r2) results.Add(e);
            }
        }

        // ------------------------------------------------------------------
        // World population
        // ------------------------------------------------------------------
        private void PopulateFromWorld(WorldDef w)
        {
            foreach (var d in w.Doors)
            {
                var door = Register(new Door { DefId = "door", Position = Vec2.TileCenter(d.X, d.Z), Side = d.Side, Open = d.Open }, d.Id);
                if (!door.Open) Grid.AddBlocker(d.X, d.Z);
            }
            foreach (var s in w.Stations)
                Register(new Station { DefId = s.Type, StationType = s.Type, Model = s.Model, Position = new Vec2(s.X + 0.5f, s.Z + 0.5f), Facing = s.Rot }, s.Id);
            foreach (var n in w.Nodes)
            {
                if (!Db.Nodes.TryGetValue(n.Def, out var def)) continue;
                var node = Register(new ResourceNode { DefId = n.Def, Def = def, Position = new Vec2(n.X, n.Z) }, n.Id);
                node.Blocks = def.Skill != Skill.Fishing;
            }
            foreach (var n in w.Npcs)
            {
                if (!Db.Npcs.TryGetValue(n.Def, out var def)) continue;
                var npc = Register(new Npc { DefId = n.Def, Def = def, Position = new Vec2(n.X, n.Z), Home = new Vec2(n.X, n.Z), Facing = n.Rot }, n.Id);
                npc.Hp = npc.MaxHp;
            }
            foreach (var p in w.FarmPatches)
                Register(new FarmPatch { DefId = "farm_patch", Position = new Vec2(p.X, p.Z) }, p.Id);
            foreach (var s in w.Stalls)
            {
                Db.Thieving.TryGetValue(s.Def == "gem_stall" ? "steal_gem" : "steal_bakery", out var td);
                Register(new Stall { DefId = s.Def, Def = td, Position = new Vec2(s.X, s.Z) }, s.Id);
            }
            foreach (var o in w.Agility)
            {
                if (!Db.Agility.TryGetValue(o.Def, out var def)) continue;
                Register(new Obstacle { DefId = o.Def, Def = def, Position = new Vec2(o.X, o.Z), Order = o.Order, End = new Vec2(o.EndX, o.EndZ) }, o.Id);
            }
            foreach (var sp in w.Spawns)
            {
                if (!Db.Monsters.TryGetValue(sp.Monster, out var def)) continue;
                for (int i = 0; i < sp.Count; i++)
                {
                    var pos = RandomWalkableNear(new Vec2(sp.X, sp.Z), sp.Radius) ?? new Vec2(sp.X + 0.5f, sp.Z + 0.5f);
                    SpawnMonster(def, pos, new Vec2(sp.X, sp.Z), sp.Radius, sp.Id);
                }
            }
        }

        public Monster SpawnMonster(MonsterDef def, Vec2 pos, Vec2 spawnPoint, float radius, int spawnId = 0)
        {
            var m = Register(new Monster
            {
                DefId = def.Id, Def = def, Position = pos, SpawnPoint = spawnPoint, SpawnRadius = Math.Max(1, radius),
                SpawnId = spawnId, State = MonsterState.Idle,
            });
            m.Hp = def.Hp;
            m.NextWanderTick = Tick + Rng.Next(5, 20);
            return m;
        }

        public Vec2? RandomWalkableNear(Vec2 center, float radius)
        {
            for (int i = 0; i < 30; i++)
            {
                int x = (int)Math.Floor(center.X + (Rng.NextDouble() * 2 - 1) * radius);
                int z = (int)Math.Floor(center.Z + (Rng.NextDouble() * 2 - 1) * radius);
                if (Grid.IsWalkable(x, z)) return Vec2.TileCenter(x, z);
            }
            return null;
        }

        public Vec2 Location(string key)
        {
            if (Db.World != null && key != null && Db.World.Locations.TryGetValue(key, out var loc)) return new Vec2(loc.X, loc.Z);
            return new Vec2(Grid.Width / 2f + 0.5f, Grid.Height / 2f + 0.5f);
        }

        // ------------------------------------------------------------------
        // Players
        // ------------------------------------------------------------------
        /// <summary>Creates a brand-new character state with the starter kit (not yet in the world).</summary>
        public Player CreateNewPlayer(string name, Appearance appearance)
        {
            var p = NewPlayerShell(name);
            p.Appearance = appearance?.Clone() ?? new Appearance();
            p.Appearance.Sanitize();
            foreach (var it in Db.Starter.Items) p.Inventory.Add(it.Item, it.Qty);
            p.Position = Location(Db.Starter.SpawnPoint);
            p.Hp = p.MaxHp;
            p.PrayerPoints = p.Skills.Level(Skill.Prayer);
            return p;
        }

        public Player NewPlayerShell(string name)
        {
            var p = new Player
            {
                DefId = "player",
                Name = name,
                Skills = new SkillSet(Db.XpTable, Db.Experience.StartingLevels),
                Inventory = new ItemContainer(Db, InventorySize),
                Equipment = new Equipment(Db),
                Bank = new ItemContainer(Db, BankCapacity, alwaysStack: true),
                Quests = new QuestLog(Db),
            };
            return p;
        }

        public Player AddPlayer(Player p)
        {
            if (!Grid.IsWalkable(p.Position.TileX, p.Position.TileZ))
                p.Position = Location(Db.Starter?.SpawnPoint);
            if (p.Hp <= 0) p.Hp = p.MaxHp;
            p.HookContainers();
            p.Skills.LevelledUp += lu => OnLevelUp(p, lu);
            Register(p);
            foreach (var patch in p.Farming.Patches.Values) FarmingRules.Update(patch, Db, Clock());
            return p;
        }

        public void RemovePlayer(Player p)
        {
            if (p == null || p.Removed) return;
            CancelTrade(p, "The other player has logged out.");
            LeaveParty(p);
            CloseInterfaces(p);
            foreach (var m in _monsters) if (m.TargetId == p.Id) { m.TargetId = 0; m.State = MonsterState.Returning; }
            foreach (var g in _groundItems) if (g.OwnerId == p.Id) g.OwnerId = 0;
            Unregister(p);
        }

        private void OnLevelUp(Player p, LevelUp lu)
        {
            Emit(new WorldEvent { Type = WorldEventType.LevelUp, PlayerId = p.Id, EntityId = p.Id, Skill = lu.Skill, Value = lu.NewLevel });
            Message(p, "Congratulations! Your " + lu.Skill + " level is now " + lu.NewLevel + ".");
            if (lu.Skill == Skill.Vitality) p.Hp = Math.Min(p.MaxHp, p.Hp + (lu.NewLevel - lu.OldLevel) * 2);
            p.MarkStateChanged();
        }

        // ------------------------------------------------------------------
        // Events / messages
        // ------------------------------------------------------------------
        public void Emit(WorldEvent e) => Events.Add(e);

        public void Message(Player p, string text)
        {
            if (p == null) return;
            Emit(new WorldEvent { Type = WorldEventType.GameMessage, PlayerId = p.Id, Text = text });
        }

        public void GiveXp(Player p, Skill s, double amount)
        {
            if (amount <= 0) return;
            p.Skills.AddXp(s, amount);
            p.SkillsDirty = true;
            Emit(new WorldEvent { Type = WorldEventType.XpDrop, PlayerId = p.Id, Skill = s, Value = (int)Math.Round(amount * SkillSet.XpScale) });
        }

        public void Animate(Actor a, ActionAnim anim, int ticks = 2)
        {
            a.SetAnim(anim, Tick, ticks);
            Emit(new WorldEvent { Type = WorldEventType.Animation, EntityId = a.Id, Value = (int)anim });
        }

        // ------------------------------------------------------------------
        // Main loop
        // ------------------------------------------------------------------
        /// <summary>Advances the simulation by one tick (GameTime.TickSeconds).</summary>
        public void Step()
        {
            Tick++;
            foreach (var p in _players.ToArray()) TickPlayer(p);
            foreach (var m in _monsters.ToArray()) TickMonster(m);
            foreach (var n in _npcs) TickNpc(n);
            TickWorldObjects();
            foreach (var e in _toRemove) Unregister(e);
            _toRemove.Clear();
        }

        private void TickWorldObjects()
        {
            foreach (var n in _nodes)
            {
                if (n.DepletedUntilTick > 0 && Tick >= n.DepletedUntilTick)
                {
                    n.DepletedUntilTick = 0;
                    n.MarkStateChanged();
                }
            }
            foreach (var d in _doors)
            {
                if (d.Open && d.AutoCloseTick > 0 && Tick >= d.AutoCloseTick)
                {
                    // don't close on someone standing in the doorway
                    bool occupied = false;
                    foreach (var p in _players) if (p.Position.TileX == d.Position.TileX && p.Position.TileZ == d.Position.TileZ) occupied = true;
                    if (occupied) d.AutoCloseTick = Tick + 5;
                    else SetDoor(d, false);
                }
            }
            foreach (var f in _fires) if (Tick >= f.ExpiresTick) _toRemove.Add(f);
            foreach (var g in _groundItems)
            {
                if (g.OwnerId != 0 && Tick >= g.PublicAtTick) { g.OwnerId = 0; g.MarkStateChanged(); }
                if (Tick >= g.DespawnAtTick) _toRemove.Add(g);
            }
            foreach (var s in _stalls)
                if (s.EmptyUntilTick > 0 && Tick >= s.EmptyUntilTick) { s.EmptyUntilTick = 0; s.MarkStateChanged(); }
            if (Tick % 20 == 0)
                foreach (var shop in Shops.Values) shop.Restock();
        }

        public void SetDoor(Door d, bool open)
        {
            if (d.Open == open) return;
            d.Open = open;
            if (open) { Grid.RemoveBlocker(d.Position.TileX, d.Position.TileZ); d.AutoCloseTick = Tick + 60; }
            else { Grid.AddBlocker(d.Position.TileX, d.Position.TileZ); d.AutoCloseTick = 0; }
            d.MarkStateChanged();
            Emit(new WorldEvent { Type = WorldEventType.Sound, EntityId = d.Id, Text = open ? "door_open" : "door_close" });
        }

        // ------------------------------------------------------------------
        // Movement
        // ------------------------------------------------------------------
        /// <summary>Moves an actor along its path for one tick. Returns true if it moved.</summary>
        public bool AdvanceMovement(Actor a)
        {
            if (a.Path.Count == 0) return false;
            if (a.StunnedUntil > Tick) return false;
            int steps = a.Running ? 2 : 1;
            bool moved = false;
            for (int i = 0; i < steps && a.Path.Count > 0; i++)
            {
                var next = a.Path[0];
                int fx = a.Position.TileX, fz = a.Position.TileZ;
                int tx = next.TileX, tz = next.TileZ;
                if (!Grid.CanStep(fx, fz, tx, tz))
                {
                    // a closed door or new obstacle: try to open the door, otherwise stop
                    var door = DoorAt(tx, tz);
                    if (door != null && !door.Open && a is Player) { SetDoor(door, true); }
                    else { a.Path.Clear(); break; }
                    if (!Grid.CanStep(fx, fz, tx, tz)) { a.Path.Clear(); break; }
                }
                a.FaceTowards(next);
                a.Position = next;
                a.Path.RemoveAt(0);
                moved = true;
            }
            if (moved)
            {
                Spatial.Update(a, a.Position);
                if (a is Player p && p.Running)
                {
                    p.RunEnergy = Math.Max(0, p.RunEnergy - 0.6 * (1.0 - p.Skills.Level(Skill.Agility) / 200.0));
                    if (p.RunEnergy <= 0) p.Running = false;
                }
            }
            return moved;
        }

        public Door DoorAt(int x, int z)
        {
            foreach (var d in _doors) if (d.Position.TileX == x && d.Position.TileZ == z) return d;
            return null;
        }

        /// <summary>Paths through doors by treating closed doors as walkable during planning.</summary>
        public List<Vec2> PlanPath(Vec2 from, int gx, int gz, int range, bool allowClosest = false, int maxDistance = 96)
        {
            var opened = new List<Door>();
            foreach (var d in _doors)
            {
                if (!d.Open && Math.Abs(d.Position.TileX - from.TileX) <= maxDistance && Math.Abs(d.Position.TileZ - from.TileZ) <= maxDistance)
                {
                    Grid.RemoveBlocker(d.Position.TileX, d.Position.TileZ);
                    opened.Add(d);
                }
            }
            try
            {
                return Pathfinder.FindPath(from.TileX, from.TileZ, gx, gz, range, allowClosest, maxDistance);
            }
            finally
            {
                foreach (var d in opened) Grid.AddBlocker(d.Position.TileX, d.Position.TileZ);
            }
        }

        private void TickNpc(Npc n)
        {
            if (n.Def.Wander > 0 && n.Path.Count == 0 && Tick >= n.NextWanderTick)
            {
                n.NextWanderTick = Tick + Rng.Next(8, 30);
                bool talking = false;
                foreach (var p in _players) if (p.DialogueNpcId == n.Id) talking = true;
                if (!talking)
                {
                    var dest = RandomWalkableNear(n.Home, n.Def.Wander);
                    if (dest.HasValue)
                    {
                        var path = Pathfinder.FindPath(n.Position.TileX, n.Position.TileZ, dest.Value.TileX, dest.Value.TileZ, 0, false, 16);
                        if (path != null) { n.Path.Clear(); n.Path.AddRange(path); }
                    }
                }
            }
            if (AdvanceMovement(n)) n.MarkStateChanged();
        }

        // ------------------------------------------------------------------
        // Ground items
        // ------------------------------------------------------------------
        public GroundItem DropToGround(ItemStack stack, Vec2 pos, Player owner, int publicAfterTicks = 100, int despawnAfterTicks = 400)
        {
            if (stack.IsEmpty) return null;
            // merge with an identical stackable pile on the same tile
            if (Db.IsStackable(stack.ItemId))
            {
                foreach (var g in _groundItems)
                    if (g.Stack.ItemId == stack.ItemId && g.Position.TileX == pos.TileX && g.Position.TileZ == pos.TileZ && g.OwnerId == (owner?.Id ?? 0))
                    {
                        g.Stack = new ItemStack(stack.ItemId, g.Stack.Quantity + stack.Quantity);
                        g.DespawnAtTick = Tick + despawnAfterTicks;
                        g.MarkStateChanged();
                        return g;
                    }
            }
            return Register(new GroundItem
            {
                DefId = stack.ItemId,
                Stack = stack,
                Position = Vec2.TileCenter(pos.TileX, pos.TileZ),
                OwnerId = owner?.Id ?? 0,
                OwnerCharacterId = owner?.CharacterId ?? 0,
                PublicAtTick = Tick + publicAfterTicks,
                DespawnAtTick = Tick + despawnAfterTicks,
            });
        }

        public bool CanSee(Player viewer, Entity e)
        {
            if (e is GroundItem g && g.OwnerId != 0 && g.OwnerId != viewer.Id) return false;
            return !e.Removed;
        }

        // ------------------------------------------------------------------
        // Interfaces
        // ------------------------------------------------------------------
        public void CloseInterfaces(Player p)
        {
            if (p.DialogueId != null)
            {
                p.DialogueId = null; p.DialogueNode = null; p.DialogueNpcId = 0;
                Emit(new WorldEvent { Type = WorldEventType.DialogueClose, PlayerId = p.Id });
            }
            if (p.OpenShopId != null)
            {
                p.OpenShopId = null; p.OpenShopNpcId = 0;
                Emit(new WorldEvent { Type = WorldEventType.ShopClose, PlayerId = p.Id });
            }
            if (p.OpenBankEntityId != 0)
            {
                p.OpenBankEntityId = 0;
                Emit(new WorldEvent { Type = WorldEventType.BankClose, PlayerId = p.Id });
            }
        }

        public void RequestCloseInterface(Player p)
        {
            if (p.Trade != null) CancelTrade(p, "Trade declined.");
            CloseInterfaces(p);
        }
    }
}
