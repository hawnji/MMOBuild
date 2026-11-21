using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Embervale.Client
{
    /// <summary>
    /// Name -> asset lookup built by the editor bootstrapper (Embervale > Setup Project) from the generated
    /// art folders. Lives at Resources/EmbervaleAssets.asset so the runtime can find models, materials,
    /// icons and audio by the names used in the game data (item "model"/"icon", monster "model", ...).
    /// </summary>
    [CreateAssetMenu(menuName = "Embervale/Asset Registry")]
    public sealed class AssetRegistry : ScriptableObject
    {
        [Serializable] public struct ModelEntry { public string Name; public GameObject Prefab; }
        [Serializable] public struct SpriteEntry { public string Name; public Sprite Sprite; }
        [Serializable] public struct ClipEntry { public string Name; public AudioClip Clip; }
        [Serializable] public struct MaterialEntry { public string Name; public Material Material; }
        [Serializable] public struct ClipSetEntry { public string Name; public AnimationClip[] Clips; }

        public List<ModelEntry> Models = new List<ModelEntry>();
        public List<SpriteEntry> Icons = new List<SpriteEntry>();
        public List<ClipEntry> Audio = new List<ClipEntry>();
        public List<MaterialEntry> Materials = new List<MaterialEntry>();
        public List<ClipSetEntry> AnimationSets = new List<ClipSetEntry>();
        public Material TerrainMaterial;
        public Material WaterMaterial;
        public Material HighlightMaterial;

        private Dictionary<string, GameObject> _models;
        private Dictionary<string, Sprite> _icons;
        private Dictionary<string, AudioClip> _audio;
        private Dictionary<string, Material> _materials;

        private static AssetRegistry _instance;

        public static AssetRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<AssetRegistry>("EmbervaleAssets");
                    if (_instance == null)
                    {
                        Debug.LogError("Resources/EmbervaleAssets.asset is missing. Run the menu Embervale > Setup Project.");
                        _instance = CreateInstance<AssetRegistry>();
                    }
                }
                return _instance;
            }
        }

        private void Build()
        {
            if (_models != null) return;
            _models = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in Models) if (m.Prefab != null) _models[m.Name] = m.Prefab;
            _icons = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in Icons) if (i.Sprite != null) _icons[i.Name] = i.Sprite;
            _audio = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in Audio) if (a.Clip != null) _audio[a.Name] = a.Clip;
            _materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in Materials) if (m.Material != null) _materials[m.Name] = m.Material;
        }

        public GameObject Model(string name)
        {
            Build();
            return name != null && _models.TryGetValue(name, out var g) ? g : null;
        }

        public bool HasModel(string name) { Build(); return name != null && _models.ContainsKey(name); }

        public Sprite Icon(string name)
        {
            Build();
            return name != null && _icons.TryGetValue(name, out var s) ? s : null;
        }

        public AudioClip Clip(string name)
        {
            Build();
            return name != null && _audio.TryGetValue(name, out var c) ? c : null;
        }

        /// <summary>Animation clips imported from an FBX (e.g. "Humanoid_Animations", "enemy_wolf").</summary>
        public AnimationClip[] Clips(string setName)
        {
            foreach (var a in AnimationSets) if (string.Equals(a.Name, setName, StringComparison.OrdinalIgnoreCase)) return a.Clips;
            return new AnimationClip[0];
        }

        public Material Material(string name)
        {
            Build();
            return name != null && _materials.TryGetValue(name, out var m) ? m : null;
        }

        /// <summary>Instantiates a named model or, if it is missing, a visible placeholder (logged once).</summary>
        public GameObject Spawn(string name, Transform parent = null)
        {
            var prefab = Model(name);
            if (prefab != null) return Instantiate(prefab, parent, false);
            if (_warned.Add(name ?? "<null>")) Debug.LogWarning("[Assets] missing model '" + name + "' - using placeholder");
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "MISSING_" + name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            Destroy(go.GetComponent<Collider>());
            return go;
        }

        private readonly HashSet<string> _warned = new HashSet<string>();
    }

    /// <summary>Named colours shared with the Blender pipeline (StreamingAssets/Art/palette.json).</summary>
    public static class Palette
    {
        private static Dictionary<string, Color> _tints;
        private static Dictionary<string, Color> _colors;

        public static readonly Color[] SkinTones =
        {
            new Color(0.96f, 0.80f, 0.69f), new Color(0.87f, 0.68f, 0.52f), new Color(0.76f, 0.57f, 0.42f),
            new Color(0.62f, 0.44f, 0.30f), new Color(0.47f, 0.32f, 0.22f), new Color(0.33f, 0.22f, 0.15f)
        };

        public static readonly Color[] HairColors =
        {
            new Color(0.10f, 0.08f, 0.07f), new Color(0.35f, 0.22f, 0.12f), new Color(0.55f, 0.35f, 0.18f),
            new Color(0.85f, 0.70f, 0.40f), new Color(0.65f, 0.25f, 0.12f), new Color(0.80f, 0.80f, 0.80f),
            new Color(0.45f, 0.45f, 0.48f), new Color(0.25f, 0.30f, 0.55f)
        };

        public static readonly Color[] ClothColors =
        {
            new Color(0.45f, 0.30f, 0.20f), new Color(0.62f, 0.18f, 0.15f), new Color(0.20f, 0.30f, 0.58f),
            new Color(0.24f, 0.45f, 0.24f), new Color(0.85f, 0.82f, 0.75f), new Color(0.40f, 0.22f, 0.50f),
            new Color(0.15f, 0.15f, 0.17f), new Color(0.80f, 0.65f, 0.25f), new Color(0.30f, 0.50f, 0.55f),
            new Color(0.55f, 0.50f, 0.45f)
        };

        public static readonly string[] HairStyles = { "short", "long", "ponytail", "bun", "spiky", "braids" };

        private static void Load()
        {
            if (_tints != null) return;
            _tints = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            _colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = Path.Combine(Application.streamingAssetsPath, "Art", "palette.json");
                var root = JObject.Parse(File.ReadAllText(path));
                foreach (var p in (JObject)root["tints"]) _tints[p.Key] = ToColor((JArray)p.Value);
                foreach (var p in (JObject)root["colors"]) _colors[p.Key] = ToColor((JArray)p.Value);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Palette] could not load palette.json: " + e.Message);
            }
        }

        private static Color ToColor(JArray a) => new Color((float)a[0], (float)a[1], (float)a[2], 1f);

        public static bool TryTint(string key, out Color c)
        {
            Load();
            c = Color.white;
            return key != null && _tints.TryGetValue(key, out c);
        }

        public static Color Named(string key, Color fallback)
        {
            Load();
            return key != null && _colors.TryGetValue(key, out var c) ? c : fallback;
        }

        public static Color Hex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }
    }
}
