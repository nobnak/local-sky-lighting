"""Render matched material studies and a fixed-camera lighting study in Blender."""
import argparse
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector

parser = argparse.ArgumentParser()
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--draft", action="store_true")
parser.add_argument("--movie", action="store_true")
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 22 if args.draft else 64
scene.cycles.use_denoising = True
scene.render.use_persistent_data = True
scene.cycles.max_bounces = 8
scene.cycles.transmission_bounces = 6
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
            print("Cycles devices:", [device.name for device in devices], flush=True)
            break
    except (RuntimeError, TypeError):
        pass

scene.render.resolution_x = 720 if args.draft else 1280
scene.render.resolution_y = 430 if args.draft else 764
scene.render.resolution_percentage = 100
scene.view_settings.view_transform = "AgX"
scene.view_settings.look = "AgX - Medium High Contrast"
scene.view_settings.exposure = -0.85
scene.world = bpy.data.worlds.new("Dark room")
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.15, 0.095, 0.085, 1)
scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.13

def principled(name, color, metallic, roughness):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    shader = material.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    return material, shader

def hammered(name, color, coated):
    material, shader = principled(name, color, 1, 0.25)
    graph = material.node_tree

    def node(kind, name, x, y):
        item = graph.nodes.new(kind)
        item.label = name
        item.location = (x, y)
        return item

    def link(src, port, dst, socket):
        graph.links.new(src.outputs[port], dst.inputs[socket])

    tex = node("ShaderNodeTexCoord", "Plate coordinates", -900, 120)
    mapping = node("ShaderNodeVectorMath", "Slightly elongated strike pattern", -710, 180)
    mapping.operation = "MULTIPLY"
    mapping.inputs[1].default_value = (0.80, 1.08, 1)
    link(tex, "Object", mapping, 0)
    cells = node("ShaderNodeTexVoronoi", "Irregular shallow strikes", -475, 150)
    cells.voronoi_dimensions = "2D"
    cells.feature = "SMOOTH_F1"
    cells.inputs["Scale"].default_value = 19
    cells.inputs["Smoothness"].default_value = 0.25
    cells.inputs["Randomness"].default_value = 0.84
    warp = node("ShaderNodeTexNoise", "Variation across groups of strikes", -705, -75)
    warp.inputs["Scale"].default_value = 3.7
    warp.inputs["Detail"].default_value = 1.2
    link(tex, "Object", warp, "Vector")
    warp_amount = node("ShaderNodeVectorMath", "Subtle warp", -510, -65)
    warp_amount.operation = "SCALE"
    warp_amount.inputs["Scale"].default_value = 0.16
    link(warp, "Color", warp_amount, 0)
    warped = node("ShaderNodeVectorMath", "Warps the stamp pattern", -360, -50)
    warped.operation = "ADD"
    link(mapping, "Vector", warped, 0)
    link(warp_amount, "Vector", warped, 1)
    link(warped, "Vector", cells, "Vector")
    tone = node("ShaderNodeMapRange", "Restrained roughness variation", -210, 200)
    tone.inputs["To Min"].default_value = 0.20
    tone.inputs["To Max"].default_value = 0.36
    link(cells, "Color", tone, "Value")
    link(tone, "Result", shader, "Roughness")
    radius = node("ShaderNodeVectorMath", "Protected center and darker patina near the rim", -700, 500)
    radius.operation = "LENGTH"
    link(tex, "Object", radius, 0)
    patina = node("ShaderNodeMapRange", "Patina blend", -470, 500)
    patina.interpolation_type = "SMOOTHERSTEP"
    patina.inputs["From Min"].default_value = 0.66
    patina.inputs["From Max"].default_value = 1.62
    link(radius, "Value", patina, "Value")
    alloy = node("ShaderNodeMixRGB", "Warm alloy with dark edge oxidation", -215, 530)
    alloy.inputs[1].default_value = (*color, 1)
    alloy.inputs[2].default_value = (color[0] * 0.24, color[1] * 0.095, color[2] * 0.18, 1)
    link(patina, "Result", alloy, 0)
    link(alloy, "Color", shader, "Base Color")

    variation = node("ShaderNodeVectorMath", "Slope of each hammer strike", -245, 20)
    variation.operation = "MULTIPLY_ADD"
    variation.inputs[1].default_value = (0.57, 0.44, 0)
    variation.inputs[2].default_value = (-0.285, -0.22, 0)
    link(cells, "Color", variation, 0)
    to_world = node("ShaderNodeVectorTransform", "Slope in world space", -45, 30)
    to_world.vector_type = "VECTOR"
    to_world.convert_from = "OBJECT"
    to_world.convert_to = "WORLD"
    link(variation, "Vector", to_world, "Vector")
    geometry = node("ShaderNodeNewGeometry", "Shallow bowl curvature", -245, -250)
    perturbed = node("ShaderNodeVectorMath", "Bowl and strike normal", 145, 30)
    perturbed.operation = "ADD"
    link(to_world, "Vector", perturbed, 0)
    link(geometry, "Normal", perturbed, 1)
    normal = node("ShaderNodeVectorMath", "Unit strike normal", 345, 30)
    normal.operation = "NORMALIZE"
    link(perturbed, "Vector", normal, 0)
    grain = node("ShaderNodeTexNoise", "Fine tooling", -465, -380)
    grain.inputs["Scale"].default_value = 65
    grain.inputs["Detail"].default_value = 2
    link(mapping, "Vector", grain, "Vector")
    grain_bump = node("ShaderNodeBump", "Fine normal on each strike", 540, 35)
    grain_bump.inputs["Strength"].default_value = 0.26
    grain_bump.inputs["Distance"].default_value = 0.0036
    link(grain, "Fac", grain_bump, "Height")
    link(normal, "Vector", grain_bump, "Normal")
    link(grain_bump, "Normal", shader, "Normal")
    shader.inputs["Anisotropic"].default_value = 0.27
    if coated:
        shader.inputs["Coat Weight"].default_value = 0.44
        shader.inputs["Coat Tint"].default_value = (0.88, 0.49, 0.24, 1)
        shader.inputs["Coat Roughness"].default_value = 0.24
        link(geometry, "Normal", shader, "Coat Normal")
    shader.location = (740, 215)
    graph.nodes.get("Material Output").location = (1060, 215)
    return material

def lathe(name, profile, material):
    count = 192
    vertices = [(r * math.cos(a * math.tau / count), r * math.sin(a * math.tau / count), z)
                for r, z in profile for a in range(count)]
    faces = [(i + a, i + (a + 1) % count, i + (a + 1) % count + count, i + a + count)
             for i in range(0, len(vertices) - count, count) for a in range(count)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    obj.data.materials.append(material)
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    return obj

bronze = hammered("A / Warm hammered metal", (0.67, 0.36, 0.090), False)
layered = hammered("B / Amber dielectric coat over reflective facets", (0.67, 0.36, 0.090), True)
bronze.use_fake_user = True
layered.use_fake_user = True
radii = [1.55 * i / 96 for i in range(96, 0, -1)] + [0.00001]
plate = lathe("Shallow bowl", [(r, 0.067 * (r / 1.55) ** 2.65) for r in radii], bronze)
rim_material, rim_shader = principled("Polished dark amber rim", (0.11, 0.030, 0.014), 1, 0.23)
rim_shader.inputs["Coat Weight"].default_value = 0.11
rim_graph = rim_material.node_tree
rim_wear = rim_graph.nodes.new("ShaderNodeTexNoise")
rim_wear.inputs["Scale"].default_value = 16
rim_wear.inputs["Detail"].default_value = 2
rim_tone = rim_graph.nodes.new("ShaderNodeMapRange")
rim_tone.inputs["To Min"].default_value = 0.16
rim_tone.inputs["To Max"].default_value = 0.45
rim_graph.links.new(rim_wear.outputs["Fac"], rim_tone.inputs["Value"])
rim_graph.links.new(rim_tone.outputs["Result"], rim_shader.inputs["Roughness"])
rim = [(1.52, .065), (1.55, .07), (1.572, .086), (1.578, .107), (1.587, .127),
       (1.606, .14), (1.64, .145), (1.662, .139), (1.674, .119), (1.675, .066),
       (1.666, .02), (1.65, -.021), (1.61, -.045), (1.54, -.049), (1.52, .065)]
lathe("Rolled rounded rim", list(reversed(rim)), rim_material)

floor, floor_shader = principled("Almost black surroundings", (0.002, 0.0006, 0.0013), 0, 0.69)
floor_shader.inputs["Specular IOR Level"].default_value = 0.18
bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -0.096))
bpy.context.object.data.materials.append(floor)

def aim(obj, point):
    obj.rotation_euler = (Vector(point) - obj.location).to_track_quat("-Z", "Y").to_euler()

def area(name, position, color, energy, size, width=None):
    data = bpy.data.lights.new(name, "AREA")
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.location = position
    data.color = color
    data.energy = energy
    data.shape = "DISK" if width is None else "RECTANGLE"
    data.size = size
    if width is not None:
        data.size_y = width
    aim(obj, (0, 0, 0))
    return obj

key = area("Broad left amber reflection", (-.58, 1.12, 1.51), (1, .76, .33), 29, .68, .42)
band = area("Warm moving band", (.10, .23, 1.14), (1, .83, .43), 11, .53, .29)
area("Soft amber upper reflection", (.13, 1.55, 1.55), (1, .57, .22), 7, .76, .50)
area("Rose accent", (-.43, .40, 1.12), (1, .19, .30), 5.4, .35)
green = area("Soft green accent", (.48, .55, 1.24), (.36, 1, .31), 7.5, .48)
area("Faint violet edge reflection", (1.95, 1.05, 1.23), (.49, .18, 1), 5, .57)

camera_data = bpy.data.cameras.new("Reference framing")
camera = bpy.data.objects.new("Reference framing", camera_data)
scene.collection.objects.link(camera)
camera.location = (0, -4.7, 8)
aim(camera, (0, 0, .065))
camera_data.type = "ORTHO"
camera_data.ortho_scale = 5.0
scene.camera = camera
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGB"

def render_still(name):
    scene.render.filepath = str(output / name)
    bpy.ops.render.render(write_still=True)

render_still("A-hammered-metal.png")
plate.data.materials[0] = layered
render_still("B-amber-coat.png")
plate.data.materials[0] = bronze
scene.render.filepath = "//A-hammered-metal.png"
bpy.ops.wm.save_as_mainfile(filepath=str(output / "plate-materials.blend"))

if args.movie:
    plate.data.materials[0] = layered
    scene.render.resolution_x = 768
    scene.render.resolution_y = 458
    scene.cycles.samples = 32
    scene.render.fps = 24
    scene.frame_start = 1
    scene.frame_end = 72
    for frame, offsets in ((1, (-.44, -.65, -.36)), (24, (-.13, -.2, -.02)),
                           (49, (.37, .31, .26)), (72, (.57, .73, .35))):
        for light, origin, offset in zip((key, band, green), ((-.58, 1.12), (.10, .23), (.48, .55)), offsets):
            light.location.x = origin[0] + offset
            light.location.y = origin[1] + offset * .50
            aim(light, (offset * .3, 0, 0))
            light.keyframe_insert("location", frame=frame)
            light.keyframe_insert("rotation_euler", frame=frame)
    scene.frame_set(1)
    scene.render.image_settings.file_format = "FFMPEG"
    scene.render.ffmpeg.format = "MPEG4"
    scene.render.ffmpeg.codec = "H264"
    scene.render.ffmpeg.constant_rate_factor = "HIGH"
    scene.render.filepath = "//lighting-study.mp4"
    bpy.ops.wm.save_as_mainfile(filepath=str(output / "plate-lighting-study.blend"))
    bpy.ops.render.render(animation=True)
