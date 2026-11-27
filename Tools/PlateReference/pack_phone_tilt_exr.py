"""Pack each light size's 5×5 scene-linear phone-tilt grid into a half-float EXR."""

import argparse
from pathlib import Path

import numpy as np
import OpenEXR


def pack(folder, output):
    with OpenEXR.File(str(folder / '00.exr')) as first:
        height, width = first.channels()['RGB'].pixels.shape[:2]
        header = {key: first.header()[key] for key in ('chromaticities',) if key in first.header()}
    header.update(compression=OpenEXR.DWAA_COMPRESSION, dwaCompressionLevel=20.0,
                  type=OpenEXR.scanlineimage)
    atlas = np.empty((height * 5, width * 5, 3), dtype=np.float16)
    for index in range(25):
        row, col = divmod(index, 5)
        with OpenEXR.File(str(folder / f'{index:02}.exr')) as frame:
            atlas[row*height:(row+1)*height, col*width:(col+1)*width] = frame.channels()['RGB'].pixels
    OpenEXR.File(header, {'RGB': atlas}).write(str(output))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('frames', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    for folder, name in (('01-current', '100'), ('02-half', '50'), ('03-quarter', '25')):
        pack(args.frames / folder, args.output / f'{name}.exr')
