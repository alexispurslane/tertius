# Tool code of t0-5tyj. walkvideo.py <out.mp4> <left label=walkframes folder> <right label=walkframes folder>: the video
# of the walk cabin-walk's end and stop, from the runs of walkframes.sh: the last 60 draws of the walk (film "g", frames
# 60 to 119) and the 200 draws after the stop (film "h", frames 0 to 199), one draw per video frame at 60 frames a
# second, the two sides side by side at full size (3840 x 1080), each with its label, and a counter of the draws after
# the stop (negative while the camera still walks). The frames are the probe's, at its fixed exposure, so both sides
# share one exposure. H.264 in yuv420p at CRF 12, BT.709 limited range, with no filter but the frame composition. SIDE
# (default 1920x1080) sets each side's size: a smaller side is scaled down with Lanczos. FFMPEG names the ffmpeg binary.
# The check decodes the video's frames at the stop, 20 and 40 draws after it and the last frame (199), and compares the
# fade with the source frames: per side and region of the 3 x 3 grid, the display-linear mean of the frame over that of
# frame 199, decoded against source. It prints the largest difference of that ratio, in % of the light. (The codec
# smooths the frames' per-pixel grain, which lowers every frame's display-linear mean by about 3 % alike. The ratio to
# frame 199 cancels that, so it shows whether the video keeps the fade.) With CHECK_DIR set, it saves the decoded frames at
# the stop and 40 draws after it there as PNGs, to look at.
import os
import subprocess
import sys
import tempfile
import numpy as np
from PIL import Image, ImageDraw, ImageFont

out, (left_label, left), (right_label, right) = sys.argv[1], *[a.rsplit('=', 1) for a in sys.argv[2:4]]
FFMPEG = os.environ.get('FFMPEG', 'ffmpeg')
NAMES = [(f'g-{k:06d}', k - 120) for k in range(60, 120)] + [(f'h-{k:06d}', k) for k in range(200)]
W, H = map(int, os.environ.get('SIDE', '1920x1080').split('x'))
try:
    FONT = ImageFont.load_default(size=28)
except TypeError:
    FONT = ImageFont.load_default()


def label(draw, x, text):
    box = draw.textbbox((x + 16, 14), text, font=FONT)
    draw.rectangle((box[0] - 8, box[1] - 6, box[2] + 8, box[3] + 6), fill=(0, 0, 0))
    draw.text((x + 16, 14), text, fill=(255, 255, 255), font=FONT)


def side_image(path):
    im = Image.open(path).convert('RGB')
    return im if im.size == (W, H) else im.resize((W, H), Image.LANCZOS)


def linear_regions(a):
    a = a.astype(float) / 255
    a = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]
    h, w = a.shape
    return np.array([a[r * h // 3:(r + 1) * h // 3, c * w // 3:(c + 1) * w // 3].mean() for r in range(3) for c in range(3)])


with tempfile.TemporaryDirectory(dir=os.path.dirname(os.path.abspath(out))) as work:
    for i, (name, after) in enumerate(NAMES):
        frame = Image.new('RGB', (2 * W, H))
        frame.paste(side_image(f'{left}/{name}.png'), (0, 0))
        frame.paste(side_image(f'{right}/{name}.png'), (W, 0))
        d = ImageDraw.Draw(frame)
        label(d, 0, left_label)
        label(d, W, right_label)
        counter = f'draws after the stop: {after:+d}' if after < 0 else f'draws after the stop: {after}'
        box = d.textbbox((0, 0), counter, font=FONT)
        label(d, 2 * W - (box[2] - box[0]) - 48, counter)
        frame.save(f'{work}/c-{i:04d}.png', compress_level=1)
    subprocess.run([FFMPEG, '-y', '-loglevel', 'error', '-framerate', '60', '-i', f'{work}/c-%04d.png', '-c:v', 'libx264',
                    '-preset', 'slow', '-crf', '12', '-vf', 'scale=out_color_matrix=bt709:out_range=tv', '-pix_fmt', 'yuv420p',
                    '-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'iec61966-2-1', '-color_range', 'tv',
                    '-movflags', '+faststart', out], check=True)
print(out, f'{os.path.getsize(out) / 1e6:.1f} MB', f'{len(NAMES)} frames at 60 per second, {2 * W} x {H}')
first_after = next(i for i, (_, a) in enumerate(NAMES) if a == 0)


def decoded(i):
    raw = subprocess.run([FFMPEG, '-loglevel', 'error', '-i', out, '-vf', f'select=eq(n\\,{i}),scale=in_color_matrix=bt709:in_range=tv',
                          '-vframes', '1', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], check=True, capture_output=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(H, 2 * W, 3)


# The labels cover the top of r00 and r02 on both sides, so those two regions are left out.
KEEP = [1, 3, 4, 5, 6, 7, 8]
last = len(NAMES) - 1
video_last = decoded(last)
for after in (0, 20, 40):
    i = first_after + after
    video = decoded(i)
    if os.environ.get('CHECK_DIR') and after in (0, 40):
        Image.fromarray(video).save(f"{os.environ['CHECK_DIR']}/decoded-{after:03d}.png")
    cells = []
    for side, folder in ((0, left), (1, right)):
        part = lambda a: a[:, side * W:(side + 1) * W]
        fade_video = linear_regions(part(video)) / linear_regions(part(video_last))
        fade_source = (linear_regions(np.asarray(side_image(f'{folder}/{NAMES[i][0]}.png')))
                       / linear_regions(np.asarray(side_image(f'{folder}/{NAMES[last][0]}.png'))))
        cells.append(f'source fade r11 {100 * (fade_source[4] - 1):+.2f} %, video {100 * (fade_video[4] - 1):+.2f} %, '
                     f'largest difference {100 * np.max(np.abs(fade_video[KEEP] - fade_source[KEEP])):.2f} %')
    print(f'draw {after} against draw 199: {left_label}: {cells[0]}; {right_label}: {cells[1]}')
