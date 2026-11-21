# Embervale (MMOBuild)

Embervale is an original, persistent fantasy MMORPG. It has a classic low-poly 3D look, an elevated isometric camera and point-and-click movement.

- **Client:** Unity **6000.6.9f1** (exactly this version) with URP, for Windows PC.
- **Server:** headless, authoritative .NET 8, using LiteNetLib over UDP and SQLite for storage.
- **Shared code:** all gameplay rules live in one C# library. The dedicated server, the Unity client and the offline mode all use it.

> **Status, stated plainly.** The server, the simulation, the content, the generated art and audio, and the client scripts are all written. The 126 server tests pass. The client and editor scripts compile against Unity reference assemblies. **The project has not yet been opened in Unity 6000.6.9f1**, because that editor cannot be installed in the build environment where this code was written. The first import, the visual check and the Unity Test Runner pass still have to happen on a Windows PC. See [Documentation/Development/Progress.md](Documentation/Development/Progress.md).

## Quick start (Windows)

1. Install **Unity 6000.6.9f1** through Unity Hub. Do not open the project with any other version, because Unity would upgrade or downgrade it.
2. Install [Git LFS](https://git-lfs.com) and run `git lfs install`. Then clone the repo and run `git lfs pull` (models, textures and audio are stored in LFS).
3. Open the repo root as a Unity project. When it finishes importing, run **Embervale ▸ Setup Project**. This step:
   - installs URP, the Input System and the Test Framework;
   - creates the materials and the URP pipeline asset;
   - sets the player settings;
   - builds `Assets/Resources/EmbervaleAssets.asset`;
   - creates `Assets/Scenes/Boot.unity`;
   - runs the asset validator.

   You can also run it headless: `Unity.exe -batchmode -projectPath . -executeMethod Embervale.EditorTools.ProjectBootstrap.SetupBatch`
4. Open `Assets/Scenes/Boot.unity` and press Play.
   - **Play Offline** runs a full server inside the client.
   - **Play Online** connects to a dedicated server.
5. Commit the `.meta` files Unity generates on first import.

## Running the dedicated server

```
dotnet run --project Server/Embervale.Server -c Release
# options: --port 7777  --db Data/embervale.db  --data <GameData dir>  --config <appsettings.json>
```

The server's defaults are in `Server/Embervale.Server/appsettings.json`. Keep production credentials and private config out of git (`Server/**/appsettings.Local.json` is ignored by git). In Unity you can also publish the server with **Embervale ▸ Build ▸ Dedicated Server**, and build the client with **Embervale ▸ Build ▸ Windows Client**.

## Controls

| Input | Action |
|---|---|
| Left click | Walk, or run the default action (attack, chop, talk, take…) |
| Right click | Context menu with every option |
| Middle-mouse drag, Q/E or arrow keys | Rotate and tilt the camera |
| Scroll wheel | Zoom |
| F1–F5 | Inventory, Equipment, Skills, Prayer, Magic |
| M | World map |
| Enter | Chat |
| Esc | Close the top window, or cancel a targeted action |

## Repository layout

| Path | Contents |
|---|---|
| `Assets/Scripts/Runtime` | Unity client: session, networking, world builder, entity views, camera, interaction, UI, audio |
| `Assets/Scripts/Editor` | Project bootstrap, model/texture/audio importers, build tools, asset validator |
| `Assets/Art` | Generated FBX models (characters, armour, creatures, buildings, props, environment, items) and textures |
| `Assets/Audio` | Generated music, ambient loops and sound effects (WAV) |
| `Assets/StreamingAssets/GameData` | Game content as JSON: items, recipes, monsters, quests, world map and more |
| `Assets/Tests` | Unity EditMode and PlayMode tests |
| `Packages/com.embervale.shared` | Shared simulation, rules, protocol, persistence model (netstandard2.1) |
| `Server/` | Dedicated server, SQLite store and xUnit test suite |
| `Tools/Blender` | Python asset pipeline, versioned `.blend` source files, previews and asset manifests |
| `Tools/Content` | Content generators for the game data and the world map |
| `Tools/Audio` | Procedural audio generator |
| `Documentation/` | Architecture, asset pipeline, testing and development progress |

## Regenerating content

```
python Tools/Content/generate_game_data.py      # items, recipes, monsters, NPCs, quests, dialogue…
python Tools/Content/generate_world.py          # 384×384 tile world, regions, spawns
python Tools/Blender/Scripts/build_all.py       # all 3D art (needs Blender 4.4+ or `pip install bpy`)
python Tools/Audio/generate_audio.py            # music, ambience and SFX (needs numpy)
```

All generators are deterministic, so running one again gives the same output unless its inputs change.

## Tests

```
dotnet test Server/Embervale.sln                 # 126 tests: rules, simulation, multiplayer, market, persistence
```

To run the Unity tests, open **Window ▸ General ▸ Test Runner** (EditMode and PlayMode). [Documentation/Testing](Documentation/Testing/Testing.md) explains what each suite covers.

## Documentation

- [Architecture](Documentation/Architecture/Architecture.md)
- [Asset pipeline](Documentation/AssetPipeline/AssetPipeline.md)
- [Testing](Documentation/Testing/Testing.md)
- [Development progress and known issues](Documentation/Development/Progress.md)

Everything here is original. There are no paid dependencies, and the core game does not use any external APIs.
