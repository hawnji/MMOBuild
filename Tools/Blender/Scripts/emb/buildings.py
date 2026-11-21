"""
Modular building kit + assembly of every building in world.json.

Kit pieces (exported individually to Assets/Art/Buildings/Kit) are written as functions that
append to a MeshBuilder, so the same code assembles complete buildings that exactly match
the server's collision footprint (perimeter tiles are walls, door tile is open, interior walkable).

Authoring happens in *Unity world axes* (x east, y up, z north) through U(); the FBX export
then converts to Unity space, so a building FBX can be placed at its footprint centre with
no rotation. Each building exports two objects: "Shell" (walls/floor/furniture) and "Roof",
so the client can fade the roof when the local player is inside.
"""
import math
import random

from mathutils import Vector, Matrix

from .core import MeshBuilder, p_box, p_taper, p_cyl, p_sphere, p_wedge, p_ico, seg_matrix, T, R, S

WALL_H = 3.0
WALL_T = 0.45


def U(x, y, z):
    """Unity (x east, y up, z north) -> Blender coordinates (Unity = (-bx, bz, -by))."""
    return Vector((-x, -z, y))


def UT(x, y, z):
    return Matrix.Translation(U(x, y, z))


def ubox(mb, cx, cy, cz, sx, sy, sz, color, mat="EV_Atlas", rot_y=0.0):
    """Axis-aligned box in Unity coordinates (size x, y(height), z). rot_y in degrees about Unity up."""
    m = UT(cx, cy, cz) @ Matrix.Rotation(math.radians(-rot_y), 4, "Z")
    mb.add(p_box(sx, sz, sy), m, mat=mat, color=color)


STYLES = {
    "stone": {"wall": "stone_light", "wall2": "stone", "trim": "stone_dark", "floor": "stone", "frame": "wood_dark",
              "roof": "roof_slate", "plinth": "stone_dark"},
    "wood": {"wall": "wood_light", "wall2": "wood", "trim": "wood_dark", "floor": "wood", "frame": "wood_dark",
             "roof": "roof_red", "plinth": "stone"},
}


# ---------------------------------------------------------------------------
# Kit pieces
# ---------------------------------------------------------------------------
def wall_segment(mb, x0, z0, x1, z1, style, kind="plain", h=WALL_H, seed=0):
    """A 1 m wall piece between two tile-centre points (axis aligned). kind: plain/window/door/broken."""
    st = STYLES[style]
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    along_x = abs(x1 - x0) > abs(z1 - z0)
    L = max(abs(x1 - x0), abs(z1 - z0))
    sx, sz = (L, WALL_T) if along_x else (WALL_T, L)
    if kind == "door":
        # lintel above a 1.0 x 2.3 opening + frame posts
        ubox(mb, cx, 2.3 + (h - 2.3) / 2, cz, sx, h - 2.3, sz, st["wall"])
        for o in (-0.5, 0.5):
            px, pz = (cx + o * L * 0.95, cz) if along_x else (cx, cz + o * L * 0.95)
            ubox(mb, px, 1.15, pz, 0.12 if along_x else WALL_T + 0.1, 2.3, WALL_T + 0.1 if along_x else 0.12, st["frame"])
        ubox(mb, cx, 2.35, cz, sx, 0.12, sz + 0.1, st["frame"])
        return
    if kind == "broken":
        rnd = random.Random(seed)
        hh = rnd.uniform(0.6, 2.0)
        ubox(mb, cx, hh / 2, cz, sx, hh, sz, st["wall2"])
        return
    if kind == "window":
        ubox(mb, cx, 0.5, cz, sx, 1.0, sz, st["wall"])
        ubox(mb, cx, 2.25 + (h - 2.0) / 2 - 0.25, cz, sx, h - 2.0, sz, st["wall"])
        for o in (-0.5, 0.5):
            px, pz = (cx + o * L * 0.8, cz) if along_x else (cx, cz + o * L * 0.8)
            ubox(mb, px, 1.5, pz, 0.2 * L if along_x else sz, 1.0, sz if along_x else 0.2 * L, st["wall"])
        ubox(mb, cx, 1.5, cz, sx * 0.62, 1.0, sz * 0.25, "glass")
        ubox(mb, cx, 1.5, cz, 0.06 if along_x else sz * 0.5, 1.0, sz * 0.5 if along_x else 0.06, st["frame"])
        ubox(mb, cx, 0.97, cz, sx * 0.75, 0.08, sz + 0.15, st["trim"])
        return
    ubox(mb, cx, h / 2, cz, sx, h, sz, st["wall"])
    if style == "wood":  # horizontal planking lines
        for k in range(1, 5):
            ubox(mb, cx, k * h / 5, cz, sx, 0.05, sz + 0.04, st["wall2"])


def corner_post(mb, x, z, style, h=WALL_H):
    st = STYLES[style]
    if style == "wood":
        ubox(mb, x, h / 2, z, 0.55, h + 0.1, 0.55, st["trim"])
    else:
        ubox(mb, x, h / 2, z, 0.6, h + 0.1, 0.6, st["wall2"])
        for k in range(3):
            ubox(mb, x, 0.3 + k * 1.1, z, 0.66, 0.35, 0.66, st["trim"])


def floor_tile(mb, x, z, style, y=0.02):
    st = STYLES[style]
    ubox(mb, x, y - 0.05, z, 1.0, 0.1, 1.0, st["floor"])


def plinth(mb, cx, cz, w, d, style):
    """Foundation that extends below ground so buildings sit cleanly on sloped terrain."""
    ubox(mb, cx, -0.6, cz, w + 0.3, 1.3, d + 0.3, STYLES[style]["plinth"])


def roof_gable(mb, cx, cz, w, d, h0, color, along_x=True, overhang=0.45, pitch_h=None):
    """Gable roof along the longer axis; slope panels + ridge + gable end triangles."""
    L = (w if along_x else d) + overhang * 2
    span = (d if along_x else w) + overhang * 2
    rise = pitch_h or span * 0.42
    half = span / 2
    slope_len = math.hypot(half, rise)
    ang = math.degrees(math.atan2(rise, half))
    for sg in (1, -1):
        # panel centred halfway up the slope
        oy = h0 + rise / 2
        if along_x:
            m = UT(cx, oy, cz + sg * half / 2) @ Matrix.Rotation(math.radians(sg * ang), 4, "X")
            mb.add(p_box(L, slope_len + 0.1, 0.12), m, color=color)
        else:
            m = UT(cx + sg * half / 2, oy, cz) @ Matrix.Rotation(math.radians(-sg * ang), 4, "Y")
            mb.add(p_box(slope_len + 0.1, L, 0.12), m, color=color)
        # shingle rows
        for k in range(1, 4):
            t = k / 4
            yy = h0 + rise * (1 - t) + 0.07
            off = half * t
            if along_x:
                m = UT(cx, yy, cz + sg * off) @ Matrix.Rotation(math.radians(sg * ang), 4, "X")
                mb.add(p_box(L + 0.02, 0.06, 0.06), m, color="roof_slate" if color != "roof_slate" else "stone_dark")
            else:
                m = UT(cx + sg * off, yy, cz) @ Matrix.Rotation(math.radians(-sg * ang), 4, "Y")
                mb.add(p_box(0.06, L + 0.02, 0.06), m, color="roof_slate" if color != "roof_slate" else "stone_dark")
    # ridge beam
    ubox(mb, cx, h0 + rise + 0.02, cz, L + 0.1 if along_x else 0.22, 0.2, 0.22 if along_x else L + 0.1, "wood_dark")
    return rise


def gable_ends(mb, cx, cz, w, d, h0, rise, style, along_x=True):
    st = STYLES[style]
    span = d if along_x else w
    for sg in (1, -1):
        v = [(-span / 2, 0, 0), (span / 2, 0, 0), (0, 0, rise)]
        verts = []
        for (a, _, c) in v:
            for t in (-WALL_T / 2, WALL_T / 2):
                if along_x:
                    verts.append(U(cx + sg * (w / 2 - 0.5) + t, h0 + c, cz + a))
                else:
                    verts.append(U(cx + a, h0 + c, cz + sg * (d / 2 - 0.5) + t))
        f = [(0, 2, 4), (1, 5, 3), (0, 1, 3, 2), (2, 3, 5, 4), (4, 5, 1, 0)]
        mb.add(([tuple(p) for p in verts], f), color=st["wall2"] if style == "stone" else st["wall"])


def roof_flat(mb, cx, cz, w, d, h0, style):
    st = STYLES[style]
    ubox(mb, cx, h0 + 0.1, cz, w + 0.2, 0.2, d + 0.2, st["trim"])
    for sg in (1, -1):  # parapet with crenels
        ubox(mb, cx, h0 + 0.45, cz + sg * (d / 2), w + 0.2, 0.5, 0.3, st["wall2"])
        ubox(mb, cx + sg * (w / 2), h0 + 0.45, cz, 0.3, 0.5, d + 0.2, st["wall2"])
    # chimney
    ubox(mb, cx + w * 0.25, h0 + 1.0, cz + d * 0.2, 0.8, 1.6, 0.8, "stone_dark")


def roof_cone(mb, cx, cz, w, h0, color):
    m = UT(cx, h0, cz)
    mb.add(p_cyl(w * 0.62, 0.0, w * 0.9, n=8, phase=math.pi / 8), m, color=color)
    mb.add(p_cyl(0.05, 0.05, 0.8, n=4, z0=w * 0.9 - 0.1), m, color="wood_dark")
    mb.add(p_box(0.03, 0.4, 0.25, center=(0, 0.2, w * 0.9 + 0.55)), m, color="cloth_red")


# ---------------------------------------------------------------------------
# Furniture (interior dressing, kept against walls so the walkable interior stays clear)
# ---------------------------------------------------------------------------
def furniture(mb, kind, x0, z0, w, d, door, style):
    """Coordinates are tile-space relative to the building's footprint centre."""
    hx, hz = w / 2 - 1.0, d / 2 - 1.0  # inner face of the walls (tile centres of interior ring)
    back = {"S": (0, 1), "N": (0, -1), "E": (-1, 0), "W": (1, 0)}[door]
    bx, bz = back[0] * (hx - 0.1), back[1] * (hz - 0.1)
    along_x = back[0] == 0
    span = (w - 3) if along_x else (d - 3)

    def counter(cx, cz, length):
        sx, sz = (length, 0.5) if along_x else (0.5, length)
        ubox(mb, cx, 0.5, cz, sx, 1.0, sz, "wood_dark")
        ubox(mb, cx, 1.02, cz, sx + 0.1, 0.06, sz + 0.1, "wood_light")

    def shelf(cx, cz, length):
        sx, sz = (length, 0.3) if along_x else (0.3, length)
        ubox(mb, cx, 1.1, cz, sx, 2.2, sz, "wood")
        rnd = random.Random(int(cx * 7 + cz * 13))
        for lvl in range(3):
            for k in range(int(length * 2)):
                t = -length / 2 + 0.25 + k * 0.5
                px, pz = (cx + t, cz) if along_x else (cx, cz + t)
                ubox(mb, px, 0.5 + lvl * 0.65, pz, 0.18, 0.22, 0.18,
                     rnd.choice(["potion_red", "potion_blue", "cloth_red", "paper", "brass", "cloth_green", "bread"]))

    if kind in ("shop", "bank"):
        shelf(bx + back[0] * 0.25, bz + back[1] * 0.25, span * 0.8)
        counter(bx - back[0] * 1.0, bz - back[1] * 1.0, span * 0.5)
        if kind == "bank":
            ubox(mb, bx - back[0] * 1.0 + (1.5 if along_x else 0), 1.3, bz - back[1] * 1.0 + (0 if along_x else 1.5), 0.4, 0.4,
                 0.4, "gold")
    elif kind == "inn":
        counter(bx - back[0] * 0.6, bz - back[1] * 0.6, span * 0.6)
        for k in range(3):  # barrels behind bar
            t = -span * 0.3 + k * span * 0.3
            px, pz = (bx + t, bz + back[1] * 0.2) if along_x else (bx + back[0] * 0.2, bz + t)
            mb.add(p_cyl(0.3, 0.3, 0.8, n=8), UT(px, 0, pz), color="wood")
        for sgx in (-1, 1):  # two small tables near the side walls
            tx = sgx * (hx - 0.4) if along_x else 0
            tz = 0 if along_x else sgx * (hz - 0.4)
            mb.add(p_cyl(0.4, 0.4, 0.06, n=8, z0=0.72), UT(tx, 0, tz), color="wood_light")
            mb.add(p_cyl(0.06, 0.06, 0.72, n=5), UT(tx, 0, tz), color="wood_dark")
    elif kind == "temple":
        for k in range(-1, 2):  # banners on the back wall
            t = k * span * 0.3
            px, pz = (bx + t, bz + back[1] * 0.2) if along_x else (bx + back[0] * 0.2, bz + t)
            ubox(mb, px, 1.8, pz, 0.6 if along_x else 0.05, 1.6, 0.05 if along_x else 0.6, "cloth_red")
        for k in range(4):  # candle stands
            px, pz = ((-hx + 0.3) * (1 if k % 2 else -1), (k - 1.5) * d * 0.15) if along_x else ((k - 1.5) * w * 0.15,
                                                                                               (-hz + 0.3) * (1 if k % 2 else -1))
            mb.add(p_cyl(0.05, 0.05, 1.2, n=4), UT(px, 0, pz), color="brass")
            mb.add(p_ico(0.07, center=(0, 0, 1.25)), UT(px, 0, pz), mat="EV_Glow")
    elif kind == "warehouse":
        rnd = random.Random(x0 * 31 + z0)
        for k in range(int(span)):
            t = -span / 2 + 0.5 + k
            px, pz = (bx + t, bz) if along_x else (bx, bz + t)
            hgt = rnd.choice([1, 1, 2])
            for lvl in range(hgt):
                ubox(mb, px, 0.35 + lvl * 0.7, pz, 0.7, 0.7, 0.7, "wood_light", rot_y=rnd.uniform(-10, 10))
    elif kind == "house":
        sx, sz = (1.8, 0.9) if along_x else (0.9, 1.8)
        ubox(mb, bx - (0.6 if along_x else 0), 0.3, bz - (0 if along_x else 0.6), sx, 0.5, sz, "wood")  # bed
        ubox(mb, bx - (0.6 if along_x else 0), 0.6, bz - (0 if along_x else 0.6), sx * 0.95, 0.15, sz * 0.95, "cloth_red")
        mb.add(p_cyl(0.35, 0.35, 0.05, n=8, z0=0.7), UT(-bx * 0.6, 0, -bz * 0.6 + (0.01 if along_x else 0)),
               color="wood_light")
        mb.add(p_cyl(0.06, 0.06, 0.7, n=5), UT(-bx * 0.6, 0, -bz * 0.6), color="wood_dark")
    elif kind == "blacksmith":
        # hearth + tool rack (the anvil and furnace are separate interactive stations)
        ubox(mb, bx, 0.6, bz, 1.4 if along_x else 0.8, 1.2, 0.8 if along_x else 1.4, "stone_dark")
        ubox(mb, bx, 1.21, bz, 1.0 if along_x else 0.5, 0.05, 0.5 if along_x else 1.0, "ember", mat="EV_Atlas")
        for k in range(4):
            t = 1.4 + k * 0.3
            px, pz = (bx + t, bz) if along_x else (bx, bz + t)
            ubox(mb, px, 1.3, pz, 0.05, 0.9, 0.05, "iron")


# ---------------------------------------------------------------------------
# Building assembly
# ---------------------------------------------------------------------------
def door_local(b):
    """Door tile centre relative to the footprint centre (Unity x/z)."""
    w, d, door = b["w"], b["d"], b["door"]
    if door == "S":
        dx, dz = w // 2, 0
    elif door == "N":
        dx, dz = w // 2, d - 1
    elif door == "W":
        dx, dz = 0, d // 2
    else:
        dx, dz = w - 1, d // 2
    return dx + 0.5 - w / 2, dz + 0.5 - d / 2


def build_building(b):
    """Returns (shell MeshBuilder, roof MeshBuilder) in footprint-centred Unity coordinates."""
    w, d, style, kind = b["w"], b["d"], b["style"], b["kind"]
    ruin = b["roof"] == "broken"
    shell = MeshBuilder("Shell")
    roof = MeshBuilder("Roof")
    rnd = random.Random(b["id"])
    plinth(shell, 0, 0, w, d, style)
    for ix in range(1, w - 1):
        for iz in range(1, d - 1):
            floor_tile(shell, ix + 0.5 - w / 2, iz + 0.5 - d / 2, style)
    ddx, ddz = door_local(b)
    hx, hz = w / 2 - 0.5, d / 2 - 0.5  # wall centre lines (perimeter tile centres)
    h = WALL_H + (0.6 if kind in ("temple", "bank", "inn") else 0) + (2.5 if kind == "tower" else 0)
    # walls: one kit segment per perimeter tile edge
    for side in ("S", "N", "W", "E"):
        if side in ("S", "N"):
            z = -hz if side == "S" else hz
            cells = [(ix + 0.5 - w / 2, z) for ix in range(1, w - 1)]
        else:
            x = -hx if side == "W" else hx
            cells = [(x, iz + 0.5 - d / 2) for iz in range(1, d - 1)]
        n = len(cells)
        for k, (cx, cz) in enumerate(cells):
            is_door = abs(cx - ddx) < 0.01 and abs(cz - ddz) < 0.01
            if ruin:
                kind_s = "door" if is_door else ("broken" if rnd.random() < 0.55 else "plain")
                if kind_s == "plain":
                    kind_s = "broken"
            else:
                kind_s = "door" if is_door else ("window" if (k % 3 == 1 and 0 < k < n - 1 and kind != "warehouse")
                                                 else "plain")
            if side in ("S", "N"):
                wall_segment(shell, cx - 0.5, cz, cx + 0.5, cz, style, kind_s, h=h, seed=b["id"] + k)
            else:
                wall_segment(shell, cx, cz - 0.5, cx, cz + 0.5, style, kind_s, h=h, seed=b["id"] + k)
    for cx, cz in ((-hx, -hz), (hx, -hz), (-hx, hz), (hx, hz)):
        if ruin:
            ubox(shell, cx, 1.0, cz, 0.5, 2.0, 0.5, STYLES[style]["trim"])
        else:
            corner_post(shell, cx, cz, style, h=h)
    # step / porch at the door
    ox, oz = {"S": (0, -1), "N": (0, 1), "W": (-1, 0), "E": (1, 0)}[b["door"]]
    ubox(shell, ddx + ox * 0.9, 0.05, ddz + oz * 0.9, 1.4 if oz else 0.8, 0.2, 0.8 if oz else 1.4, STYLES[style]["plinth"])
    if kind == "shop" or kind == "inn":  # hanging sign
        sx, sz = ddx + ox * 0.6 + (0.9 if oz else 0), ddz + oz * 0.6 + (0 if oz else 0.9)
        ubox(shell, sx, 2.6, sz, 0.08 if oz else 0.6, 0.08, 0.6 if oz else 0.08, "wood_dark")
        ubox(shell, sx + ox * 0.3, 2.2, sz + oz * 0.3, 0.05 if oz == 0 and ox else 0.6, 0.45, 0.05 if oz else 0.6,
             "paper" if kind == "shop" else "wood_red")
    if not ruin:
        furniture(shell, kind, b["x"], b["z"], w, d, b["door"], style)
    else:
        for k in range(3):  # fallen beams + rubble
            ubox(shell, rnd.uniform(-w / 3, w / 3), 0.15, rnd.uniform(-d / 3, d / 3), 0.2, 0.2, rnd.uniform(1.5, 3), "wood_dark",
                 rot_y=rnd.uniform(0, 180))
    # roofs
    along_x = w >= d
    if b["roof"] == "gable":
        rise = roof_gable(roof, 0, 0, w - 0.5, d - 0.5, h, STYLES[style]["roof"], along_x=along_x)
        gable_ends(shell, 0, 0, w, d, h, rise * (d - 1) / (d + 0.4) if along_x else rise * (w - 1) / (w + 0.4), style, along_x)
        if kind in ("inn", "house", "blacksmith", "bank"):
            cx_, cz_ = (w * 0.25, d * 0.15) if along_x else (w * 0.15, d * 0.25)
            ubox(roof, cx_, h + rise * 0.7, cz_, 0.6, rise * 1.2, 0.6, "stone_dark")
    elif b["roof"] == "flat":
        roof_flat(roof, 0, 0, w, d, h, style)
    elif b["roof"] == "cone":
        roof_cone(roof, 0, 0, max(w, d), h, STYLES[style]["roof"])
        ubox(roof, 0, h + 0.1, 0, w + 0.3, 0.2, d + 0.3, STYLES[style]["trim"])
    if kind == "temple" and not ruin:  # bell tower on the ridge
        ubox(roof, 0, h + (d if along_x else w) * 0.45, 0, 1.0, 1.6, 1.0, STYLES[style]["wall"])
        roof.add(p_cyl(0.85, 0.0, 1.2, n=4, phase=math.pi / 4), UT(0, h + (d if along_x else w) * 0.45 + 0.8, 0),
                 color=STYLES[style]["roof"])
    return shell, roof


def door_model(style):
    mb = MeshBuilder("door_" + style)
    st = STYLES[style]
    # hinge at local origin, door extends along +x (Unity) 1 m wide, 2.3 tall
    ubox(mb, 0.47, 1.12, 0, 0.92, 2.24, 0.1, "wood" if style == "wood" else "wood_dark")
    for y in (0.4, 1.8):
        ubox(mb, 0.47, y, -0.06, 0.94, 0.12, 0.04, "iron_dark")
    ubox(mb, 0.82, 1.1, -0.08, 0.06, 0.06, 0.06, "brass")
    return mb


# kit catalogue exported as standalone pieces
def kit_piece(name):
    mb = MeshBuilder(name)
    parts = name.split("_")
    style = parts[-1]
    if name.startswith("wall_plain"):
        wall_segment(mb, -0.5, 0, 0.5, 0, style, "plain")
    elif name.startswith("wall_window"):
        wall_segment(mb, -0.5, 0, 0.5, 0, style, "window")
    elif name.startswith("wall_door"):
        wall_segment(mb, -0.5, 0, 0.5, 0, style, "door")
    elif name.startswith("wall_broken"):
        wall_segment(mb, -0.5, 0, 0.5, 0, style, "broken", seed=3)
    elif name.startswith("corner"):
        corner_post(mb, 0, 0, style)
    elif name.startswith("floor"):
        floor_tile(mb, 0, 0, style)
    elif name.startswith("roof_gable"):
        roof_gable(mb, 0, 0, 1.0, 4.0, 0.0, STYLES[style]["roof"], along_x=True, overhang=0.0)
    elif name.startswith("city_wall"):
        ubox(mb, 0, 2.0, 0, 1.02, 4.0, 1.2, "stone")
        ubox(mb, 0, 4.1, 0, 1.02, 0.2, 1.5, "stone_dark")
        ubox(mb, 0.25, 4.5, 0.55, 0.45, 0.6, 0.35, "stone")
        ubox(mb, 0.25, 4.5, -0.55, 0.45, 0.6, 0.35, "stone")
        ubox(mb, 0, 0.25, 0, 1.1, 0.5, 1.4, "stone_dark")
    elif name.startswith("ruin_wall"):
        ubox(mb, 0, 1.0, 0, 1.02, 2.0, 0.9, "ruin")
        ubox(mb, 0.2, 2.2, 0, 0.6, 0.4, 0.9, "ruin_dark")
        ubox(mb, -0.3, 0.1, 0.5, 0.4, 0.2, 0.3, "stone_moss")
    elif name.startswith("cave_rock"):
        mb.add(p_ico(0.9, center=(0, 0, 0.9), jitter=0.25, seed=5, squash=(0.8, 0.8, 1.4)), color="stone_dark")
        mb.add(p_ico(0.6, center=(0.3, 0.2, 2.0), jitter=0.25, seed=6), color="stone")
    return mb


KIT = ["wall_plain_stone", "wall_window_stone", "wall_door_stone", "wall_broken_stone", "corner_stone", "floor_stone",
       "roof_gable_stone", "wall_plain_wood", "wall_window_wood", "wall_door_wood", "wall_broken_wood", "corner_wood",
       "floor_wood", "roof_gable_wood", "city_wall_stone", "ruin_wall_stone", "cave_rock_stone"]
