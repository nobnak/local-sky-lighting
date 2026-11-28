"""Bake an overlapping hammer pattern in the plate's existing object-space projection."""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image


def stamp_surface(directory, size=1536):
    rng = np.random.default_rng(6481)
    extent = 3.24
    spacing = extent / size
    coords = (np.arange(size, dtype=np.float32) + .5) * spacing - extent / 2
    height = np.zeros((size, size), np.float32)
    finish = np.full_like(height, .4)
    grain = np.zeros((*height.shape, 2), np.float32)
    coverage = np.zeros_like(height, dtype=bool)
    for step, jitter, long_axis, short_axis in (
        (.065, .004, (.059, .083), (.056, .069)),
        (.083, .030, (.038, .078), (.021, .047)),
    ):
        rows = np.arange(-1.69, 1.71, step)
        for row, center_y in enumerate(rows):
            for center_x in rows:
                cx = center_x + rng.uniform(-jitter, jitter)
                cy = center_y + rng.uniform(-jitter, jitter)
                if step > .07:
                    cx += .018 * (row % 2)
                if cx * cx + cy * cy > 1.73 ** 2:
                    continue
                xs = slice(*np.searchsorted(coords, (cx-.104, cx+.104)))
                ys = slice(*np.searchsorted(coords, (cy-.104, cy+.104)))
                patch = height[ys, xs]
                if not patch.size:
                    continue
                dx = coords[None, xs] - cx
                dy = coords[ys, None] - cy
                angle = .22 + .42 * np.sin(2.1*cx - 1.9*cy) + rng.normal(0, .44)
                c, s = np.cos(angle), np.sin(angle)
                u = (c*dx + s*dy) / rng.uniform(*long_axis)
                v = (-s*dx + c*dy) / rng.uniform(*short_axis)
                phase = rng.uniform(0, 2*np.pi)
                shape = rng.uniform(2.1, 3.7)
                distance = (np.abs(u)**shape + np.abs(v)**shape)**(1 / shape)
                distance += .024*np.sin(5*u + 3*v + phase)
                t = np.clip((1.10 - distance) / .38, 0, 1)
                weight = t*t*(3 - 2*t)
                coverage[ys, xs] |= distance <= .96

                # Fit a local tool face to the existing sheet. Its rounded shoulder
                # leaves part of the previous face visible where successive blows meet.
                shoulder = (distance > .83) & (distance < 1.15)
                base = float(patch[shoulder].mean()) if shoulder.any() else 0
                tilt_x, tilt_y = rng.normal(0, (.063, .056))
                face = base - rng.uniform(.00045, .00125) + tilt_x*dx + tilt_y*dy
                face += rng.uniform(.0004, .00115) * (u*u + .75*v*v)
                broken = .5 + .5*np.sin(7*u - 3*v + phase)
                scratches = np.sin(19*v + 1.4*np.sin(6*u + phase))
                face += .00012 * broken * scratches
                patch += weight * (face - patch)

                polish = rng.uniform(.32, .94) + .11*np.sin(5*u - 3*v + phase) + .065*broken*scratches
                local_finish = finish[ys, xs]
                local_finish += weight * (np.clip(polish, .13, .99) - local_finish)
                direction = angle + .20*np.sin(4*u - 2*v + phase)
                local_grain = grain[ys, xs]
                local_grain[..., 0] += weight * (.85*np.cos(2*direction) - local_grain[..., 0])
                local_grain[..., 1] += weight * (.85*np.sin(2*direction) - local_grain[..., 1])

    interior = coords[:, None]**2 + coords[None, :]**2 <= 1.50**2
    covered = coverage[interior].mean()
    print(f"Tool-face coverage within radius 1.50: {covered:.4%}")
    assert covered >= .999, covered
    dy, dx = np.gradient(height, spacing)
    surface = np.stack((.5-dx, .5-dy, finish, np.ones_like(finish)), axis=-1)
    direction = np.zeros((*height.shape, 4), dtype=np.uint8)
    direction[..., :2] = np.rint(np.clip(.5 + .5*grain, 0, 1) * 255)
    direction[..., 3] = 255
    directory.mkdir(parents=True, exist_ok=True)
    surface.astype("<f2").tofile(directory / "overlap-surface.rgba16f")
    direction.tofile(directory / "overlap-grain.rgba8")
    normal_preview = np.dstack((.5 - dx * 2, .5 - dy * 2, finish))
    Image.fromarray(np.rint(np.clip(normal_preview[::-1], 0, 1) * 255).astype(np.uint8)).save(
        directory / "surface-preview.png")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    stamp_surface(parser.parse_args().directory)
