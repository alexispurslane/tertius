# Tool code of ter-qt72's speck comparison (the orchestrator's size table for t7-3e22). From the ten films' counts and
# size logs (specks/films/<side>-<log>/specks.json), every render size that one film held for at least 10 frames, per
# code: its burst share (frames with more than 5 specks over its frames, over all films of that code), and the
# properties of that size that a rule in main's renderer can depend on:
#   - the parity of the width and the height at each level of the depth pyramid and the light pyramid. Both take their
#     level sizes from DepthPyramid.LevelSize: each side halved and rounded up, level times. The string gives level 0
#     (the render size) first, "e" for even and "o" for odd, up to the level where the side is 1.
#   - the width and the height modulo 2: the diffuse ray's 2 x 2 block (ScreenTraceImages: blocks of (w + 1) / 2).
#     Main has no 8 x 8 evidence block and no 17 px anchor region (those are Design X's), so no modulo 8 or 17 rule.
#   - the realized render scale per axis and the upscaler's ratio per axis (output over render).
#   - the near lamp's size in render pixels: about 120 x 72 output pixels in the films' frames, times the scale.
# Usage: python3 sizetable.py <specks dir>
import json, sys
from collections import defaultdict
from pathlib import Path

root = Path(sys.argv[1]) / 'films'


def parities(n):
    out = ''
    while True:
        out += 'e' if n % 2 == 0 else 'o'
        if n == 1:
            return out
        n = (n + 1) // 2


rows = []
for side in ('old', 'new'):
    held = defaultdict(lambda: [0, 0])  # size -> [burst frames, frames], over sizes that one film held >= 10 frames
    per_film = defaultdict(lambda: defaultdict(lambda: [0, 0]))
    for film in sorted(root.glob(f'{side}-*')):
        data = json.loads((film / 'specks.json').read_text())
        for c, s in zip(data['counts'], data['sizes']):
            per_film[film.name][s][1] += 1
            per_film[film.name][s][0] += c > 5
    for film, sizes in per_film.items():
        for s, (b, n) in sizes.items():
            if n >= 10:
                held[s][0] += b
                held[s][1] += n
    for s, (b, n) in held.items():
        w, h = map(int, s.split('x'))
        rows.append((side, w, h, b, n))

print('| Code | Size | Burst frames / frames | Share | w, h mod 2 | Width parity by level | Height parity by level |'
      ' Scale x, y | Upscale x, y | Near lamp (render px) |')
print('|---|---|---|---|---|---|---|---|---|---|')
for side, w, h, b, n in sorted(rows, key=lambda r: (r[0] != 'old', r[1], r[2])):
    code = 'ac5f23915' if side == 'old' else 'step 2'
    print(f'| {code} | {w} x {h} | {b} / {n} | {b / n:.2f} | {w % 2}, {h % 2} | {parities(w)} | {parities(h)} | '
          f'{w / 1920:.4f}, {h / 1080:.4f} | {1920 / w:.4f}, {1080 / h:.4f} | {120 * w / 1920:.0f} x {72 * h / 1080:.0f} |')
