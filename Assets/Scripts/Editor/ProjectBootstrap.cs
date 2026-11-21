using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Embervale.Client;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
#if EV_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Embervale.EditorTools
{
    /// <summary>
    /// One-click project setup (menu: Embervale > Setup Project, or batch mode:
    /// Unity.exe -batchmode -projectPath . -executeMethod Embervale.EditorTools.ProjectBootstrap.SetupBatch -logFile setup.log).
    ///
    /// 1. Adds the Unity packages by name so the Package Manager picks the versions that ship with this
    ///    editor (URP, Input System, Test Framework). Nothing is upgraded or downgraded by hand.
    /// 2. After the domain reload: creates the shared EV_* materials, the URP pipeline asset, player
    ///    settings, re-imports the generated art with the new materials, builds the AssetRegistry used at
    ///    runtime, creates the Boot scene and the build settings, then validates asset references.
    /// Every step is idempotent, so running it again is safe.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectBootstrap
    {
        public const string RegistryPath = "Assets/Resources/EmbervaleAssets.asset";
        public const string ScenePath = "Assets/Scenes/Boot.unity";
        public const string SettingsDir = "Assets/Settings";
        private const string PendingKey = "Embervale.SetupPending";
        private const string BatchKey = "Embervale.SetupBatch";

        public static readonly string[] RequiredPackages =
        {
            "com.unity.render-pipelines.universal",
            "com.unity.inputsystem",
            "com.unity.test-framework",
        };

        static ProjectBootstrap()
        {
            EditorApplication.delayCall += () =>
            {
                if (UnityEditor.SessionState.GetBool(PendingKey, false))
                {
                    UnityEditor.SessionState.SetBool(PendingKey, false);
                    ContinueSetup();
                }
                else if (!File.Exists(RegistryPath) && !Application.isBatchMode && !UnityEditor.SessionState.GetBool("Embervale.AutoAsked", false))
                {
                    UnityEditor.SessionState.SetBool("Embervale.AutoAsked", true);
                    if (EditorUtility.DisplayDialog("Embervale", "This project has not been set up yet. Run Embervale > Setup Project now?\n\nIt installs URP / Input System / Test Framework (editor-matched versions), creates materials, the asset registry and the Boot scene.", "Set up", "Later"))
                        Setup();
                }
            };
        }

        [MenuItem("Embervale/Setup Project", priority = 0)]
        public static void Setup()
        {
            Debug.Log("[Embervale] Setup started (Unity " + Application.unityVersion + ").");
            if (Application.unityVersion != "6000.6.9f1")
                Debug.LogWarning("[Embervale] This project targets Unity 6000.6.9f1 exactly. You are running " + Application.unityVersion + ". Do not let Unity upgrade the project; open it with 6000.6.9f1.");
            InstallPackagesThen(ContinueSetup);
        }

        /// <summary>Entry point for -executeMethod. Do not pass -quit: the editor exits when setup is done.</summary>
        public static void SetupBatch()
        {
            UnityEditor.SessionState.SetBool(BatchKey, true);
            Setup();
        }

        // ------------------------------------------------------------------ packages
        private static void InstallPackagesThen(Action next)
        {
            var list = UnityEditor.PackageManager.Client.List(true, false);
            EditorApplication.CallbackFunction poll = null;
            poll = () =>
            {
                if (!list.IsCompleted) return;
                EditorApplication.update -= poll;
                var installed = new HashSet<string>();
                if (list.Status == StatusCode.Success) foreach (var p in list.Result) installed.Add(p.name);
                var missing = RequiredPackages.Where(p => !installed.Contains(p)).ToArray();
                if (missing.Length == 0) { next(); return; }
                Debug.Log("[Embervale] Adding packages: " + string.Join(", ", missing));
                UnityEditor.SessionState.SetBool(PendingKey, true);
                var add = UnityEditor.PackageManager.Client.AddAndRemove(missing, null);
                EditorApplication.CallbackFunction wait = null;
                wait = () =>
                {
                    if (!add.IsCompleted) return;
                    EditorApplication.update -= wait;
                    if (add.Status != StatusCode.Success)
                    {
                        UnityEditor.SessionState.SetBool(PendingKey, false);
                        Debug.LogError("[Embervale] Package install failed: " + add.Error?.message + ". Install them from Window > Package Manager and run Setup again.");
                        FinishBatch(1);
                        return;
                    }
                    // A domain reload follows; ContinueSetup runs from the static constructor afterwards.
                    if (!EditorApplication.isCompiling) { UnityEditor.SessionState.SetBool(PendingKey, false); next(); }
                };
                EditorApplication.update += wait;
            };
            EditorApplication.update += poll;
        }

        private static void ContinueSetup()
        {
            try
            {
                EnsureFolder("Assets/Resources");
                EnsureFolder("Assets/Scenes");
                EnsureFolder(SettingsDir);
                EnsureFolder(EmbervaleAssetPostprocessor.MaterialDir);
                bool newMaterials = CreateMaterials();
                SetupRenderPipeline();
                SetupPlayerSettings();
                if (newMaterials || !File.Exists(RegistryPath)) ReimportArt();
                BuildRegistry();
                CreateBootScene();
                var problems = AssetValidator.Validate();
                foreach (var p in problems) Debug.LogWarning("[Embervale] " + p);
                Debug.Log("[Embervale] Setup finished. " + (problems.Count == 0 ? "All asset references resolved." : problems.Count + " asset warnings (see above).") + " Open Assets/Scenes/Boot.unity and press Play.");
                FinishBatch(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                FinishBatch(1);
            }
        }

        private static void FinishBatch(int code)
        {
            if (!UnityEditor.SessionState.GetBool(BatchKey, false)) return;
            UnityEditor.SessionState.SetBool(BatchKey, false);
            EditorApplication.Exit(code);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ------------------------------------------------------------------ materials
        private static Shader LitShader() => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        private static Shader UnlitShader() => Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

        private static readonly (string name, string tex, float metallic, float smooth, bool emissive)[] MaterialSpecs =
        {
            ("EV_Atlas", "ev_atlas.png", 0f, 0.12f, false),
            ("EV_Skin", "ev_skin.png", 0f, 0.25f, false),
            ("EV_Hair", "ev_hair.png", 0f, 0.3f, false),
            ("EV_Cloth", "ev_cloth.png", 0f, 0.08f, false),
            ("EV_Cloth2", "ev_cloth.png", 0f, 0.08f, false),
            ("EV_Leather", "ev_leather.png", 0f, 0.22f, false),
            ("EV_Metal", "ev_metal.png", 0.65f, 0.5f, false),
            ("EV_Wood", "ev_wood.png", 0f, 0.1f, false),
            ("EV_Glow", "ev_glow.png", 0f, 0.4f, true),
        };

        /// <summary>Creates or updates the shared materials. Returns true if any were newly created.</summary>
        public static bool CreateMaterials()
        {
            bool created = false;
            var lit = LitShader();
            foreach (var spec in MaterialSpecs)
            {
                var path = EmbervaleAssetPostprocessor.MaterialDir + "/" + spec.name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); created = true; }
                m.shader = lit;
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/Generated/" + spec.tex);
                SetTex(m, tex);
                SetColor(m, Color.white);
                SetFloat(m, "_Metallic", spec.metallic);
                SetFloat(m, "_Smoothness", spec.smooth);
                SetFloat(m, "_Glossiness", spec.smooth);
                if (spec.emissive)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", new Color(0.55f, 0.85f, 0.65f) * 2f);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
            }

            var terrain = LoadOrCreate("EV_Terrain", lit, ref created);
            SetColor(terrain, Color.white);
            SetFloat(terrain, "_Smoothness", 0.04f);
            SetFloat(terrain, "_Glossiness", 0.04f);

            var water = LoadOrCreate("EV_Water", lit, ref created);
            SetTex(water, WaterTexture());
            SetColor(water, new Color(0.22f, 0.46f, 0.6f, 0.78f));
            SetFloat(water, "_Smoothness", 0.85f);
            MakeTransparent(water);

            var highlight = LoadOrCreate("EV_Highlight", UnlitShader(), ref created);
            SetColor(highlight, new Color(1f, 0.9f, 0.4f, 1f));
            AssetDatabase.SaveAssets();
            return created;
        }

        private static Material LoadOrCreate(string name, Shader shader, ref bool created)
        {
            var path = EmbervaleAssetPostprocessor.MaterialDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); created = true; }
            m.shader = shader;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void SetTex(Material m, Texture t)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
        }

        private static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private static void SetFloat(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }

        private static void MakeTransparent(Material m)
        {
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1);
                m.SetFloat("_Blend", 0);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
            }
            else if (m.HasProperty("_Mode"))
            {
                m.SetFloat("_Mode", 3);
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_ALPHABLEND_ON");
            }
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        /// <summary>Generates a tileable ripple texture for the water (once).</summary>
        private static Texture2D WaterTexture()
        {
            const string path = "Assets/Art/Textures/Generated/ev_water.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int n = 256;
            var t = new Texture2D(n, n, TextureFormat.RGB24, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n * Mathf.PI * 2, v = y / (float)n * Mathf.PI * 2;
                    float w = 0.5f + 0.18f * Mathf.Sin(u * 3 + Mathf.Sin(v * 2) * 1.5f) + 0.12f * Mathf.Sin(v * 5 + Mathf.Sin(u * 3)) + 0.08f * Mathf.Sin((u + v) * 7);
                    float c = Mathf.Lerp(0.82f, 1f, Mathf.Clamp01(w));
                    t.SetPixel(x, y, new Color(c, c, c));
                }
            t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ URP
        private static void SetupRenderPipeline()
        {
#if EV_URP
            const string assetPath = SettingsDir + "/EV_URP.asset";
            const string rendererPath = SettingsDir + "/EV_URP_Renderer.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (asset == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            asset.shadowDistance = 70f;
            asset.shadowCascadeCount = 2;
            asset.msaaSampleCount = 4;
            asset.supportsHDR = true;
            asset.renderScale = 1f;
            EditorUtility.SetDirty(asset);
            GraphicsSettings.defaultRenderPipeline = asset;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, false);
            AssetDatabase.SaveAssets();
            Debug.Log("[Embervale] URP pipeline asset assigned.");
#else
            Debug.LogWarning("[Embervale] URP package not active yet; materials use the fallback shader. Run Setup again after the package finishes installing.");
#endif
        }

        // ------------------------------------------------------------------ player settings
        private static void SetupPlayerSettings()
        {
            PlayerSettings.companyName = "Embervale";
            PlayerSettings.productName = "Embervale";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            // Accept both input backends so the legacy fallback never throws while Input System is installing.
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (ps != null)
            {
                var so = new SerializedObject(ps);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue != 2) { prop.intValue = 2; so.ApplyModifiedProperties(); }
            }
        }

        // ------------------------------------------------------------------ art + registry
        private static void ReimportArt()
        {
            var models = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Art" }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            Debug.Log("[Embervale] Re-importing " + models.Count + " models with the shared materials...");
            AssetDatabase.StartAssetEditing();
            try { foreach (var p in models) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); }
            finally { AssetDatabase.StopAssetEditing(); }
        }

        public static AssetRegistry BuildRegistry()
        {
            var reg = AssetDatabase.LoadAssetAtPath<AssetRegistry>(RegistryPath);
            if (reg == null)
            {
                reg = ScriptableObject.CreateInstance<AssetRegistry>();
                AssetDatabase.CreateAsset(reg, RegistryPath);
            }
            reg.Models.Clear();
            reg.AnimationSets.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Art" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var name = Path.GetFileNameWithoutExtension(path);
                reg.Models.Add(new AssetRegistry.ModelEntry { Name = name, Prefab = go });
                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
                if (clips.Length > 0) reg.AnimationSets.Add(new AssetRegistry.ClipSetEntry { Name = name, Clips = clips });
            }
            reg.Icons.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { "Assets/Art/Icons" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (s != null) reg.Icons.Add(new AssetRegistry.SpriteEntry { Name = Path.GetFileNameWithoutExtension(path), Sprite = s });
            }
            reg.Audio.Clear();
            if (AssetDatabase.IsValidFolder("Assets/Audio"))
                foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var c = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (c != null) reg.Audio.Add(new AssetRegistry.ClipEntry { Name = Path.GetFileNameWithoutExtension(path), Clip = c });
                }
            reg.Materials.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { EmbervaleAssetPostprocessor.MaterialDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m != null) reg.Materials.Add(new AssetRegistry.MaterialEntry { Name = m.name, Material = m });
            }
            reg.TerrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(EmbervaleAssetPostprocessor.MaterialDir + "/EV_Terrain.mat");
            reg.WaterMaterial = AssetDatabase.LoadAssetAtPath<Material>(EmbervaleAssetPostprocessor.MaterialDir + "/EV_Water.mat");
            reg.HighlightMaterial = AssetDatabase.LoadAssetAtPath<Material>(EmbervaleAssetPostprocessor.MaterialDir + "/EV_Highlight.mat");
            EditorUtility.SetDirty(reg);
            AssetDatabase.SaveAssets();
            Debug.Log("[Embervale] Asset registry: " + reg.Models.Count + " models, " + reg.AnimationSets.Count + " animation sets, " + reg.Icons.Count + " icons, " + reg.Audio.Count + " audio clips, " + reg.Materials.Count + " materials.");
            return reg;
        }

        [MenuItem("Embervale/Rebuild Asset Registry", priority = 1)]
        private static void RebuildRegistryMenu() => BuildRegistry();

        // ------------------------------------------------------------------ scene
        private static void CreateBootScene()
        {
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(light.gameObject);
                var cam = Camera.main;
                if (cam != null)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.08f, 0.1f, 0.16f);
                }
                new GameObject("GameSession").AddComponent<GameSession>();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
