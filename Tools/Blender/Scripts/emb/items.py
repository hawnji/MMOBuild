"""
Inventory / ground item models (non-equipment). Tinted parts use EV_Metal, EV_Wood or EV_Glow so a
single model serves every material tier; the item's "tint" key selects the colour (core.TINTS).
"""
import math
import random

from mathutils import Vector

from .core import MeshBuilder, p_box, p_taper, p_cyl, p_sphere, p_wedge, p_ico, seg_matrix, T, R, S

TINT_MATERIAL = {  # which material slot the item's tint applies to
    "rune_stone": "EV_Glow", "gem": "EV_Metal", "gem_uncut": "EV_Metal", "arrowheads": "EV_Metal",
    "metal_bar": "EV_Metal", "ore_chunk": "EV_Metal", "potion": "EV_Glow", "logs": "EV_Wood", "fish": "EV_Metal",
    "fish_cooked": "EV_Metal", "amulet": "EV_Metal", "ring": "EV_Metal",
}


def build(model):
    mb = MeshBuilder(model)
    rnd = random.Random(sum(map(ord, model)))
    if model == "rune_stone":
        mb.add(p_ico(0.12, jitter=0.12, seed=3, squash=(1, 0.6, 1.1)), color="stone_light")
        mb.add(p_box(0.1, 0.02, 0.1, center=(0, -0.075, 0)), R(0, 45, 0), mat="EV_Glow")
    elif model in ("gem", "gem_uncut"):
        if model == "gem":
            mb.add(p_cyl(0.1, 0.06, 0.05, n=8), mat="EV_Metal")
            mb.add(p_cyl(0.1, 0.0, 0.12, n=8), R(180, 0, 0), mat="EV_Metal")
        else:
            mb.add(p_ico(0.1, jitter=0.25, seed=5), color="stone")
            for i in range(3):
                mb.add(p_cyl(0.035, 0.0, 0.09, n=5), T(0, 0, 0.05) @ R(20 * (i - 1), 25 * (i - 1), 0), mat="EV_Metal")
    elif model == "arrowheads":
        for i in range(4):
            mb.add(p_cyl(0.025, 0.0, 0.07, n=4), T(-0.06 + i * 0.04, (i % 2) * 0.03, 0) @ R(0, 70 + i * 8, i * 20),
                   mat="EV_Metal")
    elif model == "metal_bar":
        mb.add(p_taper(0.26, 0.12, 0.2, 0.08, 0.07), mat="EV_Metal")
    elif model == "ore_chunk":
        mb.add(p_ico(0.12, jitter=0.2, seed=2, squash=(1.2, 1, 0.8)), color="stone")
        for i in range(4):
            a = i * 1.6
            mb.add(p_ico(0.045, center=(math.cos(a) * 0.1, math.sin(a) * 0.08, 0.04), seed=i), mat="EV_Metal")
    elif model == "potion":
        mb.add(p_sphere(0.09, 0.09, 0.09, 8, 6, center=(0, 0, 0.09)), mat="EV_Glow")
        mb.add(p_cyl(0.07, 0.07, 0.012, n=8, z0=0.14), color="glass")
        mb.add(p_cyl(0.03, 0.03, 0.08, n=6, z0=0.16), color="glass")
        mb.add(p_cyl(0.035, 0.03, 0.04, n=6, z0=0.23), color="wood_light")
    elif model == "logs":
        for i, (x, z) in enumerate(((-0.08, 0.07), (0.08, 0.07), (0, 0.2))):
            mb.add(p_cyl(0.075, 0.075, 0.5, n=7), T(-0.25, x, z) @ R(0, 90, 0), mat="EV_Wood")
            mb.add(p_cyl(0.065, 0.065, 0.01, n=7), T(0.251, x, z) @ R(0, 90, 0), color="wood_light")
    elif model in ("fish", "fish_cooked", "fish_burnt"):
        mat = {"fish": "EV_Metal", "fish_cooked": "EV_Metal"}.get(model)
        col = "burnt" if model == "fish_burnt" else None
        body = p_sphere(0.24, 0.06, 0.09, 8, 5)
        if mat:
            mb.add(body, mat=mat)
        else:
            mb.add(body, color=col)
        mb.add(p_wedge(0.12, 0.02, 0.14), T(0.27, 0, -0.0) @ R(0, -90, 0) @ T(0, 0, -0.07), color=col or "fish")
        mb.add(p_wedge(0.08, 0.015, 0.06), T(0, 0, 0.07), color=col or "fish")
        if model == "fish":
            for sg in (1, -1):
                mb.add(p_box(0.02, 0.01, 0.02, center=(-0.17, sg * 0.05, 0.02)), color="black")
        elif model == "fish_cooked":
            for k in range(3):
                mb.add(p_box(0.015, 0.125, 0.06, center=(-0.06 + k * 0.07, 0, 0.0)), color="meat_cooked")
    elif model in ("meat", "meat_cooked", "meat_burnt"):
        col = {"meat": "meat", "meat_cooked": "meat_cooked", "meat_burnt": "burnt"}[model]
        mb.add(p_sphere(0.15, 0.11, 0.07, 8, 5, center=(0.03, 0, 0)), color=col)
        mb.add(p_cyl(0.02, 0.02, 0.16, n=5), T(-0.1, 0, 0) @ R(0, -90, 0), color="bone")
        mb.add(p_sphere(0.035, 0.035, 0.035, 5, 3, center=(-0.27, 0, 0)), color="bone")
    elif model == "bread":
        mb.add(p_sphere(0.17, 0.1, 0.08, 8, 5, center=(0, 0, 0.05)), color="bread")
        for k in range(3):
            mb.add(p_box(0.02, 0.12, 0.01, center=(-0.07 + k * 0.07, 0, 0.12)), R(0, 0, 20), color="wheat")
    elif model == "stew":
        mb.add(p_cyl(0.14, 0.17, 0.1, n=10), color="wood")
        mb.add(p_cyl(0.15, 0.15, 0.01, n=10, z0=0.09), color="meat_cooked")
        for i in range(4):
            a = i * 1.5
            mb.add(p_ico(0.025, center=(math.cos(a) * 0.08, math.sin(a) * 0.08, 0.105)), color=["potato", "cabbage",
                                                                                              "sunroot", "meat"][i])
        mb.add(p_box(0.02, 0.24, 0.015, center=(0.05, 0.05, 0.12)), R(0, -15, 30), color="wood_light")
    elif model == "seed_pouch":
        mb.add(p_sphere(0.1, 0.1, 0.09, 8, 5, center=(0, 0, 0.08)), color="canvas")
        mb.add(p_cyl(0.04, 0.06, 0.06, n=6, z0=0.15), color="canvas")
        mb.add(p_cyl(0.042, 0.042, 0.015, n=6, z0=0.16), color="rope")
        for i in range(3):
            mb.add(p_ico(0.015, center=(0.1 + i * 0.03, -0.05, 0.01)), color="wood_light")
    elif model.startswith("crop_"):
        c = model[5:]
        if c == "wheat":
            for i in range(5):
                mb.add(p_cyl(0.008, 0.008, 0.35, n=3), T(0, 0, 0) @ R(0, 50 + i * 4, i * 6 - 12), color="wheat")
                mb.add(p_cyl(0.02, 0.0, 0.09, n=4), R(0, 50 + i * 4, i * 6 - 12) @ T(0, 0, 0.35), color="wheat")
        elif c == "cabbage":
            mb.add(p_ico(0.13, jitter=0.12, seed=1), color="cabbage")
            mb.add(p_ico(0.09, center=(0, 0, 0.05), seed=2), color="leaf_light")
        elif c in ("potato", "onion"):
            for i in range(2):
                mb.add(p_sphere(0.08, 0.07, 0.06 if c == "potato" else 0.08, 7, 5, center=(i * 0.12 - 0.06, i * 0.02, 0)),
                       color=c)
            if c == "onion":
                mb.add(p_cyl(0.02, 0.0, 0.08, n=4, z0=0.07), T(-0.06, 0, 0), color="leaf_light")
        else:
            col = {"sunroot": "sunroot", "mirewort": "flower_purple", "ashbloom": "ashbloom"}[c]
            mb.add(p_cyl(0.012, 0.012, 0.3, n=4), R(0, 60, 0), color="leaf")
            mb.add(p_sphere(0.08, 0.08, 0.04, 8, 3), R(0, 60, 0) @ T(0, 0, 0.3), color=col)
            mb.add(p_taper(0.08, 0.02, 0.0, 0.0, 0.1), R(0, 60, 0) @ T(0, 0, 0.12) @ R(50, 0, 0), color="leaf")
    elif model == "amulet":
        for i in range(12):
            a = i * 2 * math.pi / 12
            mb.add(p_sphere(0.012, 0.012, 0.012, 4, 3, center=(math.cos(a) * 0.1, math.sin(a) * 0.1 + 0.05, 0)),
                   color="gold")
        mb.add(p_cyl(0.045, 0.045, 0.02, n=8), T(0, -0.07, 0) @ R(90, 0, 0), color="gold")
        mb.add(p_cyl(0.03, 0.0, 0.025, n=6), T(0, -0.085, 0) @ R(90, 0, 0), mat="EV_Metal")
    elif model == "ring":
        mb.add(p_cyl(0.07, 0.07, 0.03, n=12, cap0=False, cap1=False), color="gold")
        mb.add(p_cyl(0.06, 0.06, 0.03, n=12, cap0=False, cap1=False), color="gold", flip=True)
        mb.add(p_ico(0.03, center=(0, 0.075, 0.015)), mat="EV_Metal")
    # ---- items without a model in the data (id used as the model name)
    elif model in ("bones", "big_bones"):
        k = 1.4 if model == "big_bones" else 1.0
        for i in range(2):
            mb.add(p_cyl(0.02 * k, 0.02 * k, 0.24 * k, n=5), T(-0.12 * k, i * 0.06, 0) @ R(0, 90, i * 25), color="bone")
            for e in (-0.13, 0.13):
                mb.add(p_sphere(0.035 * k, 0.035 * k, 0.03 * k, 5, 3,
                                center=(e * k * math.cos(math.radians(i * 25)), i * 0.06 + e * k * math.sin(math.radians(i * 25)), 0)),
                       color="bone")
    elif model == "ashes":
        mb.add(p_cyl(0.15, 0.0, 0.08, n=8), color="ash")
        mb.add(p_ico(0.03, center=(0.05, 0.02, 0.06)), color="ember")
    elif model == "coins":
        for i in range(6):
            mb.add(p_cyl(0.06, 0.06, 0.015, n=10, z0=i * 0.016), T(rnd.uniform(-0.01, 0.01), rnd.uniform(-0.01, 0.01), 0),
                   color="coin")
        mb.add(p_cyl(0.06, 0.06, 0.015, n=10), T(0.12, 0.04, 0), color="coin")
    elif model in ("bucket", "bucket_water"):
        mb.add(p_cyl(0.1, 0.13, 0.2, n=10, cap1=False), color="wood")
        mb.add(p_cyl(0.095, 0.125, 0.2, n=10, cap0=False, cap1=False), color="wood_dark", flip=True)
        for z in (0.03, 0.17):
            mb.add(p_cyl(0.105 + z * 0.12, 0.105 + z * 0.12, 0.02, n=10, z0=z), color="iron_dark")
        if model == "bucket_water":
            mb.add(p_cyl(0.12, 0.12, 0.01, n=10, z0=0.17), color="water")
    elif model in ("vial", "vial_water"):
        mb.add(p_cyl(0.035, 0.035, 0.16, n=6), color="glass")
        mb.add(p_cyl(0.04, 0.04, 0.02, n=6, z0=0.16), color="glass")
        if model == "vial_water":
            mb.add(p_cyl(0.037, 0.037, 0.09, n=6), color="water_light")
    elif model in ("hammer", "chisel", "knife", "needle", "lockpick", "spade", "rake", "tinderbox", "watering_can"):
        if model == "hammer":
            mb.add(p_cyl(0.015, 0.015, 0.3, n=5), color="wood")
            mb.add(p_box(0.16, 0.05, 0.05, center=(0, 0, 0.3)), color="iron")
        elif model == "chisel":
            mb.add(p_cyl(0.015, 0.015, 0.1, n=5), color="wood")
            mb.add(p_taper(0.02, 0.02, 0.03, 0.004, 0.12, z0=0.1), color="steel")
        elif model == "knife":
            mb.add(p_cyl(0.015, 0.015, 0.1, n=5), color="wood_dark")
            mb.add(p_taper(0.004, 0.035, 0.002, 0.005, 0.14, z0=0.1), color="steel")
        elif model == "needle":
            mb.add(p_cyl(0.006, 0.0, 0.14, n=4), color="steel")
            mb.add(p_cyl(0.002, 0.002, 0.2, n=3), T(0, 0, 0.12) @ R(0, 60, 0), color="cloth_white")
        elif model == "lockpick":
            mb.add(p_cyl(0.004, 0.004, 0.14, n=3), color="steel")
            mb.add(p_box(0.03, 0.004, 0.008, center=(0.012, 0, 0.14)), color="steel")
            mb.add(p_cyl(0.02, 0.02, 0.01, n=8), T(0, 0, -0.01) @ R(90, 0, 0), color="brass")
        elif model == "spade":
            mb.add(p_cyl(0.015, 0.015, 0.5, n=5), color="wood")
            mb.add(p_taper(0.14, 0.02, 0.1, 0.015, 0.15, z0=-0.15), color="iron")
        elif model == "rake":
            mb.add(p_cyl(0.015, 0.015, 0.55, n=5), color="wood")
            mb.add(p_box(0.25, 0.03, 0.03, center=(0, 0, 0.55)), color="iron")
            for i in range(5):
                mb.add(p_cyl(0.006, 0.0, 0.06, n=3), T(-0.1 + i * 0.05, -0.0, 0.55) @ R(-90, 0, 0), color="iron")
        elif model == "tinderbox":
            mb.add(p_box(0.16, 0.1, 0.06, center=(0, 0, 0.03)), color="wood_red")
            mb.add(p_box(0.17, 0.11, 0.015, center=(0, 0, 0.065)), color="brass")
        elif model == "watering_can":
            mb.add(p_cyl(0.09, 0.09, 0.16, n=10), color="copper")
            mb.add(p_cyl(0.015, 0.025, 0.18, n=5), T(0.08, 0, 0.04) @ R(0, 55, 0), color="copper")
            mb.add(p_box(0.12, 0.015, 0.015, center=(-0.02, 0, 0.21)), color="copper")
    elif model in ("cowhide", "wolf_pelt", "leather"):
        col = {"cowhide": "cloth_white", "wolf_pelt": "fur_grey", "leather": "leather"}[model]
        v = []
        f = []
        n = 10
        for i in range(n):
            a = i * 2 * math.pi / n
            r = 0.17 + 0.04 * math.sin(a * 3)
            v.append((math.cos(a) * r * 1.2, math.sin(a) * r, 0.0))
        v.append((0, 0, 0.012))
        for i in range(n):
            f.append((i, (i + 1) % n, n))
        mb.add((v, f), color=col)
        if model == "cowhide":
            for i in range(3):
                mb.add(p_sphere(0.04, 0.035, 0.005, 6, 3, center=(-0.08 + i * 0.08, (i % 2) * 0.05, 0.01)), color="black")
    elif model == "feather":
        mb.add(p_cyl(0.004, 0.002, 0.24, n=3), R(0, 70, 0), color="bone")
        mb.add(p_taper(0.002, 0.06, 0.002, 0.02, 0.18), R(0, 70, 0) @ T(0, 0, 0.05), color="flower_white")
    elif model in ("flour", "sand", "soda_ash"):
        col = {"flour": "cloth_white", "sand": "sand", "soda_ash": "ash"}[model]
        mb.add(p_cyl(0.13, 0.1, 0.14, n=10), color="clay" if model != "flour" else "canvas")
        mb.add(p_cyl(0.12, 0.0, 0.06, n=8, z0=0.14), color=col)
    elif model == "bread_dough":
        mb.add(p_sphere(0.14, 0.11, 0.07, 8, 5, center=(0, 0, 0.05)), color="cloth_white")
    elif model == "bait_worms":
        mb.add(p_cyl(0.09, 0.1, 0.06, n=8), color="clay")
        for i in range(3):
            mb.add(p_cyl(0.012, 0.012, 0.08, n=4), T(-0.03 + i * 0.03, 0, 0.06) @ R(0, 30 * (i - 1), 0), color="rat_pink")
    elif model in ("blank_essence", "spirit_shard"):
        mb.add(p_ico(0.1, jitter=0.15, seed=7, squash=(0.8, 0.8, 1.2)), color="essence" if model == "blank_essence" else "spirit")
        if model == "spirit_shard":
            mb.add(p_ico(0.04, center=(0, -0.06, 0.03)), mat="EV_Glow")
    elif model in ("amulet_mould", "ring_mould"):
        mb.add(p_box(0.2, 0.14, 0.05, center=(0, 0, 0.025)), color="clay")
        if model == "ring_mould":
            mb.add(p_cyl(0.04, 0.04, 0.01, n=10, z0=0.045), color="soil")
        else:
            mb.add(p_cyl(0.035, 0.0, 0.01, n=6, z0=0.045), color="soil")
    elif model in ("arrow_shaft", "headless_arrow"):
        for i in range(3):
            mb.add(p_cyl(0.006, 0.006, 0.3, n=4), T(-0.15, -0.02 + i * 0.02, 0) @ R(0, 90, 0), color="wood_light")
            if model == "headless_arrow":
                mb.add(p_box(0.06, 0.001, 0.02, center=(-0.12, -0.02 + i * 0.02, 0.01)), color="flower_white")
    elif model == "thread":
        mb.add(p_cyl(0.05, 0.05, 0.1, n=8), color="cloth_white")
        mb.add(p_cyl(0.06, 0.06, 0.015, n=8), color="wood_light")
        mb.add(p_cyl(0.06, 0.06, 0.015, n=8, z0=0.1), color="wood_light")
    elif model == "goblin_charm":
        mb.add(p_cyl(0.06, 0.06, 0.015, n=6), R(90, 0, 0), color="bone")
        mb.add(p_ico(0.025, center=(0, -0.01, 0)), color="skin_goblin")
    elif model == "old_map_scrap":
        mb.add(p_box(0.25, 0.18, 0.005), R(0, 0, 8), color="paper")
        mb.add(p_box(0.04, 0.04, 0.006, center=(0.05, 0.02, 0.001)), color="blood")
    elif model == "ruin_relic":
        mb.add(p_cyl(0.05, 0.08, 0.16, n=6), color="ruin")
        mb.add(p_ico(0.04, center=(0, 0, 0.19)), mat="EV_Glow")
    else:
        raise ValueError(model)
    return mb


MISC_MODELS = ["rune_stone", "gem", "gem_uncut", "arrowheads", "metal_bar", "ore_chunk", "potion", "logs", "fish",
               "fish_cooked", "fish_burnt", "meat", "meat_cooked", "meat_burnt", "bread", "stew", "seed_pouch",
               "crop_potato", "crop_onion", "crop_cabbage", "crop_wheat", "crop_sunroot", "crop_mirewort", "crop_ashbloom",
               "amulet", "ring"]
