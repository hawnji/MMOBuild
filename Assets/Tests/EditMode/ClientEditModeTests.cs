using System;
using System.IO;
using System.Linq;
using Embervale.Client;
using Embervale.EditorTools;
using Embervale.Shared.Data;
using Embervale.Shared.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Embervale.Tests
{
    public class ClientEditModeTests
    {
        [Test]
        public void ProjectUsesExactUnityVersion()
        {
            var text = File.ReadAllText("ProjectSettings/ProjectVersion.txt");
            StringAssert.Contains("m_EditorVersion: 6000.6.9f1", text);
        }

        [Test]
        public void GameDataLoadsFromStreamingAssets()
        {
            var db = GameDataLoader.Load();
            Assert.That(db.Items.Count, Is.GreaterThan(100));
            Assert.That(db.World, Is.Not.Null);
            CollectionAssert.IsEmpty(db.Validate());
        }

        [Test]
        public void MaterialNamesSplitIntoKeyAndBakedColour()
        {
            EmbervaleAssetPostprocessor.SplitMaterialName("EV_Cloth__6a4c33", out var key, out var c);
            Assert.AreEqual("EV_Cloth", key);
            Assert.AreEqual(0x6a / 255f, c.r, 0.01f);
            Assert.AreEqual(1f, c.a);
            EmbervaleAssetPostprocessor.SplitMaterialName("EV_Metal.001", out key, out c);
            Assert.AreEqual("EV_Metal", key);
            Assert.AreEqual(0f, c.a);
            EmbervaleAssetPostprocessor.SplitMaterialName("Material", out key, out _);
            Assert.AreEqual("EV_Atlas", key);
        }

        [Test]
        public void EveryActionAnimationMapsToAnAuthoredClip()
        {
            var authored = new[] { "Idle", "Walk", "Run", "Turn", "AttackMelee", "AttackRanged", "Cast", "Mine", "Chop", "Fish", "Smith", "Cook", "Eat", "Drink",
                                   "Hurt", "Death", "Craft", "Pray", "Farm", "Climb", "Pickpocket", "Firemaking", "Interact", "Wave" };
            foreach (ActionAnim a in Enum.GetValues(typeof(ActionAnim)))
                CollectionAssert.Contains(authored, AnimationDriver.ClipFor(a), a + " has no clip");
        }

        [Test]
        public void TerrainHeightMatchesCornerHeights()
        {
            var db = GameDataLoader.Load();
            var t = new WorldTerrain(db.World);
            for (int i = 0; i < 50; i++)
            {
                int x = 10 + i * 7, z = 20 + i * 5;
                Assert.AreEqual(t.CornerHeight(x, z), t.SampleHeight(x, z), 1e-3f);
            }
        }

        [Test]
        public void PaletteParsesHexColours()
        {
            var c = Palette.Hex("#ff8000", Color.black);
            Assert.AreEqual(1f, c.r, 0.01f);
            Assert.AreEqual(0.5f, c.g, 0.01f);
            Assert.AreEqual(Color.black, Palette.Hex("not a colour", Color.black));
        }

        [Test]
        public void RuntimeArtJsonIsPublished()
        {
            foreach (var f in new[] { "palette.json", "equipment_attach.json", "creature_visuals.json" })
                FileAssert.Exists(Path.Combine(Application.streamingAssetsPath, "Art", f));
        }

        /// <summary>Runs after Embervale > Setup Project: every model/icon named by game data must exist.</summary>
        [Test]
        public void AllReferencedAssetsExist()
        {
            if (AssetDatabase.LoadAssetAtPath<AssetRegistry>(ProjectBootstrap.RegistryPath) == null)
                Assert.Ignore("Run Embervale > Setup Project first.");
            var problems = AssetValidator.Validate();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void RegistryHasHumanoidClipsAndPrefabs()
        {
            var reg = AssetDatabase.LoadAssetAtPath<AssetRegistry>(ProjectBootstrap.RegistryPath);
            if (reg == null) Assert.Ignore("Run Embervale > Setup Project first.");
            Assert.IsNotNull(reg.Model("Humanoid_Male"));
            Assert.IsNotNull(reg.Model("Humanoid_Female"));
            var clips = reg.Clips("Humanoid_Animations").Select(c => c.name).ToList();
            CollectionAssert.Contains(clips, "Walk");
            CollectionAssert.Contains(clips, "AttackMelee");
            Assert.IsTrue(reg.Clips("Humanoid_Animations").First(c => c.name == "Walk").legacy, "Clips must import as Legacy for the Animation component.");
        }
    }
}
