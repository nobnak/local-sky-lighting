"""Render a fixed-light, camera-only comparison of four plate finishes."""
import argparse
import math
from pathlib import Path
import sys

import bpy
from mathutils import Quaternion, Vector

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--frames", type=int, default=17)
parser.add_argument("--variant", choices=("baseline", "rounded", "thin-film", "colored-lights", "film-control"))
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
if args.frames < 3 or args.frames % 2 == 0:
    parser.error("--frames must be odd and at least 3")

scene = bpy.context.scene
camera = scene.camera
plate = bpy.data.objects["Shallow bowl"]
existing = plate.data.materials[0]
camera_origin = camera.location.copy()
camera_forward = camera.rotation_euler.to_matrix() @ Vector((0, 0, -1))
camera_target = camera_origin - camera_forward * (camera_origin.y / camera_forward.y)

scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
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

scene.render.use_persistent_data = True
scene.cycles.use_denoising = True
scene.cycles.samples = 28
scene.render.resolution_x = 600
scene.render.resolution_y = 358
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"

def by_label(material, label):
    return next(node for node in material.node_tree.nodes if node.label == label)

def refine_strikes(material):
    graph = material.node_tree
    cells = by_label(material, "Irregular shallow strikes")
    cells.inputs["Smoothness"].default_value = 0.37
    slopes = by_label(material, "Slope of each hammer strike")
    slopes.inputs[1].default_value = (.48, .40, 0)
    slopes.inputs[2].default_value = (-.24, -.20, 0)
    fine = by_label(material, "Fine tooling")
    fine.inputs["Scale"].default_value = 88
    fine_bump = by_label(material, "Fine normal on each strike")
    fine_bump.inputs["Distance"].default_value = .0015

    mapping = graph.nodes.new("ShaderNodeVectorMath")
    mapping.label = "Elongated hand-tool marks"
    mapping.operation = "MULTIPLY"
    mapping.inputs[1].default_value = (.72, 1.85, 1)
    mapping.location = (-505, -630)
    graph.links.new(by_label(material, "Plate coordinates").outputs["Object"], mapping.inputs[0])

    noise = graph.nodes.new("ShaderNodeTexNoise")
    noise.label = "Normal variation inside a strike"
    noise.inputs["Scale"].default_value = 19
    noise.inputs["Detail"].default_value = 1
    noise.location = (-260, -630)
    graph.links.new(mapping.outputs["Vector"], noise.inputs["Vector"])

    relief = graph.nodes.new("ShaderNodeBump")
    relief.label = "Rounded slope inside each strike"
    relief.inputs["Strength"].default_value = .38
    relief.inputs["Distance"].default_value = .005
    relief.location = (320, -225)
    graph.links.new(noise.outputs["Fac"], relief.inputs["Height"])
    graph.links.new(by_label(material, "Unit strike normal").outputs["Vector"], relief.inputs["Normal"])
    graph.links.new(relief.outputs["Normal"], fine_bump.inputs["Normal"])

def coated_finish(material, with_film):
    graph = material.node_tree
    surface = graph.nodes.get("Material Output")
    substrate = surface.inputs["Surface"].links[0].from_socket
    dielectric = graph.nodes.new("ShaderNodeBsdfPrincipled")
    dielectric.label = "Dielectric interface above the warm substrate"
    dielectric.location = (1020, -700)
    dielectric.inputs["Base Color"].default_value = (0, 0, 0, 1)
    dielectric.inputs["IOR"].default_value = 2.15
    dielectric.inputs["Roughness"].default_value = .11
    graph.links.new(by_label(material, "Rounded slope inside each strike").outputs["Normal"],
                    dielectric.inputs["Normal"])
    layers = graph.nodes.new("ShaderNodeMixShader")
    layers.label = "Reflection from substrate and dielectric interface"
    layers.location = (1340, -125)
    layers.inputs[0].default_value = .34
    graph.links.new(substrate, layers.inputs[1])
    graph.links.new(dielectric.outputs["BSDF"], layers.inputs[2])
    graph.links.new(layers.outputs["Shader"], surface.inputs["Surface"])
    surface.location = (1560, 215)

    if not with_film:
        return
    thickness = graph.nodes.new("ShaderNodeMapRange")
    thickness.label = "Patchy thin film thickness in nm"
    thickness.location = (530, -600)
    thickness.inputs["To Min"].default_value = 180
    thickness.inputs["To Max"].default_value = 425
    graph.links.new(by_label(material, "Variation across groups of strikes").outputs["Fac"],
                    thickness.inputs["Value"])
    dielectric.inputs["Thin Film IOR"].default_value = 1.5
    graph.links.new(thickness.outputs["Result"], dielectric.inputs["Thin Film Thickness"])

def add_colored_reflections():
    # Keep these sources fixed in the scene throughout the camera sweep.
    key = bpy.data.lights["Broad left amber reflection"]
    key.energy = 16
    for name, position, color, power, size, width in (
        ("Muted rose strip", (-.49, .55, 1.23), (1, .30, .46), 5.4, .42, .26),
        ("Muted teal strip", (.49, .98, 1.43), (.22, .83, 1), 8.8, .56, .25),

    ):
        light = bpy.data.lights.new(name, "AREA")
        light.shape = "RECTANGLE"
        light.color = color
        light.energy = power
        light.size = size
        light.size_y = width
        fixture = bpy.data.objects.new(name, light)
        scene.collection.objects.link(fixture)
        fixture.location = position
        fixture.rotation_euler = (-fixture.location).to_track_quat("-Z", "Y").to_euler()

rounded = existing.copy()
rounded.name = "Rounded strikes with internal variation"
refine_strikes(rounded)
film = rounded.copy()
film.name = "Rounded strikes with a thin film"
coated_finish(film, with_film=True)

# Run the colored rig last so all other renders retain the original lighting.
variants = (("baseline", existing), ("rounded", rounded), ("thin-film", film),
            ("colored-lights", rounded))
if args.variant == "film-control":
    control = rounded.copy()
    control.name = "Rounded strikes with an unfilmed dielectric interface"
    coated_finish(control, with_film=False)
    variants = (("film-control", control),)

for label, material in variants:
    if args.variant and label != args.variant:
        continue
    if label == "colored-lights":
        add_colored_reflections()
    plate.data.materials[0] = material
    target = args.output.resolve() / label
    target.mkdir(parents=True, exist_ok=True)
    for index in range(args.frames):
        angle = math.radians(-12 + 24 * index / (args.frames - 1))
        tilt = angle * (2 / 3)
        pivot = Quaternion((0, 0, 1), angle) @ Quaternion((1, 0, 0), tilt)
        camera.location = camera_target + pivot @ (camera_origin - camera_target)
        camera.rotation_euler = (camera_target - camera.location).to_track_quat("-Z", "Y").to_euler()
        scene.frame_set(index + 1)
        scene.render.filepath = str(target / f"{index:03d}.png")
        bpy.ops.render.render(write_still=True)
