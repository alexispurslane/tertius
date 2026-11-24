# Tool code of ter-fe63: a side-by-side film of one box, each take's frames n-<k>.png for k in [first, end), cropped
# to the box at 1:1, labeled, written as PNG frames and encoded with ffmpeg at 60 frames a second, H.264 at CRF 12.
# Usage: python3 -I video.py <box x0,y0,x1,y1> <first-end> <out.mp4> <label=take dir>...
import subprocess, sys, tempfile
from pathlib import Path
from PIL import Image, ImageDraw
x0, y0, x1, y1 = map(int, sys.argv[1].split(',')); a, b = map(int, sys.argv[2].split('-')); out = sys.argv[3]
takes = [(t.split('=')[0], Path(t.split('=')[1])) for t in sys.argv[4:]]
w, h = x1 - x0, y1 - y0
W = len(takes) * w + 8 * (len(takes) - 1); W += W % 2; H = h + 22; H += H % 2
with tempfile.TemporaryDirectory() as tmp:
    for i, k in enumerate(range(a, b)):
        s = Image.new('RGB', (W, H), 'white'); d = ImageDraw.Draw(s)
        for j, (label, t) in enumerate(takes):
            s.paste(Image.open(t / f'n-{k:06d}.png').convert('RGB').crop((x0, y0, x1, y1)), (j * (w + 8), 22))
            d.text((j * (w + 8) + 4, 5), f'{label}  frame {k}', fill='black')
        s.save(f'{tmp}/f{i:05d}.png')
    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-framerate', '60', '-i', f'{tmp}/f%05d.png', '-c:v', 'libx264',
                    '-crf', '12', '-pix_fmt', 'yuv420p', out], check=True)
print(out)
