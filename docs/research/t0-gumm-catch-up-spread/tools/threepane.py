# Tool code of t0-gumm. threepane.py <out.mp4> <label>=<run folder> <label>=<run folder> <label>=<run folder>: a video of
# three Mac picture runs of macrun.sh side by side, after compact.sh packed each run's kept pictures into frames.mp4 and
# listed their names in frames.txt. The runs must keep the same pictures. Each pane is the film's 1920 x 1080 picture
# scaled with Lanczos to 960 x 540. The pane's label sits at its top left, and the run's draw at the bottom right. An f-
# picture's number is its draw. The cut film (cut.json) draws the still's 33 draws first, so a-<n> is draw 33 + n, the
# one x-000000 picture is the draw after the last a- picture (draw 133 + k, k the shift of the cut), and b-<n> is draw
# x + 1 + n. The pictures play in draw order (frames.txt lists them by name, so the cut's x picture comes last there), at
# 30 a second, one video frame per picture. H.264, yuv420p, CRF 14.
# ffmpeg and Pillow come from the dev shell.
import subprocess
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

out = sys.argv[1]
panes = [a.split('=', 1) for a in sys.argv[2:]]
W, H, PW, PH = 1920, 1080, 960, 540
names = [Path(f, 'frames.txt').read_text().split() for _, f in panes]
assert all(n == names[0] for n in names), 'the runs keep different pictures'
names = names[0]


STILL = 33
a_last = max((int(n[2:8]) for n in names if n[0] == 'a'), default=None)
x_draw = None if a_last is None else STILL + a_last + 1


def draw_of(name):
    kind, number = name[0], int(name[2:8])
    return {'f': number, 'a': STILL + number, 'x': x_draw, 'b': (x_draw or 0) + 1 + number}[kind]


order = sorted(range(len(names)), key=lambda i: draw_of(names[i]))


def frames(folder):
    p = subprocess.Popen(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-i', str(Path(folder, 'frames.mp4')),
                          '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], stdout=subprocess.PIPE)
    out = []
    while chunk := p.stdout.read(W * H * 3):
        out.append(Image.frombytes('RGB', (W, H), chunk).resize((PW, PH), Image.LANCZOS))
    p.wait()
    return out


decoded = [frames(f) for _, f in panes]
assert all(len(d) == len(names) for d in decoded), 'frames.mp4 and frames.txt differ in length'
font = ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf', 24)
enc = subprocess.Popen(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
                        '-s', f'{PW * len(panes)}x{PH}', '-r', '30', '-i', '-', '-c:v', 'libx264', '-preset', 'slow',
                        '-crf', '14', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', out], stdin=subprocess.PIPE)
for i in order:
    frame = Image.new('RGB', (PW * len(panes), PH))
    d = ImageDraw.Draw(frame)
    for j, ((label, _), dec) in enumerate(zip(panes, decoded)):
        frame.paste(dec[i], (j * PW, 0))
        for text, anchor, xy in ((label, 'la', (j * PW + 12, 10)),
                                 (f'draw {draw_of(names[i])}', 'rd',
                                  ((j + 1) * PW - 12, PH - 10))):
            box = d.textbbox(xy, text, font=font, anchor=anchor)
            d.rectangle((box[0] - 6, box[1] - 6, box[2] + 6, box[3] + 6), fill=(0, 0, 0))
            d.text(xy, text, font=font, anchor=anchor, fill='white')
    enc.stdin.write(frame.tobytes())
enc.stdin.close()
enc.wait()
print(out, len(names), 'frames')
