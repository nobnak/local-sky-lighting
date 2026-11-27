"""Compare one light's emitting size on the saved reference plate."""
import argparse
import math
from pathlib import Path
import sys

import bpy
from mathutils import Quaternion, Vector

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--sweep", action="store_true")
parser.add_argument("--isolated", action="store_true")
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)

scene = bpy.context.scene
camera = scene.camera
focus = Vector((0, 0, .065))
orig_location = camera.location.copy()
orig_rotation = camera.rotation_euler.copy()
fixture = bpy.data.objects["Gold streak at lower center"]
light = fixture.data
width, length, power = light.size, light.size_y, light.energy

scene.cycles.samples = 96
scene.cycles.use_denoising = True
scene.render.use_persistent_data = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGB"
preferences = bpy.context.preferences.addons["cycles"].preferences
for backend in ("OPTIX", "CUDA"):
    try:
        preferences.compute_device_type = backend
        preferences.refresh_devices()
        devices = [device for device in preferences.devices if device.type == backend]
        if devices:
            for device in preferences.devices:
                device.use = device in devices
            scene.cycles.device = "GPU"
            break
    except (RuntimeError, TypeError):
        pass

sizes = (("01-current", 1), ("02-half", .5), ("03-quarter", .25), ("04-off", 0))

def configure(scale):
    fixture.hide_render = scale == 0
    light.size, light.size_y = width*(scale or 1), length*(scale or 1)

def still(path):
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)

if args.isolated:
    for other in scene.objects:
        if other.type == "LIGHT" and other != fixture:
            other.hide_render = True

for name, scale in sizes:
    configure(scale)
    still(output / f"{name}.png")

configure(1)
camera.location, camera.rotation_euler = orig_location, orig_rotation
scene.render.filepath = "//01-current.png"
if not args.isolated:
    bpy.ops.wm.save_as_mainfile(filepath=str(output / "light-size-study.blend"), compress=True)

if args.sweep:
    scene.cycles.samples = 32
    scene.render.resolution_x, scene.render.resolution_y = 800, 478
    for name, scale in sizes[:3]:
        configure(scale)
        folder = output / name
        folder.mkdir(exist_ok=True)
        for index in range(11):
            azimuth = math.radians(-8 + 16 * index / 10)
            orbit = Quaternion((0, 0, 1), azimuth) @ Quaternion((1, 0, 0), azimuth*.55)
            camera.location = focus + orbit @ (orig_location-focus)
            camera.rotation_euler = (focus-camera.location).to_track_quat("-Z", "Y").to_euler()
            still(folder / f"{index:02d}.png")

assert light.energy == power
print(f"Size at 100%: {width:.3f} m by {length:.3f} m; fixed power {power:g} W")
