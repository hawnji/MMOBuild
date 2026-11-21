using System.Collections.Generic;
using System.Linq;
using Embervale.Shared.Data;
using Embervale.Shared.Net;
using Embervale.Shared.Simulation;
using UnityEngine;

namespace Embervale.Client
{
    /// <summary>
    /// Client-side representation of one server entity. Positions arrive once per 0.5 s tick, so the view
    /// moves smoothly towards the latest authoritative tile at walking/running speed and snaps on teleports.
    /// </summary>
    public sealed class EntityView : MonoBehaviour
    {
        [System.NonSerialized] public EntitySnapshot Snapshot;
        public CharacterRig Rig;
        public AnimationDriver Anim;
        public GameObject Visual;
        public float OverheadHeight = 2.2f;
        public float Hover;
        public bool IsLocalPlayer;
        public string DisplayName => Snapshot?.Name ?? "";

        private Vector3 _target;
        private float _facing;
        private WorldTerrain _terrain;
        private bool _dead;
        private float _lastMoveTime;
        private string _visualKey;
        private System.Action<EntityView> _rebuild;

        public bool IsActor => Snapshot.Kind == EntityKind.Player || Snapshot.Kind == EntityKind.Monster || Snapshot.Kind == EntityKind.Npc;

        public void Init(EntitySnapshot s, WorldTerrain terrain, System.Action<EntityView> rebuild)
        {
            Snapshot = s;
            _terrain = terrain;
            _rebuild = rebuild;
            _target = Ground(s.X, s.Z);
            transform.position = _target;
            _facing = s.Facing;
            transform.rotation = Quaternion.Euler(0, Yaw(_facing), 0);
            _visualKey = VisualKey(s);
            rebuild(this);
            ApplyState(true);
        }

        /// <summary>The Blender pipeline exports models facing -Z; the server's facing 0 means +Z (north).</summary>
        public const float ModelYawOffset = 180f;
        private float Yaw(float facing) => IsActor ? facing + ModelYawOffset : facing;

        private Vector3 Ground(float x, float z) => new Vector3(x, _terrain.SampleHeight(x, z) + Hover, z);

        /// <summary>Visual-affecting state; when it changes the model is rebuilt (e.g. node depleted).</summary>
        private static string VisualKey(EntitySnapshot s)
        {
            switch (s.Kind)
            {
                case EntityKind.Node: return s.Has(EntitySnapshot.FlagDepleted) ? "d" : "n";
                case EntityKind.Player: return s.Equipped == null ? "" : string.Join(",", s.Equipped);
                default: return "";
            }
        }

        public void UpdateSnapshot(EntitySnapshot s)
        {
            Snapshot = s;
            var key = VisualKey(s);
            if (key != _visualKey)
            {
                _visualKey = key;
                if (s.Kind == EntityKind.Player && Rig != null) Rig.SetEquipped(s.Equipped, GameSession.Instance.Db);
                else _rebuild(this);
            }
            var t = Ground(s.X, s.Z);
            if ((t - _target).sqrMagnitude > 0.0001f) _lastMoveTime = Time.time;
            _target = t;
            ApplyState(false);
        }

        public void Teleport(float x, float z)
        {
            _target = Ground(x, z);
            transform.position = _target;
        }

        private void ApplyState(bool initial)
        {
            var s = Snapshot;
            bool dead = s.Has(EntitySnapshot.FlagDead);
            if (dead && !_dead && Anim != null) Anim.PlayOnce("Death");
            if (!dead && _dead && Anim != null) Anim.ResetDeath();
            _dead = dead;
            if (s.Kind == EntityKind.Door && Visual != null)
            {
                var hinge = Visual.transform;
                float open = s.Has(EntitySnapshot.FlagOpen) ? -95f : 0f;
                hinge.localRotation = Quaternion.Euler(0, open, 0);
            }
            if (s.Kind == EntityKind.Monster && dead && !IsActorVisibleWhenDead()) SetVisible(false);
            else if (Visual != null && !Visual.activeSelf) SetVisible(true);
        }

        private bool IsActorVisibleWhenDead() => Time.time - _deathTime < 2.5f;
        private float _deathTime = -100f;

        public void OnDeathEvent()
        {
            _deathTime = Time.time;
            Anim?.PlayOnce("Death");
        }

        private void SetVisible(bool v)
        {
            if (Visual != null) Visual.SetActive(v);
        }

        private void Update()
        {
            var pos = transform.position;
            var to = _target - pos;
            to.y = 0;
            float dist = to.magnitude;
            bool running = Snapshot.Has(EntitySnapshot.FlagRunning);
            if (dist > 8f)
            {
                transform.position = _target;
            }
            else if (dist > 0.01f)
            {
                // a walking actor covers 1 tile per tick (2 m/s); catch up faster when behind
                float speed = (running ? 4f : 2f) * Mathf.Lerp(1f, 2.2f, Mathf.Clamp01((dist - 1.2f) / 2f));
                var step = to.normalized * Mathf.Min(dist, speed * Time.deltaTime);
                var np = pos + step;
                np.y = _terrain.SampleHeight(np.x, np.z) + Hover;
                transform.position = np;
                if (IsActor) _facing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            }
            else
            {
                transform.position = new Vector3(_target.x, Mathf.Lerp(pos.y, _target.y, 10f * Time.deltaTime), _target.z);
                if (IsActor) _facing = Snapshot.Facing;
            }
            if (IsActor)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, Yaw(_facing), 0), 12f * Time.deltaTime);
            if (Anim != null && !_dead)
            {
                bool moving = dist > 0.05f || Time.time - _lastMoveTime < 0.1f;
                if (moving) Anim.SetLoop(running ? "Run" : "Walk");
                else
                {
                    var clip = AnimationDriver.ClipFor(Snapshot.Anim);
                    Anim.SetLoop(AnimationDriver.IsLooping(clip) && clip != "Walk" && clip != "Run" ? clip : "Idle");
                }
            }
        }

        /// <summary>Options shown in the right-click menu, first one is the left-click default.</summary>
        public List<string> Options(GameDatabase db, int selfId)
        {
            var o = new List<string>();
            var s = Snapshot;
            switch (s.Kind)
            {
                case EntityKind.Player:
                    if (s.Id != selfId) { o.Add("Trade"); o.Add("Follow"); }
                    break;
                case EntityKind.Monster:
                    if (!s.Has(EntitySnapshot.FlagDead)) o.Add("Attack");
                    break;
                case EntityKind.Npc:
                {
                    o.Add("Talk-to");
                    db.Npcs.TryGetValue(s.DefId, out var nd);
                    if (nd != null && nd.Role == "Merchant") o.Add("Trade");
                    if (nd != null && nd.Role == "Banker") o.Insert(0, "Bank");
                    if (nd != null && nd.Role == "Exchange") o.Insert(0, "Exchange");
                    if (db.Thieving.Values.Any(t => t.Targets != null && t.Targets.Contains(s.DefId))) o.Add("Pickpocket");
                    break;
                }
                case EntityKind.Node:
                {
                    db.Nodes.TryGetValue(s.DefId, out var nd);
                    if (s.Has(EntitySnapshot.FlagDepleted)) break;
                    switch (nd?.Skill)
                    {
                        case Skill.Woodcutting: o.Add("Chop down"); break;
                        case Skill.Mining: o.Add("Mine"); break;
                        case Skill.Fishing: o.Add(nd.ToolType == "Harpoon" ? "Harpoon" : nd.ToolType == "FishingNet" ? "Net" : "Bait"); break;
                        case Skill.Runecrafting: o.Add("Mine"); break;
                        default: o.Add("Gather"); break;
                    }
                    break;
                }
                case EntityKind.GroundItem: o.Add("Take"); break;
                case EntityKind.Door: o.Add(s.Has(EntitySnapshot.FlagOpen) ? "Close" : "Open"); break;
                case EntityKind.Station: o.Add(StationVerb(s.DefId)); break;
                case EntityKind.Fire: o.Add("Cook-at"); break;
                case EntityKind.FarmPatch: o.Add("Tend"); break;
                case EntityKind.Stall: o.Add("Steal-from"); break;
                case EntityKind.Obstacle: o.Add(ObstacleVerb(s.DefId)); break;
            }
            o.Add("Examine");
            return o;
        }

        private static string StationVerb(string type)
        {
            if (type == null) return "Use";
            if (type.StartsWith("Altar_")) return "Craft-rune";
            switch (type)
            {
                case "Anvil": return "Smith";
                case "Furnace": return "Smelt";
                case "Range": return "Cook";
                case "PrayerAltar": return "Pray-at";
                case "Water": return "Fill";
                case "Tannery": return "Tan";
                case "Mill": return "Grind";
                default: return "Use";
            }
        }

        private static string ObstacleVerb(string def)
        {
            switch (def)
            {
                case "log_balance": return "Walk-across";
                case "rope_swing": return "Swing-on";
                case "wall_climb": return "Climb";
                case "ledge_walk": return "Walk-along";
                default: return "Cross";
            }
        }
    }

    /// <summary>Creates and updates EntityViews from GameClient events, and owns their visuals.</summary>
    public sealed class EntityManager : MonoBehaviour
    {
        public readonly Dictionary<int, EntityView> Views = new Dictionary<int, EntityView>();
        private GameSession _session;
        private WorldTerrain _terrain;
        private Dictionary<string, Newtonsoft.Json.Linq.JObject> _creatureVisuals;

        public void Init(GameSession session, WorldTerrain terrain)
        {
            _session = session;
            _terrain = terrain;
            var c = session.Client;
            c.OnEntitySpawn += Spawn;
            c.OnEntityUpdate += UpdateEntity;
            c.OnEntityDespawn += Despawn;
            c.OnTeleport += (id, x, z) => { if (Views.TryGetValue(id, out var v)) v.Teleport(x, z); };
            c.OnAnimation += (id, a) => { if (Views.TryGetValue(id, out var v) && v.Anim != null) v.Anim.PlayOnce(AnimationDriver.ClipFor(a)); };
            c.OnDeath += (id, killer) => { if (Views.TryGetValue(id, out var v)) v.OnDeathEvent(); };
            c.OnFarmingChanged += () => { foreach (var v in Views.Values) if (v.Snapshot.Kind == EntityKind.FarmPatch) RefreshPatch(v); };
            foreach (var e in c.Entities.Values) Spawn(e);
            try
            {
                var path = System.IO.Path.Combine(Application.streamingAssetsPath, "Art", "creature_visuals.json");
                _creatureVisuals = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, Newtonsoft.Json.Linq.JObject>>(System.IO.File.ReadAllText(path));
            }
            catch (System.Exception e) { Debug.LogWarning("[Entities] creature_visuals.json: " + e.Message); _creatureVisuals = new Dictionary<string, Newtonsoft.Json.Linq.JObject>(); }
        }

        public EntityView Get(int id) => Views.TryGetValue(id, out var v) ? v : null;

        private void Spawn(EntitySnapshot s)
        {
            if (Views.ContainsKey(s.Id)) { UpdateEntity(s); return; }
            var go = new GameObject(s.Kind + " " + s.Name + " #" + s.Id);
            go.transform.SetParent(transform, false);
            var view = go.AddComponent<EntityView>();
            view.IsLocalPlayer = s.Id == _session.Client.SelfId;
            Views[s.Id] = view;
            view.Init(s, _terrain, BuildVisual);
        }

        private void UpdateEntity(EntitySnapshot s)
        {
            if (Views.TryGetValue(s.Id, out var v)) v.UpdateSnapshot(s);
            else Spawn(s);
        }

        private void Despawn(int id)
        {
            if (!Views.TryGetValue(id, out var v)) return;
            Views.Remove(id);
            if (v != null) Destroy(v.gameObject);
        }

        // ------------------------------------------------------------------ visuals
        private void BuildVisual(EntityView v)
        {
            if (v.Visual != null) Destroy(v.Visual);
            v.Rig = null;
            v.Anim = null;
            var s = v.Snapshot;
            var db = _session.Db;
            var assets = AssetRegistry.Instance;
            var wdef = db.World;
            GameObject vis = null;
            switch (s.Kind)
            {
                case EntityKind.Player:
                {
                    var rig = CharacterRig.Create(HumanoidLook.FromAppearance(s.Appearance), v.transform);
                    rig.SetEquipped(s.Equipped, db);
                    v.Rig = rig;
                    v.Anim = rig.Anim;
                    vis = rig.gameObject;
                    v.OverheadHeight = 2.1f;
                    break;
                }
                case EntityKind.Npc:
                {
                    db.Npcs.TryGetValue(s.DefId, out var nd);
                    var rig = CharacterRig.Create(HumanoidLook.ForNpc(nd?.Model, s.Id), v.transform);
                    v.Rig = rig;
                    v.Anim = rig.Anim;
                    vis = rig.gameObject;
                    v.OverheadHeight = 2.1f;
                    break;
                }
                case EntityKind.Monster:
                {
                    db.Monsters.TryGetValue(s.DefId, out var md);
                    var model = md?.Model ?? "enemy_goblin";
                    vis = assets.Spawn(model, v.transform);
                    Tintable.On(vis);
                    var anim = vis.AddComponent<AnimationDriver>();
                    anim.Init(assets.Clips(model));
                    v.Anim = anim;
                    if (_creatureVisuals.TryGetValue(model, out var cv))
                    {
                        v.Hover = (float?)cv["hover"] ?? 0f;
                        var weapon = (string)cv["weapon"];
                        if (!string.IsNullOrEmpty(weapon)) AttachCreatureWeapon(vis, weapon, (string)cv["weaponTint"]);
                    }
                    v.OverheadHeight = Bounds(vis).size.y + 0.4f;
                    break;
                }
                case EntityKind.Node:
                {
                    db.Nodes.TryGetValue(s.DefId, out var nd);
                    string model = nd?.Model ?? "rock_depleted";
                    if (s.Has(EntitySnapshot.FlagDepleted) && nd != null)
                        model = nd.DepletedModel == "tree_stump" ? nd.Model.Replace("tree_", "stump_") : (nd.DepletedModel ?? nd.Model);
                    vis = assets.Spawn(model, v.transform);
                    vis.transform.localRotation = Quaternion.Euler(0, (s.Id * 47) % 360, 0);
                    Tintable.On(vis);
                    if (model == "fishing_spot") vis.AddComponent<Bobber>();
                    v.OverheadHeight = 1.5f;
                    break;
                }
                case EntityKind.GroundItem:
                {
                    db.Items.TryGetValue(s.DefId, out var item);
                    var model = item?.Model;
                    if (string.IsNullOrEmpty(model) || model.StartsWith("a_") && model != "a_shield") model = assets.HasModel(s.DefId) ? s.DefId : "item_bag";
                    vis = assets.Spawn(model, v.transform);
                    if (item != null && item.Model != null && (item.Model.StartsWith("w_") || item.Model.StartsWith("t_") || item.Model == "a_shield"))
                        vis.transform.localRotation = Quaternion.Euler(90, (s.Id * 31) % 360, 0);
                    var t = Tintable.On(vis);
                    if (item != null && Palette.TryTint(item.Tint, out var c)) { t.SetTint("EV_Metal", c); t.SetTint("EV_Wood", c); t.SetTint("EV_Glow", c); }
                    v.OverheadHeight = 0.6f;
                    break;
                }
                case EntityKind.Door:
                {
                    var dp = wdef?.Doors.FirstOrDefault(d => d.Id == s.Id);
                    var bp = dp != null ? wdef.Buildings.FirstOrDefault(b => b.Id == dp.Building) : null;
                    var side = dp?.Side ?? "S";
                    var pivot = new GameObject("Hinge");
                    pivot.transform.SetParent(v.transform, false);
                    // door spans the tile along the wall; hinge on the tile's -X / -Z edge
                    bool alongX = side == "S" || side == "N";
                    pivot.transform.localPosition = alongX ? new Vector3(-0.5f, 0, 0) : new Vector3(0, 0, -0.5f);
                    var holder = new GameObject("Door");
                    holder.transform.SetParent(pivot.transform, false);
                    holder.transform.localRotation = Quaternion.Euler(0, alongX ? 0 : -90, 0);
                    var door = assets.Spawn(bp != null && bp.Style == "wood" ? "door_wood" : "door_stone", holder.transform);
                    Tintable.On(door);
                    vis = pivot;
                    v.OverheadHeight = 2.5f;
                    if (bp != null) v.transform.position = new Vector3(v.transform.position.x, bp.Height, v.transform.position.z);
                    break;
                }
                case EntityKind.Station:
                {
                    var sp = wdef?.Stations.FirstOrDefault(x => x.Id == s.Id);
                    var model = sp?.Model ?? "anvil";
                    string rune = null;
                    if (model.StartsWith("altar_")) { rune = model.Substring(6); model = "rune_altar"; }
                    vis = assets.Spawn(model, v.transform);
                    vis.transform.localRotation = Quaternion.Euler(0, sp?.Rot ?? 0, 0);
                    var t = Tintable.On(vis);
                    if (rune != null && Palette.TryTint(rune, out var rc)) t.SetTint("EV_Glow", rc);
                    if (model == "furnace" || model == "cooking_range") AddFlicker(vis, new Vector3(0, 0.6f, -0.7f), 3f);
                    v.OverheadHeight = 2f;
                    break;
                }
                case EntityKind.FarmPatch:
                    vis = new GameObject("Patch");
                    vis.transform.SetParent(v.transform, false);
                    var soil = assets.Spawn("farm_patch", vis.transform);
                    Tintable.On(soil);
                    v.Visual = vis;
                    RefreshPatch(v);
                    v.OverheadHeight = 1f;
                    break;
                case EntityKind.Fire:
                    vis = assets.Spawn("fire", v.transform);
                    Tintable.On(vis).SetTint("EV_Glow", new Color(1f, 0.55f, 0.15f));
                    AddFlicker(vis, new Vector3(0, 0.5f, 0), 4f);
                    v.OverheadHeight = 1f;
                    break;
                case EntityKind.Stall:
                    vis = assets.Spawn(s.DefId, v.transform);
                    Tintable.On(vis);
                    v.OverheadHeight = 2.6f;
                    break;
                case EntityKind.Obstacle:
                {
                    var op = wdef?.Agility.FirstOrDefault(o => o.Id == s.Id);
                    vis = assets.Spawn(s.DefId, v.transform);
                    if (op != null)
                    {
                        var dir = new Vector3(op.EndX - op.X, 0, op.EndZ - op.Z);
                        if (dir.sqrMagnitude > 0.01f) vis.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    }
                    Tintable.On(vis);
                    v.OverheadHeight = 1.5f;
                    break;
                }
            }
            v.Visual = vis;
            AddPickCollider(v);
        }

        private void AttachCreatureWeapon(GameObject creature, string weapon, string tint)
        {
            Transform socket = null;
            foreach (var t in creature.GetComponentsInChildren<Transform>(true)) if (t.name == "RightHandSocket") { socket = t; break; }
            if (socket == null) return;
            var w = AssetRegistry.Instance.Spawn(weapon, socket);
            // creatures are spawned in their rest pose, so the socket's rest rotation is its current one
            var rootRot = Quaternion.Inverse(creature.transform.rotation) * socket.rotation;
            var socketDir = new Vector3(-0.57f, 0f, 0.82f).normalized;
            w.transform.localRotation = Quaternion.Inverse(rootRot) * Quaternion.LookRotation(Vector3.up, socketDir);
            var t2 = Tintable.On(w);
            if (Palette.TryTint(tint, out var c)) t2.SetTint("EV_Metal", c);
        }

        private void RefreshPatch(EntityView v)
        {
            if (v.Visual == null) return;
            var old = v.Visual.transform.Find("Crop");
            if (old != null) Destroy(old.gameObject);
            if (!_session.Client.FarmPatches.TryGetValue(v.Snapshot.Id, out var st)) st = new PatchState { Status = PatchStatus.Weeds };
            string model;
            switch (st.Status)
            {
                case PatchStatus.Weeds: model = "grass_tuft"; break;
                case PatchStatus.Growing: model = "crop_" + st.Crop + "_" + GrowthStage(st); break;
                case PatchStatus.Ready: model = "crop_" + st.Crop + "_2"; break;
                case PatchStatus.Dead: model = "crop_" + st.Crop + "_3"; break;
                default: return;
            }
            var holder = new GameObject("Crop");
            holder.transform.SetParent(v.Visual.transform, false);
            for (int i = 0; i < 4; i++)
            {
                var c = AssetRegistry.Instance.Spawn(model, holder.transform);
                c.transform.localPosition = new Vector3(-1f + (i % 2) * 2f, 0.05f, -1f + (i / 2) * 2f);
                Tintable.On(c);
            }
        }

        private int GrowthStage(PatchState st)
        {
            if (!_session.Db.Crops.TryGetValue(st.Crop ?? "", out var crop)) return 1;
            long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            double stageSeconds = crop.TicksPerStage * Embervale.Shared.GameTime.TickSeconds;
            return (now - st.PlantedAtUnix) < stageSeconds ? 0 : 1;
        }

        private static void AddFlicker(GameObject go, Vector3 local, float intensity)
        {
            var lgo = new GameObject("FireLight");
            lgo.transform.SetParent(go.transform, false);
            lgo.transform.localPosition = local;
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.6f, 0.25f);
            l.range = 6f;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            lgo.AddComponent<Flicker>();
        }

        public static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Clicks are resolved with physics raycasts against one simple collider per entity.</summary>
        private static void AddPickCollider(EntityView v)
        {
            foreach (var old in v.GetComponents<Collider>()) Destroy(old);
            if (v.Visual == null) return;
            var b = Bounds(v.Visual);
            var center = v.transform.InverseTransformPoint(b.center);
            if (v.IsActor)
            {
                var cap = v.gameObject.AddComponent<CapsuleCollider>();
                cap.height = Mathf.Max(0.6f, b.size.y);
                cap.radius = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 0.3f, 0.25f, 1.5f);
                cap.center = new Vector3(0, cap.height * 0.5f, 0);
                cap.isTrigger = true;
            }
            else
            {
                var box = v.gameObject.AddComponent<BoxCollider>();
                box.center = center;
                var size = v.transform.InverseTransformVector(b.size);
                box.size = new Vector3(Mathf.Max(0.5f, Mathf.Abs(size.x)), Mathf.Max(0.3f, Mathf.Abs(size.y)), Mathf.Max(0.5f, Mathf.Abs(size.z)));
                box.isTrigger = true;
            }
        }
    }

    public sealed class Flicker : MonoBehaviour
    {
        private Light _l;
        private float _base;
        private void Start() { _l = GetComponent<Light>(); _base = _l.intensity; }
        private void Update() { if (_l) _l.intensity = _base * (0.85f + Mathf.PerlinNoise(Time.time * 6f, transform.position.x) * 0.3f); }
    }

    public sealed class Bobber : MonoBehaviour
    {
        private Vector3 _p;
        private void Start() { _p = transform.localPosition; }
        private void Update()
        {
            transform.localPosition = _p + new Vector3(0, Mathf.Sin(Time.time * 2f) * 0.03f, 0);
            transform.Rotate(0, 10f * Time.deltaTime, 0);
        }
    }
}
