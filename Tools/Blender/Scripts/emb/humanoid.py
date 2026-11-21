"""
Humanoid skeleton, player/NPC bodies, hair/beard modules and skinned armour.

Conventions (Blender space): Z up, character faces -Y, character's LEFT is +X, feet at Z=0.
Rest pose is a T-pose (arms along +/-X). All humanoid meshes share bone names, so armour
and hair FBX files can be re-bound to any character skeleton in Unity by bone name.
"""
import math

from mathutils import Vector, Matrix

from . import core
from .core import MeshBuilder, p_box, p_taper, p_cyl, p_sphere, p_wedge, seg_matrix, T, R, S

# ---------------------------------------------------------------------------
# Proportions
# ---------------------------------------------------------------------------
BODY_TYPES = {
    "male": {"scale": 1.0, "shoulder": 1.0, "hips": 1.0, "chest": 1.0, "limb": 1.0, "bust": 0.0, "jaw": 1.0},
    "female": {"scale": 0.95, "shoulder": 0.86, "hips": 1.08, "chest": 0.9, "limb": 0.86, "bust": 1.0, "jaw": 0.86},
    # monster humanoids reuse the same skeleton topology with their own proportions
    "goblin": {"scale": 0.62, "shoulder": 1.0, "hips": 1.0, "chest": 1.05, "limb": 1.0, "bust": 0.0, "jaw": 1.1,
               "head": 1.45, "arm_len": 1.12},
    "orc": {"scale": 1.12, "shoulder": 1.3, "hips": 1.15, "chest": 1.3, "limb": 1.35, "bust": 0.0, "jaw": 1.3,
            "head": 1.0, "arm_len": 1.08},
    "troll": {"scale": 1.55, "shoulder": 1.35, "hips": 1.2, "chest": 1.4, "limb": 1.5, "bust": 0.0, "jaw": 1.4,
              "head": 0.85, "arm_len": 1.25},
    "warden": {"scale": 1.9, "shoulder": 1.5, "hips": 1.2, "chest": 1.5, "limb": 1.7, "bust": 0.0, "jaw": 1.0,
               "head": 0.8, "arm_len": 1.15},
}

BONES = ["Root", "Hips", "Spine", "Chest", "Neck", "Head",
         "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
         "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
         "LeftHandSocket", "RightHandSocket"]


def joints(p):
    s = p["scale"]
    hs = p.get("head", 1.0)
    al = p.get("arm_len", 1.0)
    j = {}
    j["hip"] = Vector((0, 0, 0.95 * s))
    j["spine"] = Vector((0, 0, 1.07 * s))
    j["chest"] = Vector((0, 0, 1.24 * s))
    j["neck"] = Vector((0, 0, 1.47 * s))
    j["head"] = Vector((0, 0, 1.55 * s))
    j["head_top"] = Vector((0, 0, (1.55 + 0.25 * hs) * s))
    sx = 0.19 * p["shoulder"] * s
    sz = 1.41 * s
    for side, sg in (("Left", 1), ("Right", -1)):
        j[side + "shoulder"] = Vector((sg * sx, 0, sz))
        j[side + "elbow"] = Vector((sg * (sx + 0.27 * s * al), 0, sz))
        j[side + "wrist"] = Vector((sg * (sx + 0.52 * s * al), 0, sz))
        j[side + "hand_end"] = Vector((sg * (sx + 0.62 * s * al), 0, sz))
        lx = sg * 0.095 * p["hips"] * s
        j[side + "hipj"] = Vector((lx, 0, 0.92 * s))
        j[side + "knee"] = Vector((lx, 0, 0.50 * s))
        j[side + "ankle"] = Vector((lx, 0, 0.09 * s))
        j[side + "toe"] = Vector((lx, -0.16 * s, 0.03 * s))
    return j


def skeleton(p):
    j = joints(p)
    s = p["scale"]
    b = [("Root", (0, 0, 0), (0, 0, 0.15 * s), None),
         ("Hips", j["hip"], j["spine"], "Root"),
         ("Spine", j["spine"], j["chest"], "Hips"),
         ("Chest", j["chest"], j["neck"], "Spine"),
         ("Neck", j["neck"], j["head"], "Chest"),
         ("Head", j["head"], j["head_top"], "Neck")]
    for side, sg in (("Left", 1), ("Right", -1)):
        b += [(side + "UpperArm", j[side + "shoulder"], j[side + "elbow"], "Chest"),
              (side + "LowerArm", j[side + "elbow"], j[side + "wrist"], side + "UpperArm"),
              (side + "Hand", j[side + "wrist"], j[side + "hand_end"], side + "LowerArm"),
              (side + "UpperLeg", j[side + "hipj"], j[side + "knee"], "Hips"),
              (side + "LowerLeg", j[side + "knee"], j[side + "ankle"], side + "UpperLeg"),
              (side + "Foot", j[side + "ankle"], j[side + "toe"], side + "LowerLeg")]
        # grip socket: at the fist, pointing forward (-Y) => weapons are modelled with the blade along +Z
        grip = (j[side + "wrist"] + j[side + "hand_end"]) * 0.5 + Vector((0, 0, -0.02 * s))
        # tilted 35 degrees toward the body centre so weapons angle upward when the arm hangs
        b.append((side + "HandSocket", grip, grip + Vector((-sg * 0.057 * s, -0.082 * s, 0)), side + "Hand"))
    return b


# ---------------------------------------------------------------------------
# Mesh helpers
# ---------------------------------------------------------------------------
def seg(mb, a, b, r0, r1, n=8, mat="EV_Skin", color=None, bone=None, weights=None, caps=True, phase=None):
    a, b = Vector(a), Vector(b)
    L = (b - a).length
    geo = p_cyl(r0, r1, L, n=n, cap0=caps, cap1=caps, phase=math.pi / n if phase is None else phase)
    mb.add(geo, seg_matrix(a, b), mat=mat, color=color, bone=bone, weights=weights)


def ball(mb, c, r, mat="EV_Skin", bone=None, color=None, seg_=8, rings=5, sq=(1, 1, 1), weights=None):
    mb.add(p_sphere(r * sq[0], r * sq[1], r * sq[2], seg_, rings, center=tuple(c)), mat=mat, bone=bone, color=color,
           weights=weights)


def box(mb, c, size, mat="EV_Atlas", color=None, bone=None, rot=None, weights=None):
    m = T(*c) @ (rot or Matrix.Identity(4))
    mb.add(p_box(*size), m, mat=mat, color=color, bone=bone, weights=weights)


# ---------------------------------------------------------------------------
# Body
# ---------------------------------------------------------------------------
def build_body(p, name="Body", outfit=None):
    """outfit: dict with 'sleeves' (long/short/none), 'skirt' (bool), 'boots' (bool)."""
    outfit = outfit or {}
    j = joints(p)
    s = p["scale"]
    lm = p["limb"]
    mb = MeshBuilder(name)
    sh = p["shoulder"]
    ch = p["chest"]

    # pelvis / trousers
    mb.add(p_taper(0.30 * p["hips"] * s, 0.19 * s, 0.28 * ch * s, 0.18 * s, 0.17 * s, z0=j["hip"].z - 0.07 * s),
           mat="EV_Cloth2", bone="Hips")
    # belt
    mb.add(p_taper(0.30 * ch * s, 0.20 * s, 0.30 * ch * s, 0.20 * s, 0.05 * s, z0=j["spine"].z - 0.03 * s),
           mat="EV_Leather", bone="Hips")
    box(mb, (0, -0.10 * s, j["spine"].z - 0.005 * s), (0.06 * s, 0.02 * s, 0.045 * s), color="brass", bone="Hips")
    # abdomen (shirt)
    mb.add(p_taper(0.28 * ch * s, 0.18 * s, 0.31 * ch * s, 0.2 * s, j["chest"].z - j["spine"].z + 0.02 * s,
                   z0=j["spine"].z + 0.02 * s), mat="EV_Cloth", bone="Spine")
    # chest (shirt), shoulders wider at the top
    mb.add(p_taper(0.31 * ch * s, 0.2 * s, 0.40 * sh * s, 0.21 * s, j["neck"].z - j["chest"].z - 0.01 * s,
                   z0=j["chest"].z, shift=(0, 0.005 * s)), mat="EV_Cloth", bone="Chest")
    # collar / tunic hem detail
    mb.add(p_cyl(0.075 * s, 0.07 * s, 0.03 * s, n=8, z0=j["neck"].z - 0.025 * s), mat="EV_Cloth", bone="Chest")
    if p["bust"] > 0:
        for sg in (1, -1):
            ball(mb, (sg * 0.07 * s, -0.085 * s, j["chest"].z + 0.10 * s), 0.062 * s, mat="EV_Cloth", bone="Chest",
                 sq=(1, 0.7, 0.85))
    # neck
    seg(mb, j["neck"] - Vector((0, 0, 0.03 * s)), j["head"] + Vector((0, 0, 0.03 * s)), 0.055 * s * lm ** 0.5,
        0.05 * s * lm ** 0.5, n=8, bone="Neck")

    build_head(mb, p)

    for side, sg in (("Left", 1), ("Right", -1)):
        shoulder, elbow, wrist, hend = (j[side + k] for k in ("shoulder", "elbow", "wrist", "hand_end"))
        ua, la = 0.062 * s * lm, 0.05 * s * lm
        # shoulder joint ball (shirt) blends torso and arm
        ball(mb, shoulder, 0.075 * s * lm, mat="EV_Cloth", bone=side + "UpperArm", sq=(1, 1, 0.95))
        sleeves = outfit.get("sleeves", "long")
        seg(mb, shoulder, elbow, ua, 0.052 * s * lm, mat="EV_Cloth" if sleeves != "none" else "EV_Skin",
            bone=side + "UpperArm")
        ball(mb, elbow, 0.05 * s * lm, mat="EV_Cloth" if sleeves == "long" else "EV_Skin", bone=side + "LowerArm")
        if sleeves == "long":
            seg(mb, elbow, elbow.lerp(wrist, 0.82), 0.05 * s * lm, 0.045 * s * lm, mat="EV_Cloth",
                bone=side + "LowerArm")
            seg(mb, elbow.lerp(wrist, 0.82), wrist, 0.037 * s * lm, 0.034 * s * lm, mat="EV_Skin",
                bone=side + "LowerArm")
        else:
            seg(mb, elbow, wrist, la, 0.036 * s * lm, mat="EV_Skin", bone=side + "LowerArm")
        build_hand(mb, side, sg, wrist, hend, s * lm ** 0.5)

        hipj, knee, ankle, toe = (j[side + k] for k in ("hipj", "knee", "ankle", "toe"))
        tl = 0.085 * s * lm * p["hips"] ** 0.5
        ball(mb, hipj, tl * 1.05, mat="EV_Cloth2", bone=side + "UpperLeg")
        seg(mb, hipj, knee, tl, 0.062 * s * lm, mat="EV_Cloth2", bone=side + "UpperLeg")
        ball(mb, knee, 0.062 * s * lm, mat="EV_Cloth2", bone=side + "LowerLeg")
        mid = knee.lerp(ankle, 0.55)
        seg(mb, knee, mid, 0.062 * s * lm, 0.05 * s * lm, mat="EV_Cloth2", bone=side + "LowerLeg")
        # boot shaft + foot
        seg(mb, mid + Vector((0, 0, 0.02 * s)), ankle - Vector((0, 0, 0.02 * s)), 0.058 * s * lm, 0.052 * s * lm,
            mat="EV_Leather", bone=side + "LowerLeg")
        seg(mb, mid + Vector((0, 0, 0.025 * s)), mid - Vector((0, 0, 0.0)), 0.064 * s * lm, 0.064 * s * lm,
            mat="EV_Leather", bone=side + "LowerLeg")
        build_foot(mb, side, ankle, s * lm ** 0.4)

    if outfit.get("skirt"):
        # dress/robe skirt blended between the hips and each leg so it follows walking
        for i, (sg, bone) in enumerate(((1, "LeftUpperLeg"), (-1, "RightUpperLeg"))):
            mb.add(p_taper(0.17 * p["hips"] * s, 0.21 * s, 0.24 * p["hips"] * s, 0.3 * s, 0.5 * s,
                           z0=j["hip"].z - 0.55 * s), T(sg * 0.07 * s, 0, 0), mat="EV_Cloth2",
                   weights={"Hips": 0.5, bone: 0.5})
    return mb


def build_head(mb, p):
    j = joints(p)
    s = p["scale"]
    hs = p.get("head", 1.0)
    hc = j["head"] + Vector((0, -0.005 * s, 0.115 * s * hs))
    jaw = p.get("jaw", 1.0)
    # skull - an 8x6 sphere, slightly squared off
    ball(mb, hc, 0.115 * s * hs, bone="Head", seg_=10, rings=7, sq=(0.92, 1.0, 1.08))
    # jaw / chin block
    mb.add(p_taper(0.15 * s * hs * jaw, 0.15 * s * hs, 0.11 * s * hs * jaw, 0.11 * s * hs, 0.07 * s * hs,
                   z0=hc.z - 0.115 * s * hs, shift=(0, -0.02 * s)), T(0, -0.01 * s, 0), mat="EV_Skin", bone="Head")
    # nose
    mb.add(p_taper(0.03 * s * hs, 0.03 * s * hs, 0.012 * s * hs, 0.012 * s * hs, 0.05 * s * hs),
           T(0, -0.118 * s * hs + hc.y, hc.z - 0.035 * s * hs) @ R(-75, 0, 0) @ T(0, 0, -0.005 * s),
           mat="EV_Skin", bone="Head")
    # ears
    for sg in (1, -1):
        ball(mb, (sg * 0.107 * s * hs, hc.y + 0.01 * s, hc.z - 0.01 * s * hs), 0.03 * s * hs, bone="Head", seg_=6,
             rings=4, sq=(0.45, 0.8, 1.1))
    # eyes (whites + iris) and brows
    for sg in (1, -1):
        ex = sg * 0.042 * s * hs
        ez = hc.z + 0.012 * s * hs
        ey = hc.y - 0.104 * s * hs
        box(mb, (ex, ey, ez), (0.032 * s * hs, 0.01 * s, 0.02 * s * hs), color="eye_white", bone="Head")
        box(mb, (ex - sg * 0.002 * s, ey - 0.005 * s, ez), (0.015 * s * hs, 0.008 * s, 0.018 * s * hs), color="eye",
            bone="Head")
        box(mb, (ex, ey + 0.002 * s, ez + 0.03 * s * hs), (0.045 * s * hs, 0.018 * s, 0.012 * s * hs),
            mat="EV_Hair", bone="Head", rot=R(0, sg * -8, 0))
    # mouth
    box(mb, (0, hc.y - 0.11 * s * hs, hc.z - 0.07 * s * hs), (0.045 * s * hs, 0.01 * s, 0.01 * s * hs), color="lips",
        bone="Head")


def build_hand(mb, side, sg, wrist, hend, s):
    bone = side + "Hand"
    palm_c = wrist.lerp(hend, 0.45)
    box(mb, palm_c, (0.085 * s, 0.075 * s, 0.035 * s), mat="EV_Skin", bone=bone)
    # fingers as a slightly curled block
    fc = wrist.lerp(hend, 0.85) + Vector((0, 0, -0.008 * s))
    box(mb, fc, (0.05 * s, 0.07 * s, 0.028 * s), mat="EV_Skin", bone=bone, rot=R(0, sg * 25, 0))
    # thumb points forward (-Y) in the T-pose
    seg(mb, palm_c + Vector((-sg * 0.01 * s, -0.035 * s, 0)), palm_c + Vector((sg * 0.02 * s, -0.075 * s, -0.005 * s)),
        0.014 * s, 0.012 * s, n=6, bone=bone)


def build_foot(mb, side, ankle, s):
    bone = side + "Foot"
    mb.add(p_taper(0.10 * s, 0.13 * s, 0.09 * s, 0.10 * s, 0.075 * s, z0=0.0), T(ankle.x, -0.01 * s, 0),
           mat="EV_Leather", bone=bone)
    mb.add(p_taper(0.095 * s, 0.10 * s, 0.07 * s, 0.07 * s, 0.05 * s, z0=0.0), T(ankle.x, -0.10 * s, 0),
           mat="EV_Leather", bone=bone)
    # sole
    mb.add(p_box(0.105 * s, 0.25 * s, 0.018 * s, center=(ankle.x, -0.055 * s, 0.009 * s)), mat="EV_Atlas",
           color="leather_dark", bone=bone)


# ---------------------------------------------------------------------------
# Hair & beards (EV_Hair, rigid on Head)
# ---------------------------------------------------------------------------
HAIR_STYLES = ["short", "long", "ponytail", "bun", "spiky", "braids", "bald"]
BEARD_STYLES = ["none", "full", "short", "moustache", "goatee"]


def build_hair(p, style):
    j = joints(p)
    s = p["scale"]
    hs = p.get("head", 1.0)
    hc = j["head"] + Vector((0, -0.005 * s, 0.115 * s * hs))
    r = 0.115 * s * hs
    mb = MeshBuilder("Hair_" + style)
    if style == "bald":
        return None
    # cap covering the top/back of the skull
    cap = p_sphere(r * 0.98, r * 1.06, r * 1.1, 10, 7)
    v, f = cap
    v = [(x, y, z) for (x, y, z) in v]
    keep = []
    for face in f:
        cz = sum(v[i][2] for i in face) / len(face)
        cy = sum(v[i][1] for i in face) / len(face)
        if cz > -0.15 * r or (cy > 0.25 * r and cz > -0.75 * r):
            keep.append(face)
    mb.add((v, keep), T(hc.x, hc.y + 0.008 * s, hc.z + 0.012 * s), mat="EV_Hair", bone="Head")
    # fringe
    mb.add(p_wedge(0.19 * s * hs, 0.05 * s, 0.06 * s * hs), T(0, hc.y - 0.085 * s * hs, hc.z + 0.06 * s * hs) @ R(180, 0, 0)
           @ T(0, 0, -0.06 * s * hs), mat="EV_Hair", bone="Head")
    if style == "long":
        mb.add(p_taper(0.2 * s * hs, 0.07 * s, 0.24 * s * hs, 0.05 * s, 0.32 * s, z0=0),
               T(0, hc.y + 0.08 * s * hs, hc.z - 0.3 * s), mat="EV_Hair", bone="Head")
        for sg in (1, -1):
            mb.add(p_taper(0.05 * s, 0.07 * s, 0.04 * s, 0.05 * s, 0.22 * s),
                   T(sg * 0.1 * s * hs, hc.y + 0.01 * s, hc.z - 0.22 * s), mat="EV_Hair", bone="Head")
    elif style == "ponytail":
        seg(mb, (0, hc.y + 0.11 * s * hs, hc.z + 0.02 * s), (0, hc.y + 0.16 * s * hs, hc.z - 0.22 * s), 0.04 * s,
            0.015 * s, n=6, mat="EV_Hair", bone="Head")
        ball(mb, (0, hc.y + 0.115 * s * hs, hc.z + 0.02 * s), 0.035 * s, mat="EV_Leather", bone="Head", seg_=6, rings=4)
    elif style == "bun":
        ball(mb, (0, hc.y + 0.07 * s * hs, hc.z + 0.11 * s * hs), 0.06 * s, mat="EV_Hair", bone="Head", seg_=8, rings=5)
    elif style == "spiky":
        import random
        rnd = random.Random(4)
        for i in range(11):
            a = rnd.uniform(0, 2 * math.pi)
            el = rnd.uniform(0.35, 1.2)
            d = Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el) * 0.9 + 0.2, math.sin(el)))
            base = hc + d.normalized() * r * 0.9
            seg(mb, base, base + d.normalized() * 0.08 * s, 0.03 * s, 0.0, n=4, mat="EV_Hair", bone="Head", caps=True)
    elif style == "braids":
        for sg in (1, -1):
            top = Vector((sg * 0.09 * s * hs, hc.y + 0.03 * s, hc.z - 0.05 * s))
            for k in range(4):
                ball(mb, top - Vector((0, -0.01 * s * k, 0.055 * s * k)), 0.028 * s, mat="EV_Hair", bone="Head",
                     seg_=6, rings=4)
    return mb


def build_beard(p, style):
    if style == "none":
        return None
    j = joints(p)
    s = p["scale"]
    hs = p.get("head", 1.0)
    hc = j["head"] + Vector((0, -0.005 * s, 0.115 * s * hs))
    mb = MeshBuilder("Beard_" + style)
    chin = Vector((0, hc.y - 0.095 * s * hs, hc.z - 0.1 * s * hs))
    if style in ("full", "short"):
        L = 0.11 if style == "full" else 0.05
        mb.add(p_taper(0.17 * s * hs, 0.09 * s, 0.07 * s * hs, 0.05 * s, L * s, z0=-L * s), T(chin.x, chin.y + 0.03 * s,
               chin.z + 0.04 * s), mat="EV_Hair", bone="Head")
        for sg in (1, -1):
            box(mb, (sg * 0.09 * s * hs, hc.y - 0.04 * s, hc.z - 0.05 * s), (0.03 * s, 0.09 * s, 0.09 * s * hs),
                mat="EV_Hair", bone="Head")
    if style in ("full", "short", "moustache"):
        for sg in (1, -1):
            box(mb, (sg * 0.026 * s, hc.y - 0.115 * s * hs, hc.z - 0.055 * s * hs), (0.05 * s, 0.018 * s, 0.016 * s),
                mat="EV_Hair", bone="Head", rot=R(0, sg * 14, 0))
    if style == "goatee":
        mb.add(p_taper(0.06 * s, 0.04 * s, 0.02 * s, 0.02 * s, 0.07 * s, z0=-0.07 * s),
               T(chin.x, chin.y + 0.01 * s, chin.z + 0.02 * s), mat="EV_Hair", bone="Head")
    return mb


# ---------------------------------------------------------------------------
# Armour (skinned) - "tier" models are tinted per metal in Unity (EV_Metal tint)
# ---------------------------------------------------------------------------
def build_armour(p, model):
    j = joints(p)
    s = p["scale"]
    lm = p["limb"]
    sh = p["shoulder"]
    ch = p["chest"]
    hs = p.get("head", 1.0)
    hc = j["head"] + Vector((0, -0.005 * s, 0.115 * s * hs))
    r = 0.115 * s * hs
    mb = MeshBuilder(model)

    if model == "a_helm":
        dome = p_sphere(r * 1.12, r * 1.16, r * 1.18, 10, 7)
        v, f = dome
        keep = [fc for fc in f if sum(v[i][2] for i in fc) / len(fc) > -0.35 * r]
        mb.add((v, keep), T(hc.x, hc.y + 0.006 * s, hc.z + 0.01 * s), mat="EV_Metal", bone="Head")
        # rim band, nose guard, cheek plates, crest
        mb.add(p_cyl(r * 1.16, r * 1.16, 0.03 * s, n=10, z0=hc.z - 0.03 * s), mat="EV_Metal", bone="Head")
        box(mb, (0, hc.y - r * 1.12, hc.z - 0.03 * s), (0.025 * s, 0.02 * s, 0.09 * s), mat="EV_Metal", bone="Head")
        for sg in (1, -1):
            box(mb, (sg * r * 0.95, hc.y - 0.035 * s, hc.z - 0.07 * s), (0.025 * s, 0.11 * s, 0.11 * s), mat="EV_Metal",
                bone="Head", rot=R(0, 0, sg * -12))
        mb.add(p_taper(0.025 * s, 0.22 * s, 0.015 * s, 0.14 * s, 0.05 * s), T(0, hc.y + 0.01 * s, hc.z + r * 1.1),
               mat="EV_Atlas", color="cloth_red", bone="Head")
    elif model in ("a_chest", "a_leather_body", "a_robe"):
        metal = model == "a_chest"
        mat = "EV_Metal" if metal else ("EV_Leather" if model == "a_leather_body" else "EV_Cloth")
        k = 1.12
        mb.add(p_taper(0.33 * ch * s * k, 0.22 * s * k, 0.43 * sh * s * k, 0.23 * s * k, j["neck"].z - j["chest"].z,
                       z0=j["chest"].z - 0.01 * s), mat=mat, bone="Chest")
        mb.add(p_taper(0.31 * ch * s * k, 0.21 * s * k, 0.34 * ch * s * k, 0.22 * s * k, j["chest"].z - j["spine"].z + 0.01 * s,
                       z0=j["spine"].z), mat=mat, bone="Spine")
        mb.add(p_taper(0.33 * p["hips"] * s * k, 0.22 * s * k, 0.31 * ch * s * k, 0.21 * s * k, 0.1 * s,
                       z0=j["spine"].z - 0.1 * s), mat=mat, bone="Hips")
        if metal:
            # chest ridge + pauldrons
            mb.add(p_wedge(0.06 * s, 0.03 * s, 0.03 * s), T(0, -0.13 * s, j["chest"].z + 0.02 * s) @ R(90, 0, 0)
                   @ S(1, 1, 1), mat="EV_Metal", bone="Chest")
            box(mb, (0, -0.128 * s, j["chest"].z + 0.04 * s), (0.03 * s, 0.02 * s, 0.2 * s), mat="EV_Metal", bone="Chest")
            for side, sg in (("Left", 1), ("Right", -1)):
                c = j[side + "shoulder"] + Vector((sg * 0.03 * s, 0, 0.03 * s))
                ball(mb, c, 0.1 * s * lm, mat="EV_Metal", bone=side + "UpperArm", seg_=8, rings=5, sq=(1.1, 1.1, 0.75))
                seg(mb, j[side + "shoulder"] + Vector((sg * 0.06 * s, 0, 0)), j[side + "elbow"] - Vector((sg * 0.04 * s, 0, 0)),
                    0.072 * s * lm, 0.062 * s * lm, mat="EV_Metal", bone=side + "UpperArm")
            mb.add(p_cyl(0.085 * s, 0.08 * s, 0.05 * s, n=8, z0=j["neck"].z - 0.03 * s), mat="EV_Metal", bone="Chest")
            for z in (j["spine"].z - 0.05 * s, j["spine"].z + 0.06 * s):
                mb.add(p_taper(0.36 * ch * s, 0.25 * s, 0.36 * ch * s, 0.25 * s, 0.02 * s, z0=z), mat="EV_Atlas",
                       color="leather_dark", bone="Spine" if z > j["spine"].z else "Hips")
        else:
            for side, sg in (("Left", 1), ("Right", -1)):
                ball(mb, j[side + "shoulder"], 0.085 * s * lm, mat=mat, bone=side + "UpperArm", sq=(1, 1, 0.9))
                seg(mb, j[side + "shoulder"], j[side + "elbow"] - Vector((sg * 0.05 * s, 0, 0)), 0.07 * s * lm,
                    0.062 * s * lm, mat=mat, bone=side + "UpperArm")
                if model == "a_robe":
                    seg(mb, j[side + "elbow"] - Vector((sg * 0.06 * s, 0, 0)), j[side + "wrist"] - Vector((sg * 0.04 * s, 0, 0)),
                        0.066 * s * lm, 0.085 * s * lm, mat=mat, bone=side + "LowerArm")
            if model == "a_leather_body":
                # laces + collar
                for i in range(3):
                    box(mb, (0, -0.135 * s, j["chest"].z + (0.04 + 0.05 * i) * s), (0.05 * s, 0.01 * s, 0.01 * s),
                        color="rope", bone="Chest")
                mb.add(p_cyl(0.1 * s, 0.09 * s, 0.05 * s, n=8, z0=j["neck"].z - 0.03 * s), mat="EV_Leather", bone="Chest")
            else:
                # robe skirt: split into two halves blended to legs + trim + sash
                for sg, bone in ((1, "LeftUpperLeg"), (-1, "RightUpperLeg")):
                    mb.add(p_taper(0.19 * p["hips"] * s, 0.25 * s, 0.25 * p["hips"] * s, 0.32 * s, 0.75 * s,
                                   z0=j["hip"].z - 0.8 * s), T(sg * 0.08 * s, 0, 0), mat="EV_Cloth",
                           weights={"Hips": 0.45, bone: 0.55})
                mb.add(p_taper(0.36 * ch * s, 0.25 * s, 0.36 * ch * s, 0.25 * s, 0.05 * s, z0=j["spine"].z - 0.03 * s),
                       mat="EV_Atlas", color="cloth_yellow", bone="Hips")
                box(mb, (0, -0.13 * s, j["chest"].z + 0.06 * s), (0.04 * s, 0.01 * s, 0.25 * s), color="cloth_yellow",
                    bone="Chest")
    elif model in ("a_legs", "a_leather_chaps"):
        metal = model == "a_legs"
        mat = "EV_Metal" if metal else "EV_Leather"
        for side, sg in (("Left", 1), ("Right", -1)):
            hipj, knee, ankle = j[side + "hipj"], j[side + "knee"], j[side + "ankle"]
            seg(mb, hipj + Vector((0, 0, 0.04 * s)), knee + Vector((0, 0, 0.03 * s)), 0.098 * s * lm, 0.074 * s * lm,
                mat=mat, bone=side + "UpperLeg")
            if metal:
                ball(mb, knee + Vector((0, -0.02 * s, 0)), 0.065 * s * lm, mat="EV_Metal", bone=side + "LowerLeg",
                     sq=(1, 1.1, 1))
                seg(mb, knee - Vector((0, 0, 0.03 * s)), knee.lerp(ankle, 0.75), 0.07 * s * lm, 0.06 * s * lm,
                    mat="EV_Metal", bone=side + "LowerLeg")
            # tasset
            mb.add(p_taper(0.15 * s, 0.03 * s, 0.17 * s, 0.03 * s, 0.17 * s, z0=-0.17 * s),
                   T(sg * 0.1 * s, -0.115 * s, j["hip"].z + 0.02 * s) @ R(-8, 0, 0), mat=mat, weights={"Hips": 0.5, side + "UpperLeg": 0.5})
        mb.add(p_taper(0.34 * p["hips"] * s, 0.23 * s, 0.32 * ch * s, 0.22 * s, 0.12 * s, z0=j["hip"].z - 0.08 * s),
               mat=mat, bone="Hips")
    elif model in ("a_boots", "a_leather_boots"):
        metal = model == "a_boots"
        mat = "EV_Metal" if metal else "EV_Leather"
        for side in ("Left", "Right"):
            ankle, knee = j[side + "ankle"], j[side + "knee"]
            seg(mb, ankle - Vector((0, 0, 0.03 * s)), knee.lerp(ankle, 0.4), 0.066 * s * lm, 0.072 * s * lm, mat=mat,
                bone=side + "LowerLeg")
            mb.add(p_taper(0.12 * s, 0.15 * s, 0.11 * s, 0.12 * s, 0.09 * s, z0=0.0), T(ankle.x, -0.012 * s, 0), mat=mat,
                   bone=side + "Foot")
            mb.add(p_taper(0.115 * s, 0.11 * s, 0.08 * s, 0.07 * s, 0.065 * s, z0=0.0), T(ankle.x, -0.11 * s, 0), mat=mat,
                   bone=side + "Foot")
            if not metal:
                mb.add(p_cyl(0.078 * s, 0.078 * s, 0.03 * s, n=8, z0=knee.lerp(ankle, 0.4).z - 0.01 * s),
                       T(ankle.x, 0, 0), mat="EV_Atlas", color="fur_brown", bone=side + "LowerLeg")
    elif model in ("a_gloves", "a_leather_gloves"):
        metal = model == "a_gloves"
        mat = "EV_Metal" if metal else "EV_Leather"
        for side, sg in (("Left", 1), ("Right", -1)):
            wrist, hend, elbow = j[side + "wrist"], j[side + "hand_end"], j[side + "elbow"]
            seg(mb, elbow.lerp(wrist, 0.45), wrist + Vector((sg * 0.01 * s, 0, 0)), 0.05 * s * lm, 0.06 * s * lm,
                mat=mat, bone=side + "LowerArm")
            box(mb, wrist.lerp(hend, 0.45), (0.095 * s, 0.085 * s, 0.045 * s), mat=mat, bone=side + "Hand")
            box(mb, wrist.lerp(hend, 0.85) + Vector((0, 0, -0.008 * s)), (0.06 * s, 0.08 * s, 0.036 * s), mat=mat,
                bone=side + "Hand", rot=R(0, sg * 25, 0))
    elif model == "a_leather_cowl":
        hood = p_sphere(r * 1.2, r * 1.22, r * 1.22, 10, 7)
        v, f = hood
        keep = [fc for fc in f if not (sum(v[i][1] for i in fc) / len(fc) < -0.55 * r and
                                       abs(sum(v[i][2] for i in fc) / len(fc)) < 0.75 * r)]
        mb.add((v, keep), T(hc.x, hc.y + 0.01 * s, hc.z + 0.005 * s), mat="EV_Leather", bone="Head")
        mb.add(p_taper(0.3 * s, 0.24 * s, 0.18 * s, 0.18 * s, 0.1 * s, z0=j["neck"].z - 0.06 * s), mat="EV_Leather",
               bone="Chest")
    elif model == "a_hat":
        mb.add(p_cyl(r * 2.1, r * 2.0, 0.02 * s, n=12, z0=hc.z + 0.04 * s), mat="EV_Cloth", bone="Head")
        mb.add(p_cyl(r * 1.08, 0.0, 0.32 * s, n=10, z0=hc.z + 0.05 * s), R(-10, 0, 0), mat="EV_Cloth", bone="Head")
        mb.add(p_cyl(r * 1.1, r * 1.08, 0.04 * s, n=10, z0=hc.z + 0.05 * s), mat="EV_Atlas", color="cloth_yellow",
               bone="Head")
    elif model == "a_wolfhide_cape":
        top = j["neck"].z - 0.02 * s
        # cape made of three rows so it bends between chest, spine and hips
        rows = [(top, 0.36, "Chest"), (j["chest"].z, 0.44, "Chest"), (j["spine"].z, 0.5, "Spine"),
                (j["hip"].z - 0.25 * s, 0.56, "Hips")]
        for (z0, w0, b0), (z1, w1, b1) in zip(rows, rows[1:]):
            v = [(-w0 * s / 2, 0.13 * s, z0), (w0 * s / 2, 0.13 * s, z0), (w1 * s / 2, 0.16 * s, z1),
                 (-w1 * s / 2, 0.16 * s, z1)]
            vv = v + [(x, y + 0.015 * s, z) for (x, y, z) in v]
            ff = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (3, 7, 6, 2), (0, 4, 7, 3), (1, 2, 6, 5)]
            mb.add((vv, ff), mat="EV_Atlas", color="fur_grey", weights={b0: 0.5, b1: 0.5})
        mb.add(p_cyl(0.13 * s, 0.11 * s, 0.07 * s, n=10, z0=j["neck"].z - 0.07 * s), mat="EV_Atlas", color="fur_dark",
               bone="Chest")
        ball(mb, (0, -0.1 * s, j["neck"].z - 0.04 * s), 0.025 * s, mat="EV_Atlas", color="bone", bone="Chest")
    else:
        raise ValueError(model)
    return mb


ARMOUR_MODELS = ["a_helm", "a_chest", "a_legs", "a_boots", "a_gloves", "a_leather_body", "a_leather_chaps",
                 "a_leather_boots", "a_leather_gloves", "a_leather_cowl", "a_robe", "a_hat", "a_wolfhide_cape"]
