"""
Rigid held equipment: weapons, tools, shields, ammunition.

Convention: the grip point is the origin, the "business end" points along +Z, the cutting
edge faces +/-Y. In Unity the model is parented to the <Side>HandSocket transform.
Metal parts use EV_Metal and wooden bow/staff parts EV_Wood so one model serves every tier
(tinted per item via the item's "tint" key).
"""
import math

from mathutils import Vector

from .core import MeshBuilder, p_box, p_taper, p_cyl, p_sphere, p_wedge, p_ico, seg_matrix, T, R, S


def blade(mb, w, length, thick, z0, tip=0.12, mat="EV_Metal"):
    """Double-edged flat blade along +Z: a box with a pointed tip, edges along Y."""
    body = length - tip
    v = [(-thick / 2, -w / 2, z0), (thick / 2, -w / 2, z0), (thick / 2, w / 2, z0), (-thick / 2, w / 2, z0),
         (-thick / 2, -w / 2 * 0.9, z0 + body), (thick / 2, -w / 2 * 0.9, z0 + body),
         (thick / 2, w / 2 * 0.9, z0 + body), (-thick / 2, w / 2 * 0.9, z0 + body), (0, 0, z0 + length)]
    # fuller ridge: a slightly raised centre line
    f = [(0, 3, 2, 1), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (4, 5, 8), (5, 6, 8), (6, 7, 8),
         (7, 4, 8)]
    mb.add((v, f), mat=mat)
    mb.add(p_box(thick * 1.6, w * 0.12, body * 0.8, center=(0, 0, z0 + body * 0.45)), mat=mat)


def handle(mb, length, r=0.018, z0=-0.06, color="leather", pommel=True, mat="EV_Atlas"):
    mb.add(p_cyl(r, r, length, n=6, z0=z0), mat=mat, color=color)
    for k in range(3):
        mb.add(p_cyl(r * 1.15, r * 1.15, 0.012, n=6, z0=z0 + length * (0.2 + 0.3 * k)), mat="EV_Atlas", color="leather_dark")
    if pommel:
        mb.add(p_sphere(r * 1.8, r * 1.8, r * 1.6, 6, 4, center=(0, 0, z0 - r)), mat="EV_Metal")


def build(model):
    mb = MeshBuilder(model)
    if model == "w_sword":
        handle(mb, 0.16)
        mb.add(p_box(0.04, 0.2, 0.035, center=(0, 0, 0.115)), mat="EV_Metal")
        blade(mb, 0.07, 0.72, 0.014, 0.13)
    elif model == "w_dagger":
        handle(mb, 0.11, r=0.015)
        mb.add(p_box(0.03, 0.12, 0.025, center=(0, 0, 0.06)), mat="EV_Metal")
        blade(mb, 0.05, 0.3, 0.012, 0.07, tip=0.08)
    elif model == "w_greatsword":
        handle(mb, 0.34, r=0.02, z0=-0.2)
        mb.add(p_box(0.05, 0.34, 0.045, center=(0, 0, 0.15)), mat="EV_Metal")
        for sg in (1, -1):
            mb.add(p_sphere(0.03, 0.03, 0.03, 6, 4, center=(0, sg * 0.17, 0.15)), mat="EV_Metal")
        blade(mb, 0.11, 1.15, 0.02, 0.17, tip=0.18)
    elif model in ("w_axe", "t_hatchet"):
        big = model == "w_axe"
        L = 0.75 if big else 0.5
        mb.add(p_cyl(0.02, 0.022, L, n=6, z0=-0.12), mat="EV_Atlas", color="wood")
        hz = L - 0.2
        w = 0.24 if big else 0.16
        # head: wedge widening to the edge (+Y) with a back spike
        v = [(-0.02, -0.03, hz - 0.04), (0.02, -0.03, hz - 0.04), (0.02, -0.03, hz + 0.06), (-0.02, -0.03, hz + 0.06),
             (-0.004, -w, hz - 0.1), (0.004, -w, hz - 0.1), (0.004, -w, hz + 0.12), (-0.004, -w, hz + 0.12)]
        f = [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (0, 3, 2, 1), (4, 5, 6, 7)]
        mb.add((v, f), mat="EV_Metal")
        mb.add(p_box(0.05, 0.07, 0.1, center=(0, 0, hz + 0.01)), mat="EV_Metal")
        if big:
            mb.add(p_cyl(0.025, 0.0, 0.09, n=4), T(0, 0.03, hz + 0.01) @ R(-90, 0, 0), mat="EV_Metal")
    elif model == "w_mace":
        mb.add(p_cyl(0.02, 0.022, 0.5, n=6, z0=-0.1), mat="EV_Atlas", color="wood_dark")
        mb.add(p_sphere(0.07, 0.07, 0.08, 8, 5, center=(0, 0, 0.44)), mat="EV_Metal")
        for i in range(8):
            a = i * math.pi / 4
            d = Vector((math.cos(a), math.sin(a), 0))
            mb.add(p_cyl(0.022, 0.0, 0.06, n=4), _dir_matrix(Vector((0, 0, 0.44)) + d * 0.06, d), mat="EV_Metal")
        mb.add(p_cyl(0.022, 0.0, 0.07, n=4, z0=0.5), mat="EV_Metal")
    elif model == "w_staff":
        mb.add(p_cyl(0.022, 0.026, 1.45, n=6, z0=-0.55), mat="EV_Wood")
        for k, z in enumerate((0.75, 0.82)):
            mb.add(p_cyl(0.03, 0.03, 0.025, n=6, z0=z), mat="EV_Atlas", color="brass")
        # claw holding an orb
        for i in range(3):
            a = i * 2 * math.pi / 3
            mb.add(p_cyl(0.012, 0.006, 0.12, n=4), _dir_matrix(Vector((0, 0, 0.88)),
                                                         Vector((math.cos(a) * 0.5, math.sin(a) * 0.5, 1))),
                   mat="EV_Wood")
        mb.add(p_ico(0.055, center=(0, 0, 0.98)), mat="EV_Glow")
    elif model in ("w_shortbow", "w_longbow"):
        L = 0.55 if model == "w_shortbow" else 0.8
        # limbs curve back toward -Y; string straight along Z on the +Y side
        pts = []
        n = 6
        for i in range(-n, n + 1):
            t = i / n
            pts.append(Vector((0, -0.12 * (1 - t * t) * (L / 0.55) ** 0.3 + 0.04 * t * t * t * t, t * L)))
        for a, b in zip(pts, pts[1:]):
            rr = 0.02 * (1 - 0.5 * abs((a.z + b.z) / 2) / L)
            mb.add(p_cyl(rr, rr, (b - a).length, n=5), seg_matrix(a, b), mat="EV_Wood")
        mb.add(p_cyl(0.026, 0.026, 0.12, n=6, z0=-0.06), T(0, pts[n].y, 0), mat="EV_Atlas", color="leather")
        mb.add(p_cyl(0.003, 0.003, (pts[-1] - pts[0]).length, n=3), seg_matrix(pts[0], pts[-1]), mat="EV_Atlas",
               color="cloth_white")
    elif model == "w_crossbow":
        mb.add(p_box(0.05, 0.06, 0.55, center=(0, 0, 0.15)), mat="EV_Wood")
        mb.add(p_box(0.05, 0.08, 0.16, center=(0, 0.06, -0.05)), mat="EV_Wood")
        for sg in (1, -1):
            mb.add(p_cyl(0.015, 0.01, 0.28, n=5), _dir_matrix(Vector((0, 0, 0.36)), Vector((sg, 0.25, 0))),
                   mat="EV_Metal")
        mb.add(p_cyl(0.003, 0.003, 0.56, n=3), seg_matrix((-0.27, 0.07, 0.36), (0.27, 0.07, 0.36)), mat="EV_Atlas",
               color="cloth_white")
    elif model == "t_pickaxe":
        mb.add(p_cyl(0.02, 0.022, 0.7, n=6, z0=-0.12), mat="EV_Atlas", color="wood")
        hz = 0.52
        for sg in (1, -1):
            pts = [Vector((0, 0, hz)), Vector((0, sg * 0.14, hz - 0.03)), Vector((0, sg * 0.25, hz - 0.1))]
            for a, b, r0, r1 in ((pts[0], pts[1], 0.03, 0.022), (pts[1], pts[2], 0.022, 0.0)):
                mb.add(p_cyl(r0, r1, (b - a).length, n=5), seg_matrix(a, b), mat="EV_Metal")
        mb.add(p_box(0.06, 0.07, 0.07, center=(0, 0, hz)), mat="EV_Metal")
    elif model == "t_rod":
        mb.add(p_cyl(0.018, 0.006, 1.6, n=5, z0=-0.3), mat="EV_Atlas", color="wood_light")
        mb.add(p_cyl(0.04, 0.04, 0.03, n=8), T(0.04, 0, 0.1) @ R(0, 90, 0), mat="EV_Atlas", color="iron")
        mb.add(p_cyl(0.002, 0.002, 0.5, n=3), seg_matrix((0, 0, 1.3), (0, -0.15, 0.85)), mat="EV_Atlas",
               color="cloth_white")
    elif model == "t_net":
        mb.add(p_cyl(0.016, 0.016, 0.8, n=5, z0=-0.2), mat="EV_Atlas", color="wood_light")
        mb.add(p_cyl(0.2, 0.2, 0.02, n=10, cap0=False, cap1=False), T(0, -0.2, 0.62) @ R(90, 0, 0), mat="EV_Atlas",
               color="wood_light")
        mb.add(p_cyl(0.19, 0.05, 0.25, n=10, cap0=False), T(0, -0.2, 0.62) @ R(90, 0, 0), mat="EV_Atlas", color="rope",
               flip=False)
    elif model == "t_harpoon":
        mb.add(p_cyl(0.018, 0.018, 1.3, n=5, z0=-0.4), mat="EV_Atlas", color="wood")
        mb.add(p_cyl(0.035, 0.0, 0.2, n=4, z0=0.9), mat="EV_Metal")
        for sg in (1, -1):
            mb.add(p_cyl(0.012, 0.0, 0.08, n=3), _dir_matrix(Vector((0, 0, 0.95)), Vector((0, sg * 0.5, -1))),
                   mat="EV_Metal")
    elif model == "a_shield":
        # kite shield, face toward -Y in the hand socket space... socket forward is the blade axis (+Z), so the
        # shield face points along +X/-X; we model the face toward +X (outward for the left hand).
        prof = [(0, 0.24), (0.18, 0.24), (0.28, 0.2), (0.3, 0.05), (0.22, -0.2), (0.0, -0.42)]
        v, f = [], []
        pts = [Vector((0, -x, z)) for x, z in prof] + [Vector((0, x, z)) for x, z in reversed(prof[1:-1])]
        n = len(pts)
        for p in pts:
            v.append((0.03, p.y, p.z))
        for p in pts:
            v.append((-0.0, p.y * 0.96, p.z * 0.96))
        f.append(tuple(range(n)))
        f.append(tuple(reversed(range(n, 2 * n))))
        for i in range(n):
            j = (i + 1) % n
            f.append((i, n + i, n + j, j))
        mb.add((v, f), T(0.03, 0, 0.0), mat="EV_Metal", flip=True)
        mb.add(p_box(0.03, 0.5, 0.6, center=(0.0, 0, -0.03)), S(1, 0.9, 0.9), mat="EV_Wood")
        mb.add(p_sphere(0.012, 0.05, 0.05, 6, 4, center=(0.07, 0, 0.02)), mat="EV_Metal")
        mb.add(p_box(0.012, 0.04, 0.5, center=(0.065, 0, -0.06)), mat="EV_Atlas", color="cloth_red")
        mb.add(p_box(0.012, 0.36, 0.04, center=(0.066, 0, 0.08)), mat="EV_Atlas", color="cloth_red")
        mb.add(p_box(0.04, 0.12, 0.03, center=(-0.03, 0, 0)), mat="EV_Atlas", color="leather_dark")
    elif model == "arrow":
        mb.add(p_cyl(0.006, 0.006, 0.62, n=4, z0=-0.3), mat="EV_Atlas", color="wood_light")
        mb.add(p_cyl(0.016, 0.0, 0.06, n=4, z0=0.32), mat="EV_Metal")
        for i in range(3):
            a = i * 2 * math.pi / 3
            mb.add(p_box(0.002, 0.03, 0.09, center=(0, 0.018, -0.24)), R(0, 0, math.degrees(a)), mat="EV_Atlas",
                   color="flower_white")
    elif model == "bolt":
        mb.add(p_cyl(0.008, 0.008, 0.36, n=4, z0=-0.18), mat="EV_Atlas", color="wood")
        mb.add(p_cyl(0.016, 0.0, 0.05, n=4, z0=0.18), mat="EV_Metal")
        for i in range(2):
            mb.add(p_box(0.002, 0.026, 0.05, center=(0, 0.014, -0.14)), R(0, 0, 180 * i), mat="EV_Atlas",
                   color="cloth_white")
    else:
        raise ValueError(model)
    return mb


def _dir_matrix(origin, direction):
    d = Vector(direction).normalized()
    q = Vector((0, 0, 1)).rotation_difference(d)
    return T(*origin) @ q.to_matrix().to_4x4()


EQUIPMENT_MODELS = ["w_sword", "w_dagger", "w_greatsword", "w_axe", "w_mace", "w_staff", "w_shortbow", "w_longbow",
                    "w_crossbow", "t_pickaxe", "t_hatchet", "t_rod", "t_net", "t_harpoon", "a_shield", "arrow", "bolt"]
