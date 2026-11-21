# Architecture

## Overview

```
 Unity client (Embervale.Client)          Dedicated server (.NET 8, EmbervaleServer)
 ┌──────────────────────────────┐         ┌─────────────────────────────────────┐
 │ GameSession  UI  Views Audio │         │ Program: config, tick loop, autosave│
 │ GameClient ◄── packets ──────┼── UDP ──┼──► ServerCore ──► GameWorld         │
 │ LocalServerHost (offline) ───┼─Loopback│        │              │             │
 └──────────────────────────────┘         │  SqliteGameStore ◄── CharacterData  │
                                          └─────────────────────────────────────┘
            both reference  Packages/com.embervale.shared  (netstandard2.1)
```

The server is authoritative for everything. The client sends only intents, such as "move to a tile", "interact with entity X using option Y", "equip slot 4" or "place a market order". It renders whatever state the server sends back.

## Shared library (`Packages/com.embervale.shared`)

| Namespace | Responsibility |
|---|---|
| `Embervale.Shared` | Core types (`Vec2`, `GameTime`), deterministic RNG |
| `.Data` | `GameDatabase` loads the JSON content: item, recipe, monster, NPC, quest, dialogue, shop and world definitions |
| `.World` | `WorldGrid`: heights, walkability, A* pathing on the tile grid |
| `.Skills` | `ExperienceTable` (levels 1–99), `SkillSet` |
| `.Items` | `ItemContainer` (28-slot inventory, bank, stacking), `Equipment` (slots, bonuses) |
| `.Combat` | `CombatFormulas`: accuracy, max hit, attack speed, and melee/ranged/magic styles |
| `.Simulation` | `GameWorld`, a fixed 0.5 s tick. Split into Players, Skilling, Combat, Social (dialogue, bank, shops, trade, chat, parties) and Market. Includes `Marketplace` and `WorldEvents` |
| `.Net` | `Protocol` (packet ids), `Packet` serialisation, `ServerCore`, `GameClient`, `Loopback` transport |
| `.Persistence` | `CharacterData`/`WorldStateData` snapshots and the `IGameStore` interface. `FileGameStore` is the JSON store used in tests and offline play |

### Tick model
`GameWorld.Tick()` advances everything once per 0.5 s:
- movement along server-computed paths;
- skilling actions;
- combat swings and projectiles;
- respawns, crop growth and regeneration.

Anything that happened is pushed into a `WorldEvents` queue. `ServerCore` reads that queue and turns it into packets.

### Interest management
Each tick, `ServerCore` queries the world's spatial grid within `GameWorld.ViewRadius` (48 tiles) of each player. It sends spawn and despawn packets when entities enter or leave that area, and position or state deltas only for entities in view. Chat, sounds and hit splats are filtered the same way.

## Networking
- Transport: LiteNetLib (UDP: reliable-ordered delivery for game packets, sequenced delivery for frequent position updates) on port 7777. Offline play uses the in-memory `Loopback` transport, so it runs exactly the same server code.
- Sessions move through these states: Connected → Authenticated → CharacterSelect → InWorld. Account passwords are salted and hashed with PBKDF2 before storage.
- Inputs are validated on the server, including range checks, ownership checks and a limit of 40 packets per tick per session. The client never decides an outcome.

## Persistence
- `SqliteGameStore` keeps accounts, characters (a JSON document plus indexed columns) and a world-state row.
- Characters are saved on logout, by autosave (default every 60 s) and at shutdown.
- `WorldStateData` holds:
  - farming patches;
  - Exchange orders, last traded prices, the next order id and the tax total;
  - the time it was saved. On restart, elapsed real time is applied to growth timers.
- The Exchange orders the character and world saves so that coins and items are never duplicated or lost if the server crashes between the two writes (see `MarketPersist` in `GameWorld.Market.cs`).

## Embervale Exchange (player marketplace)
- Each character can hold up to 6 buy or sell orders, placed at an exchange clerk.
- Placing an order moves its coins (for a buy) or items (for a sell) out of the inventory and into escrow.
- Matching uses price, then time. A trade fills at the resting order's price, and the buyer gets back any amount they offered above that price. A player's own orders never match each other.
- Sellers pay a 1% tax on what they receive. That coin is removed from the game, which acts as a gold sink.
- Filled goods wait on the order until the owner collects them, so orders can fill while the owner is offline. A finished order is removed once it has been collected.

## Unity client (`Assets/Scripts/Runtime`)
| Area | Classes |
|---|---|
| Core | `GameSession` (state machine `ClientState`: Login → CharacterSelect → InWorld), `Networking`/`LocalServerHost`, `Settings`, `InputAdapter` (Input System or legacy), `AssetRegistry`, `Tinting` |
| World | `WorldBuilder` (terrain chunks from the heightmap, water, buildings, props, roofs), `EnvironmentController` (sun, ambient light, per-region fog and music) |
| Entities | `EntityManager` and views for players, NPCs, monsters, nodes and ground items. `Characters` assembles the modular body and equipment, and plays legacy animation clips |
| Gameplay | `CameraController` (orbiting isometric camera with collision), `PlayerInteraction` (picking, context menus, item/spell targeting, NavMesh path preview), `Effects` |
| UI | Built in code with uGUI. `UIRoot`, `FrontEnd` (login, character select and create, with a 3D preview), `Hud` (orbs, minimap, chat, overheads, XP drops), `SidePanel` (tabs), `Windows` (dialogue, shop, bank, trade, crafting, quest journal, world map), `MarketWindow` |
| Audio | `AudioManager`: music crossfade, ambience, pooled 3D sound effects |

The client draws a NavMesh path (baked at runtime by `NavMeshBuilder`) to preview movement and snap clicks to walkable ground. The route that counts is always the one the server computes.

## Editor tools (`Assets/Scripts/Editor`)
- `ProjectBootstrap`: one-click setup (packages, materials, URP asset, player settings, asset registry, boot scene).
- `AssetImporters`: rules for importing FBX, textures and audio, renaming clips, remapping materials.
- `BuildTools`: client and server builds, plus `AssetValidator`.
