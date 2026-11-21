"""
Builds nature, crops, building kit, every world building, doors, props, stations, agility
obstacles and stalls. Building FBX files are generated from world.json so they match the
server's collision footprints exactly.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
from emb import core, environment, buildings, props, preview  # noqa: E402

WORLD_JSON = os.path.join(core.REPO, "Assets", "StreamingAssets", "GameData", "world.json")
ENV = os.path.join(core.ART, "Environment")
BLD = os.path.join(core.ART, "Buildings")
PRP = os.path.join(core.ART, "Props")


def export_static(mb, path, kind, prefab=None, notes="", extra_objs=None):
    core.clear_scene_objects()
    objs = []
    if isinstance(mb, (list, tuple)):
        objs = [m.build() for m in mb]
    else:
        objs = [mb.build()]
    core.export_fbx(objs, path)
    core.record(os.path.splitext(os.path.basename(path))[0], kind, None, path, prefab, sum(core.tri_count(o) for o in objs),
                notes)
    return objs


def render_one(mb, name, folder, yaw=-35, pitch=30, res=200):
    core.clear_scene_objects()
    objs = [m.build() for m in mb] if isinstance(mb, (list, tuple)) else [mb.build()]
    preview.setup_render(res=res, samples=8)
    preview.frame_objects(objs, yaw=yaw, pitch=pitch, margin=1.08)
    return preview.render(os.path.join(preview.PREVIEW_DIR, folder, name + ".png"))


def main(render_previews=True):
    core.reset_scene()
    core.generate_textures()
    shots = {"nature": [], "buildings": [], "props": []}
    # ---------------- nature
    for t in environment.TREES:
        for v in range(2 if t in ("oak", "pine", "dead") else 1):
            name = "tree_%s" % t + ("" if v == 0 else "_b")
            mb = environment.tree(t, seed=3 + v * 17, scale=1.0 + 0.12 * v)
            export_static(mb, os.path.join(ENV, "Trees", name + ".fbx"), "nature")
            if render_previews and v == 0:
                shots["nature"].append(render_one(environment.tree(t, seed=3), name, "nature"))
        if t != "dead":
            export_static(environment.stump(t), os.path.join(ENV, "Trees", "stump_%s.fbx" % t), "nature",
                          notes="Depleted tree node")
    for ore in environment.ORES:
        export_static(environment.rock(ore), os.path.join(ENV, "Rocks", "rock_%s.fbx" % ore), "nature")
        if render_previews:
            shots["nature"].append(render_one(environment.rock(ore), "rock_" + ore, "nature"))
    export_static(environment.rock(None, depleted=True), os.path.join(ENV, "Rocks", "rock_depleted.fbx"), "nature")
    export_static(environment.fishing_spot(), os.path.join(ENV, "Water", "fishing_spot.fbx"), "nature")
    export_static(environment.sand_pit(), os.path.join(ENV, "Rocks", "sand_pit.fbx"), "nature")
    export_static(environment.farm_patch(), os.path.join(ENV, "Farming", "farm_patch.fbx"), "nature")
    for c in environment.CROPS:
        for st in range(4):
            export_static(environment.crop(c, st), os.path.join(ENV, "Farming", "crop_%s_%d.fbx" % (c, st)), "nature",
                          notes=["seedling", "growing", "mature", "dead"][st])
        if render_previews:
            shots["nature"].append(render_one(environment.crop(c, 2), "crop_%s_2" % c, "nature"))
    for f in environment.FOLIAGE:
        export_static(environment.foliage(f), os.path.join(ENV, "Foliage", f + ".fbx"), "nature")
        if render_previews:
            shots["nature"].append(render_one(environment.foliage(f), f, "nature"))
    if render_previews:
        shots["nature"].append(render_one(environment.fishing_spot(), "fishing_spot", "nature"))
        shots["nature"].append(render_one(environment.farm_patch(), "farm_patch", "nature"))
    core.save_blend("environment", 1)

    # ---------------- building kit + world buildings
    for k in buildings.KIT:
        export_static(buildings.kit_piece(k), os.path.join(BLD, "Kit", k + ".fbx"), "building_kit")
    for style in ("wood", "stone"):
        export_static(buildings.door_model(style), os.path.join(BLD, "Kit", "door_%s.fbx" % style), "building_kit",
                      notes="Door entity; hinge at origin, opens by rotating about Y")
    with open(WORLD_JSON) as f:
        world = json.load(f)
    for b in world["buildings"]:
        shell, roof = buildings.build_building(b)
        name = "building_%d" % b["id"]
        export_static([shell, roof], os.path.join(BLD, name + ".fbx"), "building",
                      notes="%s (%s, %dx%d, door %s)" % (b["name"], b["kind"], b["w"], b["d"], b["door"]))
        if render_previews and b["id"] in (1002, 1004, 1006, 1008, 1010, 1020, 1234, 1383):
            shots["buildings"].append(render_one([shell, roof], name, "buildings", yaw=-35, pitch=35, res=300))
    core.save_blend("buildings", 1)

    # ---------------- props / stations / agility / stalls
    for group in (props.STATIONS, props.PROPS, props.AGILITY, props.STALLS):
        for n in group:
            export_static(props.build(n), os.path.join(PRP, n + ".fbx"), "prop")
            if render_previews:
                shots["props"].append(render_one(props.build(n), n, "props"))
    core.save_blend("props", 1)
    if render_previews:
        preview.contact_sheet(shots["nature"], os.path.join(preview.PREVIEW_DIR, "nature_sheet.png"), cols=8, cell=170)
        preview.contact_sheet(shots["buildings"], os.path.join(preview.PREVIEW_DIR, "buildings_sheet.png"), cols=4, cell=300)
        preview.contact_sheet(shots["props"], os.path.join(preview.PREVIEW_DIR, "props_sheet.png"), cols=8, cell=170)
    core.write_manifest("world")


if __name__ == "__main__":
    main("--no-previews" not in sys.argv)
