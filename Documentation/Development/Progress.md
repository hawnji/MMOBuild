# Development progress and known issues

_Last updated 2026-11-21._

## The main blocker: Unity 6000.6.9f1 has not opened this project yet
The project targets **Unity 6000.6.9f1** and nothing else. That editor could not be installed in the cloud build environment, because Unity's download servers refuse those hosts. Following the project rules, no other Unity version was used. As a result:

- **Done:** every client, editor and test script compiles against Unity reference assemblies (see [Testing](../Testing/Testing.md)).
- **Done:** all 126 server and simulation tests pass.
- **Not done:** the first import in Unity 6000.6.9f1, the URP asset creation, a visual check of the art, socket orientation and clip names after import, the Unity Test Runner pass and a frame-rate check. All of these must happen on a Windows PC with that editor installed.
- **Not done:** the `.meta` files. Unity creates them on first import, and they should then be committed.

Until that pass is done, nothing that depends on Unity is marked as verified. Asset manifests show `validation: pending-unity-import`.

## Milestones

| # | Milestone | State | What exists |
|---|---|---|---|
| 1 | Foundation | Done, Unity import pending | Repo layout, LFS, `.gitignore`, shared library, headless server, protocol, bootstrap editor menu |
| 2 | Art pipeline | Done | Blender Python pipeline, versioned `.blend` sources, palette atlas, FBX exports, previews, manifests, importer rules, validator |
| 3 | Character and environment | Done, visual check pending | Modular male and female humanoids with 24 animations; 26 armour meshes and 18 weapon/shield models; 12 creatures; 29 buildings, 35 props, nature; a 384×384 world with 7 regions ([map](world_map.png)); runtime terrain and world builder; day lighting and per-region fog and music |
| 4 | Core gameplay | Done | 20 skills to level 99; 28-slot inventory; equipment shown on characters; bank; 174 recipes; gathering nodes; shops; 5 quests with dialogue; farming; agility; thieving; prayer; runecrafting |
| 5 | Combat | Done | Melee, ranged and magic; 10 spells; 6 prayers; 12 monsters including 2 bosses with telegraphed abilities; loot tables with ownership timers; death and respawn |
| 6 | Multiplayer and persistence | Done | Authoritative .NET 8 server over LiteNetLib UDP; interest management; anti-spoofing and flood limits; PBKDF2 accounts; SQLite persistence with autosave; reconnection; restart recovery; offline mode through Loopback |
| 7 | World content | Done | 7 regions (Brookhollow, Greenleaf Forest, Highmarch, Ironridge Mountains, Ashen Marshes, Ancient Ruins, Embervale Meadows); 255 items; 15 NPC types; 21 resource node types; spawns; dungeons/ruins; generated music, ambience and 47 sound effects |
| 8 | Economy and social | Mostly done | **Embervale Exchange**, an order-book player marketplace that also serves as the auction house (escrow, offline fills, 1% gold-sink tax, persistence, 3 clerks); safe two-player trade with escrow and disconnect recovery; chat (local, trade, private, party, system); parties. **Not yet built:** a friends and ignore list, and group activities beyond party combat |
| 9 | Polish and optimisation | Partial | GPU instancing on shared materials; one palette atlas so the world batches; pooled 3D audio sources and minimap dots; server interest management; settings for FPS cap, vsync, shadows, UI scale, roofs and volume. **Not yet built:** LOD meshes, occlusion culling bake, frame-rate profiling in Unity |

## Known issues and gaps
1. The Unity verification pass described above has not happened.
2. There are no LOD meshes. The models are low-poly already, but the pipeline does not yet export LOD1 or LOD2 versions.
3. Occlusion culling is not baked. The world is built at runtime, so this needs a baked static scene or camera-distance culling.
4. The friends and ignore list is not implemented. Private messages work by name.
5. Asset manifests list prefab paths, but the client assembles characters and creatures at runtime from the FBX files. No `.prefab` assets are committed.
6. The client uses Legacy animation (the `Animation` component) instead of Mecanim. This keeps the modular skinned parts simple. Clip names and loop flags are set by the importer and should be checked after the first import.
7. Server configuration in `appsettings.json` contains defaults only. Put real deployment values in `appsettings.Local.json`, which git ignores.

## Next steps
1. Open the project in Unity 6000.6.9f1 on Windows, run **Embervale ▸ Setup Project**, fix any import errors, then commit the `.meta` files, the `Assets/Resources` registry and the Boot scene.
2. Run the EditMode and PlayMode tests and play through the 5 quests both offline and against a dedicated server.
3. Build LODs and occlusion culling, add the friends and ignore list, then profile against the 60 FPS target.
