"""
Exports item models (ground / inventory) and renders every inventory icon (one per item, named by
the item's "icon" field) plus the 20 skill icons with Cycles.

Outputs:
  Assets/Art/Items/<model>.fbx
  Assets/Art/Icons/Items/<icon>.png   (128 px, transparent)
  Assets/Art/Icons/Skills/<icon>.png  (128 px, transparent)
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
from emb import core, items, equipment, humanoid, props, preview  # noqa: E402

DATA = os.path.join(core.REPO, "Assets", "StreamingAssets", "GameData")
ITEM_OUT = os.path.join(core.ART, "Items")
ICON_ITEMS = os.path.join(core.ART, "Icons", "Items")
ICON_SKILLS = os.path.join(core.ART, "Icons", "Skills")
WOOD_MODELS = {"w_staff", "w_shortbow", "w_longbow", "w_crossbow"}


def tint_of(key):
    return core.TINTS.get(key) if key else None


def item_builder(item):
    """Returns (MeshBuilder, tints dict, view yaw/pitch, rotation) for an item."""
    model = item.get("model") or item["id"]
    tint = tint_of(item.get("tint"))
    tints = {}
    if model in equipment.EQUIPMENT_MODELS:
        mb = equipment.build(model)
        if model in WOOD_MODELS:
            tints["EV_Wood"] = tint or core.TINTS["oak"]
        elif tint:
            tints["EV_Metal"] = tint
        return mb, tints, ("shield" if model == "a_shield" else "long")
    if model in humanoid.ARMOUR_MODELS:
        mb = humanoid.build_armour(humanoid.BODY_TYPES["male"], model)
        if tint:
            tints["EV_Metal"] = tint
            tints["EV_Cloth"] = tint
        return mb, tints, (0, 0, 0)
    mb = items.build(model)
    key = items.TINT_MATERIAL.get(model)
    if key and tint:
        tints[key] = tint
    return mb, tints, (0, 0, 25)


def render_icon(mb, tints, rot, path, res=128):
    """rot: Euler degrees, or "long" (diagonal hero angle for weapons/tools) or "shield" (face-on)."""
    core.clear_scene_objects()
    o = mb.build(tints=tints)
    if rot == "long":
        o.matrix_world = (Matrix.Rotation(math.radians(45), 4, "Y") @ Matrix.Rotation(math.radians(-75), 4, "Z"))
        yaw, pitch = -8, 8
    elif rot == "shield":
        o.matrix_world = Matrix.Rotation(math.radians(-70), 4, "Z")
        yaw, pitch = -8, 8
    else:
        o.rotation_euler = tuple(math.radians(a) for a in rot)
        yaw, pitch = -30, 35
    bpy.context.view_layer.update()
    preview.frame_objects([o], yaw=yaw, pitch=pitch, margin=1.08)
    return preview.render(path)


def skill_models():
    S = {}
    S["skill_attack"] = (equipment.build("w_sword"), {"EV_Metal": core.TINTS["steel"]}, "long")
    S["skill_strength"] = (equipment.build("w_mace"), {"EV_Metal": core.TINTS["iron"]}, "long")
    S["skill_defense"] = (equipment.build("a_shield"), {"EV_Metal": core.TINTS["steel"]}, "shield")
    S["skill_ranged"] = (equipment.build("w_shortbow"), {"EV_Wood": core.TINTS["maple"]}, "long")
    S["skill_magic"] = (equipment.build("w_staff"), {"EV_Wood": core.TINTS["yew"], "EV_Glow": core.TINTS["mind"]},
                        "long")
    heart = core.MeshBuilder("heart")
    for sg in (1, -1):
        heart.add(core.p_sphere(0.12, 0.08, 0.12, 8, 6, center=(sg * 0.09, 0, 0.1)), color="potion_red")
    heart.add(core.p_cyl(0.19, 0.0, 0.26, n=8), core.T(0, 0, 0.08) @ core.R(180, 0, 0), color="potion_red")
    S["skill_vitality"] = (heart, {}, (0, 0, 0))
    S["skill_woodcutting"] = (equipment.build("t_hatchet"), {"EV_Metal": core.TINTS["iron"]}, "long")
    S["skill_mining"] = (equipment.build("t_pickaxe"), {"EV_Metal": core.TINTS["iron"]}, "long")
    S["skill_fishing"] = (items.build("fish"), {"EV_Metal": core.TINTS["trout"]}, (0, 0, 25))
    S["skill_farming"] = (items.build("crop_wheat"), {}, (0, 0, 25))
    S["skill_smithing"] = (props.build("anvil"), {}, (0, 0, 25))
    S["skill_cooking"] = (items.build("stew"), {}, (0, 0, 25))
    S["skill_crafting"] = (items.build("gem"), {"EV_Metal": core.TINTS["sapphire"]}, (0, 0, 25))
    S["skill_fletching"] = (equipment.build("arrow"), {"EV_Metal": core.TINTS["steel"]}, "long")
    S["skill_herblore"] = (items.build("potion"), {"EV_Glow": core.TINTS["hunter_potion"]}, (0, 0, 25))
    S["skill_runecrafting"] = (items.build("rune_stone"), {"EV_Glow": core.TINTS["path"]}, (0, 0, 25))
    S["skill_agility"] = (humanoid.build_armour(humanoid.BODY_TYPES["male"], "a_leather_boots"), {}, (0, 0, 30))
    S["skill_thieving"] = (items.build("lockpick"), {}, (0, -30, 25))
    star = core.MeshBuilder("star")
    star.add(core.p_ico(0.15), mat="EV_Glow")
    for i in range(6):
        a = i * 60
        star.add(core.p_cyl(0.05, 0.0, 0.3, n=4), core.R(0, 90, a) @ core.R(0, 0, 0), mat="EV_Atlas", color="gold")
    S["skill_prayer"] = (star, {"EV_Glow": core.TINTS["ward"]}, (90, 0, 0))
    S["skill_firemaking"] = (props.build("fire"), {"EV_Glow": core.TINTS["fire"]}, (0, 0, 25))
    return S


def main():
    core.reset_scene()
    core.generate_textures()
    preview.setup_render(res=128, samples=10)
    with open(os.path.join(DATA, "items.json")) as f:
        item_list = json.load(f)
    with open(os.path.join(DATA, "skills.json")) as f:
        skills = json.load(f)
    # item models for ground / inventory
    exported = set()
    for it in item_list:
        model = it.get("model") or it["id"]
        if model in equipment.EQUIPMENT_MODELS or model in humanoid.ARMOUR_MODELS or model in exported:
            continue
        core.clear_scene_objects()
        o = items.build(model).build()
        path = os.path.join(ITEM_OUT, "%s.fbx" % model)
        core.export_fbx([o], path)
        core.record(model, "item", None, path, None, core.tri_count(o), "Ground/inventory model")
        exported.add(model)
    icons = []
    for it in item_list:
        mb, tints, rot = item_builder(it)
        icons.append(render_icon(mb, tints, rot, os.path.join(ICON_ITEMS, it["icon"] + ".png")))
    sk = skill_models()
    skill_paths = []
    for s in skills:
        mb, tints, rot = sk[s["icon"]]
        skill_paths.append(render_icon(mb, tints, rot, os.path.join(ICON_SKILLS, s["icon"] + ".png")))
    preview.contact_sheet(icons, os.path.join(preview.PREVIEW_DIR, "item_icons_sheet.png"), cols=16, cell=96)
    preview.contact_sheet(skill_paths, os.path.join(preview.PREVIEW_DIR, "skill_icons_sheet.png"), cols=10, cell=96)
    core.save_blend("items", 1)
    core.write_manifest("items")
    print("icons:", len(icons), "skills:", len(skill_paths), "item models:", len(exported))


if __name__ == "__main__":
    main()
