# Tool code of t0-5tyj (ticket t4f-71qp). cloudvideo.py <walk|switch> <frames folder> <out prefix>: in a cloud job
# (cloudvideo.sh), one side's frames of the video into <out prefix>.mp4, and their region means into
# <out prefix>-means.json. The frames: for the walk, the walk's last 60 draws (film "g", frames 60 to 119) and the 200
# draws after the stop (film "h", frames 0 to 199); for the switch, film "f" frames 140 to 459 (20 draws before the lamps
# go off at frame 160, 200 draws off, and 100 draws after they come on at 360). The encoding is H.264 yuv420p (BT.709) at
# CRF 6 and full size. A job returns at most 8 MB in base64 (about 6 MB of files) for both sides together, so a side over
# BUDGET bytes is encoded again at 1280 x 720, and then at CRF 8, 10 and up to 18, until it fits. The means: per frame, the display-linear luminance of the 3 x 3 regions of the
# full-size source frame, so that the Mac can check the composed video against the source.
import json
import os
import subprocess
import sys
import tempfile
import numpy as np
from PIL import Image

scene, frames, prefix = sys.argv[1:4]
BUDGET = 2_600_000
if scene == 'walk':
    names = [f'g-{k:06d}' for k in range(60, 120)] + [f'h-{k:06d}' for k in range(200)]
else:
    names = [f'f-{k:06d}' for k in range(140, 460)]


def regions(path):
    a = np.asarray(Image.open(path).convert('RGB')).astype(float) / 255
    a = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]
    h, w = a.shape
    return [float(a[r * h // 3:(r + 1) * h // 3, c * w // 3:(c + 1) * w // 3].mean()) for r in range(3) for c in range(3)]


missing = [n for n in names if not os.path.exists(f'{frames}/{n}.png')]
if missing:
    print(f'{prefix}: {len(missing)} frames missing, first {missing[0]}')
    sys.exit(1)
with open(f'{prefix}-means.json', 'w') as stream:
    json.dump({'names': names, 'means': [regions(f'{frames}/{n}.png') for n in names]}, stream)
with tempfile.TemporaryDirectory() as work:
    for i, n in enumerate(names):
        os.symlink(os.path.abspath(f'{frames}/{n}.png'), f'{work}/c-{i:04d}.png')
    for size, crf in (('1920:1080', 6), *[('1280:720', c) for c in (6, 8, 10, 12, 14, 16, 18)]):
        subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-framerate', '60', '-i', f'{work}/c-%04d.png', '-c:v', 'libx264',
                        '-preset', 'slow', '-crf', str(crf), '-vf', f'scale={size}:flags=lanczos:out_color_matrix=bt709:out_range=tv',
                        '-pix_fmt', 'yuv420p', '-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'iec61966-2-1',
                        '-color_range', 'tv', f'{prefix}.mp4'], check=True)
        bytes_ = os.path.getsize(f'{prefix}.mp4')
        print(f'{prefix}.mp4: {size} CRF {crf}, {bytes_ / 1e6:.2f} MB, {len(names)} frames')
        if bytes_ <= BUDGET:
            break
