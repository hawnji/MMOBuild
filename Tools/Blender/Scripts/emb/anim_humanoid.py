"""
Procedural keyframe animation for the humanoid skeleton.

Poses are described with a handful of intuitive parameters (arm lowered/swing/elbow bend,
leg swing/knee bend, spine lean, twist ...) that are turned into armature-space rotations and
converted to bone-local quaternions by rig.make_action. Every clip is authored at 30 fps.
"""
import math

from mathutils import Matrix

from . import rig

FPS = 30


def Rx(d):
    return Matrix.Rotation(math.radians(d), 3, "X")


def Ry(d):
    return Matrix.Rotation(math.radians(d), 3, "Y")


def Rz(d):
    return Matrix.Rotation(math.radians(d), 3, "Z")


REST_ARM = (78, 0, 10)


def pose(spine=0.0, chest=0.0, twist=0.0, neck=0.0, head=0.0, head_turn=0.0, tilt=0.0,
         la=REST_ARM, ra=REST_ARM, lhand=0.0, rhand=0.0,
         ll=(0, 0, 0), rl=(0, 0, 0), hips=(0, 0, 0), hip_rot=(0, 0, 0), scale=1.0):
    """
    la/ra: (lowered, swing_forward, elbow_bend[, roll[, yaw]]) degrees. lowered=0 is T-pose, 90 is straight down.
    ll/rl: (swing_forward, knee_bend, foot_pitch[, spread]).
    hips: (dx, dy, dz) metres (scaled), hip_rot: degrees about armature X/Y/Z.
    """
    r = {}
    r["Hips"] = Rz(hip_rot[2]) @ Ry(hip_rot[1]) @ Rx(hip_rot[0])
    r["Spine"] = Rz(twist * 0.5) @ Ry(tilt * 0.5) @ Rx(spine)
    r["Chest"] = Rz(twist * 0.5) @ Ry(tilt * 0.5) @ Rx(chest)
    r["Neck"] = Rx(neck)
    r["Head"] = Rz(head_turn) @ Rx(head)
    for side, sg, a, hand in (("Left", 1, la, lhand), ("Right", -1, ra, rhand)):
        lowered, swing, bend = a[0], a[1], a[2]
        roll = a[3] if len(a) > 3 else 0.0
        yaw = a[4] if len(a) > 4 else 0.0
        r[side + "UpperArm"] = Rz(yaw) @ Rx(-swing) @ Ry(sg * lowered) @ Rx(sg * roll)
        r[side + "LowerArm"] = Rz(-sg * bend)
        r[side + "Hand"] = Rz(-sg * hand)
    for side, sg, l in (("Left", 1, ll), ("Right", -1, rl)):
        swing, knee, foot = l[0], l[1], l[2]
        spread = l[3] if len(l) > 3 else 0.0
        r[side + "UpperLeg"] = Ry(-sg * spread) @ Rx(-swing)
        r[side + "LowerLeg"] = Rx(knee)
        r[side + "Foot"] = Rx(foot)
    locs = {"Hips": (hips[0] * scale, hips[1] * scale, hips[2] * scale)}
    return r, locs


def clip(arm, name, keys, scale, loop=True):
    """keys: list of (seconds, pose kwargs)."""
    k = []
    for t, kw in keys:
        rots, locs = pose(scale=scale, **kw)
        k.append((1 + round(t * FPS), rots, locs))
    return rig.make_action(arm, name, k, fps=FPS, loop=loop)


def walk_cycle(stride, arm_swing, lean, bob, dur, knee=40, run=False):
    """Four contact/passing keys mirrored -> smooth loop."""
    def ph(t, sgn):
        # sgn=+1: left leg forward
        return {"spine": lean, "chest": lean * 0.3, "twist": -sgn * (6 if run else 4),
                "ll": (sgn * stride, 6 if sgn > 0 else (knee * 0.4), -sgn * 6),
                "rl": (-sgn * stride, 6 if sgn < 0 else (knee * 0.4), sgn * 6),
                "la": (REST_ARM[0] - (8 if run else 0), -sgn * arm_swing, 25 if run else 15),
                "ra": (REST_ARM[0] - (8 if run else 0), sgn * arm_swing, 25 if run else 15),
                "hips": (0, 0, -bob)}

    def passing(sgn):
        return {"spine": lean, "chest": lean * 0.3,
                "ll": (5 if sgn > 0 else -5, knee if sgn > 0 else 5, 10 if sgn > 0 else 0),
                "rl": (5 if sgn < 0 else -5, knee if sgn < 0 else 5, 10 if sgn < 0 else 0),
                "la": (REST_ARM[0] - (8 if run else 0), 0, 25 if run else 12),
                "ra": (REST_ARM[0] - (8 if run else 0), 0, 25 if run else 12),
                "hips": (0, 0, bob * 0.5)}

    q = dur / 4
    return [(0, ph(0, 1)), (q, passing(-1)), (2 * q, ph(0, -1)), (3 * q, passing(1)), (dur, ph(0, 1))]


def humanoid_clips(arm, scale=1.0, heavy=False):
    """Creates every gameplay clip. heavy=True slows and widens motion for big monsters."""
    acts = []
    sl = 1.25 if heavy else 1.0
    A = acts.append
    idle_a = dict(chest=0, la=REST_ARM, ra=REST_ARM)
    A(clip(arm, "Idle", [(0, dict(chest=0)), (1.0 * sl, dict(chest=-2.5, spine=0.5, head=-1, hips=(0, 0, -0.006),
                                                                la=(REST_ARM[0] - 3, 2, 14), ra=(REST_ARM[0] - 3, 2, 14))),
                         (2.0 * sl, dict(chest=0))], scale))
    A(clip(arm, "Walk", walk_cycle(24, 22, 3, 0.02, 1.0 * sl), scale))
    A(clip(arm, "Run", walk_cycle(38, 42, 12, 0.045, 0.66 * sl, knee=75, run=True), scale))
    A(clip(arm, "Turn", [(0, idle_a), (0.15, dict(ll=(10, 25, 5), hip_rot=(0, 0, 8), twist=6)),
                         (0.3, dict(rl=(10, 25, 5), hip_rot=(0, 0, 4), twist=3)), (0.45, idle_a)], scale, loop=False))
    # --- combat
    A(clip(arm, "AttackMelee", [
        (0, idle_a),
        (0.25 * sl, dict(twist=-25, chest=-5, ra=(40, -30, 95, -40), la=(70, 20, 25), rl=(-10, 10, 0), ll=(15, 10, 0))),
        (0.42 * sl, dict(twist=28, spine=12, chest=6, ra=(55, 95, 10, 20), la=(80, -15, 20), rl=(-15, 8, 0),
                         ll=(22, 18, -5), hips=(0, 0, -0.03))),
        (0.6 * sl, dict(twist=18, spine=8, ra=(75, 60, 15), la=(80, -10, 15), ll=(15, 12, 0), hips=(0, 0, -0.02))),
        (0.9 * sl, idle_a)], scale, loop=False))
    A(clip(arm, "AttackRanged", [
        (0, idle_a),
        (0.2, dict(twist=-35, la=(88, 88, 5, 90, 30), ra=(85, 85, 60, 0, 30), head_turn=30)),
        (0.55, dict(twist=-40, la=(88, 90, 0, 90, 38), ra=(88, 92, 150, 0, 38), head_turn=35, chest=-3)),
        (0.62, dict(twist=-40, la=(88, 90, 0, 90, 38), ra=(70, 70, 90, 0, 38), head_turn=35)),
        (1.0, idle_a)], scale, loop=False))
    A(clip(arm, "Cast", [
        (0, idle_a),
        (0.3, dict(chest=-8, la=(30, 40, 60), ra=(30, 40, 60), head=-8)),
        (0.5, dict(chest=10, spine=6, la=(80, 85, 5), ra=(80, 85, 5), head=4, ll=(12, 10, 0))),
        (0.7, dict(chest=8, spine=4, la=(80, 80, 10), ra=(80, 80, 10))),
        (1.0, idle_a)], scale, loop=False))
    # --- skilling (loops)
    A(clip(arm, "Mine", [
        (0, dict(spine=6, twist=-10, ra=(85, 150, 50), la=(85, 140, 70), ll=(12, 10, 0), rl=(-6, 5, 0))),
        (0.45, dict(spine=26, twist=6, ra=(80, 45, 10), la=(80, 40, 30), ll=(15, 20, 0), rl=(-6, 10, 0), hips=(0, 0, -0.04))),
        (0.6, dict(spine=24, twist=4, ra=(80, 50, 12), la=(80, 46, 32), ll=(15, 20, 0), rl=(-6, 10, 0), hips=(0, 0, -0.04))),
        (1.2, dict(spine=6, twist=-10, ra=(85, 150, 50), la=(85, 140, 70), ll=(12, 10, 0), rl=(-6, 5, 0)))], scale))
    A(clip(arm, "Chop", [
        (0, dict(twist=-45, spine=5, ra=(40, 60, 70, -30), la=(55, 70, 70), ll=(10, 8, 0))),
        (0.4, dict(twist=30, spine=10, ra=(80, 80, 10, 0), la=(78, 75, 20), ll=(12, 12, 0), hips=(0, 0, -0.02))),
        (0.55, dict(twist=28, spine=10, ra=(80, 78, 12, 0), la=(78, 72, 22), ll=(12, 12, 0), hips=(0, 0, -0.02))),
        (1.1, dict(twist=-45, spine=5, ra=(40, 60, 70, -30), la=(55, 70, 70), ll=(10, 8, 0)))], scale))
    A(clip(arm, "Fish", [
        (0, dict(spine=4, ra=(80, 60, 35), la=(80, 45, 55), head=10)),
        (1.0, dict(spine=6, ra=(80, 66, 30), la=(80, 50, 50), head=12, hips=(0, 0, -0.005))),
        (1.6, dict(spine=3, ra=(80, 52, 45), la=(80, 40, 60), head=8)),
        (2.4, dict(spine=4, ra=(80, 60, 35), la=(80, 45, 55), head=10))], scale))
    A(clip(arm, "Smith", [
        (0, dict(spine=14, ra=(70, 95, 90), la=(80, 50, 60), head=18)),
        (0.25, dict(spine=18, ra=(80, 40, 30), la=(80, 50, 60), head=22, hips=(0, 0, -0.01))),
        (0.32, dict(spine=18, ra=(80, 42, 32), la=(80, 50, 60), head=22, hips=(0, 0, -0.01))),
        (0.7, dict(spine=14, ra=(70, 95, 90), la=(80, 50, 60), head=18))], scale))
    A(clip(arm, "Cook", [
        (0, dict(spine=10, ra=(80, 45, 70), la=(80, 30, 60), head=15)),
        (0.6, dict(spine=12, ra=(78, 55, 50, 15), la=(80, 30, 62), head=18, twist=5)),
        (1.2, dict(spine=10, ra=(80, 45, 70), la=(80, 30, 60), head=15))], scale))
    A(clip(arm, "Eat", [
        (0, idle_a), (0.35, dict(ra=(85, 35, 140), head=6)), (0.6, dict(ra=(85, 38, 145), head=8)),
        (0.75, dict(ra=(85, 33, 138), head=6)), (1.2, idle_a)], scale, loop=False))
    A(clip(arm, "Drink", [
        (0, idle_a), (0.35, dict(ra=(85, 45, 140), head=-5)), (0.7, dict(ra=(80, 70, 135), head=-25, chest=-6)),
        (1.0, dict(ra=(80, 68, 135), head=-22, chest=-5)), (1.4, idle_a)], scale, loop=False))
    A(clip(arm, "Hurt", [
        (0, idle_a), (0.1, dict(spine=-12, chest=-8, head=-15, la=(60, -15, 30), ra=(60, -15, 30), hips=(0, 0.04, -0.02))),
        (0.4, idle_a)], scale, loop=False))
    A(clip(arm, "Death", [
        (0, idle_a),
        (0.25, dict(spine=-10, head=-20, la=(50, -20, 20), ra=(50, -20, 20), ll=(10, 30, 0), rl=(0, 35, 0),
                    hips=(0, 0.05, -0.15))),
        (0.6, dict(hip_rot=(-55, 0, 5), spine=-10, head=-10, la=(20, 0, 10), ra=(30, -10, 10), ll=(-15, 40, 10),
                   rl=(-20, 50, 0), hips=(0, 0.3, -0.55))),
        (0.9, dict(hip_rot=(-88, 0, 8), spine=0, head=5, la=(10, 0, 5), ra=(20, 0, 15), ll=(-5, 12, 20), rl=(-8, 18, 10),
                   hips=(0, 0.55, -0.8))),
        (1.2, dict(hip_rot=(-90, 0, 8), spine=0, head=8, la=(5, 0, 5), ra=(15, 0, 15), ll=(-2, 6, 20), rl=(-6, 12, 10),
                   hips=(0, 0.58, -0.83)))], scale, loop=False))
    A(clip(arm, "Craft", [
        (0, dict(spine=8, head=22, ra=(82, 40, 95), la=(82, 40, 95))),
        (0.5, dict(spine=9, head=24, ra=(80, 48, 85, 10), la=(82, 38, 98))),
        (1.0, dict(spine=8, head=22, ra=(82, 40, 95), la=(82, 40, 95)))], scale))
    A(clip(arm, "Pray", [
        (0, idle_a),
        (0.6, dict(head=25, spine=5, ra=(85, 35, 110, 40), la=(85, 35, 110, -40), ll=(80, 80, 0), rl=(0, 95, 0),
                   hips=(0, 0, -0.45))),
        (2.4, dict(head=28, spine=6, ra=(85, 36, 112, 40), la=(85, 36, 112, -40), ll=(80, 80, 0), rl=(0, 95, 0),
                   hips=(0, 0, -0.46)))], scale, loop=False))
    A(clip(arm, "Farm", [
        (0, dict(spine=32, head=10, ra=(85, 60, 30), la=(85, 70, 35), ll=(15, 15, 0), rl=(-5, 10, 0), hips=(0, 0, -0.04))),
        (0.5, dict(spine=38, head=12, ra=(85, 30, 20), la=(85, 40, 25), ll=(15, 18, 0), rl=(-5, 12, 0), hips=(0, 0, -0.05))),
        (1.0, dict(spine=32, head=10, ra=(85, 60, 30), la=(85, 70, 35), ll=(15, 15, 0), rl=(-5, 10, 0), hips=(0, 0, -0.04)))],
        scale))
    A(clip(arm, "Climb", [
        (0, dict(head=-15, la=(-20, 170, 30), ra=(10, 120, 80), ll=(50, 80, 10), rl=(5, 20, 0))),
        (0.5, dict(head=-15, la=(10, 120, 80), ra=(-20, 170, 30), ll=(5, 20, 0), rl=(50, 80, 10), hips=(0, 0, 0.05))),
        (1.0, dict(head=-15, la=(-20, 170, 30), ra=(10, 120, 80), ll=(50, 80, 10), rl=(5, 20, 0)))], scale))
    A(clip(arm, "Pickpocket", [
        (0, idle_a),
        (0.3, dict(spine=18, head=-5, ra=(80, 55, 20), la=(70, -10, 30), ll=(20, 30, 0), rl=(-5, 25, 0), hips=(0, 0, -0.08))),
        (0.6, dict(spine=20, head=-5, ra=(82, 62, 30, 30), la=(70, -10, 30), ll=(20, 32, 0), rl=(-5, 27, 0),
                   hips=(0, 0, -0.09))),
        (1.0, idle_a)], scale, loop=False))
    A(clip(arm, "Firemaking", [
        (0, dict(spine=30, head=20, ra=(85, 50, 40), la=(85, 45, 45), ll=(80, 80, 0), rl=(0, 95, 0), hips=(0, 0, -0.45))),
        (0.25, dict(spine=32, head=22, ra=(85, 35, 30), la=(85, 50, 45), ll=(80, 80, 0), rl=(0, 95, 0), hips=(0, 0, -0.45))),
        (0.5, dict(spine=30, head=20, ra=(85, 50, 40), la=(85, 45, 45), ll=(80, 80, 0), rl=(0, 95, 0), hips=(0, 0, -0.45)))],
        scale))
    A(clip(arm, "Interact", [(0, idle_a), (0.25, dict(spine=10, ra=(80, 60, 20))), (0.5, idle_a)], scale, loop=False))
    A(clip(arm, "Wave", [(0, idle_a), (0.25, dict(ra=(-30, 20, 100, 0), head_turn=-5)),
                         (0.5, dict(ra=(-30, 20, 70, 0))), (0.75, dict(ra=(-30, 20, 100, 0))), (1.0, idle_a)],
              scale, loop=False))
    return acts


HUMANOID_CLIPS = ["Idle", "Walk", "Run", "Turn", "AttackMelee", "AttackRanged", "Cast", "Mine", "Chop", "Fish", "Smith",
                  "Cook", "Eat", "Drink", "Hurt", "Death", "Craft", "Pray", "Farm", "Climb", "Pickpocket", "Firemaking",
                  "Interact", "Wave"]
