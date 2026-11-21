"""Headless preview / icon rendering (Cycles CPU) and contact sheets."""
import math
import os

import bpy
import numpy as np
from mathutils import Vector

from . import core

PREVIEW_DIR = os.path.join(core.REPO, "Tools", "Blender", "Previews")


def setup_render(res=256, samples=12, transparent=True, engine="CYCLES"):
    sc = bpy.context.scene
    sc.render.engine = engine
    if engine == "CYCLES":
        sc.cycles.device = "CPU"
        sc.cycles.samples = samples
        sc.cycles.use_denoising = False
        sc.cycles.max_bounces = 3
    sc.render.resolution_x = res
    sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = transparent
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.view_settings.view_transform = "Standard"
    if sc.world is None:
        sc.world = bpy.data.worlds.new("World")
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.55, 0.6, 0.68, 1)
    bg.inputs[1].default_value = 0.7


def ensure_lights():
    if "EV_Sun" not in bpy.data.objects:
        sun = bpy.data.lights.new("EV_Sun", "SUN")
        sun.energy = 3.2
        sun.angle = math.radians(8)
        o = bpy.data.objects.new("EV_Sun", sun)
        o.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
        bpy.context.scene.collection.objects.link(o)
    if "EV_Cam" not in bpy.data.objects:
        cam = bpy.data.cameras.new("EV_Cam")
        o = bpy.data.objects.new("EV_Cam", cam)
        bpy.context.scene.collection.objects.link(o)
        bpy.context.scene.camera = o
    return bpy.data.objects["EV_Cam"]


def frame_objects(objs, yaw=-35.0, pitch=28.0, margin=1.15, ortho=True):
    """Points the preview camera at the world bounding box of objs (isometric-ish view from the front-left)."""
    cam = ensure_lights()
    deps = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in objs:
        if o.type != "MESH":
            continue
        ev = o.evaluated_get(deps)
        me = ev.to_mesh()
        mw = o.matrix_world
        pts.extend(mw @ v.co for v in me.vertices)
        ev.to_mesh_clear()
    if not pts:
        return cam
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    c = (mn + mx) / 2
    radius = max((mx - mn).length / 2, 0.05)
    yawr, pr = math.radians(yaw), math.radians(pitch)
    d = Vector((math.sin(yawr) * math.cos(pr), -math.cos(yawr) * math.cos(pr), math.sin(pr)))
    cam.location = c + d * radius * 6
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam.data.clip_end = radius * 20
    if ortho:
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = radius * 2 * margin
    else:
        cam.data.type = "PERSP"
        cam.data.lens = 50
    return cam


def render(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def contact_sheet(paths, out, cols=6, cell=192, labels=None):
    """Combines rendered PNGs into one sheet (loaded/saved via Blender image API)."""
    if not paths:
        return
    rows = (len(paths) + cols - 1) // cols
    sheet = np.zeros((rows * cell, cols * cell, 4), dtype=np.float32)
    sheet[:, :, :3] = 0.18
    sheet[:, :, 3] = 1.0
    for i, p in enumerate(paths):
        img = bpy.data.images.load(p)
        w, h = img.size
        px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1]
        bpy.data.images.remove(img)
        # nearest resample to cell
        ys = (np.arange(cell) * h / cell).astype(int)
        xs = (np.arange(cell) * w / cell).astype(int)
        px = px[ys][:, xs]
        r, c = divmod(i, cols)
        a = px[:, :, 3:4]
        dst = sheet[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell]
        dst[:, :, :3] = dst[:, :, :3] * (1 - a) + px[:, :, :3] * a
    core._save_png(out, sheet[:, :, :3])
    return out
