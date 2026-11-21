"""
Generic armature construction and direction-based animation authoring.

Rotations are authored in *armature space* (X = character's left, -Y = forward, Z = up)
relative to the parent's motion, which is much easier to reason about than per-bone roll
axes. They are converted to each bone's local frame with  L = M^-1 * Q * M  where M is the
bone's rest orientation, so any skeleton (humanoid, quadruped, spider) can use the same
authoring helpers.
"""
import math

import bpy
from mathutils import Matrix, Vector, Euler, Quaternion

ARMATURE_NAME = "Armature"


def build_armature(bones, name=ARMATURE_NAME):
    """bones: list of (name, head, tail, parent_or_None). Returns the armature object."""
    arm = bpy.data.armatures.new(name)
    obj = bpy.data.objects.new(name, arm)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = {}
    for bname, head, tail, parent in bones:
        b = arm.edit_bones.new(bname)
        b.head = Vector(head)
        b.tail = Vector(tail)
        b.roll = 0.0
        if parent:
            b.parent = eb[parent]
            b.use_connect = False
        eb[bname] = b
    bpy.ops.object.mode_set(mode="OBJECT")
    arm.display_type = "STICK"
    for pb in obj.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return obj


def bind_mesh(mesh_obj, arm_obj):
    """Parents a mesh to the armature with an Armature modifier (vertex groups already named after bones)."""
    mesh_obj.parent = arm_obj
    mod = mesh_obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_obj
    return mesh_obj


def _rest3(arm_obj, bone):
    return arm_obj.data.bones[bone].matrix_local.to_3x3()


def to_local_quat(arm_obj, bone, deg):
    """deg: (rx, ry, rz) in degrees, armature axes, XYZ order."""
    M = _rest3(arm_obj, bone)
    if isinstance(deg, Quaternion):
        Q = deg.to_matrix()
    elif isinstance(deg, Matrix):
        Q = deg.to_3x3()
    else:
        Q = Euler((math.radians(deg[0]), math.radians(deg[1]), math.radians(deg[2])), "XYZ").to_matrix()
    L = M.inverted() @ Q @ M
    return L.to_quaternion()


def to_local_offset(arm_obj, bone, off):
    M = _rest3(arm_obj, bone)
    return M.inverted() @ Vector(off)


def make_action(arm_obj, clip_name, keys, fps=30, loop=True):
    """
    keys: list of (frame, {bone: (rx,ry,rz)}, {bone: (dx,dy,dz)}) - unspecified bones return to rest.
    All bones that appear anywhere in the clip are keyed on every key frame so poses are explicit.
    """
    act = bpy.data.actions.new(clip_name)
    act.use_fake_user = True
    arm_obj.animation_data_create()
    arm_obj.animation_data.action = act
    rot_bones = set()
    loc_bones = set()
    for _, rots, locs in keys:
        rot_bones.update(rots.keys())
        loc_bones.update((locs or {}).keys())
    for frame, rots, locs in keys:
        locs = locs or {}
        for b in rot_bones:
            pb = arm_obj.pose.bones[b]
            pb.rotation_quaternion = to_local_quat(arm_obj, b, rots.get(b, (0, 0, 0)))
            pb.keyframe_insert("rotation_quaternion", frame=frame)
        for b in loc_bones:
            pb = arm_obj.pose.bones[b]
            pb.location = to_local_offset(arm_obj, b, locs.get(b, (0, 0, 0)))
            pb.keyframe_insert("location", frame=frame)
    # make quaternion interpolation take the short path and keep the curves smooth-ish
    _ensure_quat_continuity(act)
    for fc in _fcurves(act):
        for kp in fc.keyframe_points:
            kp.interpolation = "BEZIER"
            kp.easing = "AUTO"
    act["ev_loop"] = 1 if loop else 0
    act.frame_range = (keys[0][0], keys[-1][0])
    for pb in arm_obj.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = Vector()
    arm_obj.animation_data.action = None
    return act


def _fcurves(act):
    """Blender 4.4+ uses layered actions (slots/channelbags); older uses act.fcurves."""
    if hasattr(act, "fcurves") and len(getattr(act, "layers", [])) == 0:
        try:
            return list(act.fcurves)
        except Exception:
            pass
    out = []
    for layer in getattr(act, "layers", []):
        for strip in layer.strips:
            for bag in strip.channelbags:
                out.extend(bag.fcurves)
    return out


def _ensure_quat_continuity(act):
    groups = {}
    for fc in _fcurves(act):
        if fc.data_path.endswith("rotation_quaternion"):
            groups.setdefault(fc.data_path, {})[fc.array_index] = fc
    for path, comps in groups.items():
        if len(comps) != 4:
            continue
        n = len(comps[0].keyframe_points)
        prev = None
        for i in range(n):
            q = Quaternion([comps[c].keyframe_points[i].co[1] for c in range(4)])
            if prev is not None and prev.dot(q) < 0:
                q.negate()
                for c in range(4):
                    comps[c].keyframe_points[i].co[1] = q[c]
            prev = q
        for c in range(4):
            comps[c].update()


def push_actions_to_nla(arm_obj, actions):
    """FBX exporter with bake_anim_use_all_actions exports every action; keep them assigned via NLA tracks too."""
    arm_obj.animation_data_create()
    for act in actions:
        tr = arm_obj.animation_data.nla_tracks.new()
        tr.name = act.name
        st = tr.strips.new(act.name, int(act.frame_range[0]), act)
        tr.mute = True
    arm_obj.animation_data.action = None


def pose_at(arm_obj, act, frame):
    """Applies an action at a frame (for preview renders)."""
    arm_obj.animation_data_create()
    arm_obj.animation_data.action = act
    bpy.context.scene.frame_set(int(frame))
