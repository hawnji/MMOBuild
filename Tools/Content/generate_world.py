#!/usr/bin/env python3
"""
Embervale world layout generator.

Builds the shared world definition (Assets/StreamingAssets/GameData/world.json) used by:
  * the dedicated server  - walkability grid, doors, resource nodes, spawns, NPCs, stations
  * the Unity client      - terrain heightmap, terrain paint, building/prop placement

The world is deterministic (fixed seed) so server and client always agree. Edit the
layout tables below and re-run:

    python3 Tools/Content/generate_world.py [--preview Documentation/Development/world_map.png]

Coordinates: X = east, Z = north, 1 tile = 1 metre, origin at the south-west corner.
"""
import base64
import json
import math
import os
import random
import sys

import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "StreamingAssets", "GameData", "world.json")

W = H = 384
HM_RES = 2  # heightmap sample spacing in metres
SEED = 1337
rng = random.Random(SEED)

# Tile types (keep in sync with Embervale.Shared.World.TileType)
GRASS, PATH, WATER, SAND, ROCK, MARSH, STONE, FOREST, SNOW, DEEP = range(10)

tiles = np.full((H, W), GRASS, dtype=np.uint8)       # [z, x]
blocked = np.zeros((H, W), dtype=np.uint8)
heights = np.zeros((H // HM_RES + 1, W // HM_RES + 1), dtype=np.float32)

REGIONS = [
    # id, name, x0, z0, x1, z1, music, fog colour, ambient
    ("brookhollow", "Brookhollow", 150, 30, 232, 112, "village", "#c8d8e8", "village"),
    ("greenleaf", "Greenleaf Forest", 8, 24, 150, 205, "forest", "#a8c8a0", "forest"),
    ("highmarch", "Highmarch", 152, 198, 238, 292, "city", "#d0d4dc", "city"),
    ("ironridge", "Ironridge Mountains", 240, 168, 378, 378, "mountain", "#b8bcc8", "wind"),
    ("ashen", "Ashen Marshes", 240, 6, 378, 162, "marsh", "#8a9480", "marsh"),
    ("ruins", "Ancient Ruins", 8, 214, 150, 378, "dungeon", "#9890a0", "ruins"),
    ("meadow", "Embervale Meadows", 0, 0, 384, 384, "meadow", "#c8dce8", "meadow"),  # fallback, keep last
]


# ---------------------------------------------------------------------------
# Noise helpers
# ---------------------------------------------------------------------------
def value_noise(shape, scale, seed):
    r = np.random.default_rng(seed)
    gh, gw = int(shape[0] / scale) + 3, int(shape[1] / scale) + 3
    grid = r.random((gh, gw))
    zs = np.arange(shape[0]) / scale
    xs = np.arange(shape[1]) / scale
    z0 = zs.astype(int)
    x0 = xs.astype(int)
    tz = (zs - z0)[:, None]
    tx = (xs - x0)[None, :]
    tz = tz * tz * (3 - 2 * tz)
    tx = tx * tx * (3 - 2 * tx)
    a = grid[z0][:, x0]
    b = grid[z0][:, x0 + 1]
    c = grid[z0 + 1][:, x0]
    d = grid[z0 + 1][:, x0 + 1]
    return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz


def fbm(shape, scale, seed, octaves=4):
    total = np.zeros(shape)
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        total += value_noise(shape, scale / (2 ** o), seed + o * 101) * amp
        norm += amp
        amp *= 0.5
    return total / norm


def region_at(x, z):
    for r in REGIONS:
        if r[2] <= x < r[4] and r[3] <= z < r[5]:
            return r[0]
    return "meadow"


# ---------------------------------------------------------------------------
# Terrain: heights + base tile types
# ---------------------------------------------------------------------------
hs = heights.shape
n = fbm(hs, 40, SEED, 5)
hz, hx = np.mgrid[0:hs[0], 0:hs[1]] * HM_RES
base = 2.0 + n * 3.0
# Mountains rise to the north-east
mount = np.clip(((hx - 240) / 60.0), 0, 1) * np.clip(((hz - 168) / 60.0), 0, 1)
ridge = fbm(hs, 18, SEED + 7, 4)
base += mount * (10 + ridge * 26)
# Marsh sits low
marsh = np.clip((hx - 240) / 30.0, 0, 1) * np.clip((162 - hz) / 30.0, 0, 1)
base -= marsh * 1.5
# Ruins plateau
ruinsm = np.clip((150 - hx) / 30.0, 0, 1) * np.clip((hz - 214) / 30.0, 0, 1)
base += ruinsm * 2.5
heights[:] = base

tn = fbm((H, W), 12, SEED + 3, 3)
for z in range(H):
    for x in range(W):
        reg = region_at(x, z)
        if reg == "greenleaf":
            tiles[z, x] = FOREST if tn[z, x] > 0.35 else GRASS
        elif reg == "ironridge":
            h = heights[z // HM_RES, x // HM_RES]
            tiles[z, x] = SNOW if h > 30 else (ROCK if tn[z, x] > 0.45 or h > 18 else GRASS)
        elif reg == "ashen":
            tiles[z, x] = MARSH
        elif reg == "ruins":
            tiles[z, x] = STONE if tn[z, x] > 0.6 else GRASS


def height_at(x, z):
    return float(heights[min(hs[0] - 1, int(z) // HM_RES), min(hs[1] - 1, int(x) // HM_RES)])


# Cliffs: steep mountain areas become unwalkable rock
for z in range(1, H - 1):
    for x in range(1, W - 1):
        gx, gz = x // HM_RES, z // HM_RES
        if gx + 1 >= hs[1] or gz + 1 >= hs[0]:
            continue
        slope = max(abs(heights[gz, gx + 1] - heights[gz, gx]), abs(heights[gz + 1, gx] - heights[gz, gx]))
        if slope > 2.6:
            tiles[z, x] = ROCK
            blocked[z, x] = 1

# Map border is blocked
blocked[0, :] = blocked[-1, :] = blocked[:, 0] = blocked[:, -1] = 1


# ---------------------------------------------------------------------------
# Water: river + lakes
# ---------------------------------------------------------------------------
def paint_disc(cx, cz, r, ttype, block):
    for z in range(max(0, int(cz - r - 1)), min(H, int(cz + r + 2))):
        for x in range(max(0, int(cx - r - 1)), min(W, int(cx + r + 2))):
            if (x - cx) ** 2 + (z - cz) ** 2 <= r * r:
                tiles[z, x] = ttype
                blocked[z, x] = 1 if block else 0


def polyline(points, step=0.5):
    for (ax, az), (bx, bz) in zip(points, points[1:]):
        length = math.hypot(bx - ax, bz - az)
        for i in range(int(length / step) + 1):
            t = i * step / max(length, 1e-6)
            yield ax + (bx - ax) * t, az + (bz - az) * t


RIVER = [(318, 383), (300, 330), (262, 270), (244, 225), (238, 180), (236, 130), (238, 80), (234, 30), (238, 0)]
river_cells = []
for px, pz in polyline(RIVER):
    paint_disc(px, pz, 4.2, SAND, False)
for px, pz in polyline(RIVER):
    paint_disc(px, pz, 3.0, WATER, True)
    river_cells.append((px, pz))
LAKES = [(172, 128, 9, "brook_lake"), (300, 60, 11, "marsh_pool"), (70, 120, 7, "forest_pond")]
for cx, cz, r, _ in LAKES:
    paint_disc(cx, cz, r + 1.6, SAND, False)
    paint_disc(cx, cz, r, WATER, True)
    paint_disc(cx, cz, r * 0.45, DEEP, True)
# Marsh puddles
for i in range(40):
    cx, cz = rng.uniform(250, 370), rng.uniform(15, 150)
    paint_disc(cx, cz, rng.uniform(1.5, 3.5), WATER, True)

# ---------------------------------------------------------------------------
# Roads
# ---------------------------------------------------------------------------
ROADS = [
    [(191, 72), (192, 120), (196, 170), (195, 205)],                # Brookhollow -> Highmarch
    [(191, 72), (150, 76), (110, 82), (60, 90)],                    # -> Greenleaf
    [(191, 72), (236, 74), (270, 70), (310, 80)],                   # -> Ashen Marshes (bridge)
    [(195, 245), (240, 250), (262, 262), (290, 290), (320, 320)],   # Highmarch -> Ironridge (bridge)
    [(195, 245), (150, 250), (120, 270), (90, 300)],                # Highmarch -> Ruins
    [(191, 72), (191, 32)],                                         # south gate
]
bridges = []
for road in ROADS:
    for px, pz in polyline(road):
        for z in range(int(pz) - 1, int(pz) + 2):
            for x in range(int(px) - 1, int(px) + 2):
                if 0 < x < W - 1 and 0 < z < H - 1:
                    if tiles[z, x] in (WATER, DEEP):
                        bridges.append((x, z))
                    tiles[z, x] = PATH
                    blocked[z, x] = 0


def bridge_objects():
    """Group bridge tiles into bridge props."""
    out, seen = [], set()
    for b in bridges:
        if b in seen:
            continue
        cluster = [c for c in bridges if abs(c[0] - b[0]) < 10 and abs(c[1] - b[1]) < 10]
        seen.update(cluster)
        xs = [c[0] for c in cluster]
        zs = [c[1] for c in cluster]
        out.append({"model": "bridge_wood", "x": (min(xs) + max(xs) + 1) / 2.0, "z": (min(zs) + max(zs) + 1) / 2.0,
                    "rot": 90 if (max(xs) - min(xs)) > (max(zs) - min(zs)) else 0,
                    "length": max(max(xs) - min(xs), max(zs) - min(zs)) + 3})
    return out


# ---------------------------------------------------------------------------
# Placement helpers
# ---------------------------------------------------------------------------
buildings, doors, props, decor, nodes, spawns, npcs, stations, patches, obstacles, stalls = ([] for _ in range(11))
locations = {}
_id = [1000]


def next_id():
    _id[0] += 1
    return _id[0]


def free(x, z, r=1):
    for zz in range(int(z) - r, int(z) + r + 1):
        for xx in range(int(x) - r, int(x) + r + 1):
            if not (0 < xx < W - 1 and 0 < zz < H - 1):
                return False
            if blocked[zz, xx] or tiles[zz, xx] in (PATH, WATER, DEEP):
                return False
    return True


def block_rect(x0, z0, w, d, val=1):
    blocked[z0:z0 + d, x0:x0 + w] = val


DOOR_SIDES = {"S": (0, -1), "N": (0, 1), "W": (-1, 0), "E": (1, 0)}


def building(kind, style, x0, z0, w, d, door="S", name=None, roof="gable"):
    """Walls on the footprint perimeter are blocked; the door tile is a door entity; interior walkable."""
    bid = next_id()
    block_rect(x0, z0, w, d, 1)
    block_rect(x0 + 1, z0 + 1, w - 2, d - 2, 0)
    for zz in range(z0, z0 + d):
        for xx in range(x0, x0 + w):
            tiles[zz, xx] = STONE
    if door == "S":
        dx, dz = x0 + w // 2, z0
    elif door == "N":
        dx, dz = x0 + w // 2, z0 + d - 1
    elif door == "W":
        dx, dz = x0, z0 + d // 2
    else:
        dx, dz = x0 + w - 1, z0 + d // 2
    blocked[dz, dx] = 0
    # clear outside of door
    ox, oz = DOOR_SIDES[door]
    for k in range(1, 3):
        if 0 < dz + oz * k < H and 0 < dx + ox * k < W:
            blocked[dz + oz * k, dx + ox * k] = 0
    door_id = next_id()
    doors.append({"id": door_id, "x": dx, "z": dz, "side": door, "building": bid, "open": False})
    buildings.append({"id": bid, "kind": kind, "style": style, "x": x0, "z": z0, "w": w, "d": d, "door": door,
                      "doorId": door_id, "name": name or kind.title(), "roof": roof,
                      "height": height_at(x0 + w / 2, z0 + d / 2)})
    return bid, (x0 + w // 2, z0 + d // 2)


def station(kind, x, z, model, rot=0, block=True):
    stations.append({"id": next_id(), "type": kind, "x": x, "z": z, "model": model, "rot": rot})
    if block:
        blocked[int(z), int(x)] = 1


def node(defid, x, z, block=True):
    if block and not free(x, z, 0):
        return False
    nodes.append({"id": next_id(), "def": defid, "x": int(x) + 0.5, "z": int(z) + 0.5})
    if block:
        blocked[int(z), int(x)] = 1
    return True


def npc(defid, x, z, rot=180):
    npcs.append({"id": next_id(), "def": defid, "x": x + 0.5, "z": z + 0.5, "rot": rot})


def spawn(monster, cx, cz, count, radius):
    spawns.append({"id": next_id(), "monster": monster, "x": cx, "z": cz, "count": count, "radius": radius})


def prop(model, x, z, rot=0, block=False, scale=1.0):
    props.append({"model": model, "x": x, "z": z, "rot": rot, "scale": round(scale, 2)})
    if block:
        blocked[int(z), int(x)] = 1


def scatter_nodes(defid, x0, z0, x1, z1, count, cluster=None):
    placed, tries = 0, 0
    while placed < count and tries < count * 60:
        tries += 1
        if cluster:
            cx, cz, cr = cluster
            x, z = cx + rng.uniform(-cr, cr), cz + rng.uniform(-cr, cr)
        else:
            x, z = rng.uniform(x0, x1), rng.uniform(z0, z1)
        if free(x, z, 1) and node(defid, x, z):
            placed += 1
    return placed


# ---------------------------------------------------------------------------
# Brookhollow (starting village)
# ---------------------------------------------------------------------------
locations["brookhollow_square"] = {"x": 191.5, "z": 72.5}
for zz in range(64, 82):
    for xx in range(182, 201):
        tiles[zz, xx] = PATH
station("Water", 191, 76, "well")  # village well (fills vials/buckets)
_, bank_c = building("bank", "stone", 170, 80, 10, 8, "S", "Brookhollow Hearthvault")
_, shop_c = building("shop", "wood", 204, 80, 10, 8, "S", "General Goods")
_, smith_c = building("blacksmith", "stone", 204, 54, 12, 9, "N", "Hollin's Forge", roof="flat")
_, inn_c = building("inn", "wood", 168, 52, 12, 10, "N", "The Sleepy Otter")
_, temple_c = building("temple", "stone", 186, 92, 10, 12, "S", "Shrine of Embers")
_, h1 = building("house", "wood", 154, 66, 8, 7, "E")
_, h2 = building("house", "wood", 218, 68, 8, 7, "W")
_, h3 = building("house", "wood", 158, 96, 7, 7, "E")
_, h4 = building("warehouse", "wood", 214, 96, 12, 8, "W", "Village Storehouse")
building("tower", "stone", 184, 34, 5, 5, "N", "South Watchtower", roof="cone")
building("tower", "stone", 196, 34, 5, 5, "N", "South Watchtower", roof="cone")
npc("banker", bank_c[0], bank_c[1] + 1, 180)
npc("tilda_merchant", shop_c[0], shop_c[1] + 1, 180)
npc("hollin_smith", smith_c[0] - 2, smith_c[1], 0)
station("Furnace", smith_c[0] + 3, smith_c[1] + 1, "furnace")
station("Anvil", smith_c[0] + 1, smith_c[1] - 2, "anvil")
npc("innkeeper_bram", inn_c[0], inn_c[1] - 1, 0)
station("Range", inn_c[0] + 3, inn_c[1] + 2, "cooking_range")
npc("elder_maren", 193, 74, 180)
npc("guard", 189, 40, 180)
npc("guard", 193, 40, 180)
npc("villager_m", 186, 70)
npc("villager_f", 197, 68)
npc("fisher_odo", 176, 120, 0)
station("Tannery", 160, 78, "tanning_rack")
station("PrayerAltar", temple_c[0], temple_c[1] + 3, "prayer_altar")
# Training area with rats
prop("training_dummy", 214, 42, 0, True)
prop("training_dummy", 217, 42, 0, True)
spawn("giant_rat", 222, 44, 5, 5)
# Village props
for (mx, mz, mr) in [(185, 66, 0), (198, 66, 0), (185, 80, 0), (198, 80, 0)]:
    prop("lamp_post", mx, mz, mr, True)
for i in range(10):
    prop(rng.choice(["barrel", "crate", "crate_stack"]), rng.uniform(203, 214), rng.uniform(63, 66),
         rng.uniform(0, 360), True)
prop("bench", 188, 69, 90, True)
prop("bench", 195, 69, 270, True)
prop("signpost", 189, 64, 0, True)
prop("market_stall", 200, 76, 270, True)
prop("campfire", 222, 52, 0, True)
# Fences around farm
patch_origin = (150, 40)
for i in range(4):
    px, pz = patch_origin[0] + 2 + i * 5, patch_origin[1] + 4
    patches.append({"id": next_id(), "x": px + 1.5, "z": pz + 1.5, "w": 4, "d": 4})
for xx in range(150, 172, 2):
    prop("fence", xx + 1, 42, 0, True)
    prop("fence", xx + 1, 52, 0, True)
station("Mill", 174, 44, "windmill")
block_rect(173, 43, 3, 3)
# Copper/tin quarry east of village
scatter_nodes("copper_rock", 0, 0, 0, 0, 6, (226, 86, 5))
scatter_nodes("tin_rock", 0, 0, 0, 0, 6, (226, 86, 5))
# Trees around village
scatter_nodes("oak_tree", 150, 30, 232, 112, 22)
# Fishing at lake
for a in range(0, 360, 60):
    x = 172 + math.cos(math.radians(a)) * 10.5
    z = 128 + math.sin(math.radians(a)) * 10.5
    nodes.append({"id": next_id(), "def": "net_spot" if a < 180 else "rod_spot", "x": round(x, 1),
                  "z": round(z, 1)})

# ---------------------------------------------------------------------------
# Greenleaf Forest
# ---------------------------------------------------------------------------
scatter_nodes("oak_tree", 10, 26, 148, 200, 60)
scatter_nodes("pine_tree", 10, 120, 148, 200, 45)
scatter_nodes("willow_tree", 0, 0, 0, 0, 8, (70, 120, 13))
scatter_nodes("maple_tree", 20, 150, 90, 200, 12)
spawn("goblin", 120, 100, 8, 10)
spawn("goblin", 90, 60, 6, 8)
spawn("wolf", 50, 160, 6, 12)
spawn("wolf", 110, 175, 4, 10)
spawn("forest_spirit", 30, 60, 3, 6)
spawn("giant_spider", 25, 185, 4, 6)
for a in range(0, 360, 90):
    nodes.append({"id": next_id(), "def": "rod_spot", "x": round(70 + math.cos(math.radians(a)) * 8.3, 1),
                  "z": round(120 + math.sin(math.radians(a)) * 8.3, 1)})
# hidden grove (quest location) ringed by dense pines
locations["hidden_grove"] = {"x": 26.5, "z": 40.5, "radius": 4}
for a in range(0, 360, 20):
    if 60 < a < 100:
        continue  # secret gap
    x, z = 26 + math.cos(math.radians(a)) * 8, 40 + math.sin(math.radians(a)) * 8
    if free(x, z, 0):
        decor.append({"model": "tree_pine", "x": int(x) + 0.5, "z": int(z) + 0.5, "rot": rng.uniform(0, 360),
                      "scale": 1.3})
        blocked[int(z), int(x)] = 1
prop("mushroom_ring", 26.5, 40.5)
prop("treasure_chest", 27.5, 41.5, 30, True)
# Agility course
course = [("log_balance", 128, 140), ("rope_swing", 128, 150), ("wall_climb", 136, 156), ("ledge_walk", 144, 150)]
for i, (oid, x, z) in enumerate(course):
    obstacles.append({"id": next_id(), "def": oid, "x": x + 0.5, "z": z + 0.5, "order": i,
                      "endX": x + 0.5 + (0 if i % 2 else 4), "endZ": z + 0.5 + (4 if i % 2 else 0)})
# decorative forest density (client only, still blocks)
for i in range(700):
    x, z = rng.uniform(10, 148), rng.uniform(26, 203)
    if tiles[int(z), int(x)] == FOREST and free(x, z, 1):
        decor.append({"model": rng.choice(["tree_oak", "tree_pine", "tree_pine", "tree_dead"]), "x": int(x) + 0.5,
                      "z": int(z) + 0.5, "rot": rng.uniform(0, 360), "scale": round(rng.uniform(0.8, 1.35), 2)})
        blocked[int(z), int(x)] = 1

# ---------------------------------------------------------------------------
# Highmarch (capital)
# ---------------------------------------------------------------------------
locations["highmarch_plaza"] = {"x": 195.5, "z": 241.5}
for zz in range(232, 260):
    for xx in range(180, 212):
        tiles[zz, xx] = STONE
# city wall with four gates
for xx in range(156, 236):
    for zz in (202, 288):
        if abs(xx - 195) > 2:
            blocked[zz, xx] = 1
            tiles[zz, xx] = ROCK
for zz in range(202, 289):
    for xx in (156, 235):
        if abs(zz - 245) > 2 and abs(zz - 250) > 2:
            blocked[zz, xx] = 1
            tiles[zz, xx] = ROCK
props.append({"model": "city_wall", "x": 195.5, "z": 245.0, "rot": 0, "scale": 1.0, "w": 80, "d": 87})
_, cb = building("bank", "stone", 162, 236, 12, 10, "E", "Highmarch Grand Vault")
_, cm = building("shop", "stone", 214, 236, 10, 8, "W", "The Gilded Sigil")
_, ca = building("shop", "stone", 214, 252, 10, 8, "W", "Highmarch Armoury")
_, cp = building("shop", "wood", 162, 252, 10, 8, "E", "Mossbottle Apothecary")
_, cf = building("blacksmith", "stone", 168, 212, 12, 10, "N", "Royal Forge", roof="flat")
_, ct = building("temple", "stone", 187, 266, 16, 14, "S", "Cathedral of the Hearth")
_, cw = building("warehouse", "stone", 214, 210, 14, 10, "N", "Guild Warehouse")
_, ci = building("inn", "wood", 162, 268, 12, 10, "E", "The Gilded Tankard")
building("house", "stone", 214, 270, 10, 8, "W")
building("house", "stone", 222, 222, 8, 8, "W")
for tx, tz in [(157, 203), (230, 203), (157, 283), (230, 283)]:
    building("tower", "stone", tx, tz, 5, 5, "N", "Highmarch Tower", roof="cone")
npc("banker", cb[0] + 2, cb[1], 90)
npc("banker", cb[0] + 2, cb[1] + 2, 90)
npc("magic_vendor", cm[0] - 2, cm[1], 270)
npc("arms_vendor", ca[0] - 2, ca[1], 270)
npc("apothecary", cp[0] + 2, cp[1], 90)
npc("warden_seeker", 196, 262, 180)
for gx in (193, 197):
    npc("guard", gx, 205, 180)
    npc("guard", gx, 285, 0)
npc("merchant", 190, 250)
npc("villager_m", 200, 240)
npc("villager_f", 205, 248)
station("Furnace", cf[0] + 3, cf[1] + 1, "furnace")
station("Anvil", cf[0] - 1, cf[1] + 1, "anvil")
station("Anvil", cf[0] + 1, cf[1] + 2, "anvil")
station("Range", ci[0] + 3, ci[1] + 2, "cooking_range")
station("Water", 195, 245, "fountain")
station("PrayerAltar", ct[0], ct[1] + 4, "prayer_altar")
block_rect(194, 244, 3, 3)
# marketplace stalls (thieving + player market area)
for i, (sx, stype) in enumerate([(184, "bakery_stall"), (190, "gem_stall"), (200, "bakery_stall"),
                                 (206, "gem_stall")]):
    stalls.append({"id": next_id(), "def": stype, "x": sx + 0.5, "z": 236.5})
    blocked[236, sx] = 1
locations["highmarch_market"] = {"x": 195.5, "z": 238.5, "radius": 10}
for (lx, lz) in [(182, 234), (209, 234), (182, 258), (209, 258)]:
    prop("lamp_post", lx, lz, 0, True)

# ---------------------------------------------------------------------------
# Ironridge Mountains
# ---------------------------------------------------------------------------
scatter_nodes("iron_rock", 0, 0, 0, 0, 10, (270, 215, 12))
scatter_nodes("coal_rock", 0, 0, 0, 0, 10, (300, 240, 14))
scatter_nodes("silver_rock", 0, 0, 0, 0, 5, (330, 220, 10))
scatter_nodes("gold_rock", 0, 0, 0, 0, 5, (340, 280, 10))
scatter_nodes("mithril_rock", 0, 0, 0, 0, 4, (300, 330, 10))
scatter_nodes("duskite_rock", 0, 0, 0, 0, 3, (350, 350, 8))
scatter_nodes("starmetal_rock", 0, 0, 0, 0, 2, (365, 365, 6))
scatter_nodes("essence_rock", 0, 0, 0, 0, 4, (258, 190, 5))
scatter_nodes("pine_tree", 240, 168, 300, 240, 20)
spawn("orc", 300, 250, 6, 12)
spawn("orc", 340, 240, 4, 8)
spawn("troll", 330, 330, 3, 8)
spawn("troll", 290, 345, 2, 6)
# Cave (enclosed rock chamber)
locations["ironridge_cave"] = {"x": 320.5, "z": 300.5, "radius": 8}
for a in range(0, 360, 4):
    if 170 < a < 190:
        continue
    for rr in (11, 12):
        x, z = int(320 + math.cos(math.radians(a)) * rr), int(300 + math.sin(math.radians(a)) * rr)
        blocked[z, x] = 1
        tiles[z, x] = ROCK
for zz in range(289, 312):
    for xx in range(309, 332):
        if (xx - 320) ** 2 + (zz - 300) ** 2 < 110:
            blocked[zz, xx] = 0
            tiles[zz, xx] = ROCK
props.append({"model": "cave_shell", "x": 320.5, "z": 300.5, "rot": 0, "scale": 1.0})
scatter_nodes("mithril_rock", 0, 0, 0, 0, 3, (320, 300, 6))
spawn("troll", 322, 302, 2, 4)
# Runecrafting altars
for i, (rid, ax, az) in enumerate([("air", 262, 182), ("water", 248, 140), ("earth", 120, 180), ("fire", 360, 200),
                                   ("mind", 214, 120), ("life", 40, 70), ("chaos", 100, 340),
                                   ("void", 370, 370), ("ward", 160, 300), ("path", 230, 180)]):
    if not free(ax, az, 1):
        for _ in range(200):
            nx, nz = ax + rng.randint(-8, 8), az + rng.randint(-8, 8)
            if free(nx, nz, 1):
                ax, az = nx, nz
                break
    station("Altar_" + rid, ax, az, "altar_" + rid)

# ---------------------------------------------------------------------------
# Ashen Marshes
# ---------------------------------------------------------------------------
spawn("zombie", 290, 110, 8, 14)
spawn("zombie", 340, 60, 6, 12)
spawn("giant_spider", 330, 130, 5, 8)
spawn("bandit", 255, 40, 5, 8)
scatter_nodes("willow_tree", 245, 10, 375, 160, 15)
for a in range(0, 360, 45):
    nodes.append({"id": next_id(), "def": "marsh_spot", "x": round(300 + math.cos(math.radians(a)) * 12.4, 1),
                  "z": round(60 + math.sin(math.radians(a)) * 12.4, 1)})
# abandoned settlement
for (bx, bz) in [(320, 100), (334, 96), (326, 112)]:
    building("ruin_house", "wood", bx, bz, 7, 6, rng.choice(["S", "N", "E", "W"]), "Abandoned Hut", roof="broken")
locations["abandoned_village"] = {"x": 328.5, "z": 104.5, "radius": 10}
spawn("mire_queen", 360, 30, 1, 3)
locations["mire_queen_lair"] = {"x": 360.5, "z": 30.5, "radius": 6}
for i in range(260):
    x, z = rng.uniform(244, 376), rng.uniform(8, 160)
    if free(x, z, 1):
        decor.append({"model": rng.choice(["tree_dead", "tree_dead", "reeds", "mushroom_cluster"]), "x": int(x) + 0.5,
                      "z": int(z) + 0.5, "rot": rng.uniform(0, 360), "scale": round(rng.uniform(0.8, 1.3), 2)})
        if decor[-1]["model"] == "tree_dead":
            blocked[int(z), int(x)] = 1
# Deep water fishing at the river mouth (south)
for zz in (14, 20, 26):
    nodes.append({"id": next_id(), "def": "deep_spot", "x": 234.5 + (zz % 3), "z": zz + 0.5})

# ---------------------------------------------------------------------------
# Ancient Ruins (dungeon complex + boss)
# ---------------------------------------------------------------------------
RX0, RZ0, RX1, RZ1 = 40, 300, 120, 370
for xx in range(RX0, RX1):
    for zz in (RZ0, RZ1 - 1):
        if not (78 <= xx <= 82 and zz == RZ0):
            blocked[zz, xx] = 1
            tiles[zz, xx] = ROCK
for zz in range(RZ0, RZ1):
    for xx in (RX0, RX1 - 1):
        blocked[zz, xx] = 1
        tiles[zz, xx] = ROCK
for zz in range(RZ0 + 1, RZ1 - 1):
    for xx in range(RX0 + 1, RX1 - 1):
        tiles[zz, xx] = STONE
# inner halls
for xx in range(RX0, RX1):
    if abs(xx - 80) > 2:
        blocked[335, xx] = 1
for zz in range(RZ0, 335):
    if abs(zz - 318) > 2:
        blocked[zz, 60] = 1
        blocked[zz, 100] = 1
# boss chamber
for xx in range(65, 96):
    if abs(xx - 80) > 1:
        blocked[352, xx] = 1
props.append({"model": "ruin_complex", "x": 80.0, "z": 335.0, "rot": 0, "scale": 1.0,
              "x0": RX0, "z0": RZ0, "x1": RX1, "z1": RZ1})
locations["warden_chamber"] = {"x": 80.5, "z": 362.5, "radius": 7}
spawn("ruin_warden", 80, 362, 1, 2)
spawn("skeleton", 80, 318, 6, 12)
spawn("skeleton", 50, 345, 4, 6)
spawn("skeleton", 110, 345, 4, 6)
spawn("skeleton", 80, 240, 6, 12)
for i in range(80):
    x, z = rng.uniform(12, 148), rng.uniform(216, 296)
    if free(x, z, 1):
        prop(rng.choice(["ruin_pillar", "ruin_pillar_broken", "ruin_rubble", "statue_broken"]), int(x) + 0.5,
             int(z) + 0.5, rng.uniform(0, 360), True)
for (cx, cz) in [(48, 360), (112, 360), (60, 308)]:
    prop("treasure_chest", cx + 0.5, cz + 0.5, 0, True)
scatter_nodes("ghostwood_tree", 12, 216, 148, 296, 6)
scatter_nodes("yew_tree", 12, 216, 148, 296, 6)

# ---------------------------------------------------------------------------
# Meadows: roadside bandits, extra resources, ambient props
# ---------------------------------------------------------------------------
spawn("bandit", 160, 170, 4, 8)
spawn("giant_rat", 215, 150, 4, 6)
scatter_nodes("oak_tree", 120, 112, 236, 200, 25)
scatter_nodes("sand_pit", 0, 0, 0, 0, 3, (226, 128, 3))
scatter_nodes("yew_tree", 0, 0, 0, 0, 3, (150, 195, 6))
for i in range(300):
    x, z = rng.uniform(2, 382), rng.uniform(2, 382)
    if free(x, z, 1):
        reg = region_at(x, z)
        choices = {"ironridge": ["rock_large", "rock_small", "tree_pine"],
                   "ruins": ["rock_small", "bush", "tree_dead"],
                   "ashen": ["reeds", "rock_small"]}.get(reg, ["bush", "rock_small", "flowers", "log_fallen",
                                                              "mushroom_cluster", "bush"])
        m = rng.choice(choices)
        decor.append({"model": m, "x": int(x) + 0.5, "z": int(z) + 0.5, "rot": rng.uniform(0, 360),
                      "scale": round(rng.uniform(0.8, 1.3), 2)})
        if m in ("rock_large", "tree_pine", "tree_dead", "log_fallen"):
            blocked[int(z), int(x)] = 1

for b in bridge_objects():
    props.append(dict(b, scale=1.0))

# Spawn points must be walkable
for k, loc in locations.items():
    if blocked[int(loc["z"]), int(loc["x"])]:
        print("WARNING: location %s is blocked" % k, file=sys.stderr)
        blocked[int(loc["z"]), int(loc["x"])] = 0


# ---------------------------------------------------------------------------
# Connectivity check: flood fill from Brookhollow; anything unreachable is reported
# ---------------------------------------------------------------------------
def flood(sx, sz):
    seen = np.zeros_like(blocked)
    stack = [(sx, sz)]
    seen[sz, sx] = 1
    while stack:
        x, z = stack.pop()
        for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, nz = x + dx, z + dz
            if 0 <= nx < W and 0 <= nz < H and not seen[nz, nx] and not blocked[nz, nx]:
                seen[nz, nx] = 1
                stack.append((nx, nz))
    return seen


reach = flood(191, 72)
# doors are passable (open on interaction) so count door tiles + interiors via doors
problems = []
for name, loc in locations.items():
    if not reach[int(loc["z"]), int(loc["x"])]:
        problems.append("location " + name)
unreach_nodes = 0
for nd in nodes:
    x, z = int(nd["x"]), int(nd["z"])
    if not any(reach[z + dz, x + dx] for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)) if 0 <= z + dz < H and 0 <= x + dx < W):
        unreach_nodes += 1
for sp in spawns:
    if not reach[int(sp["z"]), int(sp["x"])]:
        problems.append("spawn %s at %s,%s" % (sp["monster"], sp["x"], sp["z"]))
if problems:
    print("WARNING unreachable: " + "; ".join(problems), file=sys.stderr)
if unreach_nodes:
    print("WARNING: %d resource nodes have no reachable neighbour" % unreach_nodes, file=sys.stderr)


# ---------------------------------------------------------------------------
# Output
# ---------------------------------------------------------------------------
def b64(arr):
    return base64.b64encode(arr.tobytes()).decode("ascii")


hq = np.clip(heights / 64.0 * 65535.0, 0, 65535).astype("<u2")

world = {
    "version": 1,
    "seed": SEED,
    "width": W,
    "height": H,
    "heightmap": {"resolution": HM_RES, "width": hs[1], "height": hs[0], "maxHeight": 64.0,
                  "encoding": "uint16le-base64", "data": b64(hq)},
    "tiles": {"encoding": "uint8-base64", "data": b64(tiles)},
    "blocked": {"encoding": "uint8-base64", "data": b64(blocked)},
    "regions": [{"id": r[0], "name": r[1], "x0": r[2], "z0": r[3], "x1": r[4], "z1": r[5], "music": r[6],
                 "fog": r[7], "ambient": r[8]} for r in REGIONS],
    "locations": locations,
    "buildings": buildings,
    "doors": doors,
    "stations": stations,
    "nodes": nodes,
    "spawns": spawns,
    "npcs": npcs,
    "farmPatches": patches,
    "agility": obstacles,
    "stalls": stalls,
    "props": props,
    "decor": decor,
    "lakes": [{"id": l[3], "x": l[0], "z": l[1], "radius": l[2]} for l in LAKES],
    "river": [{"x": p[0], "z": p[1]} for p in RIVER],
}
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w") as f:
    json.dump(world, f, separators=(",", ":"))
print("world.json: %d buildings, %d nodes, %d spawns, %d npcs, %d stations, %d props, %d decor, walkable=%.1f%%, reachable=%.1f%%"
      % (len(buildings), len(nodes), len(spawns), len(npcs), len(stations), len(props), len(decor),
         100.0 * (1 - blocked.mean()), 100.0 * reach.mean()))

if "--preview" in sys.argv:
    from PIL import Image
    path = sys.argv[sys.argv.index("--preview") + 1]
    pal = {GRASS: (110, 160, 80), PATH: (190, 160, 110), WATER: (60, 120, 200), SAND: (220, 200, 140),
           ROCK: (120, 115, 110), MARSH: (90, 105, 75), STONE: (170, 170, 165), FOREST: (60, 115, 55),
           SNOW: (235, 235, 240), DEEP: (30, 70, 150)}
    img = np.zeros((H, W, 3), dtype=np.uint8)
    for k, c in pal.items():
        img[tiles == k] = c
    img[(blocked == 1) & (tiles != WATER) & (tiles != DEEP)] = (img[(blocked == 1) & (tiles != WATER) & (tiles != DEEP)] * 0.55).astype(np.uint8)
    for nd in nodes:
        img[int(nd["z"]), int(nd["x"])] = (255, 220, 0)
    for sp in spawns:
        z, x = int(sp["z"]), int(sp["x"])
        img[max(0, z - 1):z + 2, max(0, x - 1):x + 2] = (220, 30, 30)
    for n_ in npcs:
        img[int(n_["z"]), int(n_["x"])] = (255, 255, 255)
    im = Image.fromarray(img[::-1]).resize((W * 2, H * 2), Image.NEAREST)
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    im.save(path)
    print("preview ->", path)
