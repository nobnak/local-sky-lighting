"""Create a reference-oriented plate study with procedurally struck normals."""
import argparse
import math
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Quaternion, Vector

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--draft", action="store_true")
parser.add_argument("--sweep", action="store_true")
parser.add_argument("--surface-study", action="store_true")
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
root = args.output.resolve()
root.mkdir(parents=True, exist_ok=True)
scene = bpy.context.scene
dish = bpy.data.objects["Shallow bowl"]
rim = bpy.data.objects["Rolled rounded rim"]

scene.cycles.samples = 28 if args.draft else 80
scene.render.resolution_x, scene.render.resolution_y = (760, 454) if args.draft else (1440, 859)
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGB"
scene.render.use_persistent_data = True
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

def smootherstep(t):
    t = np.clip(t, 0, 1)
    return t*t*t*(t*(6*t-15)+10)

def add_tool_fragments(u, v, rng):
    height = np.zeros_like(u)
    polish = np.zeros_like(u)
    for _ in range(rng.integers(3, 6)):
        x, y = rng.uniform(-.74, .74), rng.uniform(-.75, .75)
        length, width = rng.uniform(.21, .58), rng.uniform(.10, .29)
        along = (u-x)/length
        across = (v-y-.12*(u-x))/width
        patch = np.exp(-1.8*(along*along+across*across))
        strength = rng.uniform(-1, 1)
        height += .00025 * patch * (strength + .32*along)
        polish += patch * strength * .42
    return height, polish

def make_strike_texture(surface=None):
    size, extent = (1320 if args.draft else 1920), 3.24
    rng = np.random.default_rng(6481)
    fragment_rng = np.random.default_rng(5681)
    detail = surface == "03-detail"
    coords = np.linspace(-extent / 2, extent / 2, size, dtype=np.float32)
    x, y = np.meshgrid(coords, coords)
    channel_x = .028 * np.sin(6*x + 2*np.sin(3*y)) + .018 * np.sin(24*x - 13*y)
    channel_y = .024 * np.sin(5*y + 2*np.sin(4*x)) + .017 * np.sin(27*y + 11*x)
    coverage = np.zeros_like(x)
    if surface:
        height = (.0037*np.cos(6*x + .6*np.sin(3*y))
                  + .0034*np.cos(5*y + .65*np.sin(4*x)))
        body_mask = np.zeros_like(x)
    step_x, step_y = .079, .069
    for row, cy in enumerate(np.arange(-1.57, 1.58, step_y)):
        for cx in np.arange(-1.60, 1.61, step_x):
            cx += .018 * math.sin(2.2 * cy + 1.7 * cx) + rng.uniform(-.027, .027)
            if row % 2:
                cx += .022
            yy = cy + .016 * math.sin(5 * cx - cy) + rng.uniform(-.021, .021)
            if cx*cx + yy*yy > 1.61**2:
                continue
            support = .105 if surface else .085
            xs = slice(*np.searchsorted(coords, (cx-support, cx+support)))
            ys = slice(*np.searchsorted(coords, (yy-support, yy+support)))
            dx, dy = x[ys,xs]-cx, y[ys,xs]-yy
            angle = .20 + .39*math.sin(2.5*cx-1.7*yy) + .17*math.sin(4*yy+cx) + rng.normal(0,.27)
            c, s = math.cos(angle), math.sin(angle)
            u = (c*dx+s*dy)/rng.uniform(.023,.060)
            v = (-s*dx+c*dy)/rng.uniform(.011,.034)
            phase = rng.uniform(0, math.tau)
            v += .11 * np.sin(3*u + phase) + .07 * np.sin(9*u + phase)
            u += .10 * np.sin(4*v - phase)
            edge = (np.abs(u)**3.4 + np.abs(v)**3.4)**(1/3.4)
            weight = np.clip((1.10-edge)/.32,0,1)
            weight = weight*weight*(3-2*weight)
            tilt_x = rng.normal(.0,.105) + .052*math.sin(5*cx+2*yy)
            tilt_y = rng.normal(0,.096) + .055*math.sin(4*yy-2*cx)
            grade = rng.uniform(.22,1)
            if surface:
                shoulder = smootherstep((1.24-edge)/.86)
                local_height = -.00084 - .54*(tilt_x*dx + tilt_y*dy)
                polish = grade
                if detail:
                    fragments, patches = add_tool_fragments(u, v, fragment_rng)
                    local_height = local_height + fragments
                    polish = np.clip(grade + patches, 0, 1)
                height[ys,xs] += shoulder*local_height
                # Separate the slope transition, roughness, and metal-body blend.
                inner = smootherstep((1.21-edge)/.65)
                coverage[ys,xs] += (1-coverage[ys,xs])*inner*polish
                broader = smootherstep((1.32-edge)/1.15)
                body_mask[ys,xs] += (1-body_mask[ys,xs])*broader*(.47 + .45*grade)
            else:
                vein = np.sin(17*v + 2.1*np.sin(4*u+phase) + phase)
                local_x = tilt_x + .041*u + .025*vein
                local_y = tilt_y + .064*v + .035*vein
                channel_x[ys,xs] += weight * (local_x - channel_x[ys,xs])
                channel_y[ys,xs] += weight * (local_y - channel_y[ys,xs])
                coverage[ys,xs] += weight * (grade - coverage[ys,xs])
    if surface:
        dy, dx = np.gradient(height, float(coords[1]-coords[0]))
        channel_x, channel_y = -dx, -dy
    pixels = np.empty((size,size,4), dtype=np.float32)
    pixels[...,0] = channel_x + .5
    pixels[...,1] = channel_y + .5
    pixels[...,2] = coverage
    pixels[...,3] = body_mask if surface else 1
    name = surface or "Struck plate slope XY and polish mask"
    image = bpy.data.images.new(name, size, size, alpha=True, float_buffer=True)
    image.colorspace_settings.name = "Non-Color"
    if surface:
        image.alpha_mode = "CHANNEL_PACKED"
    image.pixels.foreach_set(pixels.ravel())
    image.update()
    image.filepath_raw = str(root / (f"{surface}.exr" if surface else "strike-slopes.exr"))
    image.file_format = "OPEN_EXR"
    image.save()
    image.pack()
    return image

def shader_setup(image, body):
    mat = bpy.data.materials.new("Amber plate with struck reflections" if body else "Reflective strikes")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links

    def node(kind, label, xy):
        n = nodes.new(kind)
        n.label, n.location = label, xy
        return n

    def wire(a, first, b, second):
        links.new(a.outputs[first], b.inputs[second])

    coord = node("ShaderNodeTexCoord", "Shallow bowl coordinates", (-1100,240))
    uv = node("ShaderNodeVectorMath", "Planar sampling", (-895,240))
    uv.operation = "MULTIPLY_ADD"
    uv.inputs[1].default_value = (1/3.24,1/3.24,0)
    uv.inputs[2].default_value = (.5,.5,0)
    wire(coord, "Object", uv, 0)
    tex = node("ShaderNodeTexImage", "Procedural tooling slopes and mask", (-655,240))
    tex.image, tex.extension = image, "EXTEND"
    wire(uv, "Vector", tex, "Vector")
    delta = node("ShaderNodeVectorMath", "Decode strike slope", (-385,240))
    delta.operation = "MULTIPLY_ADD"
    delta.inputs[1].default_value = (1,1,0)
    delta.inputs[2].default_value = (-.5,-.5,0)
    wire(tex, "Color", delta, 0)
    geom = node("ShaderNodeNewGeometry", "Base curvature", (-360,0))
    add = node("ShaderNodeVectorMath", "Base curvature plus stamp slope", (-160,240))
    add.operation = "ADD"
    wire(delta, "Vector", add, 0)
    wire(geom, "Normal", add, 1)
    norm = node("ShaderNodeVectorMath", "Normal", (30,240))
    norm.operation = "NORMALIZE"
    wire(add, "Vector", norm, 0)
    channels = node("ShaderNodeSeparateColor", "Polish coverage", (-380,-270))
    wire(tex, "Color", channels, "Color")
    rough = node("ShaderNodeMapRange", "Satin between polished stamps", (-130,-175))
    rough.inputs["To Min"].default_value = .42
    rough.inputs["To Max"].default_value = .075
    wire(channels, "Blue", rough, "Value")
    metal = nodes.get("Principled BSDF")
    metal.location = (260,250)
    metal.inputs["Metallic"].default_value = 1
    metal.inputs["Base Color"].default_value = (.72,.43,.105,1)
    metal.inputs["Anisotropic"].default_value = .34
    wire(norm, "Vector", metal, "Normal")
    wire(rough, "Result", metal, "Roughness")
    out = nodes.get("Material Output")
    out.location = (980,250)
    if not body:
        return mat

    distance = node("ShaderNodeVectorMath", "Dark copper-red near the edge", (-870,-560))
    distance.operation = "LENGTH"
    wire(coord, "Object", distance, 0)
    stain = node("ShaderNodeMapRange", "Soft radial glaze depth", (-625,-550))
    stain.interpolation_type = "SMOOTHERSTEP"
    stain.inputs["From Min"].default_value = .39
    stain.inputs["From Max"].default_value = 1.46
    wire(distance, "Value", stain, "Value")
    tint = node("ShaderNodeMixRGB", "Amber body color", (-370,-550))
    tint.inputs[1].default_value = (.37,.104,.035,1)
    tint.inputs[2].default_value = (.071,.012,.015,1)
    wire(stain, "Result", tint, 0)
    alloy = node("ShaderNodeMixRGB", "Gold reflections on a darker copper-red base", (-126, -653))
    alloy.inputs[1].default_value = (.79,.53,.20,1)
    alloy.inputs[2].default_value = (.19,.046,.031,1)
    wire(stain, "Result", alloy, 0)
    wire(alloy, "Color", metal, "Base Color")
    resin = node("ShaderNodeBsdfPrincipled", "Amber body and broad surface reflections", (265,-365))
    resin.inputs["Roughness"].default_value = .29
    resin.inputs["Metallic"].default_value = .06
    resin.inputs["Specular IOR Level"].default_value = .13
    resin.inputs["Coat Weight"].default_value = .14
    resin.inputs["Coat Roughness"].default_value = .19
    wire(tint, "Color", resin, "Base Color")
    wire_norm = node("ShaderNodeMix", "Smooth broad reflection across strikes", (35,-510))
    wire_norm.data_type = "VECTOR"
    wire_norm.inputs[0].default_value = .38
    links.new(geom.outputs["Normal"], wire_norm.inputs[4])
    links.new(norm.outputs["Vector"], wire_norm.inputs[5])
    links.new(wire_norm.outputs[1], resin.inputs["Normal"])
    wire(geom, "Normal", resin, "Coat Normal")
    blend = node("ShaderNodeMapRange", "Separate body and sharp stamp contribution", (-115,-420))
    blend.inputs["To Min"].default_value = .99
    blend.inputs["To Max"].default_value = .22
    wire(channels, "Blue", blend, "Value")
    sheen = node("ShaderNodeMixShader", "Reflection over continuous amber body", (770,250))
    wire(blend, "Result", sheen, 0)
    wire(metal, "BSDF", sheen, 1)
    wire(resin, "BSDF", sheen, 2)
    wire(sheen, "Shader", out, "Surface")
    return mat

def light(name, location, color, power, size, width, target):
    data = bpy.data.lights.new(name, "AREA")
    data.color, data.energy, data.shape = color, power, "RECTANGLE"
    data.size, data.size_y = size, width
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()

def replace_lighting():
    for obj in tuple(scene.objects):
        if obj.type == "LIGHT":
            obj.hide_render = True
    for setup in (
        ("Warm field", (.02,.95,2.0), (1,.75,.50), 31, 2.55, 1.92, (0,0,0)),
        ("Wide soft shoulder", (-.38,.24,1.78), (1,.86,.65), 16, 1.31,.73, (-.28,-.18,0)),
        ("Gold streak at left", (-.73,.96,1.38), (1,.76,.13), 14,.24,.61, (-.58,0,0)),
        ("Gold streak at lower center", (-.26,.18,1.23), (1,.70,.094), 12,.25,.43, (-.28,-.5,0)),
        ("Gold points across right", (.67,.88,1.65), (1,.82,.19), 10,.46,.27, (.52,-.09,0)),
        ("Soft rose on left", (-.42,.42,1.26), (1,.20,.30), 7.6,.46,.53, (-.42,-.32,0)),
        ("Soft olive on right", (.54,.56,1.29), (.33,1,.63), 10,.64,.46, (.46,-.32,0)),
        ("Faint purple outer reflection", (2.05,1.47,1.1), (.49,.18,1), 1.7,.36,.40, (1.7,0,0)),
    ):
        light(*setup)
    glass = rim.data.materials[0].copy()
    glass.name = "Dark amber rim with broad and sharp reflections"
    shader = glass.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (.062,.020,.018,1)
    shader.inputs["Metallic"].default_value = .06
    shader.inputs["Transmission Weight"].default_value = .38
    shader.inputs["Coat Weight"].default_value = .19
    shader.inputs["Coat Roughness"].default_value = .16
    shader.inputs["Specular IOR Level"].default_value = .20
    rim.data.materials[0] = glass
    for vertex in rim.data.vertices:
        radius = math.hypot(vertex.co.x, vertex.co.y)
        width = 1 - max(0, (1.675 - radius)) * .11
        vertex.co.x *= width
        vertex.co.y *= width
        if vertex.co.z > .05:
            vertex.co.z = .05 + (vertex.co.z - .05) * 1.30

def still(filename):
    scene.render.filepath = str(root / filename)
    bpy.ops.render.render(write_still=True)

def surface_study():
    material = dish.data.materials[0]
    camera = scene.camera
    home = camera.location.copy()
    focus = Vector((0, 0, .065))
    variants = [("01-current", material)]
    for name in ("02-boundary", "03-detail"):
        match = material.copy()
        match.name = name
        tree = match.node_tree
        tex = next(node for node in tree.nodes if node.label == "Procedural tooling slopes and mask")
        tex.image = make_strike_texture(name)
        mix = next(node for node in tree.nodes if node.label == "Separate body and sharp stamp contribution")
        tree.links.new(tex.outputs["Alpha"], mix.inputs["Value"])
        variants.append((name, match))
        match.use_fake_user = True
    material.use_fake_user = True

    for name, match in variants:
        dish.data.materials[0] = match
        still(f"{name}.png")
    scene.render.filepath = "//03-detail.png"
    bpy.ops.wm.save_as_mainfile(filepath=str(root / "surface-study.blend"), compress=True)

    if args.sweep:
        scene.cycles.samples = 32
        scene.render.resolution_x, scene.render.resolution_y = 760, 454
        for name, match in variants:
            dish.data.materials[0] = match
            (root / name).mkdir(exist_ok=True)
            for index in range(13):
                yaw = math.radians(-8 + 16*index/12)
                pitch = yaw*.62
                rotate = Quaternion((0,0,1), yaw) @ Quaternion((1,0,0), pitch)
                camera.location = focus + rotate @ (home-focus)
                camera.rotation_euler = (focus-camera.location).to_track_quat("-Z", "Y").to_euler()
                still(f"{name}/{index:02d}.png")

if args.surface_study:
    surface_study()
    sys.exit(0)

still("01-previous.png")
maps = make_strike_texture()
dish.data.materials[0] = shader_setup(maps, body=False)
still("02-shaped-strokes.png")
body = shader_setup(maps, body=True)
dish.data.materials[0] = body
still("03-amber-body.png")
replace_lighting()
scene.view_settings.look = "AgX - Medium High Contrast"
still("04-reference-light.png")
scene.view_settings.view_transform = "Khronos PBR Neutral"
still("05-neutral-tone.png")
scene.view_settings.view_transform = "AgX"
scene.view_settings.look = "AgX - Medium High Contrast"
low_gold = bpy.data.objects["Gold streak at lower center"]
low_gold.location = (-.26, .12, 1.12)
low_gold.rotation_euler = (Vector((-.28, -.5, 0)) - low_gold.location).to_track_quat("-Z", "Y").to_euler()
low_gold.data.size, low_gold.data.size_y = .38, .36
bpy.data.lights["Gold points across right"].energy *= .78
light("Small warm-white glint", (-.48,.045,1.23), (1,.87,.68), 2.4,.19,.26, (-.44,-.55,0))
rim_mat = rim.data.materials[0]
rim_shader = rim_mat.node_tree.nodes.get("Principled BSDF")
rim_shader.inputs["Specular IOR Level"].default_value = .13
rim_shader.inputs["Coat Weight"].default_value = .12
still("06-final.png")
scene.render.filepath = "//06-final.png"
bpy.ops.wm.save_as_mainfile(filepath=str(root / "reference-plate.blend"))

if args.sweep:
    camera = scene.camera
    home = camera.location.copy()
    focus = Vector((0,0,.065))
    scene.cycles.samples = 40
    scene.render.resolution_x, scene.render.resolution_y = 900, 537
    for index, degrees in enumerate((-10,-5,0,5,10)):
        rad = math.radians(degrees)
        camera.location = (-home.y*math.sin(rad), home.y*math.cos(rad), home.z)
        camera.rotation_euler = (focus-camera.location).to_track_quat("-Z", "Y").to_euler()
        still(f"camera-{index:02d}.png")
