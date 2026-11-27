"""Render a fixed front view with the light rig counter-rotating for phone tilt."""
import argparse
import math
from pathlib import Path
import sys

import bpy
from mathutils import Quaternion, Vector

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--format", choices=("png", "exr"), default="png")
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)

scene = bpy.context.scene
scene.cycles.samples = 32
scene.cycles.use_denoising = True
scene.render.use_persistent_data = True
scene.render.resolution_x, scene.render.resolution_y = 640, 382
scene.render.resolution_percentage = 100
if args.format == "exr":
    scene.render.image_settings.file_format = "OPEN_EXR"
    scene.render.image_settings.color_depth = "16"
    scene.render.image_settings.exr_codec = "ZIP"
else:
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

camera = scene.camera
level = Quaternion((1, 0, 0), math.atan2(-camera.location.y, camera.location.z))
camera.location = Vector((0, 0, camera.location.length))
camera.rotation_euler = (0, 0, 0)
camera.data.ortho_scale = 6.2
lights = [(obj, level @ obj.location, level @ obj.rotation_euler.to_quaternion())
          for obj in scene.objects if obj.type == "LIGHT" and not obj.hide_render]
fixture = bpy.data.objects["Gold streak at lower center"].data
width, height = fixture.size, fixture.size_y

for folder, scale in (("01-current", 1), ("02-half", .5), ("03-quarter", .25)):
    fixture.size, fixture.size_y = width*scale, height*scale
    target = output / folder
    target.mkdir(exist_ok=True)
    for row, vertical in enumerate((-24, -12, 0, 12, 24)):
        for column, horizontal in enumerate((-24, -12, 0, 12, 24)):
            tilt = Quaternion((0, 1, 0), math.radians(horizontal)) @ Quaternion((1, 0, 0), math.radians(-vertical))
            for light, location, rotation in lights:
                light.location = tilt @ location
                light.rotation_euler = (tilt @ rotation).to_euler()
            scene.render.filepath = str(target / f"{row*5+column:02}.{args.format}")
            bpy.ops.render.render(write_still=True)
