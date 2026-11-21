using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Embervale.Client;
using Embervale.Shared.Data;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Embervale.EditorTools
{
    /// <summary>Build menu: Windows client player and the dedicated .NET server.</summary>
    public static class BuildTools
    {
        public const string ClientOut = "Builds/Windows/Embervale.exe";
        public const string ServerOut = "Builds/Server";

        [MenuItem("Embervale/Build/Windows Client", priority = 20)]
        public static void BuildClient()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ProjectBootstrap.ScenePath },
                locationPathName = ClientOut,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            var s = report.summary;
            if (s.result == BuildResult.Succeeded) Debug.Log("[Embervale] Client built: " + ClientOut + " (" + (s.totalSize / (1024 * 1024)) + " MB, " + s.totalTime.TotalSeconds.ToString("0") + " s)");
            else Debug.LogError("[Embervale] Client build " + s.result + " with " + s.totalErrors + " errors.");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        [MenuItem("Embervale/Build/Dedicated Server (dotnet publish)", priority = 21)]
        public static void BuildServer()
        {
            var psi = new ProcessStartInfo("dotnet", "publish Server/Embervale.Server -c Release -o " + ServerOut)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                WorkingDirectory = Path.GetFullPath("."),
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode == 0) Debug.Log("[Embervale] Server published to " + ServerOut + "\n" + output);
                    else Debug.LogError("[Embervale] dotnet publish failed:\n" + output);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Embervale] Could not run dotnet (install the .NET 8 SDK): " + e.Message);
            }
        }

        [MenuItem("Embervale/Validate Assets", priority = 2)]
        public static void ValidateMenu()
        {
            var problems = AssetValidator.Validate();
            foreach (var p in problems) Debug.LogWarning("[Embervale] " + p);
            Debug.Log("[Embervale] Validation finished: " + (problems.Count == 0 ? "no problems." : problems.Count + " problems."));
        }
    }

    /// <summary>Checks that every model and icon named by the game data exists in the AssetRegistry.</summary>
    public static class AssetValidator
    {
        public static List<string> Validate()
        {
            var problems = new List<string>();
            var reg = AssetDatabase.LoadAssetAtPath<AssetRegistry>(ProjectBootstrap.RegistryPath);
            if (reg == null) { problems.Add("Asset registry missing. Run Embervale > Setup Project."); return problems; }
            GameDatabase db;
            try { db = GameDatabase.LoadFromDirectory(Path.Combine(Application.streamingAssetsPath, "GameData")); }
            catch (System.Exception e) { problems.Add("Game data failed to load: " + e.Message); return problems; }
            foreach (var e in db.Validate()) problems.Add("Data: " + e);

            var models = new HashSet<string>(reg.Models.Where(m => m.Prefab != null).Select(m => m.Name), System.StringComparer.OrdinalIgnoreCase);
            var icons = new HashSet<string>(reg.Icons.Where(i => i.Sprite != null).Select(i => i.Name), System.StringComparer.OrdinalIgnoreCase);
            void Model(string what, string name) { if (!string.IsNullOrEmpty(name) && !models.Contains(name)) problems.Add(what + " uses missing model '" + name + "'"); }

            foreach (var i in db.Items.Values)
            {
                if (!string.IsNullOrEmpty(i.Icon) && !icons.Contains(i.Icon)) problems.Add("Item " + i.Id + " uses missing icon '" + i.Icon + "'");
                Model("Item " + i.Id, i.Model);
            }
            foreach (var m in db.Monsters.Values) Model("Monster " + m.Id, m.Model);
            foreach (var n in db.Nodes.Values) { Model("Node " + n.Id, n.Model); Model("Node " + n.Id, n.DepletedModel); }
            foreach (var a in db.Agility.Values) Model("Agility " + a.Id, a.Model);
            foreach (var s in db.World.Stations) Model("Station " + s.Id, s.Model);
            foreach (var sk in db.SkillDefs.Values) if (!icons.Contains("skill_" + sk.Id.ToString().ToLowerInvariant())) problems.Add("Missing skill icon for " + sk.Id);
            foreach (var set in new[] { "Humanoid_Animations", "Humanoid_Female_Animations" })
                if (!reg.AnimationSets.Any(s => s.Name == set && s.Clips != null && s.Clips.Length > 0)) problems.Add("Animation set " + set + " has no clips");
            return problems;
        }
    }
}
