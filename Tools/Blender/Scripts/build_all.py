"""
Runs the whole Embervale asset pipeline headless:

    python Tools/Blender/Scripts/build_all.py            # bpy module (pip install bpy)
    blender -b -P Tools/Blender/Scripts/build_all.py     # or a Blender 4.4+/5.x install

Steps: textures -> characters & animations -> equipment -> creatures -> world assets -> items & icons.
Pass --no-previews to skip preview renders (icons are always rendered).
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from emb import core  # noqa: E402
import build_characters  # noqa: E402
import build_equipment  # noqa: E402
import build_creatures  # noqa: E402
import build_world_assets  # noqa: E402
import build_items_icons  # noqa: E402


def main():
    previews = "--no-previews" not in sys.argv
    t0 = time.time()
    core.reset_scene()
    core.generate_textures(force=True)
    for name, fn in (("characters", lambda: build_characters.main(previews)),
                     ("equipment", lambda: build_equipment.main(previews)),
                     ("creatures", lambda: build_creatures.main(previews)),
                     ("world", lambda: build_world_assets.main(previews)),
                     ("items+icons", build_items_icons.main)):
        t = time.time()
        fn()
        print("[build_all] %s done in %.1fs" % (name, time.time() - t))
    print("[build_all] finished in %.1fs" % (time.time() - t0))


if __name__ == "__main__":
    main()
