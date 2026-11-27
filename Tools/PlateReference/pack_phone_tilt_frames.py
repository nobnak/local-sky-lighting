from pathlib import Path
import argparse
from PIL import Image

parser = argparse.ArgumentParser(description="Pack the frontal phone-tilt renders for Unity.")
parser.add_argument("frames", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
side, width, height = 5, 640, 382
for name, folder in (("current", "01-current"), ("half", "02-half"), ("quarter", "03-quarter")):
    atlas = Image.new("RGB", (width*side, height*side))
    for index in range(side*side):
        path = args.frames / folder / f"{index:02}.png"
        with Image.open(path) as frame:
            if frame.size != (width, height):
                raise ValueError(f"Expected {width} x {height}: {path}")
            atlas.paste(frame, (width*(index%side), height*(index//side)))
    atlas.save(args.output / f"{name}.jpg", quality=91, subsampling=0, optimize=True)
