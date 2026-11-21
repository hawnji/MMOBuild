# Asset pipeline

Every 3D asset in the game is built by Python scripts that run in Blender. The scripts are deterministic, versioned and headless. Nothing is hand-placed, and no primitive cubes are used as stand-ins: characters, armour, weapons, creatures, buildings, props, nature and item models are all modelled in code from a mesh builder. Each one has its own silhouette, UVs, materials and (where it applies) a skeleton and animations.

## Running it

```
pip install bpy                                  # or use Blender 4.4+ / 5.x:
python Tools/Blender/Scripts/build_all.py        # blender -b -P Tools/Blender/Scripts/build_all.py
python Tools/Blender/Scripts/build_all.py --no-previews
```

Order: textures, then characters and animations, then equipment, creatures, world assets, and finally items and icons. You can also run each `build_*.py` script on its own.

## Layout

| Path | What |
|---|---|
| `Tools/Blender/Scripts/emb/core.py` | Mesh builder, palette atlas, materials, export, manifests, `publish_runtime` |
| `emb/humanoid.py`, `rig.py`, `anim_humanoid.py` | Modular male and female body, 20-bone skeleton, animation clips |
| `emb/equipment.py` | Weapons, shields, armour pieces fitted to the body (per sex) |
| `emb/creatures.py` | 12 creatures and bosses, each with its own rig and clips |
| `emb/buildings.py`, `props.py`, `environment.py` | Houses, forge, bank, castle pieces, ruins; furniture and town props; trees, rocks, ore nodes, plants |
| `emb/items.py`, `preview.py` | Ground-item models, inventory icon renders, preview contact sheets |
| `Tools/Blender/SourceFiles/*_v001.blend` | Versioned source scenes saved by each build |
| `Tools/Blender/Previews/` | Preview renders and contact sheets (characters, equipment, creatures, buildings, item icons, animation strips) |
| `Tools/Blender/AssetDocumentation/manifest_*.json` | One entry per asset: source, exported path, triangle and bone counts, clips, status, validation state |
| `Tools/Blender/Export/` | Intermediate exports |

## Output in Unity

| Folder | Contents |
|---|---|
| `Assets/Art/Characters/Humanoid` | `Humanoid_Male.fbx`, `Humanoid_Female.fbx` and their animation FBX files |
| `Assets/Art/Characters/Armour` | 26 armour meshes (13 pieces × male/female) that are skinned to the same skeleton |
| `Assets/Art/Equipment` | Weapons and shields that attach to sockets (`equipment_attach.json`) |
| `Assets/Art/Creatures` | 12 rigged creatures plus `creature_visuals.json` |
| `Assets/Art/Buildings`, `Props`, `Environment`, `Items` | Static models |
| `Assets/Art/Icons`, `Textures` | Item icon atlas, palette atlas and generated tiling textures |
| `Assets/StreamingAssets/Art` | `palette.json`, `equipment_attach.json`, `creature_visuals.json` for the runtime |

### Style rules
- Flat-shaded low-poly. Characters are about 3k triangles, creatures 0.5–3k, props mostly under 500.
- One palette atlas (16×16 named colour cells). Material slots are named `EV_<Name>`. The Unity importer remaps them to shared URP materials in `Assets/Art/Materials`, so the whole world batches together and uses GPU instancing.
- Tintable materials (skin, hair, cloth, metal tiers) take their colour from `TintInfo` at runtime. This is how bronze, iron, steel and mithril gear share one mesh.
- Forward axis and scale are baked on export. The importer sets readable meshes and Legacy animation for Characters and Creatures, and renames clips and sets their loop flags.

## Animations
The humanoid has 24 clips, all authored at 30 fps: Idle, Walk, Run, Turn, AttackMelee, AttackRanged, Cast, Mine, Chop, Fish, Smith, Cook, Eat, Drink, Hurt, Death, Craft, Pray, Farm, Climb, Pickpocket, Firemaking, Interact and Wave. Humanoid enemies reuse this set; heavy enemies use a slowed-down version of it. Four-legged creatures and spiders have their own clip sets. Preview strips are in `Tools/Blender/Previews/anims`.

## Audio
`Tools/Audio/generate_audio.py` uses numpy to synthesise all audio deterministically:
- 47 sound effects in `Assets/Audio/Sfx`;
- 8 music loops in `Assets/Audio/Music` (one per region plus the title theme);
- 7 ambient loops in `Assets/Audio/Ambient`.

It writes `manifest_audio.json`.

## Validation
- `Embervale ▸ Validate Assets` (`AssetValidator`) checks:
  - the game data loads and passes `GameDatabase.Validate()`;
  - every model named by the data exists in the asset registry;
  - every item and skill icon exists;
  - every animation set has clips.
- EditMode tests run the same validator.
- Manifest entries show `validation: pending-unity-import` until the project has been imported in Unity 6000.6.9f1. See [Progress](../Development/Progress.md).
- The manifests list a `prefab` path for some assets. The client assembles those objects at runtime from the FBX files, so no prefab assets are committed yet.

## Git LFS
`.fbx`, `.blend`, `.png`, `.wav` and other binary formats are tracked by LFS (see `.gitattributes`).
