using System.Collections.Generic;
using Embervale.Shared.Net;
using Embervale.Shared.Simulation;
using UnityEngine;
using UnityEngine.AI;

namespace Embervale.Client
{
    /// <summary>
    /// Point-and-click control. Left click performs the default action of whatever is under the cursor
    /// (or walks there), right click opens a context menu with every option. The server decides whether
    /// anything actually happens: the client only sends requests and shows a click marker / path preview.
    /// </summary>
    public sealed class PlayerInteraction : MonoBehaviour
    {
        private GameSession _session;
        private readonly RaycastHit[] _hits = new RaycastHit[32];
        private LineRenderer _pathLine;
        private float _pathLineTimer;

        /// <summary>Inventory slot selected with "Use" (-1 = none). Next click uses it on the target.</summary>
        public int UseSlot { get; private set; } = -1;
        /// <summary>Spell selected from the magic book waiting for a target.</summary>
        public string PendingSpell { get; private set; }

        public void Init(GameSession session)
        {
            _session = session;
            var go = new GameObject("PathPreview");
            go.transform.SetParent(transform, false);
            _pathLine = go.AddComponent<LineRenderer>();
            _pathLine.widthMultiplier = 0.12f;
            _pathLine.material = AssetRegistry.Instance.HighlightMaterial != null ? AssetRegistry.Instance.HighlightMaterial : new Material(Shader.Find("Sprites/Default"));
            _pathLine.startColor = _pathLine.endColor = new Color(1f, 0.9f, 0.4f, 0.6f);
            _pathLine.positionCount = 0;
            _pathLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public void BeginUse(int slot)
        {
            UseSlot = slot;
            PendingSpell = null;
            var c = _session.Client;
            var def = c != null && slot >= 0 && slot < c.Inventory.Length ? _session.Db.Item(c.Inventory[slot].ItemId) : null;
            UIRoot.Instance.SetCursorHint(def != null ? "Use " + def.Name + " ->" : null);
        }

        public void BeginSpell(string spellId)
        {
            PendingSpell = spellId;
            UseSlot = -1;
            _session.Db.Spells.TryGetValue(spellId, out var sp);
            UIRoot.Instance.SetCursorHint(sp != null ? "Cast " + sp.Name + " ->" : null);
        }

        public void CancelUse()
        {
            UseSlot = -1;
            PendingSpell = null;
            UIRoot.Instance.SetCursorHint(null);
        }

        private struct Pick
        {
            public EntityView Entity;
            public Vector3 Point;
            public bool HasGround;
            public List<EntityView> All;
        }

        private Pick Raycast()
        {
            var pick = new Pick { All = new List<EntityView>() };
            var cam = _session.Camera != null ? _session.Camera.Cam : null;
            if (cam == null) return pick;
            var ray = cam.ScreenPointToRay(InputAdapter.MousePosition);
            int n = Physics.RaycastNonAlloc(ray, _hits, 300f, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(_hits, 0, n, Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance)));
            float groundDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                var ev = h.collider.GetComponentInParent<EntityView>();
                if (ev != null)
                {
                    if (ev.IsLocalPlayer) continue;
                    if (!pick.All.Contains(ev)) pick.All.Add(ev);
                    continue;
                }
                if (!h.collider.isTrigger && h.distance < groundDist)
                {
                    groundDist = h.distance;
                    pick.Point = h.point;
                    pick.HasGround = true;
                }
            }
            // Entities hidden behind the ground hit do not count.
            pick.All.RemoveAll(e => Vector3.Distance(ray.origin, e.transform.position) > groundDist + 3f);
            if (pick.All.Count > 0) pick.Entity = pick.All[0];
            return pick;
        }

        private void Update()
        {
            if (_session == null || _session.Client == null || _session.State != ClientState.InWorld) return;
            if (_pathLineTimer > 0)
            {
                _pathLineTimer -= Time.deltaTime;
                if (_pathLineTimer <= 0) _pathLine.positionCount = 0;
            }

            if (InputAdapter.Down(InputAdapter.Key.Escape) && (UseSlot >= 0 || PendingSpell != null)) { CancelUse(); return; }
            if (UIRoot.PointerOverUI) { UIRoot.Instance.SetHover(null); return; }

            var pick = Raycast();
            UpdateHover(pick);

            if (UIRoot.Instance.MenuBlocksClick) return;
            if (InputAdapter.MouseDown(0)) LeftClick(pick);
            else if (InputAdapter.MouseDown(1)) RightClick(pick);
        }

        private void UpdateHover(Pick pick)
        {
            string text = null;
            if (pick.Entity != null)
            {
                var opts = pick.Entity.Options(_session.Db, _session.Client.SelfId);
                string verb = UseSlot >= 0 ? "Use with" : PendingSpell != null ? "Cast on" : opts[0];
                text = verb + " " + Describe(pick.Entity);
                if (pick.All.Count > 1) text += "  (+" + (pick.All.Count - 1) + " more)";
            }
            else if (pick.HasGround && UseSlot < 0 && PendingSpell == null) text = "Walk here";
            UIRoot.Instance.SetHover(text);
        }

        public string Describe(EntityView v)
        {
            var s = v.Snapshot;
            switch (s.Kind)
            {
                case EntityKind.Monster:
                {
                    int me = _session.Client.CombatLevel;
                    string col = s.Level > me + 5 ? "#ff5050" : s.Level > me ? "#ffa040" : s.Level < me - 5 ? "#60ff60" : "#ffff60";
                    return "<color=#ffd75e>" + s.Name + "</color> <color=" + col + ">(level " + s.Level + ")</color>";
                }
                case EntityKind.Player: return "<color=#ffffff>" + s.Name + "</color> (level " + s.Level + ")";
                case EntityKind.Npc: return "<color=#ffd75e>" + s.Name + "</color>";
                case EntityKind.GroundItem: return "<color=#ff9a40>" + s.Name + (s.Quantity > 1 ? " x" + s.Quantity : "") + "</color>";
                default: return "<color=#40e0ff>" + s.Name + "</color>";
            }
        }

        private void LeftClick(Pick pick)
        {
            var c = _session.Client;
            if (UseSlot >= 0)
            {
                if (pick.Entity != null)
                {
                    c.Send(ClientPackets.UseItemOnEntity(UseSlot, pick.Entity.Snapshot.Id));
                    Effects.ClickMarker(pick.Entity.transform.position, true);
                }
                CancelUse();
                return;
            }
            if (PendingSpell != null)
            {
                if (pick.Entity != null)
                {
                    c.Send(ClientPackets.CastSpell(PendingSpell, pick.Entity.Snapshot.Id));
                    Effects.ClickMarker(pick.Entity.transform.position, true);
                }
                CancelUse();
                return;
            }
            if (pick.Entity != null)
            {
                var opts = pick.Entity.Options(_session.Db, c.SelfId);
                DoOption(pick.Entity, opts[0]);
                return;
            }
            if (pick.HasGround) WalkTo(pick.Point);
        }

        private void RightClick(Pick pick)
        {
            var items = new List<UIRoot.MenuItem>();
            if (UseSlot >= 0 || PendingSpell != null)
            {
                foreach (var e in pick.All)
                {
                    var ev = e;
                    items.Add(new UIRoot.MenuItem(UseSlot >= 0 ? "Use with " + Describe(ev) : "Cast on " + Describe(ev), () =>
                    {
                        if (UseSlot >= 0) _session.Client.Send(ClientPackets.UseItemOnEntity(UseSlot, ev.Snapshot.Id));
                        else _session.Client.Send(ClientPackets.CastSpell(PendingSpell, ev.Snapshot.Id));
                        CancelUse();
                    }));
                }
                items.Add(new UIRoot.MenuItem("Cancel", CancelUse));
            }
            else
            {
                foreach (var e in pick.All)
                {
                    var ev = e;
                    foreach (var o in ev.Options(_session.Db, _session.Client.SelfId))
                    {
                        var opt = o;
                        items.Add(new UIRoot.MenuItem(opt + " " + Describe(ev), () => DoOption(ev, opt)));
                    }
                }
                if (pick.HasGround)
                {
                    var p = pick.Point;
                    items.Add(new UIRoot.MenuItem("Walk here", () => WalkTo(p)));
                }
                items.Add(new UIRoot.MenuItem("Cancel", null));
            }
            UIRoot.Instance.ShowMenu(InputAdapter.MousePosition, "Choose Option", items);
        }

        public void DoOption(EntityView ev, string option)
        {
            if (ev == null || ev.Snapshot == null) return;
            _session.Client.Interact(ev.Snapshot.Id, option);
            if (option != "Examine") Effects.ClickMarker(ev.transform.position, true);
        }

        public void WalkTo(Vector3 point)
        {
            int x = Mathf.FloorToInt(point.x), z = Mathf.FloorToInt(point.z);
            var terrain = _session.World.Terrain;
            if (terrain.IsBlocked(x, z) && NavMesh.SamplePosition(point, out var nh, 3f, NavMesh.AllAreas))
            {
                point = nh.position;
                x = Mathf.FloorToInt(point.x); z = Mathf.FloorToInt(point.z);
            }
            _session.Client.MoveTo(x, z, _session.Client.Running);
            Effects.ClickMarker(new Vector3(x + 0.5f, terrain.SampleHeight(x + 0.5f, z + 0.5f), z + 0.5f), false);
            ShowPathPreview(new Vector3(x + 0.5f, 0, z + 0.5f));
        }

        /// <summary>Shows the expected route using the runtime NavMesh. The server path is authoritative.</summary>
        private void ShowPathPreview(Vector3 target)
        {
            var self = _session.SelfView;
            if (self == null) return;
            target.y = _session.World.Terrain.SampleHeight(target.x, target.z);
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(self.transform.position, target, NavMesh.AllAreas, path) || path.corners.Length < 2) { _pathLine.positionCount = 0; return; }
            var corners = path.corners;
            _pathLine.positionCount = corners.Length;
            for (int i = 0; i < corners.Length; i++) _pathLine.SetPosition(i, corners[i] + Vector3.up * 0.15f);
            _pathLineTimer = 1.5f;
        }
    }

    /// <summary>Bakes a NavMesh at runtime from the generated world geometry (terrain, buildings, walls).</summary>
    public static class NavMeshBaker
    {
        private static NavMeshDataInstance _instance;

        public static void Bake(GameObject root)
        {
            if (_instance.valid) _instance.Remove();
            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            NavMeshBuilder.CollectSources(root.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.35f;
            settings.agentHeight = 1.8f;
            settings.agentClimb = 0.6f;
            settings.agentSlope = 50f;
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in root.GetComponentsInChildren<Collider>())
            {
                if (r.isTrigger) continue;
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            b.Expand(2f);
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, b, Vector3.zero, Quaternion.identity);
            if (data != null) _instance = NavMesh.AddNavMeshData(data);
        }
    }
}
