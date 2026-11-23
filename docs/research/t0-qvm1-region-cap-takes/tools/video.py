# Tool code of the region cap's takes (t0-qvm1). video.py <out.mp4> <first draw> <note> <side>...: a side-by-side video,
# one pane per side from left to right, each at the size of its PNGs (1920 x 1080 output, drawn at the fixed render scale
# 2/3, 1280 x 720). A side is "label|folder|render size": its folder holds one PNG per draw (film.py), in draw order, and
# the first PNG is draw <first draw>. A band above the panes holds each side's label and render size, the draw, and
# <note> (the exposure). One draw per video frame at 60 frames a second, H.264 yuv420p at CRF 12, no filter but the
# conversion to yuv420p (BT.709). FFMPEG names the ffmpeg binary (nix shell nixpkgs#ffmpeg).
import os
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

out, first, note, *specs = sys.argv[1:]
first = int(first)
sides = [s.split('|') for s in specs]
files = [sorted(Path(folder).glob('*.png')) for _, folder, _ in sides]
count = min(len(f) for f in files)
width, height = Image.open(files[0][0]).size
BAND = 72
total = (len(sides) * width, height + BAND)
try:
    font = ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf', 30)
except OSError:
    font = ImageFont.load_default()
command = [os.environ.get('FFMPEG', 'ffmpeg'), '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
           '-s', f'{total[0]}x{total[1]}', '-r', '60', '-i', '-', '-c:v', 'libx264', '-preset', 'slow', '-crf', '12',
           '-pix_fmt', 'yuv420p', '-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'bt709',
           '-movflags', '+faststart', out]
encoder = subprocess.Popen(command, stdin=subprocess.PIPE)
for k in range(count):
    frame = Image.new('RGB', total, (16, 16, 16))
    draw = ImageDraw.Draw(frame)
    for i, (label, _, size) in enumerate(sides):
        frame.paste(Image.open(files[i][k]).convert('RGB'), (i * width, BAND))
        draw.text((i * width + 16, 6), label, fill=(255, 255, 255), font=font)
        draw.text((i * width + 16, 38), f'render size {size}, draw {first + k}, {note}', fill=(200, 200, 200), font=font)
    encoder.stdin.write(frame.tobytes())
encoder.stdin.close()
encoder.wait()
print(f'{out}: {count} frames of {total[0]} x {total[1]}')
