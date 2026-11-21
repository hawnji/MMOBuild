"""
Enemies and bosses.

Humanoid monsters (goblin, orc, troll, skeleton, zombie, bandit, forest spirit, Hollow Warden)
use the humanoid skeleton topology with their own proportions and get the humanoid clip set
re-generated for their proportions. Beasts get bespoke rigs:
  * quadruped (rat, wolf, Mire Queen toad)
  * arachnid (webspinner spider)
Each creature exports one FBX with mesh + armature + clips (Idle, Walk, Attack, Hurt, Death).
"""
import math
import random

from mathutils import Vector, Matrix

from . import rig, humanoid
from .core import MeshBuilder, p_box, p_taper, p_cyl, p_sphere, p_wedge, p_ico, seg_matrix, T, R, S
from .humanoid import seg, ball, box, joints


def Rx(d):
    return Matrix.Rotation(math.radians(d), 3, "X")


def Ry(d):
    return Matrix.Rotation(math.radians(d), 3, "Y")


def Rz(d):
    return Matrix.Rotation(math.radians(d), 3, "Z")


# ===========================================================================
# Humanoid monsters
# ===========================================================================
def _limbs(mb, p, j, skin, upper, lower, foot, arm_r=1.0, leg_r=1.0, upper_arm=None):
    s = p["scale"]
    lm = p["limb"]
    for side, sg in (("Left", 1), ("Right", -1)):
        sh, el, wr, he = (j[side + k] for k in ("shoulder", "elbow", "wrist", "hand_end"))
        ball(mb, sh, 0.075 * s * lm * arm_r, mat="EV_Atlas", color=upper_arm or skin, bone=side + "UpperArm")
        seg(mb, sh, el, 0.065 * s * lm * arm_r, 0.055 * s * lm * arm_r, mat="EV_Atlas", color=upper_arm or skin,
            bone=side + "UpperArm")
        ball(mb, el, 0.052 * s * lm * arm_r, mat="EV_Atlas", color=skin, bone=side + "LowerArm")
        seg(mb, el, wr, 0.055 * s * lm * arm_r, 0.042 * s * lm * arm_r, mat="EV_Atlas", color=skin, bone=side + "LowerArm")
        pc = wr.lerp(he, 0.45)
        box(mb, pc, (0.1 * s * arm_r, 0.085 * s * arm_r, 0.045 * s * arm_r), color=skin, bone=side + "Hand")
        box(mb, wr.lerp(he, 0.9), (0.06 * s * arm_r, 0.08 * s * arm_r, 0.035 * s * arm_r), color=skin, bone=side + "Hand",
            rot=R(0, sg * 25, 0))
        hp, kn, an = (j[side + k] for k in ("hipj", "knee", "ankle"))
        ball(mb, hp, 0.09 * s * lm * leg_r, mat="EV_Atlas", color=upper, bone=side + "UpperLeg")
        seg(mb, hp, kn, 0.088 * s * lm * leg_r, 0.066 * s * lm * leg_r, mat="EV_Atlas", color=upper, bone=side + "UpperLeg")
        ball(mb, kn, 0.064 * s * lm * leg_r, mat="EV_Atlas", color=lower, bone=side + "LowerLeg")
        seg(mb, kn, an, 0.064 * s * lm * leg_r, 0.05 * s * lm * leg_r, mat="EV_Atlas", color=lower, bone=side + "LowerLeg")
        mb.add(p_taper(0.12 * s * leg_r, 0.15 * s * leg_r, 0.1 * s * leg_r, 0.1 * s * leg_r, 0.08 * s, z0=0),
               T(an.x, -0.02 * s, 0), color=foot, bone=side + "Foot")
        mb.add(p_taper(0.11 * s * leg_r, 0.11 * s * leg_r, 0.08 * s * leg_r, 0.06 * s * leg_r, 0.05 * s, z0=0),
               T(an.x, -0.12 * s * leg_r, 0), color=foot, bone=side + "Foot")


def _torso(mb, p, j, chest_col, belly_col, hip_col, belly=1.0):
    s = p["scale"]
    ch = p["chest"]
    sh = p["shoulder"]
    mb.add(p_taper(0.3 * p["hips"] * s, 0.2 * s, 0.3 * ch * s, 0.2 * s, 0.17 * s, z0=j["hip"].z - 0.07 * s), color=hip_col,
           bone="Hips")
    mb.add(p_taper(0.3 * ch * s * belly, 0.22 * s * belly, 0.33 * ch * s, 0.21 * s, j["chest"].z - j["spine"].z + 0.03 * s,
                   z0=j["spine"].z + 0.0 * s), color=belly_col, bone="Spine")
    mb.add(p_taper(0.33 * ch * s, 0.21 * s, 0.42 * sh * s, 0.22 * s, j["neck"].z - j["chest"].z, z0=j["chest"].z),
           color=chest_col, bone="Chest")
    seg(mb, j["neck"] - Vector((0, 0, 0.04 * s)), j["head"] + Vector((0, 0, 0.03 * s)), 0.065 * s * ch,
        0.055 * s * ch, mat="EV_Atlas", color=chest_col if chest_col.startswith("skin") else belly_col, bone="Neck")


def _face(mb, hc, r, skin, eye="eye", brow=None, nose=1.0, jaw=1.0, s=1.0):
    ball(mb, hc, r, mat="EV_Atlas", color=skin, bone="Head", seg_=10, rings=7, sq=(0.95, 1.0, 1.05))
    mb.add(p_taper(1.3 * r * jaw, 1.3 * r, 0.95 * r * jaw, 0.95 * r, 0.6 * r, z0=hc.z - r * 0.95, shift=(0, -0.15 * r)),
           T(0, hc.y - 0.1 * r, 0), color=skin, bone="Head")
    mb.add(p_cyl(0.18 * r * nose, 0.05 * r, 0.45 * r * nose, n=5), T(0, hc.y - 0.95 * r, hc.z - 0.2 * r) @ R(80, 0, 0),
           color=skin, bone="Head")
    for sg in (1, -1):
        box(mb, (sg * 0.38 * r, hc.y - 0.9 * r, hc.z + 0.08 * r), (0.28 * r, 0.08 * r, 0.18 * r), color="black",
            bone="Head")
        box(mb, (sg * 0.38 * r, hc.y - 0.95 * r, hc.z + 0.08 * r), (0.13 * r, 0.06 * r, 0.12 * r), color=eye, bone="Head")
        if brow:
            box(mb, (sg * 0.38 * r, hc.y - 0.88 * r, hc.z + 0.3 * r), (0.4 * r, 0.12 * r, 0.1 * r), color=brow,
                bone="Head", rot=R(0, sg * -15, 0))
    box(mb, (0, hc.y - 0.92 * r, hc.z - 0.55 * r), (0.5 * r, 0.08 * r, 0.08 * r), color="black", bone="Head")


def head_center(p):
    j = joints(p)
    s = p["scale"]
    hs = p.get("head", 1.0)
    return j["head"] + Vector((0, -0.005 * s, 0.115 * s * hs)), 0.115 * s * hs


def build_goblin(p):
    j = joints(p)
    s = p["scale"]
    mb = MeshBuilder("Goblin")
    _torso(mb, p, j, "leather", "skin_goblin", "cloth_brown", belly=1.1)
    _limbs(mb, p, j, "skin_goblin", "skin_goblin", "skin_goblin", "skin_goblin")
    hc, r = head_center(p)
    _face(mb, hc, r, "skin_goblin", eye="flower_yellow", brow="leaf_dark", nose=1.6, s=s)
    for sg in (1, -1):  # long ears
        mb.add(p_cyl(0.25 * r, 0.0, 1.3 * r, n=4), T(sg * 0.85 * r, hc.y + 0.1 * r, hc.z + 0.1 * r) @ R(0, sg * 75, 0)
               @ R(-15, 0, 0), color="skin_goblin", bone="Head")
    # loincloth + vest straps + teeth
    box(mb, (0, -0.11 * s, j["hip"].z - 0.12 * s), (0.18 * s, 0.03 * s, 0.22 * s), color="cloth_brown", bone="Hips")
    box(mb, (0, 0.11 * s, j["hip"].z - 0.12 * s), (0.18 * s, 0.03 * s, 0.22 * s), color="cloth_brown", bone="Hips")
    for sg in (1, -1):
        mb.add(p_cyl(0.06 * r, 0.0, 0.2 * r, n=3), T(sg * 0.25 * r, hc.y - 0.95 * r, hc.z - 0.62 * r), color="teeth",
               bone="Head")
    return mb


def build_orc(p, troll=False):
    j = joints(p)
    s = p["scale"]
    skin = "skin_troll" if troll else "skin_orc"
    mb = MeshBuilder("Troll" if troll else "Orc")
    _torso(mb, p, j, skin if troll else "iron_dark", skin, "fur_brown" if troll else "leather_dark",
           belly=1.35 if troll else 1.0)
    _limbs(mb, p, j, skin, "fur_brown" if troll else "leather_dark", skin if troll else "leather", "skin_troll" if troll
           else "leather_dark", arm_r=1.25 if troll else 1.1, leg_r=1.2 if troll else 1.0)
    hc, r = head_center(p)
    _face(mb, hc, r, skin, eye="ember" if not troll else "flower_yellow", brow=skin, nose=1.2, jaw=1.4, s=s)
    for sg in (1, -1):  # tusks
        mb.add(p_cyl(0.1 * r, 0.0, 0.45 * r, n=4), T(sg * 0.35 * r, hc.y - 0.95 * r, hc.z - 0.75 * r) @ R(-10, 0, 0),
               color="bone", bone="Head")
        if not troll:  # shoulder plates with spikes
            c = j[("Left" if sg > 0 else "Right") + "shoulder"]
            ball(mb, c + Vector((sg * 0.03 * s, 0, 0.04 * s)), 0.1 * s, mat="EV_Atlas", color="iron",
                 bone=("Left" if sg > 0 else "Right") + "UpperArm", sq=(1.1, 1.1, 0.7))
            mb.add(p_cyl(0.03 * s, 0.0, 0.1 * s, n=4), T(c.x + sg * 0.05 * s, 0, c.z + 0.1 * s), color="iron_dark",
                   bone=("Left" if sg > 0 else "Right") + "UpperArm")
    if troll:
        rnd = random.Random(3)
        for i in range(7):  # mossy back lumps
            ball(mb, (rnd.uniform(-0.15, 0.15) * s, 0.12 * s, j["chest"].z + rnd.uniform(0, 0.2) * s), 0.06 * s,
                 mat="EV_Atlas", color="moss", bone="Chest", seg_=6, rings=4)
        mb.add(p_cyl(0.5 * r, 0.6 * r, 0.25 * r, n=6), T(0, hc.y + 0.1 * r, hc.z + 0.75 * r), color="moss", bone="Head")
    else:
        box(mb, (0, -0.12 * s, j["spine"].z), (0.12 * s, 0.03 * s, 0.1 * s), color="bone", bone="Spine")
        mb.add(p_cyl(0.6 * r, 0.0, 0.3 * r, n=6), T(0, hc.y + 0.2 * r, hc.z + 0.6 * r) @ R(-30, 0, 0), color="black",
               bone="Head")
    return mb


def build_skeleton_monster(p):
    j = joints(p)
    s = p["scale"]
    mb = MeshBuilder("Skeleton")
    c = "bone"
    seg(mb, j["hip"] - Vector((0, 0, 0.05 * s)), j["neck"], 0.025 * s, 0.025 * s, n=6, mat="EV_Atlas", color=c,
        bone="Spine")
    mb.add(p_taper(0.28 * s, 0.14 * s, 0.22 * s, 0.1 * s, 0.1 * s, z0=j["hip"].z - 0.08 * s), color=c, bone="Hips")
    for k in range(4):  # ribs
        z = j["chest"].z - 0.05 * s + k * 0.06 * s
        w = (0.30 - 0.02 * abs(k - 1.5)) * s
        mb.add(p_cyl(w / 2, w / 2, 0.03 * s, n=8, z0=z, cap0=False, cap1=False), S(1, 0.7, 1), color=c,
               bone="Chest" if k > 0 else "Spine")
    seg(mb, j["Leftshoulder"], j["Rightshoulder"], 0.025 * s, 0.025 * s, n=5, mat="EV_Atlas", color=c, bone="Chest")
    hc, r = head_center(p)
    ball(mb, hc, r, mat="EV_Atlas", color=c, bone="Head", seg_=8, rings=6, sq=(0.9, 1.0, 1.05))
    mb.add(p_taper(0.9 * r, 0.9 * r, 0.7 * r, 0.7 * r, 0.4 * r, z0=hc.z - 1.15 * r), T(0, -0.2 * r, 0), color=c,
           bone="Head")
    for sg in (1, -1):
        box(mb, (sg * 0.38 * r, hc.y - 0.85 * r, hc.z), (0.32 * r, 0.3 * r, 0.3 * r), color="black", bone="Head")
        box(mb, (sg * 0.38 * r, hc.y - 0.9 * r, hc.z), (0.12 * r, 0.3 * r, 0.12 * r), color="spirit", bone="Head")
    box(mb, (0, hc.y - 0.95 * r, hc.z - 0.35 * r), (0.15 * r, 0.12 * r, 0.2 * r), color="black", bone="Head")
    for side in ("Left", "Right"):
        for a, b, bone, rr in (("shoulder", "elbow", "UpperArm", 0.022), ("elbow", "wrist", "LowerArm", 0.02),
                               ("hipj", "knee", "UpperLeg", 0.028), ("knee", "ankle", "LowerLeg", 0.024)):
            seg(mb, j[side + a], j[side + b], rr * s, rr * 0.85 * s, n=5, mat="EV_Atlas", color=c, bone=side + bone)
            ball(mb, j[side + b], rr * 1.4 * s, mat="EV_Atlas", color=c, bone=side + bone, seg_=6, rings=4)
        box(mb, j[side + "wrist"].lerp(j[side + "hand_end"], 0.5), (0.08 * s, 0.06 * s, 0.02 * s), color=c,
            bone=side + "Hand")
        mb.add(p_taper(0.07 * s, 0.2 * s, 0.05 * s, 0.15 * s, 0.04 * s, z0=0), T(j[side + "ankle"].x, -0.06 * s, 0),
               color=c, bone=side + "Foot")
    # tattered loincloth + rusty helmet
    box(mb, (0, -0.08 * s, j["hip"].z - 0.15 * s), (0.2 * s, 0.02 * s, 0.25 * s), color="cloth_red", bone="Hips")
    dome = p_sphere(r * 1.1, r * 1.12, r * 1.12, 8, 6)
    v, f = dome
    mb.add((v, [fc for fc in f if sum(v[i][2] for i in fc) / len(fc) > 0.1 * r]), T(hc.x, hc.y, hc.z + 0.02 * s),
           color="iron_dark", bone="Head")
    return mb


def build_zombie(p):
    j = joints(p)
    s = p["scale"]
    mb = MeshBuilder("Zombie")
    _torso(mb, p, j, "cloth_brown", "skin_zombie", "cloth_black")
    _limbs(mb, p, j, "skin_zombie", "cloth_black", "skin_zombie", "skin_zombie", upper_arm="cloth_brown")
    hc, r = head_center(p)
    _face(mb, hc, r, "skin_zombie", eye="spirit", brow=None, nose=0.8, s=s)
    rnd = random.Random(5)
    for i in range(6):  # marsh weed + rags
        z = j["spine"].z + rnd.uniform(-0.1, 0.35) * s
        box(mb, (rnd.uniform(-0.15, 0.15) * s, -0.12 * s, z), (0.05 * s, 0.02 * s, 0.12 * s), color="marsh",
            bone="Chest" if z > j["chest"].z else "Spine", rot=R(0, rnd.uniform(-30, 30), 0))
    mb.add(p_cyl(0.7 * r, 0.75 * r, 0.3 * r, n=6), T(0, hc.y + 0.15 * r, hc.z + 0.55 * r), color="moss", bone="Head")
    return mb


def build_spirit(p):
    """Forest spirit: a floating bark-and-leaf figure; legs become a trailing wisp."""
    j = joints(p)
    s = p["scale"]
    mb = MeshBuilder("ForestSpirit")
    mb.add(p_taper(0.26 * s, 0.18 * s, 0.38 * s, 0.22 * s, j["neck"].z - j["spine"].z, z0=j["spine"].z), color="bark_pale",
           bone="Chest")
    mb.add(p_ico(0.08 * s, center=(0, -0.1 * s, j["chest"].z + 0.08 * s)), mat="EV_Glow", bone="Chest")
    # trailing wisp in place of legs
    for k, (a, b, w) in enumerate(((j["spine"].z, j["hip"].z - 0.1 * s, 0.3), (j["hip"].z - 0.1 * s, j["hip"].z - 0.5 * s, 0.2),
                                   (j["hip"].z - 0.5 * s, j["hip"].z - 0.8 * s, 0.1))):
        bone = ["Hips", "LeftUpperLeg", "LeftLowerLeg"][k]
        mb.add(p_cyl(w * s / 2, w * s / 3, a - b, n=7, z0=b), mat="EV_Glow", weights={"Hips": 0.5, bone: 0.5} if k else
               {"Hips": 1.0})
    for side, sg in (("Left", 1), ("Right", -1)):
        seg(mb, j[side + "shoulder"], j[side + "elbow"], 0.045 * s, 0.035 * s, n=5, mat="EV_Atlas", color="bark_pale",
            bone=side + "UpperArm")
        seg(mb, j[side + "elbow"], j[side + "hand_end"], 0.035 * s, 0.01 * s, n=5, mat="EV_Atlas", color="bark_pale",
            bone=side + "LowerArm")
        for k in range(3):  # twig fingers
            d = Vector((sg, -0.3 + 0.3 * k, 0.2 * (k - 1))).normalized()
            seg(mb, j[side + "wrist"], j[side + "wrist"] + d * 0.12 * s, 0.012 * s, 0.0, n=4, mat="EV_Atlas",
                color="bark_pale", bone=side + "Hand")
        ball(mb, j[side + "shoulder"] + Vector((0, 0, 0.06 * s)), 0.08 * s, mat="EV_Atlas", color="leaf_light",
             bone="Chest", seg_=6, rings=4)
    hc, r = head_center(p)
    ball(mb, hc, r, mat="EV_Atlas", color="bark_pale", bone="Head", seg_=8, rings=6, sq=(0.85, 0.9, 1.15))
    for sg in (1, -1):
        box(mb, (sg * 0.35 * r, hc.y - 0.82 * r, hc.z + 0.05 * r), (0.25 * r, 0.1 * r, 0.12 * r), mat="EV_Glow",
            bone="Head")
    rnd = random.Random(8)
    for i in range(9):  # leafy crown
        a = rnd.uniform(0, 2 * math.pi)
        c = hc + Vector((math.cos(a) * 0.6 * r, math.sin(a) * 0.6 * r, 0.8 * r + rnd.uniform(0, 0.4) * r))
        ball(mb, c, 0.45 * r, mat="EV_Atlas", color=rnd.choice(["leaf", "leaf_light", "flower_white"]), bone="Head",
             seg_=5, rings=3)
    return mb


def build_bandit(p):
    """Bandit = human body in dark clothes with hood and face scarf."""
    mb = humanoid.build_body(p, "Bandit", {"sleeves": "long"})
    am = humanoid.build_armour(p, "a_leather_cowl")
    hc, r = head_center(p)
    # merge cowl + scarf into the same mesh builder
    off = len(mb.verts)
    mb.verts.extend(am.verts)
    mb.weights.extend(am.weights)
    for idx, mi, uvs in am.faces:
        key = am.materials[mi]
        mb.faces.append(([i + off for i in idx], mb._mat_index(key), uvs))
    box(mb, (0, hc.y - 0.88 * r, hc.z - 0.5 * r), (1.6 * r, 0.5 * r, 0.6 * r), color="cloth_red", bone="Head")
    return mb


def build_warden(p):
    """The Hollow Warden: an ancient stone guardian with a glowing core."""
    j = joints(p)
    s = p["scale"]
    mb = MeshBuilder("HollowWarden")
    rnd = random.Random(11)

    def rock(c, rr, bone, col="ruin"):
        mb.add(p_ico(rr, center=tuple(c), jitter=0.18, seed=rnd.randint(0, 999)), color=col, bone=bone)

    mb.add(p_taper(0.3 * s, 0.22 * s, 0.46 * s, 0.26 * s, j["neck"].z - j["spine"].z, z0=j["spine"].z), color="ruin",
           bone="Chest")
    mb.add(p_ico(0.07 * s, center=(0, -0.13 * s, j["chest"].z + 0.06 * s)), mat="EV_Glow", bone="Chest")
    rock(j["hip"] + Vector((0, 0, 0.02 * s)), 0.15 * s, "Hips", "ruin_dark")
    for side, sg in (("Left", 1), ("Right", -1)):
        for a, b, bone, rr in (("shoulder", "elbow", "UpperArm", 0.06), ("elbow", "wrist", "LowerArm", 0.055),
                               ("hipj", "knee", "UpperLeg", 0.07), ("knee", "ankle", "LowerLeg", 0.06)):
            seg(mb, j[side + a], j[side + b], rr * s, rr * 0.85 * s, n=6, mat="EV_Atlas", color="stone_dark",
                bone=side + bone)
        seg(mb, j["hip"], j["spine"] + Vector((0, 0, 0.05 * s)), 0.11 * s, 0.13 * s, n=6, mat="EV_Atlas",
            color="stone_dark", bone="Spine")
        rock(j[side + "shoulder"] + Vector((sg * 0.04 * s, 0, 0.04 * s)), 0.11 * s, side + "UpperArm", "ruin_dark")
        rock(j[side + "shoulder"].lerp(j[side + "elbow"], 0.55), 0.08 * s, side + "UpperArm")
        rock(j[side + "elbow"].lerp(j[side + "wrist"], 0.5), 0.085 * s, side + "LowerArm")
        rock(j[side + "wrist"].lerp(j[side + "hand_end"], 0.6), 0.09 * s, side + "Hand", "ruin_dark")
        rock(j[side + "hipj"].lerp(j[side + "knee"], 0.5), 0.1 * s, side + "UpperLeg")
        rock(j[side + "knee"].lerp(j[side + "ankle"], 0.5), 0.09 * s, side + "LowerLeg")
        mb.add(p_taper(0.14 * s, 0.18 * s, 0.11 * s, 0.12 * s, 0.07 * s, z0=0), T(j[side + "ankle"].x, -0.03 * s, 0),
               color="ruin_dark", bone=side + "Foot")
        for k in range(3):  # glowing seams
            box(mb, j[side + "elbow"].lerp(j[side + "wrist"], 0.2 + 0.25 * k) + Vector((0, -0.07 * s, 0)),
                (0.03 * s, 0.01 * s, 0.03 * s), mat="EV_Glow", bone=side + "LowerArm")
    hc, r = head_center(p)
    mb.add(p_box(1.5 * r, 1.4 * r, 1.4 * r, center=tuple(hc)), color="ruin", bone="Head")
    box(mb, (0, hc.y - 0.71 * r, hc.z + 0.1 * r), (1.0 * r, 0.05 * r, 0.18 * r), mat="EV_Glow", bone="Head")
    for sg in (1, -1):
        mb.add(p_cyl(0.18 * r, 0.0, 0.9 * r, n=4), T(sg * 0.6 * r, hc.y, hc.z + 0.6 * r) @ R(0, sg * 25, 0), color="ruin_dark",
               bone="Head")
    for i in range(5):
        box(mb, (rnd.uniform(-0.12, 0.12) * s, 0.13 * s, j["chest"].z + rnd.uniform(0, 0.2) * s), (0.06 * s, 0.03 * s, 0.06 * s),
            color="moss", bone="Chest")
    return mb


HUMANOID_MONSTERS = {
    # model: (proportions, builder, weapon model or None, weapon tint, heavy animation)
    "enemy_goblin": ("goblin", build_goblin, "w_dagger", "bronze", False),
    "enemy_orc": ("orc", lambda p: build_orc(p, False), "w_axe", "iron", True),
    "enemy_troll": ("troll", lambda p: build_orc(p, True), "w_mace", "iron", True),
    "enemy_skeleton": ("male", build_skeleton_monster, "w_sword", "iron", False),
    "enemy_zombie": ("male", build_zombie, None, None, False),
    "enemy_bandit": ("male", build_bandit, "w_dagger", "steel", False),
    "enemy_forest_spirit": ("male", build_spirit, None, None, False),
    "boss_warden": ("warden", build_warden, None, None, True),
}
MONSTER_CLIPS = ["Idle", "Walk", "Run", "AttackMelee", "Cast", "Hurt", "Death"]

# ===========================================================================
# Quadrupeds
# ===========================================================================
QUADS = {
    # length scale, height, colours
    "enemy_rat": {"len": 0.55, "h": 0.22, "body": "rat", "belly": "rat_pink", "kind": "rat"},
    "enemy_wolf": {"len": 1.3, "h": 0.75, "body": "fur_grey", "belly": "fur_dark", "kind": "wolf"},
    "boss_mire_queen": {"len": 2.6, "h": 1.2, "body": "toad", "belly": "toad_belly", "kind": "toad"},
}


def quad_skeleton(q):
    L, H = q["len"], q["h"]
    toad = q["kind"] == "toad"
    hipz = H * (0.55 if toad else 0.85)
    shz = H * (0.7 if toad else 0.9)
    hy, sy = L * 0.32, -L * 0.28
    lw = L * (0.22 if toad else 0.1)
    b = [("Root", (0, 0, 0), (0, 0, H * 0.2), None),
         ("Hips", (0, hy, hipz), (0, 0, (hipz + shz) / 2), "Root"),
         ("Spine", (0, 0, (hipz + shz) / 2), (0, sy, shz), "Hips"),
         ("Neck", (0, sy, shz), (0, sy - L * 0.15, shz + H * (0.05 if toad else 0.18)), "Spine"),
         ("Head", (0, sy - L * 0.15, shz + H * (0.05 if toad else 0.18)), (0, sy - L * 0.38, shz + H * 0.1), "Neck"),
         ("Jaw", (0, sy - L * 0.18, shz - H * (0.02 if toad else 0.0)), (0, sy - L * 0.38, shz - H * 0.02), "Head"),
         ("Tail1", (0, hy + L * 0.05, hipz), (0, hy + L * 0.3, hipz - H * 0.1), "Hips"),
         ("Tail2", (0, hy + L * 0.3, hipz - H * 0.1), (0, hy + L * (0.7 if q["kind"] == "rat" else 0.5), hipz - H * 0.3),
          "Tail1")]
    for pre, y0, z0, par in (("Front", sy, shz, "Spine"), ("Back", hy, hipz, "Hips")):
        for side, sg in (("Left", 1), ("Right", -1)):
            x = sg * lw
            knee_z = z0 * 0.5
            if toad and pre == "Back":
                b += [(pre + side + "Upper", (x, y0, z0), (x * 1.6, y0 - L * 0.15, z0 * 0.45), par),
                      (pre + side + "Lower", (x * 1.6, y0 - L * 0.15, z0 * 0.45), (x * 1.7, y0 + L * 0.05, 0.06 * H),
                       pre + side + "Upper"),
                      (pre + side + "Foot", (x * 1.7, y0 + L * 0.05, 0.06 * H), (x * 1.9, y0 - L * 0.12, 0.02 * H),
                       pre + side + "Lower")]
            else:
                bend = 0.04 * L if pre == "Back" else -0.02 * L
                b += [(pre + side + "Upper", (x, y0, z0), (x * (1.3 if toad else 1), y0 + bend, knee_z), par),
                      (pre + side + "Lower", (x * (1.3 if toad else 1), y0 + bend, knee_z), (x * (1.4 if toad else 1), y0, 0.05 * H),
                       pre + side + "Upper"),
                      (pre + side + "Foot", (x * (1.4 if toad else 1), y0, 0.05 * H), (x * (1.5 if toad else 1), y0 - 0.06 * L,
                                                                                       0.01 * H), pre + side + "Lower")]
    return b


def build_quad_mesh(q, bones):
    bd = {n: (Vector(h), Vector(t)) for n, h, t, _ in bones}
    L, H = q["len"], q["h"]
    kind = q["kind"]
    mb = MeshBuilder({"rat": "GiantRat", "wolf": "Wolf", "toad": "MireQueen"}[kind])
    col, belly = q["body"], q["belly"]
    hh, ht = bd["Hips"]
    sh, st = bd["Spine"]
    if kind == "toad":
        ball(mb, (0, 0.05 * L, H * 0.55), L * 0.38, mat="EV_Atlas", color=col, weights={"Hips": 0.5, "Spine": 0.5},
             seg_=12, rings=8, sq=(1.05, 1.0, 0.62))
        ball(mb, (0, -0.05 * L, H * 0.42), L * 0.3, mat="EV_Atlas", color=belly, weights={"Hips": 0.4, "Spine": 0.6},
             seg_=10, rings=6, sq=(1.0, 1.05, 0.55))
        rnd = random.Random(2)
        for i in range(14):  # warts
            a = rnd.uniform(0, 2 * math.pi)
            ball(mb, (math.cos(a) * L * 0.3, math.sin(a) * L * 0.25 + 0.05 * L, H * 0.75 + rnd.uniform(-0.1, 0.1) * H),
                 L * 0.035, mat="EV_Atlas", color="moss", bone="Hips" if math.sin(a) > 0 else "Spine", seg_=5, rings=3)
    else:
        seg(mb, hh + Vector((0, 0.05 * L, 0)), st, H * 0.2, H * 0.22, n=8, mat="EV_Atlas", color=col, bone="Spine")
        ball(mb, hh, H * 0.22, mat="EV_Atlas", color=col, bone="Hips", seg_=8, rings=5, sq=(1, 1.3, 1))
        ball(mb, st, H * 0.24, mat="EV_Atlas", color=col, bone="Spine", seg_=8, rings=5, sq=(1, 1.2, 1.05))
        seg(mb, sh.lerp(st, 0.3) - Vector((0, 0, H * 0.12)), hh - Vector((0, 0, H * 0.1)), H * 0.12, H * 0.1, n=6,
            mat="EV_Atlas", color=belly, bone="Spine")
    nh, nt = bd["Neck"]
    hdh, hdt = bd["Head"]
    if kind != "toad":
        seg(mb, nh, nt, H * 0.17, H * 0.14, n=8, mat="EV_Atlas", color=col if kind == "rat" else "fur_dark", bone="Neck")
        ball(mb, hdh, H * 0.16, mat="EV_Atlas", color=col, bone="Head", seg_=8, rings=5)
        seg(mb, hdh, hdt, H * 0.12, H * 0.05, n=6, mat="EV_Atlas", color=col, bone="Head")
        jh, jt = bd["Jaw"]
        seg(mb, jh, jt + Vector((0, 0.01 * L, 0)), H * 0.07, H * 0.03, n=5, mat="EV_Atlas", color=belly, bone="Jaw")
        ball(mb, hdt, H * 0.04, mat="EV_Atlas", color="black", bone="Head", seg_=5, rings=3)  # nose
        for sg in (1, -1):
            ear_base = hdh + Vector((sg * H * 0.09, 0.0, H * 0.12))
            if kind == "rat":
                ball(mb, ear_base, H * 0.08, mat="EV_Atlas", color="rat_pink", bone="Head", seg_=6, rings=4, sq=(1, 0.4, 1))
            else:
                mb.add(p_cyl(H * 0.06, 0.0, H * 0.16, n=4), T(*ear_base) @ R(0, sg * 12, 0), color=col, bone="Head")
            box(mb, hdh.lerp(hdt, 0.3) + Vector((sg * H * 0.1, 0, H * 0.06)), (H * 0.03, H * 0.04, H * 0.03),
                color="spider_mark" if kind == "wolf" else "black", bone="Head")
        for k in range(3 if kind == "wolf" else 0):  # teeth
            box(mb, jh.lerp(jt, 0.5 + 0.15 * k) + Vector((0, 0, H * 0.05)), (H * 0.08, H * 0.01, H * 0.03), color="teeth",
                bone="Jaw")
    else:
        hc = hdh.lerp(hdt, 0.4)
        ball(mb, hc, L * 0.2, mat="EV_Atlas", color=col, bone="Head", seg_=10, rings=6, sq=(1.25, 1.0, 0.55))
        jh, jt = bd["Jaw"]
        mb.add(p_sphere(L * 0.22, L * 0.18, L * 0.06, 10, 4, center=tuple(jh.lerp(jt, 0.45) - Vector((0, 0, L * 0.02)))),
               color=belly, bone="Jaw")
        for sg in (1, -1):
            ball(mb, hc + Vector((sg * L * 0.13, -L * 0.02, L * 0.1)), L * 0.06, mat="EV_Atlas", color="flower_yellow",
                 bone="Head", seg_=6, rings=4)
            ball(mb, hc + Vector((sg * L * 0.13, -L * 0.07, L * 0.11)), L * 0.025, mat="EV_Atlas", color="black",
                 bone="Head", seg_=5, rings=3)
        # crown of reeds and bone
        for i in range(7):
            a = math.pi * (0.15 + 0.7 * i / 6)
            base = hc + Vector((math.cos(a) * L * 0.1, L * 0.04, L * 0.1))
            mb.add(p_cyl(L * 0.015, 0.0, L * (0.12 + 0.05 * (i % 2)), n=4), T(*base) @ R(0, (i - 3) * 8, 0),
                   color="gold" if i % 2 else "bone", bone="Head")
        mb.add(p_cyl(L * 0.12, L * 0.12, L * 0.03, n=10, z0=0), T(hc.x, hc.y + L * 0.04, hc.z + L * 0.09), color="gold",
               bone="Head")
    t1h, t1t = bd["Tail1"]
    t2h, t2t = bd["Tail2"]
    if kind == "rat":
        seg(mb, t1h, t1t, H * 0.06, H * 0.045, n=5, mat="EV_Atlas", color="rat_pink", bone="Tail1")
        seg(mb, t2h, t2t, H * 0.045, H * 0.01, n=5, mat="EV_Atlas", color="rat_pink", bone="Tail2")
    elif kind == "wolf":
        seg(mb, t1h, t1t, H * 0.07, H * 0.09, n=6, mat="EV_Atlas", color=col, bone="Tail1")
        seg(mb, t2h, t2t, H * 0.09, H * 0.02, n=6, mat="EV_Atlas", color="fur_dark", bone="Tail2")
        for i in range(5):  # mane tufts
            mb.add(p_cyl(H * 0.06, 0.0, H * 0.14, n=4), T(*(nh.lerp(st, i / 4) + Vector((0, 0, H * 0.15)))) @ R(30, 0, 0),
                   color="fur_dark", bone="Neck" if i < 2 else "Spine")
    for pre in ("Front", "Back"):
        for side in ("Left", "Right"):
            uh, ut = bd[pre + side + "Upper"]
            lh, lt = bd[pre + side + "Lower"]
            fh, ft = bd[pre + side + "Foot"]
            k = 1.6 if kind == "toad" else 1.0
            r0 = H * (0.1 if pre == "Back" else 0.08) * k
            seg(mb, uh, ut, r0, r0 * 0.6, n=6, mat="EV_Atlas", color=col, bone=pre + side + "Upper")
            seg(mb, lh, lt, r0 * 0.55, r0 * 0.4, n=6, mat="EV_Atlas", color=col if kind != "wolf" else "fur_dark",
                bone=pre + side + "Lower")
            seg(mb, fh - Vector((0, 0, H * 0.02)), ft, r0 * 0.5, r0 * 0.3, n=5, mat="EV_Atlas",
                color=belly if kind != "wolf" else "fur_dark", bone=pre + side + "Foot")
    return mb


def quad_clips(arm, q):
    L, H = q["len"], q["h"]
    kind = q["kind"]
    acts = []

    def pose(spine=0, neck=0, head=0, jaw=0, tail=(0, 0), fl=(0, 0, 0), fr=(0, 0, 0), bl=(0, 0, 0), br=(0, 0, 0),
             hips=(0, 0, 0), roll=0, tail_yaw=0):
        r = {"Hips": Ry(roll) @ Rx(-spine * 0.3), "Spine": Rx(spine), "Neck": Rx(neck), "Head": Rx(head), "Jaw": Rx(jaw),
             "Tail1": Rz(tail_yaw) @ Rx(tail[0]), "Tail2": Rx(tail[1])}
        for pre, side, l in (("Front", "Left", fl), ("Front", "Right", fr), ("Back", "Left", bl), ("Back", "Right", br)):
            r[pre + side + "Upper"] = Rx(-l[0])
            r[pre + side + "Lower"] = Rx(l[1])
            r[pre + side + "Foot"] = Rx(l[2])
        return r, {"Hips": hips}

    def mk(name, keys, loop=True, fps=30):
        k = [(1 + round(t * fps), *pose(**kw)) for t, kw in keys]
        acts.append(rig.make_action(arm, name, k, loop=loop))

    if kind == "toad":
        mk("Idle", [(0, {}), (0.8, dict(jaw=-4, hips=(0, 0, 0.02 * H), head=-3)), (1.6, {})])
        hop = dict(fl=(-20, 10, 0), fr=(-20, 10, 0), bl=(-40, -30, 30), br=(-40, -30, 30), hips=(0, -0.1 * L, 0.35 * H),
                   head=-10)
        mk("Walk", [(0, {}), (0.25, dict(bl=(10, 20, -10), br=(10, 20, -10), hips=(0, 0, -0.05 * H))), (0.5, hop),
                    (0.8, dict(fl=(20, 10, 0), fr=(20, 10, 0), hips=(0, 0, 0.05 * H))), (1.0, {})])
        mk("Run", [(0, {}), (0.2, hop), (0.4, {})])
        mk("AttackMelee", [(0, {}), (0.3, dict(head=-20, jaw=-35, spine=-8, bl=(10, 20, 0), br=(10, 20, 0))),
                           (0.5, dict(head=10, jaw=-45, spine=8, hips=(0, -0.2 * L, 0.1 * H))),
                           (0.65, dict(head=5, jaw=0, hips=(0, -0.15 * L, 0))), (1.1, {})], loop=False)
        mk("Cast", [(0, {}), (0.4, dict(head=-25, jaw=-40, hips=(0, 0, 0.12 * H))), (0.8, dict(head=-28, jaw=-42,
                                                                                               hips=(0, 0, 0.14 * H))),
                    (1.2, {})], loop=False)
    else:
        sw = 30 if kind == "wolf" else 35
        knee = 35
        # trot: diagonal pairs move together
        a = dict(fl=(sw, 10, 0), br=(sw, 10, -10), fr=(-sw, 5, 25), bl=(-sw, 5, 25), tail=(10, 5), head=4,
                 hips=(0, 0, -0.02 * H))
        b = dict(fl=(0, knee, 15), br=(0, knee, 20), fr=(0, 0, 0), bl=(0, 0, 0), tail=(14, 8), hips=(0, 0, 0.02 * H))
        c = dict(fr=(sw, 10, 0), bl=(sw, 10, -10), fl=(-sw, 5, 25), br=(-sw, 5, 25), tail=(10, 5), head=4,
                 hips=(0, 0, -0.02 * H))
        d = dict(fr=(0, knee, 15), bl=(0, knee, 20), fl=(0, 0, 0), br=(0, 0, 0), tail=(14, 8), hips=(0, 0, 0.02 * H))
        dur = 0.6 if kind == "wolf" else 0.4
        mk("Idle", [(0, dict(tail=(10, 5), tail_yaw=-10)), (1.0, dict(tail=(12, 6), tail_yaw=12, head=-6, neck=3,
                                                                       hips=(0, 0, -0.01 * H))),
                    (2.0, dict(tail=(10, 5), tail_yaw=-10))])
        mk("Walk", [(0, a), (dur / 4, b), (dur / 2, c), (3 * dur / 4, d), (dur, a)])
        ga = dict(fl=(45, 10, 0), fr=(35, 15, 0), bl=(-40, 5, 30), br=(-30, 5, 30), spine=-8, tail=(20, 10),
                  hips=(0, 0, 0.04 * H))
        gb = dict(fl=(-40, 10, 30), fr=(-30, 10, 30), bl=(45, 15, -10), br=(35, 20, -10), spine=8, tail=(10, 0),
                  hips=(0, 0, -0.02 * H))
        mk("Run", [(0, ga), (dur * 0.35, gb), (dur * 0.7, ga)])
        mk("AttackMelee", [(0, {}), (0.25, dict(spine=-10, neck=-15, head=-10, jaw=0, bl=(15, 30, -10), br=(15, 30, -10),
                                                hips=(0, 0.05 * L, -0.08 * H))),
                           (0.45, dict(spine=6, neck=10, head=12, jaw=-40, fl=(40, 0, 0), fr=(40, 0, 0), bl=(-20, 0, 20),
                                       br=(-20, 0, 20), hips=(0, -0.2 * L, 0.06 * H))),
                           (0.6, dict(spine=4, neck=8, head=10, jaw=0, fl=(20, 0, 0), fr=(20, 0, 0), hips=(0, -0.1 * L, 0))),
                           (0.9, {})], loop=False)
        mk("Cast", [(0, {}), (0.5, dict(neck=-35, head=-25, jaw=-30, spine=-6)), (1.0, {})], loop=False)  # howl
    mk("Hurt", [(0, {}), (0.12, dict(spine=-8, neck=-10, head=-8, hips=(0, 0.04 * L, 0))), (0.4, {})], loop=False)
    mk("Death", [(0, {}), (0.35, dict(roll=40, neck=10, fl=(20, 40, 0), fr=(20, 40, 0), hips=(0, 0, -0.3 * H))),
                 (0.7, dict(roll=85, neck=20, head=10, jaw=-20, fl=(30, 20, 0), fr=(20, 30, 0), bl=(10, 20, 0),
                            br=(20, 10, 0), tail=(-10, -10), hips=(0.0, 0, -0.55 * H))),
                 (1.0, dict(roll=88, neck=22, head=12, jaw=-22, fl=(30, 20, 0), fr=(20, 30, 0), bl=(10, 20, 0),
                            br=(20, 10, 0), tail=(-10, -10), hips=(0.0, 0, -0.56 * H)))], loop=False)
    return acts


# ===========================================================================
# Spider
# ===========================================================================
def spider_skeleton(size=1.0):
    s = size
    b = [("Root", (0, 0, 0), (0, 0, 0.1 * s), None),
         ("Body", (0, 0, 0.35 * s), (0, -0.25 * s, 0.35 * s), "Root"),
         ("Abdomen", (0, 0.05 * s, 0.38 * s), (0, 0.55 * s, 0.45 * s), "Body"),
         ("Head", (0, -0.25 * s, 0.35 * s), (0, -0.4 * s, 0.33 * s), "Body"),
         ("FangL", (0.05 * s, -0.38 * s, 0.3 * s), (0.04 * s, -0.45 * s, 0.2 * s), "Head"),
         ("FangR", (-0.05 * s, -0.38 * s, 0.3 * s), (-0.04 * s, -0.45 * s, 0.2 * s), "Head")]
    for i in range(4):
        y = (-0.2 + i * 0.12) * s
        for side, sg in (("L", 1), ("R", -1)):
            hip = Vector((sg * 0.12 * s, y, 0.35 * s))
            knee = Vector((sg * 0.45 * s, y + (i - 1.5) * 0.12 * s, 0.6 * s))
            foot = Vector((sg * 0.75 * s, y + (i - 1.5) * 0.25 * s, 0.0))
            b += [("Leg%d%sUpper" % (i, side), hip, knee, "Body"), ("Leg%d%sLower" % (i, side), knee, foot,
                                                                   "Leg%d%sUpper" % (i, side))]
    return b


def build_spider_mesh(bones, size=1.0):
    s = size
    bd = {n: (Vector(h), Vector(t)) for n, h, t, _ in bones}
    mb = MeshBuilder("Webspinner")
    ball(mb, (0, -0.08 * s, 0.36 * s), 0.17 * s, mat="EV_Atlas", color="spider", bone="Body", seg_=8, rings=5,
         sq=(1, 1.2, 0.7))
    ball(mb, (0, 0.38 * s, 0.48 * s), 0.3 * s, mat="EV_Atlas", color="spider", bone="Abdomen", seg_=10, rings=6,
         sq=(0.9, 1.1, 0.85))
    for k in range(3):  # red hourglass-ish markings
        box(mb, (0, 0.3 * s + k * 0.12 * s, 0.73 * s - abs(k - 1) * 0.04 * s), (0.12 * s * (1 - 0.3 * abs(k - 1)), 0.08 * s,
                                                                              0.03 * s), color="spider_mark", bone="Abdomen")
    ball(mb, (0, -0.32 * s, 0.35 * s), 0.1 * s, mat="EV_Atlas", color="spider", bone="Head", seg_=8, rings=5)
    for i in range(4):  # eyes
        ex = (-0.045 + 0.03 * i) * s
        box(mb, (ex, -0.41 * s, 0.39 * s + (0.02 * s if i in (1, 2) else 0)), (0.02 * s, 0.02 * s, 0.02 * s),
            color="spider_mark", bone="Head")
    for side in ("L", "R"):
        h, t = bd["Fang" + side]
        seg(mb, h, t, 0.025 * s, 0.0, n=4, mat="EV_Atlas", color="bone_dark", bone="Fang" + side)
    for i in range(4):
        for side in ("L", "R"):
            uh, ut = bd["Leg%d%sUpper" % (i, side)]
            lh, lt = bd["Leg%d%sLower" % (i, side)]
            seg(mb, uh, ut, 0.03 * s, 0.025 * s, n=5, mat="EV_Atlas", color="spider", bone="Leg%d%sUpper" % (i, side))
            ball(mb, ut, 0.032 * s, mat="EV_Atlas", color="spider_mark", bone="Leg%d%sLower" % (i, side), seg_=5, rings=3)
            seg(mb, lh, lt, 0.025 * s, 0.006 * s, n=5, mat="EV_Atlas", color="spider", bone="Leg%d%sLower" % (i, side))
    return mb


def spider_clips(arm):
    acts = []

    def pose(lift=None, body=(0, 0, 0), head=0, fang=0, abdomen=0, curl=0, rear=0):
        r = {"Body": Rx(-rear), "Abdomen": Rx(abdomen + rear * 0.5), "Head": Rx(head),
             "FangL": Rx(fang), "FangR": Rx(fang)}
        lift = lift or {}
        for i in range(4):
            for side, sg in (("L", 1), ("R", -1)):
                key = "%d%s" % (i, side)
                up, swing = lift.get(key, (0, 0))
                # legs fan out along X: raise = rotate about Y, swing = rotate about Z
                r["Leg%sUpper" % key] = Rz(swing * sg) @ Ry(-sg * (up + curl))
                r["Leg%sLower" % key] = Ry(-sg * curl * 1.4)
        return r, {"Body": body}

    def mk(name, keys, loop=True):
        acts.append(rig.make_action(arm, name, [(1 + round(t * 30), *pose(**kw)) for t, kw in keys], loop=loop))

    groupA = ["0L", "1R", "2L", "3R"]
    groupB = ["0R", "1L", "2R", "3L"]

    def step(g_up, g_down, phase):
        d = {}
        for k in g_up:
            d[k] = (18, 15 * phase)
        for k in g_down:
            d[k] = (0, -15 * phase)
        return d

    mk("Idle", [(0, dict()), (1.0, dict(abdomen=-4, body=(0, 0, -0.01), fang=8)), (2.0, dict())])
    mk("Walk", [(0, dict(lift=step(groupA, groupB, 1))), (0.25, dict(lift=step([], groupA + groupB, 0))),
                (0.5, dict(lift=step(groupB, groupA, 1))), (0.75, dict(lift=step([], groupA + groupB, 0))),
                (1.0, dict(lift=step(groupA, groupB, 1)))])
    mk("Run", [(0, dict(lift=step(groupA, groupB, 1.3))), (0.2, dict(lift=step(groupB, groupA, 1.3))),
               (0.4, dict(lift=step(groupA, groupB, 1.3)))])
    mk("AttackMelee", [(0, dict()), (0.3, dict(rear=25, lift={"0L": (40, 20), "0R": (40, 20)}, fang=25)),
                       (0.5, dict(rear=-5, head=10, lift={"0L": (5, -10), "0R": (5, -10)}, fang=-10,
                                  body=(0, -0.1, -0.03))), (0.9, dict())], loop=False)
    mk("Cast", [(0, dict()), (0.4, dict(rear=-10, abdomen=-30, body=(0, 0, 0.05))), (0.9, dict())], loop=False)
    mk("Hurt", [(0, dict()), (0.12, dict(rear=8, body=(0, 0.05, 0.03))), (0.4, dict())], loop=False)
    mk("Death", [(0, dict()), (0.4, dict(curl=30, body=(0, 0, -0.12))), (0.9, dict(curl=55, body=(0, 0, -0.22),
                                                                                 abdomen=-8, head=12))], loop=False)
    return acts


QUAD_MODELS = list(QUADS.keys())
SPIDER_MODELS = ["enemy_spider"]
