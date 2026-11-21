using System.Collections.Generic;
using UnityEngine;

namespace Embervale.Client
{
    /// <summary>
    /// Short-lived world effects driven by server events: click markers, projectiles (arrows, bolts, spell
    /// orbs), boss ability telegraphs and level-up bursts. Everything is pooled-light and self-destroying.
    /// </summary>
    public static class Effects
    {
        private static GameSession _session;
        private static Transform _root;
        private static GameObject _marker;
        private static Material _fxMat;

        public static readonly Dictionary<string, Color> ElementColors = new Dictionary<string, Color>
        {
            { "air", new Color(0.85f, 0.95f, 1f) }, { "water", new Color(0.3f, 0.6f, 1f) }, { "earth", new Color(0.55f, 0.4f, 0.2f) },
            { "fire", new Color(1f, 0.45f, 0.1f) }, { "life", new Color(0.4f, 1f, 0.5f) }, { "void", new Color(0.55f, 0.2f, 0.8f) },
            { "path", new Color(1f, 0.9f, 0.5f) }, { "spirit", new Color(0.6f, 1f, 0.85f) },
        };

        public static void Init(GameSession session, Transform root)
        {
            _session = session;
            _root = new GameObject("Effects").transform;
            _root.SetParent(root, false);
            _fxMat = AssetRegistry.Instance.Material("EV_Glow");
            if (_fxMat == null) _fxMat = new Material(Shader.Find("Sprites/Default"));
            var c = session.Client;
            c.OnProjectile += Projectile;
            c.OnTelegraph += Telegraph;
            c.OnLevelUp += (skill, level) => { var s = session.SelfView; if (s != null) Burst(s.transform.position + Vector3.up, new Color(1f, 0.85f, 0.3f), 40); };
        }

        public static void Shutdown()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
            _marker = null;
            _session = null;
        }

        public static void ClickMarker(Vector3 pos, bool action)
        {
            if (_root == null) return;
            if (_marker == null)
            {
                _marker = new GameObject("ClickMarker");
                _marker.transform.SetParent(_root, false);
                var lr = _marker.AddComponent<LineRenderer>();
                lr.loop = true;
                lr.useWorldSpace = false;
                lr.widthMultiplier = 0.08f;
                lr.positionCount = 24;
                for (int i = 0; i < 24; i++)
                {
                    float a = i / 24f * Mathf.PI * 2;
                    lr.SetPosition(i, new Vector3(Mathf.Cos(a) * 0.4f, 0.08f, Mathf.Sin(a) * 0.4f));
                }
                lr.material = new Material(Shader.Find("Sprites/Default"));
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _marker.AddComponent<MarkerFade>();
            }
            var line = _marker.GetComponent<LineRenderer>();
            line.startColor = line.endColor = action ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.9f, 0.3f);
            _marker.transform.position = pos;
            _marker.GetComponent<MarkerFade>().Restart();
        }

        private static Vector3 Chest(EntityView v) => v.transform.position + Vector3.up * Mathf.Min(1.2f, v.OverheadHeight * 0.55f);

        private static void Projectile(int from, int to, string kind)
        {
            var em = _session?.Entities;
            if (em == null) return;
            var a = em.Get(from); var b = em.Get(to);
            if (a == null || b == null) return;
            GameObject go;
            kind = (kind ?? "arrow").ToLowerInvariant();
            if (kind == "arrow" || kind == "bolt")
            {
                go = AssetRegistry.Instance.HasModel(kind) ? AssetRegistry.Instance.Spawn(kind, _root) : AssetRegistry.Instance.Spawn("arrow", _root);
                go.transform.localScale = Vector3.one;
            }
            else
            {
                go = Orb(ElementColors.TryGetValue(kind, out var col) ? col : Color.white, 0.25f);
                go.transform.SetParent(_root, false);
            }
            var p = go.AddComponent<ProjectileMotion>();
            p.Init(a.transform, b.transform, Chest(a) - a.transform.position, Chest(b) - b.transform.position, kind == "arrow" || kind == "bolt" ? 0.8f : 0.3f);
            _session.Audio?.PlayAt(kind == "arrow" || kind == "bolt" ? "arrow" : "spell_" + kind, a.transform.position);
        }

        private static GameObject Orb(Color c, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = _fxMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            mpb.SetColor("_EmissionColor", c * 3f);
            r.SetPropertyBlock(mpb);
            var light = go.AddComponent<Light>();
            light.color = c;
            light.range = 3f;
            light.intensity = 2f;
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.25f;
            trail.widthMultiplier = size * 0.8f;
            trail.material = new Material(Shader.Find("Sprites/Default"));
            trail.startColor = c;
            trail.endColor = new Color(c.r, c.g, c.b, 0);
            return go;
        }

        private static void Telegraph(int monsterId, float x, float z, int radius, string ability)
        {
            if (_root == null) return;
            float ticks = 2;
            foreach (var m in _session.Db.Monsters.Values)
                foreach (var ab in m.Abilities)
                    if (ab.Id == ability && ab.TelegraphTicks > 0) ticks = ab.TelegraphTicks;
            var go = new GameObject("Telegraph " + ability);
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(x, _session.World.Terrain.SampleHeight(x, z) + 0.06f, z);
            var t = go.AddComponent<TelegraphRing>();
            t.Init(Mathf.Max(1, radius) + 0.5f, ticks * Shared.GameTime.TickSeconds);
            _session.Audio?.PlayAt("telegraph", go.transform.position);
        }

        public static void Burst(Vector3 pos, Color c, int count)
        {
            if (_root == null) return;
            var go = new GameObject("Burst");
            go.transform.SetParent(_root, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = 1.2f;
            main.startSpeed = 3.5f;
            main.startSize = 0.12f;
            main.startColor = c;
            main.gravityModifier = 0.4f;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0, (short)count) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            ps.Play();
        }
    }

    public sealed class MarkerFade : MonoBehaviour
    {
        private float _t;
        private LineRenderer _lr;
        public void Restart() { _t = 0; gameObject.SetActive(true); }
        private void Update()
        {
            if (_lr == null) _lr = GetComponent<LineRenderer>();
            _t += Time.deltaTime;
            float s = 1f - _t / 0.6f;
            transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, s);
            if (_t > 0.6f) gameObject.SetActive(false);
        }
    }

    public sealed class ProjectileMotion : MonoBehaviour
    {
        private Transform _from, _to;
        private Vector3 _fromOff, _toOff, _start;
        private float _t, _duration, _arc;

        public void Init(Transform from, Transform to, Vector3 fromOff, Vector3 toOff, float arc)
        {
            _from = from; _to = to; _fromOff = fromOff; _toOff = toOff; _arc = arc;
            _start = from.position + fromOff;
            float dist = Vector3.Distance(_start, to.position + toOff);
            _duration = Mathf.Clamp(dist / 14f, 0.15f, 0.9f);
            transform.position = _start;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            if (_to == null) { Destroy(gameObject); return; }
            float k = Mathf.Clamp01(_t / _duration);
            var end = _to.position + _toOff;
            var p = Vector3.Lerp(_start, end, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * _arc;
            var dir = p - transform.position;
            if (dir.sqrMagnitude > 1e-5f) transform.rotation = Quaternion.LookRotation(dir);
            transform.position = p;
            if (k >= 1f) Destroy(gameObject);
        }
    }

    public sealed class TelegraphRing : MonoBehaviour
    {
        private float _radius, _duration, _t;
        private LineRenderer _outer;
        private LineRenderer _fill;

        public void Init(float radius, float duration)
        {
            _radius = radius;
            _duration = Mathf.Max(0.3f, duration);
            _outer = Ring("Outer", 0.1f);
            _fill = Ring("Fill", 0.06f);
        }

        private LineRenderer Ring(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = 48;
            lr.widthMultiplier = width;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = new Color(1f, 0.2f, 0.1f, 0.9f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetRadius(lr, _radius);
            return lr;
        }

        private static void SetRadius(LineRenderer lr, float r)
        {
            for (int i = 0; i < lr.positionCount; i++)
            {
                float a = i / (float)lr.positionCount * Mathf.PI * 2;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
            }
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _duration);
            SetRadius(_fill, Mathf.Max(0.05f, _radius * k));
            var c = Color.Lerp(new Color(1f, 0.6f, 0.1f, 0.7f), new Color(1f, 0.1f, 0.05f, 1f), k);
            _fill.startColor = _fill.endColor = c;
            if (_t > _duration + 0.3f) Destroy(gameObject);
        }
    }
}
