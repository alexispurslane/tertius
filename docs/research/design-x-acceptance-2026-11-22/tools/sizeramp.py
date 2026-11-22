# Tool code of t0-5tyj. sizeramp.py: the cabin's window jambs at every render size that the dynamic scale can produce,
# to find the sizes at which sunlight leaks onto them (the record's section on the jambs).
#
# The sizes: render_sizes() implements UpscalingSetting.RenderExtent of FSR 3.1 for the output 1920 x 1080 on main
# c4c1f1f2f and the line's head: each axis is floor(pixels * scale) in single precision, rounded down to a multiple of 8,
# at least 8 (UpscalingSetting.Scaled). A new pair starts wherever either axis steps, as the scale goes from 0.50 to 1.0.
# Replace render_sizes() when that rule changes (the wobble's second step on main gives pairs of an exact ratio).
#
#   sizeramp.py prepare <folder> [N]: writes into <folder>:
#     - script.json: scene orbit, camera person (the switch case "cabin"), 60 ticks, the still, then a film "f" of
#       N draws per size, one capture per draw; the lamps stay on (no TERTIUS_PROBE_LAMP_OFF);
#     - replay.txt: the scale of each draw for TERTIUS_X_SIZE_REPLAY. The still's 33 draws hold the first size, and film
#       frame k (draw 33 + k) holds size k // N;
#     - keep.txt: TERTIUS_PROBE_KEEP, the last film frame of each size (k = i N + N - 1), and for the settle check the
#       frame 10 draws before it (i N + N - 11) at the three sizes nearest the scales 0.70, 0.956 and 1.0.
#     N (default 40) is the number of draws per size. The jamb's light after a size change settles within it: the check
#     below compares each check size's last frame with the frame 10 draws before.
#   sizeramp.py measure <folder> <frames folder> <out prefix>: per size, the render size, the display-linear mean of the
#     right jamb crop (x 1770 to 1807, y 495 to 584) and of the left jamb crop (the nearest opening of the left wall,
#     x 113 to 145, y 500 to 579), and "leaks" where either mean is above LEAK. It prints the table, the count, the list of
#     leaking sizes and the settle check, writes <out prefix>.png (both means against the render width) and
#     <out prefix>.txt (the table), and deletes the frames of the sizes that do not leak.
import json
import os
import sys
import numpy as np
from PIL import Image, ImageDraw

OUTPUT = (1920, 1080)
STILL = 33
LEAK = 0.08
RIGHT = (1770, 495, 1808, 585)
LEFT = (113, 500, 146, 580)
CHECKS = (0.70, 0.956, 1.0)


def scaled(pixels, scale):
    return max(8, int(np.float32(pixels) * np.float32(scale)) // 8 * 8)


def render_sizes():
    """Each distinct (width, height) of RenderExtent for scales 0.50 to 1.0, with one scale inside its interval."""
    edges = {0.5, 1.0}
    for pixels in OUTPUT:
        for k in range(1, pixels // 8 + 1):
            s = 8 * k / pixels
            if 0.5 < s < 1.0:
                edges.add(s)
    edges = sorted(edges)
    sizes = {}
    for a, b in zip(edges, edges[1:]):
        for s in ((a + b) / 2, a, b):
            pair = (scaled(OUTPUT[0], s), scaled(OUTPUT[1], s))
            if pair not in sizes and 0.5 <= s <= 1.0:
                sizes[pair] = s
    return sorted(sizes.items(), key=lambda p: p[1])


def frame_name(k):
    return f'f-{k:06d}'


def prepare(folder, n):
    os.makedirs(folder, exist_ok=True)
    sizes = render_sizes()
    script = {'scene': 'orbit', 'window': {'width': OUTPUT[0], 'height': OUTPUT[1]},
              'steps': [{'do': 'camera', 'entity': 'person'}, {'do': 'run', 'ticks': 60}, {'do': 'capture', 'name': 'still'},
                        {'do': 'film', 'name': 'f', 'frames': len(sizes) * n, 'every': 1}]}
    with open(f'{folder}/script.json', 'w') as stream:
        json.dump(script, stream, indent=1)
    with open(f'{folder}/replay.txt', 'w') as stream:
        for d in range(STILL + len(sizes) * n):
            pair, s = sizes[max(0, d - STILL) // n]
            stream.write(f'{d} {pair[0]} {pair[1]} {s!r}\n')
    keep = [frame_name(i * n + n - 1) for i in range(len(sizes))]
    for c in CHECKS:
        i = min(range(len(sizes)), key=lambda j: abs(sizes[j][1] - c))
        keep.append(frame_name(i * n + n - 11))
    with open(f'{folder}/keep.txt', 'w') as stream:
        stream.write(','.join(keep))
    with open(f'{folder}/sizes.json', 'w') as stream:
        json.dump({'n': n, 'sizes': [[w, h, s] for (w, h), s in sizes]}, stream)
    print(f'{len(sizes)} sizes, {n} draws each, {STILL + len(sizes) * n} draws, {len(keep)} kept frames')


def mean(y, box):
    return float(y[box[1]:box[3], box[0]:box[2]].mean())


def luminance(path):
    a = np.asarray(Image.open(path).convert('RGB')).astype(float) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]


def measure(folder, frames, prefix):
    meta = json.load(open(f'{folder}/sizes.json'))
    n, sizes = meta['n'], meta['sizes']
    rows = []
    for i, (w, h, s) in enumerate(sizes):
        y = luminance(f'{frames}/{frame_name(i * n + n - 1)}.png')
        right, left = mean(y, RIGHT), mean(y, LEFT)
        rows.append((w, h, s, right, left, right > LEAK or left > LEAK))
    lines = [f'{"width":>6} {"height":>6} {"scale":>8} {"right":>7} {"left":>7}  leaks']
    lines += [f'{w:6d} {h:6d} {s:8.5f} {r:7.4f} {l:7.4f}  {"leaks" if k else ""}' for w, h, s, r, l, k in rows]
    leaking = [(w, h) for w, h, _, _, _, k in rows if k]
    lines.append(f'{len(rows)} sizes, {len(leaking)} leak (a jamb mean above {LEAK}): ' + ', '.join(f'{w} x {h}' for w, h in leaking))
    for c in CHECKS:
        i = min(range(len(sizes)), key=lambda j: abs(sizes[j][2] - c))
        before = luminance(f'{frames}/{frame_name(i * n + n - 11)}.png')
        lines.append(f'settle check at {sizes[i][0]} x {sizes[i][1]}: right {mean(before, RIGHT):.4f} 10 draws before the last '
                     f'frame, {rows[i][3]:.4f} at it; left {mean(before, LEFT):.4f}, {rows[i][4]:.4f}')
    print('\n'.join(lines))
    with open(f'{prefix}.txt', 'w') as stream:
        stream.write('\n'.join(lines) + '\n')
    W, H, L, B = 1300, 520, 70, 50
    im = Image.new('RGB', (W, H), 'white')
    d = ImageDraw.Draw(im)
    # The axis reaches at least the threshold, so that a ramp without a leak still draws the line of LEAK.
    top = max(LEAK, max(max(r[3], r[4]) for r in rows)) * 1.05
    x = lambda w: L + (w - 960) / 960 * (W - L - 20)
    y = lambda v: 30 + (top - v) / top * (H - 30 - B)
    for v in np.arange(0, top, 0.05):
        d.line([(L, y(v)), (W - 20, y(v))], fill=(230, 230, 230))
        d.text((8, y(v) - 6), f'{v:.2f}', fill='black')
    d.line([(L, y(LEAK)), (W - 20, y(LEAK))], fill=(160, 160, 160))
    for w in range(960, 1921, 64):
        d.text((x(w) - 14, H - B + 8), str(w), fill='black')
    for k, colour in ((3, (200, 60, 60)), (4, (40, 110, 200))):
        for r in rows:
            d.ellipse([x(r[0]) - 3, y(r[k]) - 3, x(r[0]) + 3, y(r[k]) + 3], fill=colour)
    d.text((L, 8), f'jamb crop mean (display-linear) per render size, lamps on, {n} draws per size: red right jamb, blue left '
                   f'jamb; grey line: {LEAK} (leaks above)', fill='black')
    d.text((L, H - 20), 'render width (output 1920 x 1080)', fill='black')
    im.save(f'{prefix}.png')
    for i, row in enumerate(rows):
        if not row[5]:
            path = f'{frames}/{frame_name(i * n + n - 1)}.png'
            if os.path.exists(path):
                os.remove(path)
    print(f'{prefix}.png {prefix}.txt; kept the frames of the {len(leaking)} leaking sizes')


if __name__ == '__main__':
    if sys.argv[1] == 'prepare':
        prepare(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 40)
    elif sys.argv[1] == 'measure':
        measure(*sys.argv[2:5])
    else:
        for (w, h), s in render_sizes():
            print(w, h, s)
