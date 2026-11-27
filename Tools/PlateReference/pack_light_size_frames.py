"""Pack one Blender camera sweep per source size for the Unity Web viewer."""
import argparse
from pathlib import Path

from PIL import Image

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("frames", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
width, height = 800, 478
columns, rows = 4, 3
for name, folder in (("current", "01-current"), ("half", "02-half"), ("quarter", "03-quarter")):
    atlas = Image.new("RGB", (width*columns, height*rows))
    for index in range(11):
        path = args.frames / folder / f"{index:02}.png"
        with Image.open(path) as frame:
            if frame.size != (width, height):
                raise ValueError(f"Expected {width} x {height} at {path}, got {frame.size}")
            atlas.paste(frame, (width*(index%columns), height*(index//columns)))
    atlas.save(args.output / f"{name}.jpg", quality=92, subsampling=0, optimize=True)
