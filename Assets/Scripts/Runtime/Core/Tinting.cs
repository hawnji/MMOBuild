using System.Collections.Generic;
using UnityEngine;

namespace Embervale.Client
{
    /// <summary>
    /// Added to every imported renderer by the model postprocessor. Records which shared EV_* material each
    /// sub-mesh uses and the colour baked into the source material name (EV_Cloth__6a4c33 -> #6a4c33).
    /// </summary>
    public sealed class TintInfo : MonoBehaviour
    {
        public string[] SlotKeys = new string[0];
        public Color[] BakedColors = new Color[0];

        /// <summary>Per-renderer colour overrides (e.g. a steel helmet on a character in bronze legs).</summary>
        [System.NonSerialized] public Dictionary<string, Color> Local;

        public void SetLocal(string key, Color c)
        {
            if (Local == null) Local = new Dictionary<string, Color>();
            Local[key] = c;
        }
    }

    /// <summary>
    /// Recolours the tintable EV_* materials of a model per instance with MaterialPropertyBlocks
    /// (no material copies, so batching-friendly). Used for skin/hair/clothing colours and metal tiers.
    /// </summary>
    public sealed class Tintable : MonoBehaviour
    {
        public static readonly Dictionary<string, Color> Defaults = new Dictionary<string, Color>
        {
            { "EV_Atlas", Color.white },
            { "EV_Skin", new Color(0.87f, 0.68f, 0.52f) },
            { "EV_Hair", new Color(0.35f, 0.22f, 0.12f) },
            { "EV_Cloth", new Color(0.45f, 0.30f, 0.20f) },
            { "EV_Cloth2", new Color(0.30f, 0.28f, 0.25f) },
            { "EV_Leather", new Color(0.45f, 0.30f, 0.18f) },
            { "EV_Metal", new Color(0.70f, 0.50f, 0.28f) },
            { "EV_Wood", new Color(0.60f, 0.43f, 0.25f) },
            { "EV_Glow", new Color(0.55f, 0.85f, 0.65f) },
        };

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private readonly Dictionary<string, Color> _overrides = new Dictionary<string, Color>();
        private TintInfo[] _infos;
        private MaterialPropertyBlock _mpb;

        public void SetTint(string key, Color c)
        {
            _overrides[key] = c;
            Apply();
        }

        public void ClearTint(string key)
        {
            _overrides.Remove(key);
            Apply();
        }

        public void Refresh()
        {
            _infos = null;
            Apply();
        }

        private void Awake() => Apply();

        public void Apply()
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            if (_infos == null) _infos = GetComponentsInChildren<TintInfo>(true);
            foreach (var info in _infos)
            {
                var r = info.GetComponent<Renderer>();
                if (r == null) continue;
                int n = Mathf.Min(info.SlotKeys.Length, r.sharedMaterials.Length);
                for (int i = 0; i < n; i++)
                {
                    var key = info.SlotKeys[i];
                    Color c;
                    if (info.Local != null && info.Local.TryGetValue(key, out var lc)) c = lc;
                    else if (!_overrides.TryGetValue(key, out c))
                    {
                        c = i < info.BakedColors.Length && info.BakedColors[i].a > 0 ? info.BakedColors[i]
                            : (Defaults.TryGetValue(key, out var d) ? d : Color.white);
                    }
                    r.GetPropertyBlock(_mpb, i);
                    _mpb.SetColor(BaseColorId, c);
                    _mpb.SetColor(ColorId, c);
                    if (key == "EV_Glow") _mpb.SetColor(EmissionId, c * 2f);
                    r.SetPropertyBlock(_mpb, i);
                }
            }
        }

        /// <summary>Ensures a Tintable exists on the object and applies the baked colours.</summary>
        public static Tintable On(GameObject go)
        {
            var t = go.GetComponent<Tintable>();
            if (t == null) t = go.AddComponent<Tintable>();
            t.Refresh();
            return t;
        }
    }
}
