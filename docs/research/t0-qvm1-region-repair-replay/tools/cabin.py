# Tool code of the region repair's replay (t0-qvm1), part 1. cabin.py: is the still cabin a frozen case? From the runs of
# queue.sh r2-main-cabin-full and r2-noreg-cabin-full (/private/tmp/t0-5tyj/r2/runs/), per build:
#   - the share of the surface pixels (L above 0) whose slow diffuse history is under 31.5 draws, and from 8 to 23 draws,
#     at the dumped draws 190 and 890;
#   - the fires per tested anchor-draw (regionstat.py's test: 36 valid draws in a row, |D| > 3 a) in draws 100 to 190
#     and in draws 600 to the end;
#   - per anchor, the mean of x over draws 150 to 190 against the mean over the last 100 draws, as a relative difference
#     (early / late - 1): its median over the anchors, its largest size, and its median size;
#   - per span of 100 draws from draw 100: the fire rate, and the median relative difference of the span's mean light
#     from the last 100 draws;
#   - the grain of the capture that holds draw 190 (v-s05) and draw 890 (v-s26): the survey's blotch score, its RMS and
#     the specks, against the mean of the captures v-s08 to v-s27 of the same run.
# Run it in the dev shell from the checkout's root.
import importlib.util
import re
import sys
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import replay  # noqa: E402

ROOT = HERE.parents[3]
spec = importlib.util.spec_from_file_location('survey', ROOT / 'tools' / 'renderer-survey.py')
survey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(survey)


def dump(run, draw):
    path = next((run / 'dump').glob(f'history-{draw}-*.f32'))
    w, h = map(int, re.search(r'-(\d+)x(\d+)\.f32$', path.name).groups())
    return np.fromfile(path, dtype=np.float32).reshape(h, w)


def grain(run, capture):
    frames = run / 'frames'
    shown = survey.luminance(frames / f'v-s{capture:02d}.png')
    reference = sum(survey.luminance(frames / f'v-s{i:02d}.png') for i in range(8, 28)) / 20
    score, rms = survey.blotch(shown, reference)
    return score, rms, survey.specks(shown, reference)


def fmt(v):
    return 'n/a' if v is None else f'{v:.4f}'


for build in ('r2-main', 'r2-noreg'):
    run = replay.RUNS / f'{build}-cabin-full'
    print(f'\n== {build}, the still cabin')
    for draw in (190, 890):
        L = dump(run, draw)
        surface = L > 0
        n = int(surface.sum())
        under = int((surface & (L < 31.5)).sum())
        mid = int(((L >= 8) & (L <= 23)).sum())
        print(f'  draw {draw}: {n} surface pixels, under 31.5: {under} ({100 * under / n:.2f} %), 8 to 23: {mid} '
              f'({100 * mid / n:.2f} %), median {np.median(L[surface]):.2f}')
    draws, x, g, moved = replay.load(run / 'region' / 'region-none.txt')
    tested, D, s1, a = replay.statistics(x, g, 18)
    fire = tested & (np.abs(D) > 3 * a)
    agree = int((fire == moved)[tested].sum())
    print(f'  the replayed test agrees with the logged answer at {agree} of {int(tested.sum())} tested anchor-draws; '
          f'{int((fire != moved).sum())} disagree in all')
    for lo, hi in ((100, 191), (600, int(draws[-1]) + 1)):
        span = (draws >= lo) & (draws < hi)
        t, f = int(tested[span].sum()), int(fire[span].sum())
        print(f'  draws {lo} to {hi - 1}: {f} fires of {t} tested anchor-draws, {100 * f / max(t, 1):.2f} %')
    early = (draws >= 150) & (draws <= 190)
    late = draws > draws[-1] - 100
    ok = (g[early] > 0).all(0) & (g[late] > 0).all(0)
    rel = x[early].mean(0)[ok] / x[late].mean(0)[ok] - 1
    print(f'  the region light, draws 150 to 190 against the last 100 draws, over {int(ok.sum())} anchors: median '
          f'{100 * np.median(rel):+.2f} %, median size {100 * np.median(np.abs(rel)):.2f} %, largest size '
          f'{100 * np.abs(rel).max():.2f} % (at {100 * rel[np.argmax(np.abs(rel))]:+.2f} %)')
    # The same over two late spans of the same length, as the size of a frozen light's own scatter.
    a1 = (draws >= draws[-1] - 240) & (draws <= draws[-1] - 200)
    rel2 = x[a1].mean(0)[ok] / x[late].mean(0)[ok] - 1
    print(f'  for scale: draws {int(draws[-1]) - 240} to {int(draws[-1]) - 200} against the last 100: median size '
          f'{100 * np.median(np.abs(rel2)):.2f} %, largest size {100 * np.abs(rel2).max():.2f} %')
    # Frozen from which draw: per span of 100 draws, the fire rate and the median relative difference of the span's mean
    # light from the last 100 draws.
    spans = []
    for lo in range(100, int(draws[-1]) - 99, 100):
        span = (draws >= lo) & (draws < lo + 100)
        t, f = int(tested[span].sum()), int(fire[span].sum())
        rel3 = x[span].mean(0)[ok] / x[late].mean(0)[ok] - 1
        spans.append(f'{lo}-{lo + 99}: {100 * f / max(t, 1):.2f} %, {100 * np.median(rel3):+.2f} %')
    print('  per span (fires per tested anchor-draw, median light against the last 100 draws): ' + '; '.join(spans))
    for capture in (5, 26):
        score, rms, specks = grain(run, capture)
        print(f'  grain of v-s{capture:02d}: blotch {fmt(score)}, RMS {fmt(rms)}, specks {specks:.4f}')
