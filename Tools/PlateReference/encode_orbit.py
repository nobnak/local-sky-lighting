from pathlib import Path
import sys

import bpy

root = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
frames = sorted((root / 'frames').glob('orbit-*.png'))
assert len(frames) == 264, len(frames)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
strip = scene.sequence_editor_create().strips.new_image('Unity capture', str(frames[0]), 1, 1)
for frame in frames[1:]:
    strip.elements.append(frame.name)
scene.frame_start = 1
scene.frame_end = len(frames)
scene.render.resolution_x = 960
scene.render.resolution_y = 540
scene.render.resolution_percentage = 100
scene.render.fps = 24
scene.view_settings.view_transform = 'Standard'
scene.render.image_settings.file_format = 'FFMPEG'
scene.render.ffmpeg.format = 'MPEG4'
scene.render.ffmpeg.codec = 'H264'
scene.render.ffmpeg.constant_rate_factor = 'HIGH'
scene.render.filepath = str(root / 'unity-orbit.mp4')
bpy.ops.render.render(animation=True)

# Verify the video was decoded at the expected length and dimensions.
bpy.ops.wm.read_factory_settings(use_empty=True)
strips = bpy.context.scene.sequence_editor_create().strips
video = strips.new_movie('Review orbit', str(root / 'unity-orbit.mp4'), 1, 1)
assert video.frame_duration == len(frames), video.frame_duration
assert (video.elements[0].orig_width, video.elements[0].orig_height) == (960, 540)
print('Verified 264 frames at 960x540')
