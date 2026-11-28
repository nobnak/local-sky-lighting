"""Add broken tool grooves to the existing packed strike slope map."""
import argparse
from pathlib import Path

import numpy as np
import OpenEXR
from PIL import Image
from scipy.ndimage import find_objects, label

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('source', type=Path)
parser.add_argument('direction', type=Path)
parser.add_argument('destination', type=Path)
args = parser.parse_args()
with OpenEXR.File(str(args.source), separate_channels=True) as exr:
    pixels = {name: channel.pixels.copy() for name, channel in exr.channels().items()}
    header = {name: exr.header()[name] for name in ('compression', 'type')}

polish = pixels['B'].astype(np.float32)
direction = Image.open(args.direction).convert('RGB').resize(polish.shape[::-1], Image.Resampling.BILINEAR)
grains = np.asarray(direction, dtype=np.float32)[:, :, :2] / 127.5 - 1
part, count = label(polish > .27)
ridges = np.zeros((*polish.shape, 2), dtype=np.float32)
rng = np.random.default_rng(147)
for index, region in enumerate(find_objects(part), 1):
    if region is None:
        continue
    y, x = np.indices(part[region].shape, dtype=np.float32)
    inside = part[region] == index
    if inside.sum() < 20:
        continue
    vectors = grains[region][inside].mean(axis=0)
    angle = .5 * np.arctan2(vectors[1], vectors[0])
    tangent = np.array([np.cos(angle), np.sin(angle)])
    across = np.array([-tangent[1], tangent[0]])
    u = x * tangent[0] + y * tangent[1]
    v = x * across[0] + y * across[1]
    phase = rng.uniform(0, 2 * np.pi, 3)
    coarse = np.sin(v * rng.uniform(.38, .67) + .45 * np.sin(u * .18 + phase[0]) + phase[1])
    broken = np.clip(.45 + .65 * np.sin(u * .33 + v * .14 + phase[2]), 0, 1)
    strength = .061 * coarse * broken * np.clip((polish[region] - .27) / .33, 0, 1) * inside
    ridges[region] += strength[..., None] * across

for index, channel in enumerate('RG'):
    pixels[channel] = np.clip(pixels[channel].astype(np.float32) + ridges[..., index], 0, 1).astype(np.float16)
OpenEXR.File(header, pixels).write(str(args.destination))
fraction = np.any(abs(ridges) > .004, axis=-1).mean()
print(f'Refined {count} polish regions, measurable slope detail over {fraction:.1%} of the texture')
