"""Export the bowl geometry, light rig and packed surface data from reference-plate.blend."""
import argparse
import json
import math
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Quaternion, Vector


def unity(vector):
    return {'x': round(vector.x, 6), 'y': round(vector.y, 6), 'z': round(-vector.z, 6)}


def geometry(objects):
    vertices, normals, triangles = [], [], []
    for name in objects:
        obj = bpy.data.objects[name].evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = obj.to_mesh()
        mesh.calc_loop_triangles()
        offset = len(vertices)
        matrix = obj.matrix_world
        normal_matrix = matrix.to_3x3().inverted().transposed()
        for vert in mesh.vertices:
            vertices.append(unity(matrix @ vert.co))
            normals.append(unity((normal_matrix @ vert.normal).normalized()))
        triangles.append([offset + i for tri in mesh.loop_triangles for i in reversed(tri.vertices)])
        obj.to_mesh_clear()
    return {'vertices': vertices, 'normals': normals, 'bowl': triangles[0], 'rim': triangles[1]}


def lights(scene):
    home = scene.camera.location
    level = Quaternion((1, 0, 0), math.atan2(-home.y, home.z))
    result = []
    for obj in scene.objects:
        if obj.type != 'LIGHT' or obj.hide_render:
            continue
        data = obj.data
        rotation = level @ obj.rotation_euler.to_quaternion()
        result.append({'name': obj.name, 'position': unity(level @ obj.location),
                       'right': unity(rotation @ Vector((1, 0, 0))),
                       'up': unity(rotation @ Vector((0, 1, 0))),
                       'forward': unity(rotation @ Vector((0, 0, -1))),
                       'color': dict(zip(('x', 'y', 'z'), data.color)), 'power': data.energy,
                       'width': data.size, 'height': data.size_y})
    return result


def export_scratch_direction(image, target):
    width, height = image.size
    data = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(data)
    surface = data.reshape(height, width, 4)
    dy, dx = np.gradient(surface[..., :2], axis=(0, 1))
    weight = np.clip(surface[..., 2] * 3, 0, 1)

    def average(values):
        for axis in (0, 1):
            values = sum(np.roll(values, offset, axis) for offset in range(-3, 4)) / 7
        return values[::2, ::2]

    xx = average(np.sum(dx * dx, axis=2) * weight)
    yy = average(np.sum(dy * dy, axis=2) * weight)
    xy = average(np.sum(dx * dy, axis=2) * weight)
    # Encode a line direction using the doubled angle, so opposite directions filter together.
    denom = xx + yy + 0.00001
    pixels = np.zeros((*xx.shape, 4), dtype=np.float32)
    pixels[..., 0] = 0.5 - 0.5 * (xx - yy) / denom
    pixels[..., 1] = 0.5 - xy / denom
    pixels[..., 3] = 1
    direction = bpy.data.images.new('Scratch direction', pixels.shape[1], pixels.shape[0])
    direction.colorspace_settings.name = 'Non-Color'
    direction.pixels.foreach_set(np.clip(pixels, 0, 1).ravel())
    direction.file_format = 'PNG'
    direction.filepath_raw = str(target)
    direction.save()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    out = parser.parse_args(sys.argv[sys.argv.index('--') + 1:]).output.resolve()
    out.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    data = geometry(('Shallow bowl', 'Rolled rounded rim'))
    data['lights'] = lights(scene)
    (out / 'PlateGeometry.json').write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    image = bpy.data.images['Struck plate slope XY and polish mask']
    scene.render.image_settings.file_format = 'OPEN_EXR'
    scene.render.image_settings.color_depth = '16'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.image_settings.exr_codec = 'ZIP'
    image.save_render(str(out / 'Strikes.exr'), scene=scene)
    export_scratch_direction(image, out / 'ScratchDirection.png')
    print(f'Exported {len(data["vertices"])} vertices, {len(data["lights"])} lights, {list(image.size)} surface')
