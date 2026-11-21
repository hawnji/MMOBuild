"""
Nature: trees (+stumps for depleted nodes), ore rocks (+depleted), fishing spot, sand pit,
crops with growth stages, farm patch, foliage and decor. All static meshes, origin at the base,
1 Blender unit = 1 m = 1 world tile.
"""
import math
import random

from mathutils import Vector

from .core import MeshBuilder, p_box, p_taper, p_cyl, p_sphere, p_wedge, p_ico, p_plane, seg_matrix, T, R, S


def _trunk(mb, h, r0, r1, color, seed, bend=0.15, segs=3, n=7):
    rnd = random.Random(seed)
    pts = [Vector((0, 0, -0.1))]
    for i in range(1, segs + 1):
        pts.append(Vector((rnd.uniform(-bend, bend) * i / segs, rnd.uniform(-bend, bend) * i / segs, h * i / segs)))
    for i, (a, b) in enumerate(zip(pts, pts[1:])):
        ra = r0 + (r1 - r0) * i / segs
        rb = r0 + (r1 - r0) * (i + 1) / segs
        mb.add(p_cyl(ra, rb, (b - a).length, n=n), seg_matrix(a, b), color=color)
    # root flare
    for k in range(4):
        a = k * math.pi / 2 + rnd.uniform(-0.3, 0.3)
        d = Vector((math.cos(a), math.sin(a), -0.35)).normalized()
        mb.add(p_cyl(r0 * 0.5, 0.0, r0 * 1.8, n=4), seg_matrix(Vector((0, 0, r0 * 0.8)), Vector((0, 0, r0 * 0.8)) + d * r0 * 1.8),
               color=color)
    return pts[-1]


def _blob(mb, c, r, color, seed, squash=(1, 1, 0.8), jitter=0.15):
    mb.add(p_ico(r, center=tuple(c), jitter=jitter, seed=seed, squash=squash), color=color)


def tree(kind, seed=1, scale=1.0):
    mb = MeshBuilder("tree_" + kind)
    rnd = random.Random(seed)
    if kind == "oak":
        top = _trunk(mb, 2.0 * scale, 0.22 * scale, 0.14 * scale, "bark", seed)
        for i in range(3):
            a = i * 2.1 + 0.4
            d = Vector((math.cos(a), math.sin(a), 0.9)).normalized()
            mb.add(p_cyl(0.09 * scale, 0.05 * scale, 0.9 * scale, n=5), seg_matrix(top * 0.8, top * 0.8 + d * 0.9 * scale),
                   color="bark")
        for i, (c, r) in enumerate([((0, 0, 3.0), 1.25), ((0.8, 0.3, 2.6), 0.9), ((-0.7, -0.4, 2.7), 0.95),
                                    ((0.1, -0.8, 2.5), 0.8), ((-0.2, 0.7, 3.3), 0.85)]):
            _blob(mb, Vector(c) * scale, r * scale, ["leaf", "leaf_light", "leaf_dark"][i % 3], seed + i)
    elif kind == "pine":
        _trunk(mb, 1.2 * scale, 0.16 * scale, 0.1 * scale, "bark_dark", seed, bend=0.03)
        for i in range(4):
            z = (0.8 + i * 0.75) * scale
            r = (1.25 - i * 0.26) * scale
            mb.add(p_cyl(r, 0.0, 1.25 * scale, n=8, z0=z, phase=i * 0.4), color="pine" if i % 2 else "leaf_dark")
    elif kind == "willow":
        top = _trunk(mb, 2.0 * scale, 0.24 * scale, 0.15 * scale, "bark", seed, bend=0.25)
        _blob(mb, top + Vector((0, 0, 0.5 * scale)), 1.3 * scale, "willow", seed, squash=(1, 1, 0.6))
        for i in range(10):  # drooping fronds
            a = i * 2 * math.pi / 10
            base = top + Vector((math.cos(a) * 1.0 * scale, math.sin(a) * 1.0 * scale, 0.3 * scale))
            mb.add(p_taper(0.35 * scale, 0.12 * scale, 0.15 * scale, 0.06 * scale, 1.6 * scale, z0=-1.6 * scale),
                   T(*base) @ R(0, 0, math.degrees(a) + 90), color="willow" if i % 2 else "leaf_light")
    elif kind == "maple":
        top = _trunk(mb, 2.2 * scale, 0.2 * scale, 0.12 * scale, "bark", seed)
        for i, (c, r) in enumerate([((0, 0, 3.2), 1.1), ((0.7, 0.2, 2.8), 0.85), ((-0.6, -0.3, 2.9), 0.85),
                                    ((0, 0.5, 3.7), 0.7)]):
            _blob(mb, Vector(c) * scale, r * scale, ["maple", "flower_yellow", "maple", "wood_red"][i], seed + i)
    elif kind == "yew":
        top = _trunk(mb, 1.6 * scale, 0.32 * scale, 0.22 * scale, "bark_dark", seed, bend=0.1)
        for i in range(6):
            a = i * math.pi / 3
            _blob(mb, Vector((math.cos(a) * 0.9, math.sin(a) * 0.9, 2.3 + (i % 2) * 0.4)) * scale, 0.85 * scale,
                  "yew" if i % 2 else "leaf_dark", seed + i)
        _blob(mb, Vector((0, 0, 3.2)) * scale, 1.0 * scale, "yew", seed + 9)
    elif kind == "ghostwood":
        top = _trunk(mb, 2.6 * scale, 0.2 * scale, 0.1 * scale, "bark_pale", seed, bend=0.35)
        for i in range(5):
            a = i * 1.3
            d = Vector((math.cos(a), math.sin(a), 0.6)).normalized()
            end = top * 0.7 + d * 1.2 * scale
            mb.add(p_cyl(0.07 * scale, 0.02 * scale, 1.2 * scale, n=5), seg_matrix(top * 0.7, end), color="bark_pale")
            _blob(mb, end, 0.45 * scale, "ghost_leaf", seed + i, squash=(1, 1, 0.7))
        mb.add(p_ico(0.18 * scale, center=tuple(top + Vector((0, 0, 0.2 * scale)))), mat="EV_Glow")
    elif kind == "dead":
        top = _trunk(mb, 2.2 * scale, 0.18 * scale, 0.08 * scale, "bark_dark", seed, bend=0.3)
        for i in range(4):
            a = rnd.uniform(0, 6.28)
            d = Vector((math.cos(a), math.sin(a), rnd.uniform(0.2, 0.9))).normalized()
            base = top * rnd.uniform(0.5, 0.9)
            mb.add(p_cyl(0.06 * scale, 0.01 * scale, 0.9 * scale, n=4), seg_matrix(base, base + d * 0.9 * scale),
                   color="bark_dark")
    return mb


def stump(kind):
    mb = MeshBuilder("stump_" + kind)
    col = {"pine": "bark_dark", "yew": "bark_dark", "ghostwood": "bark_pale"}.get(kind, "bark")
    mb.add(p_cyl(0.3, 0.24, 0.35, n=8, z0=-0.05), color=col)
    mb.add(p_cyl(0.23, 0.23, 0.02, n=8, z0=0.3), color="wood_light")
    for k in range(4):
        a = k * math.pi / 2 + 0.3
        d = Vector((math.cos(a), math.sin(a), -0.4)).normalized()
        mb.add(p_cyl(0.1, 0.0, 0.45, n=4), seg_matrix(Vector((0, 0, 0.15)), Vector((0, 0, 0.15)) + d * 0.45), color=col)
    return mb


ORE_COLORS = {"copper": "ore_copper", "tin": "ore_tin", "iron": "ore_iron", "coal": "ore_coal", "silver": "ore_silver",
              "gold": "ore_gold", "mithril": "ore_mithril", "duskite": "ore_duskite", "starmetal": "ore_starmetal",
              "essence": "essence"}


def rock(ore=None, seed=3, depleted=False):
    name = ("rock_" + ore) if ore else "rock_depleted"
    mb = MeshBuilder(name)
    rnd = random.Random(seed)
    base = "stone_dark" if ore == "essence" else "stone"
    if depleted:
        for i in range(4):
            a = i * 1.7
            _blob(mb, (math.cos(a) * 0.35, math.sin(a) * 0.35, 0.12), 0.25, "stone" if i % 2 else "stone_dark", seed + i,
                  squash=(1, 1, 0.6))
        return mb
    _blob(mb, (0, 0, 0.45), 0.65, base, seed, squash=(1.1, 1, 0.8), jitter=0.2)
    _blob(mb, (0.45, 0.25, 0.25), 0.4, "stone_dark", seed + 1, squash=(1, 1, 0.8))
    _blob(mb, (-0.4, -0.2, 0.2), 0.35, base, seed + 2, squash=(1, 1, 0.8))
    col = ORE_COLORS[ore]
    for i in range(7 if ore != "essence" else 0):
        a = rnd.uniform(0, 6.28)
        el = rnd.uniform(0.2, 1.1)
        c = Vector((math.cos(a) * math.cos(el) * 0.62, math.sin(a) * math.cos(el) * 0.58, 0.45 + math.sin(el) * 0.5))
        mb.add(p_ico(rnd.uniform(0.09, 0.15), center=tuple(c), jitter=0.2, seed=seed + 10 + i), color=col)
    if ore == "essence":
        for i in range(5):
            a = i * 1.25
            d = Vector((math.cos(a) * 0.4, math.sin(a) * 0.4, 1)).normalized()
            base_p = Vector((math.cos(a) * 0.25, math.sin(a) * 0.25, 0.6))
            mb.add(p_cyl(0.1, 0.0, 0.6 + 0.15 * (i % 2), n=5), seg_matrix(base_p, base_p + d * (0.6 + 0.15 * (i % 2))),
                   mat="EV_Glow")
    if ore in ("mithril", "duskite", "starmetal"):
        mb.add(p_cyl(0.07, 0.0, 0.4, n=5), T(0.15, -0.2, 0.85) @ R(15, 10, 0), color=col)
    return mb


def fishing_spot():
    mb = MeshBuilder("fishing_spot")
    for i, r in enumerate((0.35, 0.6, 0.85)):
        mb.add(p_cyl(r, r, 0.015, n=14, z0=0.02 + 0.002 * i, cap0=False), color="water_light")
        mb.add(p_cyl(r - 0.06, r - 0.06, 0.016, n=14, z0=0.021 + 0.002 * i, cap0=False), color="water", flip=True)
    for i in range(3):
        a = i * 2.1
        mb.add(p_sphere(0.04, 0.04, 0.03, 5, 3, center=(math.cos(a) * 0.5, math.sin(a) * 0.5, 0.04)), color="white")
    return mb


def sand_pit():
    mb = MeshBuilder("sand_pit")
    mb.add(p_cyl(0.9, 0.75, 0.12, n=10, z0=-0.02), color="sand")
    for i in range(10):
        a = i * 2 * math.pi / 10
        mb.add(p_box(0.25, 0.12, 0.15, center=(math.cos(a) * 0.95, math.sin(a) * 0.95, 0.05)), R(0, 0, math.degrees(a)),
               color="stone" if i % 2 else "stone_light")
    mb.add(p_cyl(0.3, 0.0, 0.25, n=7, z0=0.08), T(0.2, 0.1, 0), color="sand")
    return mb


CROP_COLORS = {"potato": ("leaf", "potato"), "onion": ("leaf_light", "onion"), "cabbage": ("cabbage", "cabbage"),
               "wheat": ("grass_dry", "wheat"), "sunroot": ("leaf", "sunroot"), "mirewort": ("mirewort", "flower_purple"),
               "ashbloom": ("ash", "ashbloom")}


def crop(name, stage):
    """stage: 0 seedling, 1 growing, 2 mature (harvestable), 3 dead."""
    leaf, fruit = CROP_COLORS[name]
    mb = MeshBuilder("crop_%s_%d" % (name, stage))
    rnd = random.Random(sum(map(ord, name)))
    if stage == 3:
        leaf, fruit = "grass_dry", "burnt"
    h = [0.15, 0.35, 0.6, 0.3][stage]
    for i in range(2):
        for jdx in range(2):  # a 2x2 cluster per tile
            cx, cy = -0.22 + 0.44 * i, -0.22 + 0.44 * jdx
            if name == "wheat":
                for k in range(5):
                    a = k * 1.26
                    x, y = cx + math.cos(a) * 0.08, cy + math.sin(a) * 0.08
                    mb.add(p_cyl(0.012, 0.008, h, n=4), T(x, y, 0) @ R(rnd.uniform(-8, 8), rnd.uniform(-8, 8), 0),
                           color=leaf if stage < 2 else "wheat")
                    if stage >= 2:
                        mb.add(p_cyl(0.025, 0.0, 0.12, n=4, z0=h), T(x, y, 0), color=fruit)
            else:
                for k in range(4 if stage else 2):
                    a = k * math.pi / 2 + 0.4
                    mb.add(p_taper(0.06, 0.02, 0.1 * (1 + stage * 0.6), 0.02, h, z0=0), T(cx, cy, 0) @ R(0, 0, math.degrees(a))
                           @ R(25 if stage < 3 else 60, 0, 0), color=leaf)
                if stage == 2:
                    if name in ("cabbage",):
                        mb.add(p_ico(0.14, center=(cx, cy, 0.14), jitter=0.1, seed=i), color=fruit)
                    elif name in ("sunroot", "ashbloom", "mirewort"):
                        mb.add(p_cyl(0.015, 0.015, h * 1.2, n=4), T(cx, cy, 0), color="leaf_dark")
                        mb.add(p_sphere(0.09, 0.09, 0.05, 8, 3, center=(cx, cy, h * 1.2)), color=fruit)
                    else:
                        mb.add(p_ico(0.07, center=(cx + 0.06, cy, 0.04), jitter=0.2, seed=jdx), color=fruit)
    return mb


def farm_patch():
    mb = MeshBuilder("farm_patch")
    mb.add(p_box(3.8, 3.8, 0.12, center=(0, 0, 0.0)), color="soil")
    for i in range(4):  # furrows
        mb.add(p_box(3.6, 0.22, 0.06, center=(0, -1.35 + i * 0.9, 0.07)), color="dirt")
    for sx, sy, w, d in ((0, 2.0, 4.2, 0.2), (0, -2.0, 4.2, 0.2), (2.0, 0, 0.2, 4.2), (-2.0, 0, 0.2, 4.2)):
        mb.add(p_box(w, d, 0.2, center=(sx, sy, 0.05)), color="wood")
    return mb


def foliage(kind, seed=1):
    mb = MeshBuilder(kind)
    rnd = random.Random(seed)
    if kind == "bush":
        for i in range(4):
            a = i * 1.6
            _blob(mb, (math.cos(a) * 0.3, math.sin(a) * 0.3, 0.35), 0.4, ["leaf", "leaf_dark", "leaf_light"][i % 3], seed + i)
        for i in range(5):
            a = rnd.uniform(0, 6.28)
            mb.add(p_ico(0.05, center=(math.cos(a) * 0.5, math.sin(a) * 0.5, rnd.uniform(0.3, 0.6))), color="flower_red")
    elif kind == "flowers":
        for i in range(9):
            x, y = rnd.uniform(-0.4, 0.4), rnd.uniform(-0.4, 0.4)
            h = rnd.uniform(0.15, 0.3)
            mb.add(p_cyl(0.01, 0.01, h, n=3), T(x, y, 0), color="leaf")
            mb.add(p_sphere(0.05, 0.05, 0.025, 6, 3, center=(x, y, h)),
                   color=rnd.choice(["flower_red", "flower_yellow", "flower_blue", "flower_white", "flower_purple"]))
    elif kind == "reeds":
        for i in range(12):
            x, y = rnd.uniform(-0.35, 0.35), rnd.uniform(-0.35, 0.35)
            h = rnd.uniform(0.6, 1.2)
            mb.add(p_cyl(0.02, 0.005, h, n=3), T(x, y, 0) @ R(rnd.uniform(-10, 10), rnd.uniform(-10, 10), 0), color="reed")
            if i % 3 == 0:
                mb.add(p_cyl(0.035, 0.035, 0.15, n=5, z0=h * 0.8), T(x, y, 0), color="wood_dark")
    elif kind == "mushroom_cluster":
        for i in range(5):
            x, y = rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3)
            h = rnd.uniform(0.1, 0.3)
            mb.add(p_cyl(0.03, 0.025, h, n=5), T(x, y, 0), color="mushroom_stem")
            mb.add(p_sphere(0.1 * (h / 0.2), 0.1 * (h / 0.2), 0.06, 7, 3, center=(x, y, h)),
                   color="mushroom_red" if i % 2 else "mushroom_cap")
    elif kind == "rock_small":
        _blob(mb, (0, 0, 0.12), 0.25, "stone", seed, squash=(1.2, 1, 0.6))
        _blob(mb, (0.25, 0.1, 0.06), 0.13, "stone_dark", seed + 1)
    elif kind == "rock_large":
        _blob(mb, (0, 0, 0.7), 1.0, "stone", seed, squash=(1.3, 1, 0.8), jitter=0.22)
        _blob(mb, (0.8, 0.4, 0.3), 0.5, "stone_moss", seed + 1)
        _blob(mb, (-0.6, -0.5, 0.25), 0.45, "stone_dark", seed + 2)
    elif kind == "log_fallen":
        mb.add(p_cyl(0.22, 0.2, 2.2, n=7), T(-1.1, 0, 0.2) @ R(0, 90, 0), color="bark")
        mb.add(p_cyl(0.19, 0.19, 0.02, n=7), T(1.1, 0, 0.2) @ R(0, 90, 0), color="wood_light")
        _blob(mb, (0.3, 0.1, 0.4), 0.15, "moss", seed)
    elif kind == "grass_tuft":
        for i in range(8):
            a = i * 0.8
            mb.add(p_taper(0.05, 0.01, 0.0, 0.0, rnd.uniform(0.15, 0.3)), T(math.cos(a) * 0.1, math.sin(a) * 0.1, 0) @
                   R(rnd.uniform(-20, 20), rnd.uniform(-20, 20), 0), color=rnd.choice(["grass", "leaf_light"]))
    return mb


TREES = ["oak", "pine", "willow", "maple", "yew", "ghostwood", "dead"]
ORES = ["copper", "tin", "iron", "coal", "silver", "gold", "mithril", "duskite", "starmetal", "essence"]
CROPS = list(CROP_COLORS.keys())
FOLIAGE = ["bush", "flowers", "reeds", "mushroom_cluster", "rock_small", "rock_large", "log_fallen", "grass_tuft"]
