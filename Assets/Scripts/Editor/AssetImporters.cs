using System.Collections.Generic;
using System.IO;
using Embervale.Client;
using UnityEditor;
using UnityEngine;

namespace Embervale.EditorTools
{
    /// <summary>
    /// Import rules for everything the Blender pipeline exports into Assets/Art and the generated audio in
    /// Assets/Audio. Models get axis conversion baked, legacy animation, readable meshes (runtime static
    /// batching), clip names without the "Armature|" prefix, the shared EV_* materials and a TintInfo per
    /// renderer so colours baked into material names ("EV_Cloth__6a4c33") survive as per-instance tints.
    /// </summary>
    public sealed class EmbervaleAssetPostprocessor : AssetPostprocessor
    {
        public const string ArtRoot = "Assets/Art/";
        public const string MaterialDir = "Assets/Art/Materials";

        private static readonly HashSet<string> LoopClips = new HashSet<string>
        {
            "Idle", "Walk", "Run", "Mine", "Chop", "Fish", "Smith", "Cook", "Craft", "Pray", "Farm", "Climb", "Firemaking", "Pickpocket"
        };

        private static bool IsArt(string path) => path.Replace('\\', '/').StartsWith(ArtRoot);

        private static bool IsAnimated(string path)
        {
            path = path.Replace('\\', '/');
            return path.Contains("/Characters/") || path.Contains("/Creatures/");
        }

        private static bool HasClips(string path)
        {
            var file = Path.GetFileNameWithoutExtension(path);
            return file.EndsWith("_Animations") || path.Replace('\\', '/').Contains("/Creatures/");
        }

        private void OnPreprocessModel()
        {
            if (!IsArt(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.isReadable = true;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importBlendShapes = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.addCollider = false;
            if (IsAnimated(assetPath))
            {
                mi.animationType = ModelImporterAnimationType.Legacy;
                mi.generateAnimations = ModelImporterGenerateAnimations.GenerateAnimations;
                mi.importAnimation = HasClips(assetPath);
                mi.optimizeGameObjects = false;
            }
            else
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
            }
        }

        private void OnPreprocessAnimation()
        {
            if (!IsArt(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            if (!mi.importAnimation) return;
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (var c in clips)
            {
                var n = c.name;
                int bar = n.LastIndexOf('|');
                if (bar >= 0) n = n.Substring(bar + 1);
                c.name = n;
                c.loopTime = LoopClips.Contains(n);
                c.wrapMode = c.loopTime ? WrapMode.Loop : WrapMode.Once;
            }
            mi.clipAnimations = clips;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsArt(assetPath)) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                var keys = new string[mats.Length];
                var baked = new Color[mats.Length];
                var replaced = new Material[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i] != null ? mats[i].name : "EV_Atlas";
                    SplitMaterialName(src, out keys[i], out baked[i]);
                    var shared = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/" + keys[i] + ".mat");
                    replaced[i] = shared != null ? shared : mats[i];
                }
                r.sharedMaterials = replaced;
                var info = r.gameObject.AddComponent<TintInfo>();
                info.SlotKeys = keys;
                info.BakedColors = baked;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
        }

        /// <summary>"EV_Cloth__6a4c33" -> key EV_Cloth, colour #6a4c33. Unknown names map to EV_Atlas.</summary>
        public static void SplitMaterialName(string name, out string key, out Color baked)
        {
            baked = new Color(0, 0, 0, 0);
            name = name.Replace(" (Instance)", "");
            int dot = name.IndexOf('.');
            if (dot > 0) name = name.Substring(0, dot); // Blender duplicates: EV_Metal.001
            int sep = name.IndexOf("__");
            key = sep > 0 ? name.Substring(0, sep) : name;
            if (sep > 0 && ColorUtility.TryParseHtmlString("#" + name.Substring(sep + 2), out var c)) baked = c;
            if (!key.StartsWith("EV_")) key = "EV_Atlas";
        }

        private void OnPreprocessTexture()
        {
            var path = assetPath.Replace('\\', '/');
            var ti = (TextureImporter)assetImporter;
            if (path.StartsWith("Assets/Art/Icons/"))
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.filterMode = FilterMode.Bilinear;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.maxTextureSize = 256;
            }
            else if (path.StartsWith("Assets/Art/Textures/"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Repeat;
                bool atlas = path.EndsWith("ev_atlas.png");
                ti.filterMode = atlas ? FilterMode.Point : FilterMode.Bilinear;
                ti.textureCompression = atlas ? TextureImporterCompression.Uncompressed : TextureImporterCompression.Compressed;
                if (atlas) { ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; }
            }
            else if (path.StartsWith("Tools/") || path.Contains("/Previews/"))
            {
                ti.textureType = TextureImporterType.Default;
            }
        }

        private void OnPreprocessAudio()
        {
            var path = assetPath.Replace('\\', '/');
            if (!path.StartsWith("Assets/Audio/")) return;
            var ai = (AudioImporter)assetImporter;
            var s = ai.defaultSampleSettings;
            bool music = path.Contains("/Music/") || path.Contains("/Ambient/");
            s.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = music ? 0.6f : 0.8f;
            ai.defaultSampleSettings = s;
            ai.forceToMono = !music;
            ai.loadInBackground = music;
        }
    }
}
