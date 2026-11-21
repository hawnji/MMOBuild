"""
Builds every enemy and boss: mesh + rig + clips in one FBX per creature under Assets/Art/Creatures.
Writes Assets/Art/Creatures/creature_visuals.json (weapon mounts, clip names, scale hints).
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
from emb import core, rig, humanoid, anim_humanoid, creatures, equipment, preview  # noqa: E402
import build_equipment  # noqa: E402

OUT = os.path.join(core.ART, "Creatures")


def export_creature(model, arm, mesh_objs, acts, extra=None):
    path = os.path.join(OUT, "%s.fbx" % model)
    core.export_fbx([arm] + mesh_objs, path, animated=True, actions=True)
    tris = sum(core.tri_count(o) for o in mesh_objs)
    core.record(model, "creature", None, path, "Assets/Prefabs/Creatures/%s.prefab" % model, tris,
                (extra or {}).get("notes", ""), bones=len(arm.data.bones), clips=[a.name for a in acts])
    return path


def main(render_previews=True):
    core.reset_scene()
    core.generate_textures()
    visuals = {}
    shots = []
    preview.setup_render(res=300, samples=10)
    for model, (body, builder, weapon, tint, heavy) in creatures.HUMANOID_MONSTERS.items():
        core.clear_scene_objects()
        p = humanoid.BODY_TYPES[body]
        arm = rig.build_armature(humanoid.skeleton(p))
        mesh = rig.bind_mesh(builder(p).build(), arm)
        acts = anim_humanoid.humanoid_clips(arm, scale=p["scale"], heavy=heavy)
        for a in list(acts):
            if a.name not in creatures.MONSTER_CLIPS:
                bpy.data.actions.remove(a)
                acts.remove(a)
        export_creature(model, arm, [mesh], acts)
        visuals[model] = {"rig": "humanoid", "weapon": weapon, "weaponTint": tint, "clips": [a.name for a in acts],
                          "hover": 0.35 if model == "enemy_forest_spirit" else 0.0}
        if render_previews:
            shots += shoot(model, arm, [mesh], acts, weapon, tint)
    for model, q in creatures.QUADS.items():
        core.clear_scene_objects()
        bones = creatures.quad_skeleton(q)
        arm = rig.build_armature(bones)
        mesh = rig.bind_mesh(creatures.build_quad_mesh(q, bones).build(), arm)
        acts = creatures.quad_clips(arm, q)
        export_creature(model, arm, [mesh], acts)
        visuals[model] = {"rig": "quadruped", "weapon": None, "clips": [a.name for a in acts], "hover": 0.0}
        if render_previews:
            shots += shoot(model, arm, [mesh], acts, None, None)
    for model in creatures.SPIDER_MODELS:
        core.clear_scene_objects()
        bones = creatures.spider_skeleton(1.0)
        arm = rig.build_armature(bones)
        mesh = rig.bind_mesh(creatures.build_spider_mesh(bones, 1.0).build(), arm)
        acts = creatures.spider_clips(arm)
        export_creature(model, arm, [mesh], acts)
        visuals[model] = {"rig": "arachnid", "weapon": None, "clips": [a.name for a in acts], "hover": 0.0}
        if render_previews:
            shots += shoot(model, arm, [mesh], acts, None, None)
    core.save_blend("creatures", 1)
    with open(os.path.join(OUT, "creature_visuals.json"), "w") as f:
        json.dump(visuals, f, indent=1)
    if render_previews:
        preview.contact_sheet(shots, os.path.join(preview.PREVIEW_DIR, "creatures_sheet.png"), cols=6, cell=220)
    core.write_manifest("creatures")


def shoot(model, arm, meshes, acts, weapon, tint):
    out = []
    for clip, t in (("Idle", 0.0), ("AttackMelee", 0.45)):
        act = [a for a in acts if a.name == clip][0]
        f0, f1 = act.frame_range
        rig.pose_at(arm, act, int(f0 + (f1 - f0) * t))
        bpy.context.view_layer.update()
        objs = list(meshes)
        w = None
        if weapon:
            w = equipment.build(weapon).build(tints={"EV_Metal": core.TINTS[tint]})
            spec = build_equipment.ATTACH.get(weapon, build_equipment.DEFAULT_ATTACH)
            w.matrix_world = build_equipment.socket_world(arm, spec["socket"], spec["up"])
            objs.append(w)
        preview.frame_objects(objs, yaw=-40, pitch=18, margin=1.1)
        out.append(preview.render(os.path.join(preview.PREVIEW_DIR, "creatures", "%s_%s.png" % (model, clip))))
        if w:
            bpy.data.objects.remove(w, do_unlink=True)
    arm.animation_data.action = None
    return out


if __name__ == "__main__":
    main("--no-previews" not in sys.argv)
