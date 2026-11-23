# Tool code of t1-g1jd. count.py <pics folder>: the count of short diffuse histories by writer, from the runs of queue.sh
# in /private/tmp/t0-5tyj/g1jd/runs/<build>-<view>-<run>/ (the dump of the slow diffuse history's length that one draw
# left, float32 per render pixel, and the still's captures v-s00 ... v-s27). Terms:
#   surface pixel   a render pixel whose length is above 0 (a sky pixel holds no history, length 0);
#   8 to 23         a length from 8 to 23 draws, both included (t1-g1jd's range: a cut to 4 or 8 draws, then growth);
#   under 31.5      a length under 31.5 draws (the region take's range: the cap is 32, and the reprojection's weights
#                   leave a settled pixel a little under it);
#   short           under 31.5, for the writers' sets.
# Per view, build and run: the counts and shares of both ranges among the surface pixels, and the median length. Per view
# and build: whether r1 and r2 are equal bit for bit (dump and captures), and the spread of each share between them. Per
# view, from r1 of each build: each writer's own share and the overlap, as sets of pixels:
#   anti-lag alone  short with region evidence off (g1-noreg) and not short with both off (g1-none);
#   region alone    short with the anti-lag off (g1-noal) and not short with both off;
#   overlap         short in both g1-noreg and g1-noal and not short with both off;
#   neither         short with both off (the third writer: the reprojection's history test, and growth from a restart).
# The four builds draw different noise once their histories differ, so the sets compare like shares, not like pixels.
# The grain: the survey's blotch score and its RMS (renderer-survey.py, blotch) of the capture whose draws first reach the
# dumped draw, against the mean of the captures v-s08 to v-s27 (the survey's reference), and its specks.
# It writes <pics folder>/sheet-<view>.png: the four builds' maps of r1, in the bands of t0-gumm's histmap.py, with white
# for a pixel that holds no history (length 0: sky, or an emitter such as a lamp's tube).
# Run it in the dev shell from the checkout's root.
import importlib.util
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location('survey', ROOT / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)

RUNS = Path('/private/tmp/t0-5tyj/g1jd/runs')
BUILDS = [('g1-main', 'main'), ('g1-noreg', 'region evidence off'), ('g1-noal', 'diffuse anti-lag off'),
          ('g1-none', 'both off')]
VIEWS = ['lamp', 'cabin', 'bay']
BANDS = [(-1e9, 1e-6, (255, 255, 255), '0: no history'), (1e-6, 8, (220, 50, 50), 'above 0, under 8 draws'),
         (8, 16, (240, 150, 40), '8 to 16 draws'), (16, 24, (240, 220, 60), '16 to 24 draws'),
         (24, 31.5, (120, 180, 230), '24 to 31.5 draws'), (31.5, 1e9, (60, 60, 60), '31.5 draws or more')]
pics = Path(sys.argv[1])
pics.mkdir(parents=True, exist_ok=True)


def dump(build, view, run):
    path = next((RUNS / f'{build}-{view}-{run}' / 'dump').glob('*.f32'))
    w, h = map(int, re.search(r'-(\d+)x(\d+)\.f32$', path.name).groups())
    draw = int(re.search(r'history-(\d+)-', path.name).group(1))
    return np.fromfile(path, dtype=np.float32).reshape(h, w), draw, path


def shares(L):
    surface = L > 0
    n = int(surface.sum())
    mid = int(((L >= 8) & (L <= 23)).sum())
    under = int((surface & (L < 31.5)).sum())
    return n, mid, under, float(np.median(L[surface]))


def grain(build, view, run, draw):
    frames = RUNS / f'{build}-{view}-{run}' / 'frames'
    k = max(0, -(-(draw + 1 - survey.STILL_DRAWS) // survey.STILL_DRAWS))
    shown = survey.luminance(frames / f'v-s{k:02d}.png')
    reference = sum(survey.luminance(frames / f'v-s{i:02d}.png') for i in range(8, 28)) / 20
    score, rms = survey.blotch(shown, reference)
    return k, score, rms, survey.specks(shown, reference)


def fmt(value):
    return 'n/a' if value is None else f'{value:.4f}'


def same(build, view):
    a, b = (RUNS / f'{build}-{view}-{r}' for r in ('r1', 'r2'))
    files = [p.relative_to(a) for p in sorted(a.rglob('*')) if p.suffix in ('.f32', '.png')]
    return all((b / f).exists() and (a / f).read_bytes() == (b / f).read_bytes() for f in files), len(files)


for view in VIEWS:
    print(f'\n== {view}')
    print(f"{'build':22s} {'run':3s} {'surface':>8s} {'8 to 23':>8s} {'share':>7s} {'< 31.5':>8s} {'share':>7s} {'median':>7s}"
          f"  grain: capture, blotch, RMS, specks")
    short = {}
    maps = []
    for build, label in BUILDS:
        row = {}
        for run in ('r1', 'r2'):
            L, draw, _ = dump(build, view, run)
            n, mid, under, median = shares(L)
            row[run] = (mid / n, under / n)
            k, score, rms, specks = grain(build, view, run, draw)
            print(f'{label:22s} {run:3s} {n:8d} {mid:8d} {100 * mid / n:6.2f}% {under:8d} {100 * under / n:6.2f}% {median:7.2f}'
                  f'  v-s{k:02d}, {fmt(score)}, {fmt(rms)}, {specks:.4f}')
            if run == 'r1':
                short[build] = (L > 0) & (L < 31.5)
                maps.append((label, L, draw))
        equal, count = same(build, view)
        print(f'{"":22s} r1 against r2: {"equal bit for bit" if equal else "differ"} ({count} files); spread of the shares '
              f'{100 * abs(row["r1"][0] - row["r2"][0]):.3f} and {100 * abs(row["r1"][1] - row["r2"][1]):.3f} points')
    n = int((dump('g1-main', view, 'r1')[0] > 0).sum())
    none = short['g1-none']
    antilag = short['g1-noreg'] & ~none
    region = short['g1-noal'] & ~none
    both = antilag & region
    for name, mask in (('anti-lag alone', antilag & ~region), ('region alone', region & ~antilag), ('overlap', both),
                       ('neither (both off)', none), ('main, all writers', short['g1-main'])):
        print(f'  {name:20s} {int(mask.sum()):8d} pixels, {100 * mask.sum() / n:6.2f}% of the surface pixels')
    # The sheet: the four maps of r1, two by two, each with its label and its two shares.
    h, w = maps[0][1].shape
    tiles = []
    for label, L, draw in maps:
        rgb = np.zeros((h, w, 3), np.uint8)
        for lo, hi, colour, _ in BANDS:
            rgb[(L >= lo) & (L < hi)] = colour
        n_, mid, under, median = shares(L)
        tile = Image.new('RGB', (w, h + 26), 'white')
        tile.paste(Image.fromarray(rgb), (0, 26))
        ImageDraw.Draw(tile).text((4, 6), f'{view}, {label}, draw {draw}: 8 to 23 draws {100 * mid / n_:.2f} %, under 31.5 '
                                          f'{100 * under / n_:.2f} %, median {median:.2f}', fill='black')
        tiles.append(tile)
    legend = Image.new('RGB', (2 * w + 10, 28), 'white')  # 170 px per band
    d = ImageDraw.Draw(legend)
    for i, (_, _, colour, name) in enumerate(BANDS):
        d.rectangle([10 + 170 * i, 8, 26 + 170 * i, 22], fill=colour, outline='black')
        d.text((32 + 170 * i, 9), name, fill='black')
    sheet = Image.new('RGB', (2 * w + 10, 2 * (h + 26) + 10 + 28), 'white')
    for i, tile in enumerate(tiles):
        sheet.paste(tile, ((i % 2) * (w + 10), (i // 2) * (h + 36)))
    sheet.paste(legend, (0, 2 * (h + 36) - 10))
    sheet.save(pics / f'sheet-{view}.png')
    print(f'  sheet: {pics / f"sheet-{view}.png"}')
