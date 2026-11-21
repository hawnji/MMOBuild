# Testing

## Server and shared library: `dotnet test Server/Embervale.sln`

All 126 tests pass. They run in about 15 seconds and need no Unity install.

| Suite | Tests | Covers |
|---|---|---|
| `CoreRulesTests` | 46 | Game data cross-references; XP table (levels 1–99, boundaries, round trip, cap, configurable formula); level-ups and requirements; combat level; inventory (stacking, atomic add/remove, overflow, bad quantities); bank; equipment (stats, level requirements, two-handed weapons, ammo, space checks); combat formulas (hit chance, max hit, damage reduction, magic); loot tables; A* pathing (walls, corners, unreachable tiles, the village-to-capital route) |
| `SimulationTests` | 51 | Gathering (tools, levels, full inventory, two players racing for the last ore, walking to the node); fishing bait; smelting, forging, fletching, firemaking and cooking with burn chance; station proximity; melee, ranged and magic combat, loot ownership, aggression, death and respawn, eating, boss telegraphs; quests end to end; bank and shop rules; atomic player trades, escrow and a randomised no-duplication test; farming; save/restore round trips; the SQLite store; password hashing; prayer, runecrafting, teleport; chat sanitising and rate limits; doors |
| `MultiplayerTests` (Loopback plus real UDP) | 17 | Login, registration and character creation; bad passwords and names; movement sync; interest management; anti-spoofing; packet flood throttling; local chat; combat seen by observers; resource depletion seen by everyone; network trades; disconnecting mid-trade; reconnecting; double login; server restart recovery; parties; a load test with many players kept within the tick budget; LiteNetLib UDP end to end |
| `MarketTests` | 9 | Exchange matching (price-time, resting price, refunds, no self-trades); cancel and escrow return; full escrow flow in the world; rejection of bad orders and the per-character order limit; requirement to stand at a clerk; a randomised 400-step test that value is never created or destroyed; orders that survive a server restart and fill while the owner is offline |

## Unity client: Window ▸ General ▸ Test Runner

These tests are written and compile against Unity reference assemblies. **They have not yet been run inside Unity 6000.6.9f1** (see [Progress](../Development/Progress.md)).

| Suite | Tests |
|---|---|
| EditMode (`Assets/Tests/EditMode`) | `ProjectUsesExactUnityVersion`, `GameDataLoadsFromStreamingAssets`, `MaterialNamesSplitIntoKeyAndBakedColour`, `EveryActionAnimationMapsToAnAuthoredClip`, `TerrainHeightMatchesCornerHeights`, `PaletteParsesHexColours`, `RuntimeArtJsonIsPublished`, `AllReferencedAssetsExist` (asset validator), `RegistryHasHumanoidClipsAndPrefabs` |
| PlayMode (`Assets/Tests/PlayMode`) | `OfflineLoopbackServerAcceptsLoginAndCharacter`, `FullClientBootsIntoWorldAndWalks` |

To run them headless:
```
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults editmode.xml
Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults playmode.xml
```

## Asset pipeline checks
- Each Blender build writes a manifest entry per asset with triangle and bone counts, clip names, status and validation state.
- `AssetValidator` runs from the editor menu, inside Setup Project, and in the `AllReferencedAssetsExist` EditMode test.

## How the client was compile-checked without Unity
In the cloud build environment, the client, editor and test assemblies were built with `dotnet` against:
- `UnityEngine.Modules` 2021.3 reference assemblies;
- `UnityEditor.dll` from `Unity3D.SDK` 2021.1;
- a hand-written uGUI API stub.

A few Unity 6 editor APIs that the 2021 assemblies lack, such as `PackageManager.Client.AddAndRemove` and `NamedBuildTarget`, were patched out of a copy for that check only. This catches syntax and API errors. It does **not** replace an import in Unity 6000.6.9f1.
