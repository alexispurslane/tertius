# Tool code of the region cap's takes (t0-qvm1). stills.py <out folder>: the measures of the stills of still.py on main's
# code (pm) and the cap (pc), from /private/tmp/t0-qvm1c/runs/<build>-<view>/:
#   - the share of the surface pixels (a history length above 0) whose slow diffuse history is under 31.5 draws, and
#     under 24, at draw 131 (the survey's shown still) and draw 890 (the late still);
#   - the grain of the captures that hold those draws (v-s03 and v-s26): the survey's blotch score and its RMS, and the
#     specks, against the mean of the captures v-s08 to v-s27 of the same run (tools/renderer-survey.py);
#   - the exposure of capture 26 in stops and its step per draw from capture 25, (stops 26 - stops 25) / 33;
#   - <out folder>/lengths-<view>.png: per build, the still of capture 26 beside the map of the lengths at draw 890, in the
#     bands of t0-gumm's histmap.py plus white for no history (sky).
# It prints one Markdown table row per view and build. Run it in the dev shell from the checkout's root.
import importlib.util
import json
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location('survey', ROOT / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)

RUNS = Path('/private/tmp/t0-qvm1c/runs')
BUILDS = {'pm': 'main 76a4fbfeb', 'pc': 'the region cap'}
VIEWS = {'lamp': 'lamp hall', 'cabin': 'still cabin', 'bay': 'bay', 'cabinoff': 'cabin, lamps off'}
BANDS = [(0, 1e-9, (255, 255, 255), 'no history'), (1e-9, 8, (220, 50, 50), 'under 8'),
         (8, 16, (240, 150, 40), '8 to 16'), (16, 24, (240, 220, 60), '16 to 24'),
         (24, 31.5, (120, 180, 230), '24 to 31.5'), (31.5, 1e9, (60, 60, 60), '31.5 or more')]


def lengths(run, draw):
    path = next((run / 'dump').glob(f'history-{draw}-*.f32'))
    w, h = map(int, re.search(r'-(\d+)x(\d+)\.f32$', path.name).groups())
    return np.fromfile(path, dtype=np.float32).reshape(h, w)


def grain(run, capture):
    frames = run / 'frames'
    shown = survey.luminance(frames / f'v-s{capture:02d}.png')
    reference = sum(survey.luminance(frames / f'v-s{i:02d}.png') for i in range(8, 28)) / 20
    score, rms = survey.blotch(shown, reference)
    return score, rms, survey.specks(shown, reference)


def band_map(L):
    rgb = np.zeros((*L.shape, 3), np.uint8)
    for lo, hi, colour, _ in BANDS:
        rgb[(L >= lo) & (L < hi)] = colour
    return Image.fromarray(rgb)


out = Path(sys.argv[1])
out.mkdir(parents=True, exist_ok=True)
print('| Still | Build | Under 31.5 draws, draw 131 / 890 | Under 24 draws, draw 131 / 890 | Blotch / RMS / specks, draw 131 | '
      'Blotch / RMS / specks, draw 890 | Exposure at draw 890, step per draw |')
print('|' + '---|' * 7)
for view, title in VIEWS.items():
    rows = []
    for build, label in BUILDS.items():
        run = RUNS / f'{build}-{view}'
        if not (run / 'dump').exists():
            continue
        shares = []
        for draw in (131, 890):
            L = lengths(run, draw)
            surface = L > 0
            n = surface.sum()
            shares.append((100 * (surface & (L < 31.5)).sum() / n, 100 * (surface & (L < 24)).sum() / n))
        g = [grain(run, c) for c in (3, 26)]
        stops = {e['capture']: e['stops'] for e in json.loads((run / 'frames' / 'exposure.json').read_text())}
        step = (stops['v-s26'] - stops['v-s25']) / 33
        print(f"| {title} | {label} | {shares[0][0]:.2f} / {shares[1][0]:.2f} % | {shares[0][1]:.2f} / {shares[1][1]:.2f} % | "
              + ' | '.join(f'{s:.4f} / {r:.4f} / {k:.3f}' for s, r, k in g)
              + f" | {stops['v-s26']:+.3f} stops, {step:+.5f} |")
        L = lengths(run, 890)
        h, w = L.shape
        pic = Image.open(run / 'frames' / 'v-s26.png').convert('RGB').resize((w, h), Image.LANCZOS)
        row = Image.new('RGB', (2 * w + 10, h + 28), 'white')
        row.paste(pic, (0, 28))
        row.paste(band_map(L), (w + 10, 28))
        ImageDraw.Draw(row).text((4, 6), f'{title}, {label}: the still at draw 890 ({stops["v-s26"]:+.2f} stops), and its '
                                 f'history lengths ({shares[1][0]:.1f} % under 31.5 draws)', fill='black')
        rows.append(row)
    if rows:
        key = Image.new('RGB', (rows[0].width, 30), 'white')
        d = ImageDraw.Draw(key)
        for i, (_, _, colour, name) in enumerate(BANDS):
            d.rectangle((10 + 160 * i, 8, 26 + 160 * i, 24), fill=colour, outline='black')
            d.text((32 + 160 * i, 10), name, fill='black')
        sheet = Image.new('RGB', (rows[0].width, sum(r.height for r in rows) + key.height), 'white')
        y = 0
        for r in rows:
            sheet.paste(r, (0, y))
            y += r.height
        sheet.paste(key, (0, y))
        sheet.save(out / f'lengths-{view}.png')
