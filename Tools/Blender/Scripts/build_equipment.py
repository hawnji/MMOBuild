"""
Builds held equipment (weapons, tools, shields, ammo), exports one FBX per model and writes
Assets/Art/Equipment/equipment_attach.json describing how Unity mounts each model.
Also renders a "showcase" of a fully equipped character for visual verification.
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
from mathutils import Matrix  # noqa: E402
from emb import core, rig, humanoid, anim_humanoid, equipment, preview  # noqa: E402

OUT = os.path.join(core.ART, "Equipment")

# how Unity mounts each model: socket, and which skeleton-space direction the model's +Z (Unity +Y) faces
ATTACH = {
    "a_shield": {"socket": "LeftHandSocket", "up": "hand_reverse", "face": "out"},
    "w_shortbow": {"socket": "LeftHandSocket", "up": "hand_reverse", "face": "forward"},
    "w_longbow": {"socket": "LeftHandSocket", "up": "hand_reverse", "face": "forward"},
    "arrow": {"socket": "RightHandSocket", "up": "forward", "face": "out"},
    "bolt": {"socket": "RightHandSocket", "up": "forward", "face": "out"},
}
DEFAULT_ATTACH = {"socket": "RightHandSocket", "up": "forward", "face": "out"}
PREVIEW_TINTS = ["bronze", "iron", "steel", "mithril", "duskite", "starmetal"]


def socket_world(arm, bone, up="forward"):
    pb = arm.pose.bones[bone]
    m = arm.matrix_world @ pb.matrix
    if up == "forward":
        local = Matrix.Rotation(math.radians(-90), 4, "X")  # model +Z -> bone +Y
    else:
        # model +Z -> opposite of the hand direction (bone -X of socket parent ~ up when arm hangs)
        hand = arm.pose.bones[bone.replace("Socket", "")]
        hm = arm.matrix_world @ hand.matrix
        d = -(hm.to_3x3() @ __import__("mathutils").Vector((0, 1, 0)))
        q = __import__("mathutils").Vector((0, 0, 1)).rotation_difference(d)
        return Matrix.Translation(m.translation) @ q.to_matrix().to_4x4()
    return m @ local


def main(render_previews=True):
    core.reset_scene()
    core.generate_textures()
    attach = {}
    paths = []
    for model in equipment.EQUIPMENT_MODELS:
        core.clear_scene_objects()
        obj = equipment.build(model).build()
        path = os.path.join(OUT, "%s.fbx" % model)
        core.export_fbx([obj], path)
        attach[model] = ATTACH.get(model, DEFAULT_ATTACH)
        core.record(model, "equipment", None, path, None, core.tri_count(obj), "Tintable (EV_Metal/EV_Wood)")
        if render_previews:
            preview.setup_render(res=192, samples=8)
            for k, tint in enumerate(PREVIEW_TINTS[:3] if model not in ("t_rod", "t_net", "t_harpoon") else ["iron"]):
                core.clear_scene_objects()
                key = "EV_Wood" if model in ("w_staff", "w_shortbow", "w_longbow", "w_crossbow") else "EV_Metal"
                tints = {"EV_Metal": core.TINTS[tint]}
                if key == "EV_Wood":
                    tints = {"EV_Wood": core.TINTS[["oak", "willow", "yew"][k]], "EV_Metal": core.TINTS[tint]}
                o = equipment.build(model).build(tints=tints)
                o.rotation_euler = (0, math.radians(-50), math.radians(30))
                preview.frame_objects([o], yaw=-20, pitch=20, margin=1.1)
                paths.append(preview.render(os.path.join(preview.PREVIEW_DIR, "equipment", "%s_%s.png" % (model, tint))))
    core.save_blend("equipment", 1)
    with open(os.path.join(OUT, "equipment_attach.json"), "w") as f:
        json.dump({"default": DEFAULT_ATTACH, "models": attach}, f, indent=1)
    if render_previews:
        preview.contact_sheet(paths, os.path.join(preview.PREVIEW_DIR, "equipment_sheet.png"), cols=8, cell=160)
        showcase()
    core.write_manifest("equipment")


def showcase():
    """Renders equipped characters in poses - used to eyeball the armour fit and grip sockets."""
    core.clear_scene_objects()
    shots = []
    outfits = [
        ("male", "steel", ["a_helm", "a_chest", "a_legs", "a_boots", "a_gloves"], "w_sword", "a_shield", "Idle", 0.5),
        ("female", "mithril", ["a_chest", "a_legs", "a_boots"], "w_greatsword", None, "AttackMelee", 0.45),
        ("male", "oak", ["a_leather_body", "a_leather_chaps", "a_leather_boots", "a_leather_cowl", "a_leather_gloves"],
         "w_longbow", None, "AttackRanged", 0.5),
        ("female", "blue", ["a_robe", "a_hat"], "w_staff", None, "Cast", 0.45),
        ("male", "bronze", ["a_wolfhide_cape"], "t_pickaxe", None, "Mine", 0.05),
        ("female", "iron", [], "t_hatchet", None, "Chop", 0.05),
    ]
    preview.setup_render(res=420, samples=12)
    for i, (body, tint, armour, weapon, offhand, clip, t) in enumerate(outfits):
        core.clear_scene_objects()
        p = humanoid.BODY_TYPES[body]
        arm = rig.build_armature(humanoid.skeleton(p))
        tints = {"EV_Metal": core.TINTS.get(tint, (0.6, 0.6, 0.6)), "EV_Cloth": (0.35, 0.3, 0.55) if i % 2 else None}
        tints = {k: v for k, v in tints.items() if v}
        robed = "a_robe" in armour
        body_obj = rig.bind_mesh(humanoid.build_body(p, "Body", {"skirt": robed}).build(tints=tints), arm)
        hair = humanoid.build_hair(p, ["short", "long", "spiky", "bun", "short", "ponytail"][i])
        objs = [body_obj]
        if hair and not any(a in armour for a in ("a_helm", "a_leather_cowl", "a_hat")):
            objs.append(rig.bind_mesh(hair.build(), arm))
        if body == "male" and "a_helm" not in armour:
            objs.append(rig.bind_mesh(humanoid.build_beard(p, "full" if i == 4 else "short").build(), arm))
        for a in armour:
            objs.append(rig.bind_mesh(humanoid.build_armour(p, a).build(tints={**tints, "EV_Cloth": (0.25, 0.3, 0.6)}),
                                      arm))
        acts = anim_humanoid.humanoid_clips(arm, scale=p["scale"])
        act = [a for a in acts if a.name == clip][0]
        f0, f1 = act.frame_range
        rig.pose_at(arm, act, int(f0 + (f1 - f0) * t))
        bpy.context.view_layer.update()
        wt = {"EV_Metal": core.TINTS.get(tint, (0.6, 0.6, 0.6)), "EV_Wood": core.TINTS.get(tint, core.TINTS["oak"])}
        for model in (weapon, offhand):
            if not model:
                continue
            o = equipment.build(model).build(tints=wt)
            spec = ATTACH.get(model, DEFAULT_ATTACH)
            o.matrix_world = socket_world(arm, spec["socket"], spec["up"])
            objs.append(o)
        preview.frame_objects(objs, yaw=-40, pitch=14, margin=1.08)
        shots.append(preview.render(os.path.join(preview.PREVIEW_DIR, "showcase", "showcase_%d.png" % i)))
        for a in list(bpy.data.actions):
            bpy.data.actions.remove(a)
    preview.contact_sheet(shots, os.path.join(preview.PREVIEW_DIR, "showcase_sheet.png"), cols=3, cell=360)


if __name__ == "__main__":
    main("--no-previews" not in sys.argv)
