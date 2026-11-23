# Tool code of t0-xwdz step 2, copied for t1-yi11. grid.py <out.mp4> <first draw> <note> <side>...: a video of up to four panes in a 2 x 2 grid
# (one or two sides: one row), each pane at the size of its PNGs (1920 x 1080 output, drawn at the headless render scale
# 2/3, 1280 x 720) with a band of 72 px above it that holds the side's label, its render size, the draw and <note> (the
# exposure). A side is "label|folder|render size": its folder holds one PNG per draw of one film, in draw order, and the
# first PNG is draw <first draw>; PREFIX picks the film (default: every PNG). One draw per video frame at 60 frames a
# second, H.264 yuv420p at CRF 12, no filter but the conversion to yuv420p (BT.709), as t0-qvm1's video.py. FFMPEG names
# the ffmpeg binary.
import os
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

out, first, note, *specs = sys.argv[1:]
first = int(first)
prefix = os.environ.get('PREFIX', '')
sides = [s.split('|') for s in specs]
files = [sorted(Path(folder).glob(f'{prefix}*.png')) for _, folder, _ in sides]
count = min(len(f) for f in files)
width, height = Image.open(files[0][0]).size
BAND = 72
columns = 2 if len(sides) > 1 else 1
rows = (len(sides) + 1) // 2
total = (columns * width, rows * (height + BAND))
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
        x, y = (i % columns) * width, (i // columns) * (height + BAND)
        frame.paste(Image.open(files[i][k]).convert('RGB'), (x, y + BAND))
        draw.text((x + 16, y + 6), label, fill=(255, 255, 255), font=font)
        draw.text((x + 16, y + 38), f'render size {size}, draw {first + k}, {note}', fill=(200, 200, 200), font=font)
    encoder.stdin.write(frame.tobytes())
encoder.stdin.close()
encoder.wait()
print(f'{out}: {count} frames of {total[0]} x {total[1]}')
