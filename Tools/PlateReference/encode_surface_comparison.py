"""Compose synchronized Unity renders into two labeled H.264 comparison videos."""
import argparse
import csv
from pathlib import Path
import subprocess

from PIL import Image, ImageDraw, ImageFont


LABELS = ("A   Current", "B   Continuous metal", "C   Overlapping strikes")
HEADER = 42


def composite(directory, index, font):
    frames = [Image.open(directory / f"{index:03}-{variant}.png").convert("RGB") for variant in range(3)]
    width, height = frames[0].size
    image = Image.new("RGB", (width * 3, height + HEADER), (14, 14, 14))
    draw = ImageDraw.Draw(image)
    for column, (frame, label) in enumerate(zip(frames, LABELS)):
        assert frame.size == (width, height)
        image.paste(frame, (width * column, HEADER))
        draw.text((width * column + width // 2, HEADER // 2), label, fill=(240, 240, 240),
                  anchor="mm", font=font)
    return image


def encode(root, ffmpeg, fps, stills_only=False):
    font = ImageFont.load_default(size=22)
    for mode in ("light", "orbit"):
        directory = root / mode
        with (directory / "poses.csv").open(newline="") as poses:
            rows = list(csv.DictReader(poses))
        frame_count = len(rows)
        assert frame_count > 2
        fixed = ("light_x", "light_y") if mode == "orbit" else ("azimuth",)
        assert all(len({row[key] for row in rows}) == 1 for key in (*fixed, "ortho_size"))
        assert len(list(directory.glob("???-?.png"))) == 3 * frame_count
        times = (0, frame_count // 2, frame_count - 1)
        sheets = [composite(directory, time, font) for time in times]
        width, height = sheets[0].size
        overview = Image.new("RGB", (width, 3 * height))
        for i, image in enumerate(sheets):
            overview.paste(image, (0, i * height))
        overview.resize((width * 3 // 4, 3 * height * 3 // 4), Image.Resampling.LANCZOS).save(
            root / f"{mode}-stills.png")
        if stills_only:
            continue
        video = root / f"{mode}-comparison.mp4"
        command = [ffmpeg, "-hide_banner", "-loglevel", "error", "-y",
                   "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{width}x{height}", "-r", str(fps),
                   "-i", "-", "-an", "-c:v", "libx264", "-crf", "15", "-pix_fmt", "yuv420p",
                   "-movflags", "+faststart", str(video)]
        with subprocess.Popen(command, stdin=subprocess.PIPE) as process:
            for index in range(frame_count):
                process.stdin.write(composite(directory, index, font).tobytes())
            process.stdin.close()
            if process.wait():
                raise RuntimeError(f"Could not encode {video}")
        print(f"{video}: {frame_count} frames at {fps} fps", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--fps", type=int, default=24)
    parser.add_argument("--stills-only", action="store_true")
    args = parser.parse_args()
    encode(args.root, args.ffmpeg, args.fps, args.stills_only)
