"""
Embervale Blender asset pipeline - core utilities.

Everything here runs headless (``blender -b -P script.py`` or the ``bpy`` Python module).
Geometry is assembled with a small MeshBuilder rather than bpy.ops so results are
deterministic and fast; the final mesh carries:
  * flat shading (classic low-poly look)
  * one UV map (atlas cells for palette materials, box-projected tiling for tintable ones)
  * material slots named EV_* (remapped to URP materials by the Unity importer)
  * vertex groups named after skeleton bones (rigid / blended skinning)
"""
import math
import os
import json

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector, Quaternion, Euler

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
ART = os.path.join(REPO, "Assets", "Art")
TEX_DIR = os.path.join(ART, "Textures", "Generated")
SOURCE_DIR = os.path.join(REPO, "Tools", "Blender", "SourceFiles")
DOC_DIR = os.path.join(REPO, "Tools", "Blender", "AssetDocumentation")

# ---------------------------------------------------------------------------
# Palette: every atlas cell is a named colour. 16x16 cells of 32px in a 512 atlas.
# ---------------------------------------------------------------------------
PALETTE = {
    # woods
    "wood_light": (0.62, 0.45, 0.28), "wood": (0.47, 0.32, 0.19), "wood_dark": (0.30, 0.20, 0.12),
    "wood_red": (0.50, 0.25, 0.16), "wood_grey": (0.45, 0.42, 0.38), "bark": (0.33, 0.25, 0.18),
    "bark_pale": (0.60, 0.56, 0.48), "bark_dark": (0.20, 0.16, 0.13), "thatch": (0.72, 0.60, 0.33),
    # stone / earth
    "stone_light": (0.70, 0.68, 0.64), "stone": (0.52, 0.51, 0.49), "stone_dark": (0.33, 0.33, 0.34),
    "stone_warm": (0.62, 0.55, 0.47), "stone_moss": (0.40, 0.46, 0.33), "slate": (0.30, 0.33, 0.40),
    "roof_red": (0.58, 0.22, 0.16), "roof_blue": (0.25, 0.32, 0.50), "roof_slate": (0.27, 0.29, 0.33),
    "dirt": (0.42, 0.32, 0.22), "soil": (0.28, 0.20, 0.13), "sand": (0.82, 0.74, 0.52), "clay": (0.62, 0.40, 0.30),
    "ruin": (0.55, 0.53, 0.46), "ruin_dark": (0.36, 0.35, 0.31), "marble": (0.86, 0.85, 0.82),
    # foliage
    "leaf": (0.30, 0.52, 0.20), "leaf_light": (0.45, 0.64, 0.25), "leaf_dark": (0.17, 0.35, 0.15),
    "pine": (0.16, 0.36, 0.22), "willow": (0.44, 0.58, 0.28), "maple": (0.72, 0.34, 0.14), "yew": (0.12, 0.28, 0.18),
    "ghost_leaf": (0.65, 0.78, 0.80), "grass": (0.38, 0.58, 0.22), "grass_dry": (0.62, 0.60, 0.30),
    "moss": (0.33, 0.45, 0.20), "reed": (0.55, 0.55, 0.30), "marsh": (0.30, 0.35, 0.22),
    # flowers / crops
    "flower_red": (0.80, 0.20, 0.20), "flower_yellow": (0.95, 0.80, 0.20), "flower_blue": (0.35, 0.45, 0.85),
    "flower_white": (0.92, 0.92, 0.88), "flower_purple": (0.60, 0.35, 0.75), "potato": (0.70, 0.55, 0.33),
    "onion": (0.85, 0.70, 0.45), "cabbage": (0.55, 0.75, 0.40), "wheat": (0.88, 0.75, 0.38),
    "sunroot": (0.95, 0.65, 0.15), "mirewort": (0.35, 0.55, 0.45), "ashbloom": (0.75, 0.70, 0.75),
    "mushroom_red": (0.75, 0.15, 0.12), "mushroom_cap": (0.65, 0.50, 0.35), "mushroom_stem": (0.90, 0.86, 0.75),
    # ores / minerals
    "ore_copper": (0.80, 0.45, 0.22), "ore_tin": (0.72, 0.74, 0.74), "ore_iron": (0.55, 0.32, 0.25),
    "ore_coal": (0.10, 0.10, 0.11), "ore_silver": (0.85, 0.87, 0.92), "ore_gold": (0.95, 0.76, 0.20),
    "ore_mithril": (0.35, 0.50, 0.85), "ore_duskite": (0.45, 0.25, 0.60), "ore_starmetal": (0.85, 0.95, 1.00),
    "essence": (0.70, 0.80, 0.95), "gem_amber": (0.95, 0.60, 0.10), "gem_jade": (0.30, 0.75, 0.45),
    "gem_garnet": (0.65, 0.10, 0.20), "gem_sapphire": (0.15, 0.30, 0.85), "gem_starstone": (0.95, 0.95, 1.0),
    # metals (atlas versions for props)
    "iron": (0.40, 0.40, 0.42), "iron_dark": (0.22, 0.22, 0.24), "steel": (0.62, 0.64, 0.68), "brass": (0.75, 0.60, 0.30),
    "gold": (0.90, 0.72, 0.25), "copper": (0.72, 0.42, 0.25), "bronze": (0.70, 0.50, 0.28),
    # cloth / leather
    "leather": (0.45, 0.30, 0.18), "leather_dark": (0.26, 0.17, 0.10), "rope": (0.68, 0.58, 0.40),
    "cloth_red": (0.62, 0.18, 0.15), "cloth_blue": (0.20, 0.30, 0.58), "cloth_green": (0.24, 0.45, 0.24),
    "cloth_white": (0.88, 0.86, 0.80), "cloth_brown": (0.45, 0.34, 0.24), "cloth_purple": (0.40, 0.22, 0.50),
    "cloth_black": (0.12, 0.12, 0.14), "cloth_yellow": (0.85, 0.70, 0.25), "canvas": (0.80, 0.75, 0.62),
    "fur_grey": (0.48, 0.47, 0.45), "fur_dark": (0.25, 0.23, 0.22), "fur_brown": (0.45, 0.35, 0.25),
    # creatures
    "skin_goblin": (0.42, 0.58, 0.25), "skin_orc": (0.36, 0.48, 0.30), "skin_troll": (0.45, 0.50, 0.42),
    "skin_zombie": (0.50, 0.55, 0.42), "bone": (0.88, 0.85, 0.75), "bone_dark": (0.60, 0.56, 0.48),
    "rat": (0.40, 0.33, 0.28), "rat_pink": (0.85, 0.60, 0.58), "spider": (0.15, 0.13, 0.14), "spider_mark": (0.75, 0.15, 0.10),
    "spirit": (0.55, 0.85, 0.65), "spirit_core": (0.85, 1.00, 0.80), "toad": (0.35, 0.42, 0.25), "toad_belly": (0.75, 0.72, 0.50),
    # misc
    "black": (0.05, 0.05, 0.06), "white": (0.95, 0.95, 0.95), "eye": (0.10, 0.08, 0.07), "eye_white": (0.95, 0.95, 0.92),
    "teeth": (0.92, 0.90, 0.80), "lips": (0.65, 0.35, 0.32), "fire": (1.00, 0.55, 0.10), "fire_core": (1.00, 0.90, 0.40),
    "ember": (0.85, 0.25, 0.05), "water": (0.25, 0.50, 0.75), "water_light": (0.55, 0.75, 0.90), "glass": (0.70, 0.85, 0.90),
    "potion_red": (0.85, 0.15, 0.15), "potion_green": (0.25, 0.75, 0.30), "potion_blue": (0.25, 0.45, 0.90),
    "potion_purple": (0.60, 0.25, 0.75), "potion_yellow": (0.90, 0.80, 0.20), "rune_glow": (0.55, 0.80, 1.00),
    "paper": (0.90, 0.85, 0.70), "bread": (0.80, 0.58, 0.30), "meat": (0.70, 0.30, 0.25), "meat_cooked": (0.50, 0.30, 0.18),
    "burnt": (0.26, 0.19, 0.14), "fish": (0.60, 0.65, 0.68), "fish_belly": (0.88, 0.86, 0.80), "ash": (0.55, 0.55, 0.55),
    "coin": (0.85, 0.55, 0.25), "lamp_glow": (1.0, 0.85, 0.50), "blood": (0.45, 0.05, 0.05), "ghost": (0.60, 0.85, 1.00),
}
PALETTE_NAMES = list(PALETTE.keys())
CELLS = 16
CELL_PX = 32
ATLAS_PX = CELLS * CELL_PX
assert len(PALETTE_NAMES) <= CELLS * CELLS, "palette too large"

# Tintable materials use a near-white detail texture; Unity multiplies by a per-instance colour.
MATERIALS = {
    "EV_Atlas": {"texture": "ev_atlas.png", "tint": (1, 1, 1)},
    "EV_Skin": {"texture": "ev_skin.png", "tint": (0.87, 0.68, 0.52)},
    "EV_Hair": {"texture": "ev_hair.png", "tint": (0.35, 0.22, 0.12)},
    "EV_Cloth": {"texture": "ev_cloth.png", "tint": (0.45, 0.30, 0.20)},
    "EV_Cloth2": {"texture": "ev_cloth.png", "tint": (0.30, 0.28, 0.25)},
    "EV_Leather": {"texture": "ev_leather.png", "tint": (0.45, 0.30, 0.18)},
    "EV_Metal": {"texture": "ev_metal.png", "tint": (0.70, 0.50, 0.28)},
    "EV_Wood": {"texture": "ev_wood.png", "tint": (0.60, 0.43, 0.25)},
    "EV_Glow": {"texture": "ev_glow.png", "tint": (0.55, 0.85, 0.65), "emission": 2.0},
}

# Tier/tint colours shared with Unity (EquipmentTints.json) so equipment can be recoloured.
TINTS = {
    "bronze": (0.70, 0.48, 0.25), "iron": (0.45, 0.45, 0.48), "steel": (0.72, 0.74, 0.78), "mithril": (0.40, 0.55, 0.90),
    "duskite": (0.42, 0.25, 0.55), "starmetal": (0.90, 0.95, 1.00), "silver": (0.85, 0.87, 0.92), "gold": (0.92, 0.75, 0.25),
    "oak": (0.60, 0.43, 0.25), "pine": (0.55, 0.40, 0.25), "willow": (0.68, 0.58, 0.38), "maple": (0.70, 0.38, 0.20),
    "yew": (0.45, 0.25, 0.15), "ghostwood": (0.70, 0.80, 0.82), "blue": (0.25, 0.35, 0.70), "fire": (0.80, 0.30, 0.10),
    "amber": (0.95, 0.60, 0.10), "jade": (0.30, 0.75, 0.45), "garnet": (0.65, 0.10, 0.20), "sapphire": (0.15, 0.30, 0.85),
    "starstone": (0.95, 0.95, 1.0), "copper": (0.80, 0.45, 0.22), "tin": (0.72, 0.74, 0.74), "coal": (0.15, 0.15, 0.16),
    # runes
    "air": (0.85, 0.92, 1.0), "water": (0.25, 0.5, 0.95), "earth": (0.55, 0.4, 0.2), "mind": (0.95, 0.6, 0.2),
    "life": (0.35, 0.9, 0.4), "chaos": (0.85, 0.2, 0.6), "void": (0.35, 0.15, 0.55), "ward": (0.95, 0.85, 0.35),
    "path": (0.3, 0.85, 0.85),
    # fish
    "minnow": (0.70, 0.72, 0.70), "perch": (0.60, 0.65, 0.35), "trout": (0.65, 0.55, 0.50), "pike": (0.45, 0.55, 0.40),
    "eel": (0.35, 0.35, 0.30), "salmon": (0.90, 0.55, 0.45), "deepcod": (0.45, 0.50, 0.60), "swordfish": (0.40, 0.55, 0.75),
    # potions
    "healing_potion": (0.90, 0.20, 0.20), "focus_potion": (0.30, 0.50, 0.95), "guard_potion": (0.85, 0.75, 0.25),
    "hunter_potion": (0.35, 0.80, 0.35), "vigor_potion": (0.70, 0.30, 0.85),
}


def cell_rect(name):
    """UV rectangle (u0, v0, u1, v1) of a palette cell, inset to avoid bleeding."""
    i = PALETTE_NAMES.index(name)
    cx, cy = i % CELLS, i // CELLS
    pad = 3.0 / ATLAS_PX
    u0, v0 = cx / CELLS + pad, 1.0 - (cy + 1) / CELLS + pad
    return u0, v0, u0 + 1.0 / CELLS - 2 * pad, v0 + 1.0 / CELLS - 2 * pad


# ---------------------------------------------------------------------------
# Procedural "hand-painted" textures (numpy)
# ---------------------------------------------------------------------------
def _noise(size, scale, seed, octaves=4, tile=True):
    rng = np.random.default_rng(seed)
    out = np.zeros((size, size))
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        g = max(2, int(scale * (2 ** o)))
        grid = rng.random((g, g))
        if tile:
            grid = np.concatenate([grid, grid[:1]], axis=0)
            grid = np.concatenate([grid, grid[:, :1]], axis=1)
        else:
            grid = rng.random((g + 1, g + 1))
        c = np.linspace(0, g, size, endpoint=False)
        i0 = c.astype(int)
        t = c - i0
        t = t * t * (3 - 2 * t)
        a = grid[i0][:, i0]
        b = grid[i0][:, i0 + 1]
        cc = grid[i0 + 1][:, i0]
        d = grid[i0 + 1][:, i0 + 1]
        tx, ty = t[None, :], t[:, None]
        out += ((a * (1 - tx) + b * tx) * (1 - ty) + (cc * (1 - tx) + d * tx) * ty) * amp
        norm += amp
        amp *= 0.5
    return out / norm


def _strokes(img, rng, count, length, width, strength, angle=None):
    """Paint soft brush strokes (lighter and darker) into a float image in place."""
    h, w = img.shape[:2]
    for _ in range(count):
        x, y = rng.uniform(0, w), rng.uniform(0, h)
        a = angle if angle is not None else rng.uniform(0, math.pi)
        a += rng.uniform(-0.25, 0.25)
        s = rng.choice([-1, 1]) * strength * rng.uniform(0.4, 1.0)
        L = length * rng.uniform(0.5, 1.2)
        steps = int(L)
        for k in range(steps):
            px = int(x + math.cos(a) * k) % w
            py = int(y + math.sin(a) * k) % h
            for dw in range(-width, width + 1):
                qx = int(px - math.sin(a) * dw) % w
                qy = int(py + math.cos(a) * dw) % h
                fall = 1.0 - abs(dw) / (width + 1)
                img[qy, qx] += s * fall
    return img


def _save_png(path, rgb):
    """Writes an HxWx3 float array (0..1, row 0 = top) as PNG via Blender's image API."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    h, w = rgb.shape[:2]
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[:, :, :3] = np.clip(rgb, 0, 1)
    rgba = rgba[::-1]  # Blender images are bottom-up
    name = "__tmp_" + os.path.basename(path)
    img = bpy.data.images.new(name, width=w, height=h, alpha=False)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def generate_textures(force=False):
    """Creates the atlas and tileable detail textures in Assets/Art/Textures/Generated."""
    os.makedirs(TEX_DIR, exist_ok=True)
    atlas_path = os.path.join(TEX_DIR, "ev_atlas.png")
    if force or not os.path.exists(atlas_path):
        rng = np.random.default_rng(7)
        atlas = np.zeros((ATLAS_PX, ATLAS_PX, 3))
        for i, name in enumerate(PALETTE_NAMES):
            base = np.array(PALETTE[name])
            cx, cy = i % CELLS, i // CELLS
            n = _noise(CELL_PX, 2, 100 + i, 3)
            shade = (n - 0.5) * 0.22
            shade = _strokes(shade, rng, 5, 14, 1, 0.06, angle=0.3 if "wood" in name or "bark" in name else None)
            # gentle top-light gradient gives a painted feel
            grad = np.linspace(0.06, -0.06, CELL_PX)[:, None]
            cell = base[None, None, :] * (1.0 + shade[:, :, None] + grad[:, :, None])
            atlas[cy * CELL_PX:(cy + 1) * CELL_PX, cx * CELL_PX:(cx + 1) * CELL_PX] = cell
        _save_png(atlas_path, atlas)

    def tile(name, fn):
        p = os.path.join(TEX_DIR, name)
        if force or not os.path.exists(p):
            _save_png(p, fn())

    def skin():
        n = _noise(256, 4, 1, 4)
        v = 0.92 + (n - 0.5) * 0.08
        return np.repeat(v[:, :, None], 3, axis=2) * np.array([1.0, 0.98, 0.97])

    def hair():
        rng = np.random.default_rng(2)
        img = (_noise(256, 3, 2, 3) - 0.5) * 0.15
        img = _strokes(img, rng, 220, 60, 1, 0.12, angle=math.pi / 2)
        v = 0.85 + img
        return np.repeat(v[:, :, None], 3, axis=2)

    def cloth():
        y, x = np.mgrid[0:256, 0:256]
        weave = (np.sin(x * math.pi / 2) * np.sin(y * math.pi / 2)) * 0.04
        n = (_noise(256, 6, 3, 3) - 0.5) * 0.12
        v = 0.88 + weave + n
        return np.repeat(v[:, :, None], 3, axis=2)

    def leather():
        n = _noise(256, 10, 4, 4)
        cracks = (np.abs(_noise(256, 6, 5, 2) - 0.5) < 0.02) * -0.12
        v = 0.85 + (n - 0.5) * 0.18 + cracks
        return np.repeat(v[:, :, None], 3, axis=2)

    def metal():
        rng = np.random.default_rng(6)
        img = (_noise(256, 5, 6, 3) - 0.5) * 0.1
        img = _strokes(img, rng, 160, 80, 0, 0.05, angle=0.0)
        v = 0.9 + img
        return np.repeat(v[:, :, None], 3, axis=2)

    def wood():
        rng = np.random.default_rng(8)
        y, x = np.mgrid[0:256, 0:256]
        grain = np.sin((y + _noise(256, 3, 9, 2) * 40) * 0.35) * 0.05
        img = grain + (_noise(256, 8, 10, 3) - 0.5) * 0.1
        img = _strokes(img, rng, 90, 120, 0, 0.05, angle=math.pi / 2)
        v = 0.88 + img
        return np.repeat(v[:, :, None], 3, axis=2)

    def glow():
        n = _noise(256, 3, 7, 3)
        v = 0.8 + (n - 0.5) * 0.3
        return np.repeat(v[:, :, None], 3, axis=2)

    tile("ev_skin.png", skin)
    tile("ev_hair.png", hair)
    tile("ev_cloth.png", cloth)
    tile("ev_leather.png", leather)
    tile("ev_metal.png", metal)
    tile("ev_wood.png", wood)
    tile("ev_glow.png", glow)
    with open(os.path.join(TEX_DIR, "palette.json"), "w") as f:
        json.dump({"cells": CELLS, "cellPx": CELL_PX, "names": PALETTE_NAMES,
                   "colors": {k: list(v) for k, v in PALETTE.items()}, "tints": {k: list(v) for k, v in TINTS.items()}},
                  f, indent=1)


# ---------------------------------------------------------------------------
# Scene helpers
# ---------------------------------------------------------------------------
def reset_scene():
    MANIFEST.clear()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for m in ("io_scene_fbx", "io_scene_gltf2"):
        try:
            import addon_utils
            addon_utils.enable(m, default_set=True)
        except Exception:
            pass
    sc = bpy.context.scene
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0
    sc.render.fps = 30


_material_cache = {}


def get_material(key, tint=None):
    """Returns (creating if needed) the shared EV_* material. Previews may pass a tint variant."""
    name = key if tint is None else "%s__%02x%02x%02x" % (key, int(tint[0] * 255), int(tint[1] * 255), int(tint[2] * 255))
    mat = bpy.data.materials.get(name)
    if mat:
        return mat
    spec = MATERIALS[key]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = nt.nodes.new("ShaderNodeTexImage")
    path = os.path.join(TEX_DIR, spec["texture"])
    img = bpy.data.images.get(spec["texture"]) or bpy.data.images.load(path, check_existing=True)
    tex.image = img
    tex.interpolation = "Closest" if key == "EV_Atlas" else "Linear"
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs[0].default_value = 1.0
    t = tint or spec["tint"]
    mix.inputs[7].default_value = (t[0], t[1], t[2], 1.0)
    nt.links.new(tex.outputs["Color"], mix.inputs[6])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.35 if key == "EV_Metal" else 0.85
    bsdf.inputs["Metallic"].default_value = 0.6 if key == "EV_Metal" else 0.0
    if spec.get("emission"):
        nt.links.new(mix.outputs[2], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = spec["emission"]
    return mat


# ---------------------------------------------------------------------------
# Primitive geometry (returns verts, faces in local space)
# ---------------------------------------------------------------------------
def p_box(sx, sy, sz, center=(0, 0, 0)):
    hx, hy, hz = sx / 2, sy / 2, sz / 2
    cx, cy, cz = center
    v = [(cx + x * hx, cy + y * hy, cz + z * hz) for z in (-1, 1) for y in (-1, 1) for x in (-1, 1)]
    f = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    return v, f


def p_taper(bx, by, tx, ty, h, z0=0.0, shift=(0, 0)):
    """Box tapering from (bx,by) at z0 to (tx,ty) at z0+h; top may be shifted in x/y."""
    sx, sy = shift
    v = [(-bx / 2, -by / 2, z0), (bx / 2, -by / 2, z0), (-bx / 2, by / 2, z0), (bx / 2, by / 2, z0),
         (-tx / 2 + sx, -ty / 2 + sy, z0 + h), (tx / 2 + sx, -ty / 2 + sy, z0 + h), (-tx / 2 + sx, ty / 2 + sy, z0 + h),
         (tx / 2 + sx, ty / 2 + sy, z0 + h)]
    f = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    return v, f


def p_cyl(r0, r1, h, n=6, z0=0.0, cap0=True, cap1=True, phase=0.0):
    v, f = [], []
    for ring, (r, z) in enumerate(((r0, z0), (r1, z0 + h))):
        for i in range(n):
            a = 2 * math.pi * i / n + phase
            v.append((math.cos(a) * r, math.sin(a) * r, z))
    for i in range(n):
        j = (i + 1) % n
        f.append((i, j, n + j, n + i))
    if cap0 and r0 > 1e-5:
        f.append(tuple(reversed(range(n))))
    if cap1 and r1 > 1e-5:
        f.append(tuple(range(n, 2 * n)))
    if r1 <= 1e-5:  # cone: collapse top ring into apex
        v = v[:n] + [(0, 0, z0 + h)]
        f = [(i, (i + 1) % n, n) for i in range(n)]
        if cap0:
            f.append(tuple(reversed(range(n))))
    return v, f


def p_sphere(rx, ry, rz, seg=8, rings=6, center=(0, 0, 0)):
    cx, cy, cz = center
    v = [(cx, cy, cz - rz)]
    for r in range(1, rings):
        phi = math.pi * r / rings - math.pi / 2
        for s in range(seg):
            th = 2 * math.pi * s / seg
            v.append((cx + math.cos(phi) * math.cos(th) * rx, cy + math.cos(phi) * math.sin(th) * ry, cz + math.sin(phi) * rz))
    v.append((cx, cy, cz + rz))
    f = []
    top = len(v) - 1
    for s in range(seg):
        f.append((0, 1 + (s + 1) % seg, 1 + s))
    for r in range(rings - 2):
        b0 = 1 + r * seg
        b1 = b0 + seg
        for s in range(seg):
            f.append((b0 + s, b0 + (s + 1) % seg, b1 + (s + 1) % seg, b1 + s))
    last = 1 + (rings - 2) * seg
    for s in range(seg):
        f.append((last + s, last + (s + 1) % seg, top))
    return v, f


def p_ico(r, center=(0, 0, 0), jitter=0.0, seed=0, squash=(1, 1, 1)):
    """Low-poly rock-friendly icosahedron."""
    t = (1 + 5 ** 0.5) / 2
    base = [(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t), (0, -1, -t), (0, 1, -t),
            (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)]
    f = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6),
         (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10),
         (8, 6, 7), (9, 8, 1)]
    rng = np.random.default_rng(seed)
    v = []
    for p in base:
        n = Vector(p).normalized() * r * (1 + rng.uniform(-jitter, jitter))
        v.append((center[0] + n.x * squash[0], center[1] + n.y * squash[1], center[2] + n.z * squash[2]))
    return v, f


def p_plane(sx, sy, z=0.0):
    hx, hy = sx / 2, sy / 2
    return [(-hx, -hy, z), (hx, -hy, z), (hx, hy, z), (-hx, hy, z)], [(0, 1, 2, 3)]


def p_wedge(sx, sy, sz):
    """Triangular prism (roof gable) - ridge along X at top centre."""
    hx, hy = sx / 2, sy / 2
    v = [(-hx, -hy, 0), (hx, -hy, 0), (hx, hy, 0), (-hx, hy, 0), (-hx, 0, sz), (hx, 0, sz)]
    f = [(0, 1, 2, 3)[::-1], (0, 1, 5, 4), (2, 3, 4, 5), (0, 4, 3), (1, 2, 5)]
    return v, f


def seg_matrix(a, b):
    """Matrix mapping local +Z (0..1 scaled by length) onto the segment a->b."""
    a, b = Vector(a), Vector(b)
    d = b - a
    q = Vector((0, 0, 1)).rotation_difference(d.normalized()) if d.length > 1e-6 else Quaternion()
    return Matrix.Translation(a) @ q.to_matrix().to_4x4()


def T(x=0, y=0, z=0):
    return Matrix.Translation((x, y, z))


def R(x=0, y=0, z=0):
    return Euler((math.radians(x), math.radians(y), math.radians(z))).to_matrix().to_4x4()


def S(x=1, y=None, z=None):
    y = x if y is None else y
    z = x if z is None else z
    return Matrix.Diagonal((x, y, z, 1))


# ---------------------------------------------------------------------------
# MeshBuilder
# ---------------------------------------------------------------------------
class MeshBuilder:
    """Accumulates parts (with material, UV mode and bone weights) into one mesh object."""

    def __init__(self, name):
        self.name = name
        self.verts = []
        self.faces = []      # (indices, material key, uvs)
        self.weights = []    # per vertex: {bone: weight}
        self.materials = []

    def _mat_index(self, key):
        if key not in self.materials:
            self.materials.append(key)
        return self.materials.index(key)

    def add(self, geo, matrix=None, mat="EV_Atlas", color=None, bone=None, weights=None, uv="auto", density=2.0,
            flip=False):
        """geo: (verts, faces). color: palette name for EV_Atlas. bone: rigid skin bone name."""
        verts, faces = geo
        m = matrix or Matrix.Identity(4)
        base = len(self.verts)
        wv = [m @ Vector(v) for v in verts]
        self.verts.extend(wv)
        for i in range(len(wv)):
            if weights:
                self.weights.append(dict(weights))
            elif bone:
                self.weights.append({bone: 1.0})
            else:
                self.weights.append({})
        if mat == "EV_Atlas" and color is None:
            color = "stone"
        rect = cell_rect(color) if mat == "EV_Atlas" else None
        # bounds for atlas normalisation
        mn = Vector((min(v.x for v in wv), min(v.y for v in wv), min(v.z for v in wv)))
        mx = Vector((max(v.x for v in wv), max(v.y for v in wv), max(v.z for v in wv)))
        size = mx - mn
        for face in faces:
            idx = [base + i for i in (reversed(face) if flip else face)]
            pts = [self.verts[i] for i in idx]
            nrm = Vector((0, 0, 0))
            for k in range(len(pts)):
                nrm += (pts[k] - pts[0]).cross(pts[(k + 1) % len(pts)] - pts[0])
            ax = max(range(3), key=lambda a: abs(nrm[a]))
            a1, a2 = [(1, 2), (0, 2), (0, 1)][ax]
            uvs = []
            for p in pts:
                if rect:
                    u = (p[a1] - mn[a1]) / (size[a1] or 1)
                    v = (p[a2] - mn[a2]) / (size[a2] or 1)
                    uvs.append((rect[0] + u * (rect[2] - rect[0]), rect[1] + v * (rect[3] - rect[1])))
                else:
                    uvs.append((p[a1] * density, p[a2] * density))
            self.faces.append((idx, self._mat_index(mat), uvs))
        return self

    def build(self, link=True, smooth=False, tints=None):
        me = bpy.data.meshes.new(self.name)
        bm = bmesh.new()
        uvl = bm.loops.layers.uv.new("UVMap")
        dl = bm.verts.layers.deform.verify()
        bverts = [bm.verts.new(v) for v in self.verts]
        groups = []
        for i, w in enumerate(self.weights):
            for bone, val in w.items():
                if bone not in groups:
                    groups.append(bone)
                bverts[i][dl][groups.index(bone)] = val
        for idx, mi, uvs in self.faces:
            try:
                f = bm.faces.new([bverts[i] for i in idx])
            except ValueError:
                continue  # duplicate face
            f.material_index = mi
            f.smooth = smooth
            for loop, uv in zip(f.loops, uvs):
                loop[uvl].uv = uv
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        bm.to_mesh(me)
        bm.free()
        obj = bpy.data.objects.new(self.name, me)
        for g in groups:
            obj.vertex_groups.new(name=g)
        for key in self.materials:
            me.materials.append(get_material(key, (tints or {}).get(key)))
        if link:
            bpy.context.scene.collection.objects.link(obj)
        return obj


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


# ---------------------------------------------------------------------------
# Export + bookkeeping
# ---------------------------------------------------------------------------
MANIFEST = []


def export_fbx(objects, path, animated=False, actions=False):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH", "EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        use_mesh_modifiers=True, mesh_smooth_type="FACE", use_tspace=False, add_leaf_bones=False,
        primary_bone_axis="Y", secondary_bone_axis="X", armature_nodetype="NULL",
        bake_anim=animated, bake_anim_use_all_actions=actions, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, path_mode="STRIP",
        embed_textures=False, use_custom_props=False)


def save_blend(category, version=1, force=False):
    """Keeps every version of the source: <category>_v###.blend is never overwritten unless forced."""
    os.makedirs(SOURCE_DIR, exist_ok=True)
    path = os.path.join(SOURCE_DIR, "%s_v%03d.blend" % (category, version))
    if os.path.exists(path) and not force:
        return path
    bpy.ops.wm.save_as_mainfile(filepath=path, compress=True, copy=True)
    return path


def record(name, kind, source, exported, prefab, tris, notes="", bones=0, clips=None):
    MANIFEST.append({"name": name, "type": kind, "source": os.path.relpath(source, REPO) if source else None,
                     "exported": os.path.relpath(exported, REPO), "prefab": prefab, "triangles": tris,
                     "bones": bones, "clips": clips or [], "status": "exported", "validation": "pending-unity-import",
                     "notes": notes})


def write_manifest(category):
    os.makedirs(DOC_DIR, exist_ok=True)
    path = os.path.join(DOC_DIR, "manifest_%s.json" % category)
    with open(path, "w") as f:
        json.dump(MANIFEST, f, indent=1)
    return path


def clear_scene_objects():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    for a in list(bpy.data.armatures):
        bpy.data.armatures.remove(a)
