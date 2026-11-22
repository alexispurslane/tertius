# Tool code of t0-gbgj. wobvideo.py <out.mp4> <label|PNG folder|size log|edge dump>...(two sides): the side-by-side video
# of the wobble take, the first side left, the second right, each at the output's own 1920 x 1080. One video frame per
# kept film frame (140 to 459, film frame k is draw 33 + k), 60 frames a second, H.264, yuv420p, CRF 12, preset slow.
# On each side: its label at the top left, the draw's render scale and render size (from the side's size log) under it,
# and short cyan reference ticks above and below the tracked rows (output rows 525 to 554) at the mean places of the two
# window edges that wobble.py tracks (from its WOBBLE_DUMP lines, mean over the film). The ticks stay fixed, so an eye
# sees an edge move against them. The film frame, the draw and the lamps' state go at the bottom right of the right side.
# ffmpeg comes from nix shell nixpkgs#ffmpeg.
import subprocess
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

out, sides = sys.argv[1], [s.split('|') for s in sys.argv[2:]]
font = ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf', 34)
STILL = 33
info = []
for label, folder, size_log, dump in sides:
    log = [l.split() for l in open(size_log)]
    d = np.loadtxt(dump)
    left, right = (d[:, 2] - d[:, 1] / 2).mean(), (d[:, 2] + d[:, 1] / 2).mean()
    info.append((label, folder, log, (left, right)))

ff = subprocess.Popen(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
                       '-s', '3840x1080', '-framerate', '60', '-i', '-', '-c:v', 'libx264', '-preset', 'slow',
                       '-crf', '12', '-pix_fmt', 'yuv420p', '-r', '60', '-movflags', '+faststart', out],
                      stdin=subprocess.PIPE)


def box(d, xy, text):
    l, t, r, b = d.textbbox(xy, text, font=font)
    d.rectangle((l - 6, t - 6, r + 6, b + 6), fill=(0, 0, 0))
    d.text(xy, text, font=font, fill='white')


for k in range(140, 460):
    frame = Image.new('RGB', (3840, 1080))
    for n, (label, folder, log, edges) in enumerate(info):
        im = Image.open(f'{folder}/f-{k:06d}.png').convert('RGB')
        d = ImageDraw.Draw(im)
        for x in edges:
            d.line([(x, 495), (x, 514)], fill=(0, 255, 255), width=1)
            d.line([(x, 565), (x, 584)], fill=(0, 255, 255), width=1)
        _, w, h, scale = log[STILL + k]
        box(d, (18, 16), label)
        box(d, (18, 66), f'render scale {float(scale):.4f}, render size {w} x {h}')
        frame.paste(im, (1920 * n, 0))
    d = ImageDraw.Draw(frame)
    draw = STILL + k
    lamps = 'lamps on' if draw < 193 or draw >= 393 else 'lamps off'
    text = f'film frame {k}, draw {draw}, {lamps}'
    l, t, r, b = d.textbbox((0, 0), text, font=font)
    box(d, (3840 - (r - l) - 24, 1080 - (b - t) - 30), text)
    ff.stdin.write(frame.tobytes())
ff.stdin.close()
sys.exit(ff.wait())
