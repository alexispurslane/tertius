# lengths.py <truth> <variant...>: the count of history lengths of each variant's still (TERTIUS_X_LENGTHS of the
# scratch probe, see take.sh). For each signal (diffuse, specular) and each region of light.py, it prints the share of
# the pixel draws whose slow-history length is below the full rate (8 draws by default) and the mean length in draws,
# over the counted draws that draws.txt lists. Regions are light.py's polygons cut to the truth's smooth pixels, at
# 1920 x 1080, read at the center of each render pixel of 1280 x 720.
import sys, numpy as np
from pathlib import Path
from light import REGIONS, mask, scene, smooth_of

def counted(v):
    rows = [l.split() for l in open(f's/{v}/lengths/draws.txt')]
    return min(int(r[0]) for r in rows), max(int(r[0]) for r in rows), max(int(r[4]) for r in rows)

if __name__ == '__main__':
    truth, variants = sys.argv[1], sys.argv[2:]
    for case, regions in REGIONS.items():
        try:
            smooth = smooth_of(scene(f's/{truth}/images/still/{case}-reference.png'))
        except FileNotFoundError:
            continue
        for v in variants:
            folder = Path(f's/{v}/lengths')
            if not (folder / 'draws.txt').exists() or not Path(f's/{v}/images/still/{case}.png').exists():
                continue
            first, last, draws = counted(v)
            for light in ('diffuse', 'specular'):
                raw = np.fromfile(folder / f'{light}.f32', dtype='<f4')
                h, w = 720, 1280
                pairs = raw.reshape(h, w, 2)
                ys = ((2 * np.arange(h) + 1) * 1080 // (2 * h))[:, None]; xs = ((2 * np.arange(w) + 1) * 1920 // (2 * w))[None, :]
                cells = []
                for name, polys in regions.items():
                    m = (mask(polys) & smooth)[ys, xs]
                    cells.append(f"{name} below {100 * pairs[..., 1][m].mean():5.1f}% mean {pairs[..., 0][m].mean():5.2f}")
                print(f"{case:10s} {v:12s} {light:8s} draws {first}-{last} ({draws}) " + "   ".join(cells))
