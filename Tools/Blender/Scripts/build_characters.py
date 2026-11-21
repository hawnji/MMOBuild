"""
Builds the humanoid skeleton, player bodies (male/female), hair/beard modules, NPC outfits,
skinned armour and the shared humanoid animation set.

Outputs:
  Assets/Art/Characters/Humanoid/Humanoid_<body>.fbx       body + hair + beard meshes on the skeleton
  Assets/Art/Characters/Humanoid/Humanoid_Animations.fbx   skeleton + every clip (Generic rig)
  Assets/Art/Characters/Armour/<model>_<body>.fbx          skinned armour pieces
  Tools/Blender/SourceFiles/characters_v001.blend
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
from emb import core, rig, humanoid, anim_humanoid, preview  # noqa: E402

OUT = os.path.join(core.ART, "Characters")


def build_body_set(body):
    p = humanoid.BODY_TYPES[body]
    arm = rig.build_armature(humanoid.skeleton(p))
    objs = [arm]
    mb = humanoid.build_body(p, "Body", {"sleeves": "long"})
    objs.append(rig.bind_mesh(mb.build(), arm))
    for style in humanoid.HAIR_STYLES:
        h = humanoid.build_hair(p, style)
        if h:
            objs.append(rig.bind_mesh(h.build(), arm))
    if body == "male":
        for style in humanoid.BEARD_STYLES:
            b = humanoid.build_beard(p, style)
            if b:
                objs.append(rig.bind_mesh(b.build(), arm))
    # robe/dress body variant used by some NPCs (skirted lower body)
    mb2 = humanoid.build_body(p, "Body_Robed", {"sleeves": "long", "skirt": True})
    objs.append(rig.bind_mesh(mb2.build(), arm))
    return arm, objs


def main(render_previews=True):
    core.reset_scene()
    core.generate_textures()
    previews = []
    for body in ("male", "female"):
        core.clear_scene_objects()
        arm, objs = build_body_set(body)
        path = os.path.join(OUT, "Humanoid", "Humanoid_%s.fbx" % body.capitalize())
        core.export_fbx(objs, path)
        tris = sum(core.tri_count(o) for o in objs if o.type == "MESH" and o.name.startswith("Body"))
        core.record("Humanoid_%s" % body, "character", None, path, "Assets/Prefabs/Characters/Player_%s.prefab" % body,
                    tris, "Body, %d hair and beard modules; tint skin/hair/cloth via EV_* material colours" % (len(objs) - 3),
                    bones=len(humanoid.BONES))
        # armour fitted to this body
        for model in humanoid.ARMOUR_MODELS:
            for o in list(bpy.data.objects):
                if o.type == "MESH" and o.name.startswith("a_"):
                    bpy.data.objects.remove(o, do_unlink=True)
            am = humanoid.build_armour(humanoid.BODY_TYPES[body], model).build()
            rig.bind_mesh(am, arm)
            apath = os.path.join(OUT, "Armour", "%s_%s.fbx" % (model, body))
            core.export_fbx([arm, am], apath)
            core.record("%s_%s" % (model, body), "armour", None, apath, None, core.tri_count(am),
                        "Skinned to humanoid bones; rebind by bone name")
        if render_previews:
            previews += render_character_previews(body, arm)
        if body == "male":
            # the animation FBX carries the skeleton and all clips (no meshes needed in Unity, but keep the body
            # so the file previews nicely in the importer)
            acts = anim_humanoid.humanoid_clips(arm, scale=humanoid.BODY_TYPES[body]["scale"])
            if render_previews:
                previews += render_anim_previews(arm, acts, [o for o in objs if o.name in ("Body", "Hair_short")])
            body_obj = bpy.data.objects["Body"]
            apath = os.path.join(OUT, "Humanoid", "Humanoid_Animations.fbx")
            core.export_fbx([arm, body_obj], apath, animated=True, actions=True)
            core.record("Humanoid_Animations", "animation", None, apath, None, 0,
                        "Clips: " + ", ".join(a.name for a in acts), bones=len(humanoid.BONES),
                        clips=[a.name for a in acts])
        else:
            # female uses its own clip set so proportions are preserved
            acts = anim_humanoid.humanoid_clips(arm, scale=humanoid.BODY_TYPES[body]["scale"])
            body_obj = bpy.data.objects["Body"]
            apath = os.path.join(OUT, "Humanoid", "Humanoid_Female_Animations.fbx")
            core.export_fbx([arm, body_obj], apath, animated=True, actions=True)
            core.record("Humanoid_Female_Animations", "animation", None, apath, None, 0, "Female clip set",
                        bones=len(humanoid.BONES), clips=[a.name for a in acts])
        core.save_blend("characters_%s" % body, 1)
    if render_previews:
        preview.contact_sheet(previews, os.path.join(preview.PREVIEW_DIR, "characters_sheet.png"), cols=6)
    core.write_manifest("characters")


def render_character_previews(body, arm):
    out = []
    preview.setup_render(res=320, samples=10)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    # show body + one hair style, then a few armour sets
    combos = [("plain", ["Body", "Hair_short" if body == "male" else "Hair_long"] + (["Beard_short"] if body == "male" else [])),
              ("robed", ["Body_Robed", "Hair_bun"])]
    for tag, names in combos:
        for o in meshes:
            o.hide_render = o.name not in names
        preview.frame_objects([o for o in meshes if o.name in names], yaw=-30, pitch=12, margin=1.1)
        out.append(preview.render(os.path.join(preview.PREVIEW_DIR, "characters", "%s_%s.png" % (body, tag))))
    for o in meshes:
        o.hide_render = False
    return out


def render_anim_previews(arm, acts, show):
    out = []
    preview.setup_render(res=256, samples=8)
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o not in show
    for act in acts:
        f0, f1 = act.frame_range
        frame = int(f0 + (f1 - f0) * (0.45 if act.name not in ("Death",) else 1.0))
        rig.pose_at(arm, act, frame)
        preview.frame_objects(show, yaw=-55, pitch=15, margin=1.25)
        out.append(preview.render(os.path.join(preview.PREVIEW_DIR, "anims", "%s.png" % act.name)))
    arm.animation_data.action = None
    for o in bpy.data.objects:
        o.hide_render = False
    return out


if __name__ == "__main__":
    main("--no-previews" not in sys.argv)
