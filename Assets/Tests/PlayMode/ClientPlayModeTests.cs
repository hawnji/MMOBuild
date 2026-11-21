using System.Collections;
using System.IO;
using Embervale.Client;
using Embervale.Shared.Net;
using Embervale.Shared.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Embervale.Tests
{
    /// <summary>
    /// End-to-end checks of the real client against the in-process server: login, character creation,
    /// entering the world, the world/entities/UI being built, and server-authoritative movement.
    /// </summary>
    public class ClientPlayModeTests
    {
        private static IEnumerator WaitFor(System.Func<bool> cond, float timeout, string what)
        {
            float t = 0;
            while (!cond())
            {
                t += Time.unscaledDeltaTime;
                if (t > timeout) Assert.Fail("Timed out waiting for " + what);
                yield return null;
            }
        }

        [SetUp]
        public void CleanLocalWorld()
        {
            var dir = Path.Combine(Application.persistentDataPath, "LocalWorld");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        [UnityTest]
        public IEnumerator OfflineLoopbackServerAcceptsLoginAndCharacter()
        {
            var db = GameDataLoader.Load();
            using (var host = new LocalServerHost(db))
            {
                var client = new GameClient(db);
                host.Connect(client);
                client.Connect();
                bool listed = false;
                client.OnCharacterList += () => listed = true;
                host.Update(0);
                Assert.IsTrue(client.HandshakeDone);
                client.Register("tester", "secret123");
                yield return WaitFor(() => { host.Update(Time.deltaTime); return listed; }, 5, "character list");
                client.CreateCharacter("Tester", new Appearance { Gender = 1, SkinTone = 2 });
                yield return WaitFor(() => { host.Update(Time.deltaTime); return client.Characters.Count == 1; }, 5, "character created");
                client.SelectCharacter(client.Characters[0].Id);
                yield return WaitFor(() => { host.Update(Time.deltaTime); return client.InWorld && client.Self != null; }, 5, "enter world");
                Assert.That(client.Entities.Count, Is.GreaterThan(1));
                Assert.That(client.Inventory[0].IsEmpty, Is.False, "starter items");
            }
        }

        [UnityTest]
        public IEnumerator FullClientBootsIntoWorldAndWalks()
        {
            // GameSession auto-creates itself when a scene loads; reuse it if present.
            var session = GameSession.Instance != null ? GameSession.Instance : new GameObject("TestSession").AddComponent<GameSession>();
            var go = session.gameObject;
            try
            {
                Assert.IsNotNull(session.Db, "game data");
                session.PlayOffline("walker", "secret123", true);
                yield return WaitFor(() => session.State == ClientState.CharacterCreate, 10, "character creation screen");
                session.CreateCharacter("Walker", new Appearance());
                yield return WaitFor(() => session.State == ClientState.CharacterSelect || session.Client.Characters.Count > 0, 10, "character list");
                session.SelectCharacter(session.Client.Characters[0].Id);
                yield return WaitFor(() => session.State == ClientState.InWorld && session.SelfView != null, 60, "world built");

                Assert.IsNotNull(session.World.MapTexture);
                Assert.That(session.Entities.Views.Count, Is.GreaterThan(10));
                Assert.IsNotNull(session.SelfView.Rig, "player uses the humanoid rig");
                Assert.IsNotNull(Object.FindAnyObjectByType<Camera>());

                var start = session.SelfView.transform.position;
                int tx = Mathf.FloorToInt(start.x) + 4, tz = Mathf.FloorToInt(start.z);
                if (session.World.Terrain.IsBlocked(tx, tz)) tx = Mathf.FloorToInt(start.x) - 4;
                session.Client.MoveTo(tx, tz, false);
                yield return new WaitForSeconds(4f);
                Assert.That(Vector3.Distance(start, session.SelfView.transform.position), Is.GreaterThan(1.5f), "player moved after a server-approved MoveTo");

                // Frame time budget sanity check (not a benchmark): the client must stay interactive.
                float worst = 0;
                for (int i = 0; i < 60; i++) { worst = Mathf.Max(worst, Time.unscaledDeltaTime); yield return null; }
                Debug.Log("[Test] worst frame over 60 frames: " + (worst * 1000f).ToString("0.0") + " ms");
            }
            finally
            {
                session.ReturnToLogin();
                Object.Destroy(go);
            }
        }
    }
}
