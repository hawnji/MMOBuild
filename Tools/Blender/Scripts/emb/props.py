"""
Props, skilling stations, agility obstacles, market stalls and misc world objects.
Authored in Blender axes with the model's "front" facing -Y (Unity +Z), origin at the base.
"""
import math
import random

from mathutils import Vector

from .core import MeshBuilder, p_box, p_taper, p_cyl, p_sphere, p_wedge, p_ico, seg_matrix, T, R, S


def bx(mb, c, s, color, rot=None, mat="EV_Atlas"):
    m = T(*c) @ (R(*rot) if rot else R())
    mb.add(p_box(*s), m, mat=mat, color=color)


def cyl(mb, c, r0, r1, h, color, n=8, rot=None, mat="EV_Atlas"):
    m = T(*c) @ (R(*rot) if rot else R())
    mb.add(p_cyl(r0, r1, h, n=n), m, mat=mat, color=color)


def build(name):
    mb = MeshBuilder(name)
    rnd = random.Random(sum(map(ord, name)))
    if name == "anvil":
        bx(mb, (0, 0, 0.2), (0.5, 0.4, 0.4), "wood_dark")
        bx(mb, (0, 0, 0.45), (0.3, 0.2, 0.15), "iron_dark")
        bx(mb, (0, 0, 0.6), (0.7, 0.28, 0.16), "iron")
        mb.add(p_cyl(0.12, 0.0, 0.3, n=5), T(0.35, 0, 0.6) @ R(0, 90, 0), color="iron")
        bx(mb, (-0.1, 0.05, 0.7), (0.25, 0.05, 0.04), "wood_dark", rot=(0, 0, 20))
    elif name == "furnace":
        mb.add(p_taper(1.4, 1.2, 1.0, 0.9, 1.6), color="stone_warm")
        mb.add(p_taper(0.9, 0.8, 0.5, 0.5, 0.9, z0=1.6), color="stone")
        cyl(mb, (0, 0.1, 2.4), 0.25, 0.2, 0.7, "stone_dark")
        bx(mb, (0, -0.58, 0.55), (0.6, 0.06, 0.5), "black")
        bx(mb, (0, -0.6, 0.5), (0.5, 0.06, 0.35), "fire", mat="EV_Glow")
        bx(mb, (0, -0.62, 0.25), (0.8, 0.2, 0.08), "stone_dark")
    elif name == "cooking_range":
        bx(mb, (0, 0, 0.45), (1.4, 0.9, 0.9), "stone")
        bx(mb, (0, 0, 0.92), (1.45, 0.95, 0.06), "iron_dark")
        bx(mb, (0, -0.46, 0.4), (0.7, 0.04, 0.4), "black")
        bx(mb, (0, -0.47, 0.35), (0.55, 0.04, 0.25), "fire", mat="EV_Glow")
        cyl(mb, (0.3, 0.1, 0.95), 0.2, 0.22, 0.25, "iron")
        cyl(mb, (-0.35, 0.15, 0.95), 0.15, 0.15, 0.08, "iron_dark")
        cyl(mb, (0.5, 0.3, 0.95), 0.12, 0.12, 1.2, "stone_dark")
    elif name == "prayer_altar":
        bx(mb, (0, 0, 0.1), (1.6, 1.0, 0.2), "marble")
        bx(mb, (0, 0, 0.55), (1.3, 0.75, 0.7), "marble")
        bx(mb, (0, 0, 0.95), (1.5, 0.85, 0.1), "gold")
        bx(mb, (0, 0, 0.95), (1.2, 0.9, 0.11), "cloth_red")
        for x in (-0.45, 0.45):
            cyl(mb, (x, 0.1, 1.0), 0.04, 0.04, 0.25, "flower_white")
            mb.add(p_ico(0.05, center=(x, 0.1, 1.3)), mat="EV_Glow")
        cyl(mb, (0, 0.2, 1.0), 0.12, 0.08, 0.15, "gold")
        mb.add(p_ico(0.12, center=(0, 0.2, 1.3)), mat="EV_Glow")
    elif name == "well":
        mb.add(p_cyl(0.75, 0.75, 0.75, n=10), color="stone")
        mb.add(p_cyl(0.55, 0.55, 0.02, n=10, z0=0.6), color="water")
        for x in (-0.7, 0.7):
            bx(mb, (x, 0, 1.2), (0.12, 0.12, 1.4), "wood_dark")
        mb.add(p_wedge(1.8, 1.3, 0.5), T(0, 0, 1.85), color="roof_red")
        cyl(mb, (-0.65, 0, 1.4), 0.08, 0.08, 1.3, "wood", rot=(0, 90, 0), n=6)
        cyl(mb, (0, 0, 0.8), 0.01, 0.01, 0.6, "rope", n=3)
        cyl(mb, (0, 0, 0.75), 0.13, 0.15, 0.22, "wood")
    elif name == "tanning_rack":
        for x in (-0.7, 0.7):
            bx(mb, (x, 0, 0.9), (0.1, 0.1, 1.8), "wood")
        bx(mb, (0, 0, 1.75), (1.6, 0.1, 0.1), "wood")
        bx(mb, (0, 0, 0.25), (1.6, 0.1, 0.1), "wood")
        bx(mb, (0, 0.02, 1.0), (1.1, 0.03, 1.2), "leather")
        for z in (0.45, 1.55):
            for x in (-0.55, 0.55):
                cyl(mb, (x, 0, z), 0.01, 0.01, 0.2, "rope", n=3)
    elif name == "windmill":
        mb.add(p_cyl(1.6, 1.1, 5.0, n=8), color="stone_light")
        mb.add(p_cyl(1.3, 0.0, 1.6, n=8, z0=5.0), color="roof_red")
        bx(mb, (0, -1.4, 0.9), (0.9, 0.3, 1.8), "wood_dark")
        hub = Vector((0, -1.35, 4.3))
        cyl(mb, tuple(hub), 0.2, 0.2, 0.5, "wood_dark", rot=(90, 0, 0))
        for i in range(4):
            a = i * 90 + 15
            d = Vector((math.cos(math.radians(a)), 0, math.sin(math.radians(a))))
            mb.add(p_box(0.12, 0.06, 3.4), seg_matrix(hub - Vector((0, 0.2, 0)), hub - Vector((0, 0.2, 0)) + d) @ T(0, 0, 1.7),
                   color="wood")
            mb.add(p_box(0.6, 0.03, 2.4), seg_matrix(hub - Vector((0, 0.22, 0)), hub - Vector((0, 0.22, 0)) + d)
                   @ T(0.32, 0, 2.0), color="canvas")
    elif name == "fountain":
        mb.add(p_cyl(1.8, 1.8, 0.5, n=12), color="stone_light")
        mb.add(p_cyl(1.55, 1.55, 0.02, n=12, z0=0.42), color="water")
        mb.add(p_cyl(0.3, 0.25, 1.4, n=8), color="stone")
        mb.add(p_cyl(0.8, 0.6, 0.2, n=10, z0=1.3), color="stone_light")
        mb.add(p_cyl(0.65, 0.65, 0.02, n=10, z0=1.45), color="water_light")
        mb.add(p_cyl(0.12, 0.06, 0.7, n=6, z0=1.5), color="stone")
        mb.add(p_ico(0.18, center=(0, 0, 2.3)), color="gold")
    elif name.startswith("altar_") or name == "rune_altar":
        for i in range(6):
            a = i * math.pi / 3
            mb.add(p_taper(0.35, 0.3, 0.2, 0.18, 1.3 + 0.4 * (i % 2)), T(math.cos(a) * 1.6, math.sin(a) * 1.6, 0) @
                   R(0, 0, math.degrees(a)), color="ruin")
        mb.add(p_cyl(1.0, 1.1, 0.3, n=8), color="stone_dark")
        mb.add(p_taper(0.7, 0.7, 0.5, 0.5, 0.6, z0=0.3), color="ruin_dark")
        mb.add(p_ico(0.35, center=(0, 0, 1.3), jitter=0.1, seed=4, squash=(0.8, 0.8, 1.3)), mat="EV_Glow")
        for i in range(4):
            a = i * math.pi / 2 + math.pi / 4
            bx(mb, (math.cos(a) * 0.62, math.sin(a) * 0.62, 0.32), (0.18, 0.18, 0.03), "rune_glow", mat="EV_Glow")
    # ---------------- props
    elif name == "fence":
        for x in (-0.5, 0.5):
            bx(mb, (x, 0, 0.5), (0.1, 0.1, 1.0), "wood_dark")
        for z in (0.35, 0.75):
            bx(mb, (0, 0, z), (1.1, 0.06, 0.1), "wood")
    elif name == "lamp_post":
        cyl(mb, (0, 0, 0), 0.12, 0.08, 0.2, "stone_dark")
        cyl(mb, (0, 0, 0.2), 0.05, 0.05, 2.4, "iron_dark", n=6)
        bx(mb, (0, -0.2, 2.4), (0.05, 0.45, 0.05), "iron_dark")
        mb.add(p_taper(0.22, 0.22, 0.3, 0.3, 0.35, z0=2.0), T(0, -0.4, 0), color="iron_dark")
        mb.add(p_box(0.18, 0.18, 0.25, center=(0, -0.4, 2.17)), mat="EV_Glow")
    elif name == "crate":
        bx(mb, (0, 0, 0.35), (0.7, 0.7, 0.7), "wood_light")
        for z in (0.06, 0.64):
            bx(mb, (0, 0, z), (0.74, 0.74, 0.1), "wood")
        bx(mb, (0, -0.36, 0.35), (0.08, 0.02, 0.8), "wood", rot=(0, 45, 0))
    elif name == "crate_stack":
        for i, (x, y, z, r) in enumerate(((0, 0, 0, 0), (0.75, 0.1, 0, 10), (0.35, 0.05, 0.7, -8))):
            bx(mb, (x, y, z + 0.35), (0.7, 0.7, 0.7), "wood_light", rot=(0, 0, r))
            bx(mb, (x, y, z + 0.06), (0.74, 0.74, 0.1), "wood", rot=(0, 0, r))
    elif name == "barrel":
        mb.add(p_cyl(0.3, 0.36, 0.45, n=10), color="wood")
        mb.add(p_cyl(0.36, 0.3, 0.45, n=10, z0=0.45), color="wood")
        for z in (0.1, 0.45, 0.8):
            mb.add(p_cyl(0.33 + (0.035 if z == 0.45 else 0.01), 0.33 + (0.035 if z == 0.45 else 0.01), 0.05, n=10, z0=z - 0.02),
                   color="iron_dark")
    elif name == "treasure_chest":
        bx(mb, (0, 0, 0.25), (0.9, 0.55, 0.5), "wood_red")
        mb.add(p_cyl(0.28, 0.28, 0.88, n=8), T(-0.44, 0, 0.5) @ R(0, 90, 0), color="wood_red")
        for x in (-0.35, 0.35):
            bx(mb, (x, 0, 0.45), (0.08, 0.6, 0.92), "gold")
        bx(mb, (0, -0.29, 0.48), (0.14, 0.04, 0.16), "gold")
    elif name == "bridge_wood":
        bx(mb, (0, 0, 0.3), (2.6, 6.0, 0.15), "wood")
        for k in range(12):
            bx(mb, (0, -2.75 + k * 0.5, 0.39), (2.6, 0.4, 0.04), "wood_light" if k % 2 else "wood")
        for x in (-1.25, 1.25):
            for y in (-2.9, -1, 1, 2.9):
                bx(mb, (x, y, 0.2), (0.15, 0.15, 1.6), "wood_dark")
            bx(mb, (x, 0, 1.0), (0.08, 6.0, 0.08), "wood_dark")
    elif name == "training_dummy":
        cyl(mb, (0, 0, 0), 0.06, 0.06, 1.4, "wood_dark", n=6)
        cyl(mb, (0, 0, 0.6), 0.22, 0.2, 0.7, "canvas")
        mb.add(p_sphere(0.16, 0.16, 0.16, 8, 5, center=(0, 0, 1.45)), color="canvas")
        bx(mb, (0, 0, 1.1), (1.0, 0.08, 0.08), "wood_dark")
        bx(mb, (0, -0.2, 0.95), (0.25, 0.02, 0.25), "cloth_red")
    elif name == "bench":
        bx(mb, (0, 0, 0.45), (1.6, 0.45, 0.07), "wood_light")
        for x in (-0.65, 0.65):
            bx(mb, (x, 0, 0.22), (0.1, 0.4, 0.45), "wood_dark")
    elif name == "signpost":
        cyl(mb, (0, 0, 0), 0.06, 0.05, 2.0, "wood_dark", n=6)
        for i, (z, r) in enumerate(((1.7, 20), (1.4, -35))):
            bx(mb, (0, 0, z), (0.8, 0.04, 0.2), "wood_light", rot=(0, 0, r))
    elif name == "market_stall" or name in ("bakery_stall", "gem_stall"):
        for x in (-1.0, 1.0):
            for y in (-0.6, 0.6):
                bx(mb, (x, y, 1.1), (0.1, 0.1, 2.2), "wood_dark")
        bx(mb, (0, -0.3, 0.9), (2.2, 0.8, 0.1), "wood_light")
        bx(mb, (0, -0.3, 0.45), (2.1, 0.7, 0.9), "wood")
        col = {"bakery_stall": "cloth_yellow", "gem_stall": "cloth_purple"}.get(name, "cloth_red")
        for k in range(5):  # striped awning
            mb.add(p_box(0.46, 1.6, 0.05), T(-0.92 + k * 0.46, 0, 2.3) @ R(-12, 0, 0), color=col if k % 2 else "cloth_white")
        goods = {"bakery_stall": ["bread", "bread", "wheat"], "gem_stall": ["gem_sapphire", "gem_jade", "gem_garnet",
                                                                            "gem_amber"]}.get(name, ["potato", "cabbage", "onion"])
        for k in range(8):
            mb.add(p_ico(0.08, center=(-0.85 + k * 0.24, -0.35 + 0.15 * (k % 2), 1.0), seed=k), color=goods[k % len(goods)])
    elif name == "campfire":
        for i in range(8):
            a = i * math.pi / 4
            mb.add(p_ico(0.12, center=(math.cos(a) * 0.45, math.sin(a) * 0.45, 0.06), seed=i), color="stone")
        for i in range(4):
            a = i * math.pi / 2 + 0.3
            mb.add(p_cyl(0.05, 0.05, 0.6, n=5), T(math.cos(a) * 0.25, math.sin(a) * 0.25, 0.0) @ R(0, 0, math.degrees(a))
                   @ R(0, -60, 0), color="wood_dark")
        mb.add(p_cyl(0.2, 0.0, 0.5, n=5, z0=0.05), mat="EV_Glow")
    elif name == "fire":
        for i in range(3):
            a = i * 2.1
            mb.add(p_cyl(0.05, 0.05, 0.6, n=5), T(math.cos(a) * 0.2, math.sin(a) * 0.2, 0.03) @ R(0, 0, math.degrees(a))
                   @ R(0, -75, 0), color="wood")
        mb.add(p_cyl(0.18, 0.0, 0.45, n=5, z0=0.05), mat="EV_Glow")
        mb.add(p_cyl(0.1, 0.0, 0.6, n=4, z0=0.05), T(0.05, 0.02, 0), mat="EV_Glow")
    elif name == "mushroom_ring":
        for i in range(10):
            a = i * math.pi / 5
            x, y = math.cos(a) * 1.4, math.sin(a) * 1.4
            cyl(mb, (x, y, 0), 0.04, 0.03, 0.18, "mushroom_stem", n=5)
            mb.add(p_sphere(0.12, 0.12, 0.07, 7, 3, center=(x, y, 0.18)), color="mushroom_red" if i % 2 else "flower_white")
    elif name == "statue_broken":
        bx(mb, (0, 0, 0.3), (1.0, 1.0, 0.6), "ruin_dark")
        mb.add(p_taper(0.5, 0.35, 0.45, 0.3, 1.1, z0=0.6), color="ruin")
        mb.add(p_ico(0.22, center=(0.4, -0.5, 0.15), seed=2), color="ruin")
        cyl(mb, (0.25, 0, 1.3), 0.08, 0.06, 0.5, "ruin", rot=(0, 30, 0), n=6)
    elif name == "ruin_pillar":
        cyl(mb, (0, 0, 0), 0.45, 0.45, 0.25, "ruin_dark")
        cyl(mb, (0, 0, 0.25), 0.3, 0.27, 3.2, "ruin", n=10)
        cyl(mb, (0, 0, 3.45), 0.4, 0.42, 0.25, "ruin_dark")
    elif name == "ruin_pillar_broken":
        cyl(mb, (0, 0, 0), 0.45, 0.45, 0.25, "ruin_dark")
        mb.add(p_taper(0.55, 0.55, 0.35, 0.3, 1.4, z0=0.25, shift=(0.05, 0)), color="ruin")
        cyl(mb, (0.8, 0.2, 0.3), 0.28, 0.28, 1.4, "ruin", rot=(0, 90, 20), n=10)
    elif name == "ruin_rubble":
        for i in range(6):
            mb.add(p_ico(rnd.uniform(0.15, 0.35), center=(rnd.uniform(-0.6, 0.6), rnd.uniform(-0.6, 0.6), 0.1), seed=i,
                         squash=(1.2, 1, 0.7)), color=rnd.choice(["ruin", "ruin_dark", "stone_moss"]))
    elif name == "gravestone":
        mb.add(p_box(0.5, 0.15, 0.7, center=(0, 0, 0.35)), color="stone")
        mb.add(p_cyl(0.25, 0.25, 0.14, n=8), T(0, 0.07, 0.7) @ R(90, 0, 0), color="stone")
        bx(mb, (0, 0.3, 0.03), (0.6, 0.9, 0.06), "dirt")
    elif name == "item_bag":
        mb.add(p_sphere(0.18, 0.18, 0.15, 8, 5, center=(0, 0, 0.14)), color="cloth_brown")
        cyl(mb, (0, 0, 0.26), 0.06, 0.08, 0.08, "cloth_brown", n=6)
        cyl(mb, (0, 0, 0.27), 0.065, 0.065, 0.02, "rope", n=6)
    # ---------------- agility
    elif name == "log_balance":
        mb.add(p_cyl(0.18, 0.18, 4.4, n=8), T(0, 0.2, 0.25) @ R(90, 0, 0), color="bark")
        for y in (0.2, -4.0):
            bx(mb, (0, y, 0.1), (0.6, 0.3, 0.2), "stone")
    elif name == "rope_swing":
        for x in (-1.2, 1.2):
            cyl(mb, (x, -2.0, 0), 0.1, 0.1, 4.0, "wood_dark", n=6)
        bx(mb, (0, -2.0, 4.0), (2.6, 0.2, 0.2), "wood_dark")
        cyl(mb, (0, -2.0, 1.2), 0.02, 0.02, 2.8, "rope", n=3)
        mb.add(p_sphere(0.06, 0.06, 0.1, 5, 3, center=(0, -2.0, 1.2)), color="rope")
    elif name == "wall_climb":
        bx(mb, (0, -2.0, 1.4), (1.6, 0.6, 2.8), "stone")
        for k in range(6):
            bx(mb, ((k % 2) * 0.5 - 0.25, -1.68, 0.3 + k * 0.42), (0.18, 0.08, 0.08), "stone_dark")
    elif name == "ledge_walk":
        bx(mb, (0, -2.0, 1.2), (0.3, 4.6, 2.4), "stone")
        bx(mb, (-0.25, -2.0, 1.0), (0.25, 4.6, 0.08), "stone_dark")
    else:
        raise ValueError(name)
    return mb


STATIONS = ["anvil", "furnace", "cooking_range", "prayer_altar", "well", "tanning_rack", "windmill", "fountain",
            "rune_altar"]
PROPS = ["fence", "lamp_post", "crate", "crate_stack", "barrel", "treasure_chest", "bridge_wood", "training_dummy", "bench",
         "signpost", "market_stall", "campfire", "fire", "mushroom_ring", "statue_broken", "ruin_pillar",
         "ruin_pillar_broken", "ruin_rubble", "gravestone", "item_bag"]
AGILITY = ["log_balance", "rope_swing", "wall_climb", "ledge_walk"]
STALLS = ["bakery_stall", "gem_stall"]
