"""Diagnostic tool code: the volume's cell faces that hold the helmet, from two dumps of one frame (normal helmet, black
helmet). For each face whose summed reflectance differs, it prints the level, the summed mean reflectance, the helmet's
share of the reflectance, the saturation of the sum, the factor that restrainNeutral applies, and the outgoing radiance.
usage: helmet_buckets.py <normal.bin> <black.bin>"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vol2 import Vol, LEVELS, SIDES, FIRST


def smooth(e0, e1, x):
    t = min(max((x - e0) / (e1 - e0), 0.0), 1.0)
    return t * t * (3 - 2 * t)


def factor(rgb):
    peak, valley = max(rgb), min(rgb)
    sat = (peak - valley) / max(peak, 1e-12)
    return sat, 0.02 + 0.98 * smooth(0.58, 0.78, sat)


a, b = Vol(sys.argv[1]), Vol(sys.argv[2])
rows = []
for level in range(LEVELS):
    n = SIDES[level] ** 3
    for local in range(n):
        cell = FIRST[level] + local
        for side in range(6):
            an, ca, sa = a.bucket(cell, side)
            _, cb, sb = b.bucket(cell, side)
            if an[3] <= 0 or ca[:3] == cb[:3]:
                continue
            w = an[3]
            mean = [ca[i] / w for i in range(3)]
            helmet = [(ca[i] - cb[i]) / w for i in range(3)]
            share = sum(helmet) / max(sum(mean), 1e-12)
            sat, f = factor(mean)
            hsat, hf = factor(helmet)
            rows.append((level, a.coords(level, local), side, w / 4096, mean, share, sat, f, hf,
                         [sa[i] / w for i in range(3)], [sb[i] / w for i in range(3)]))

print("level cell side area(faces) mean-reflectance helmet-share sat factor helmet-alone-factor out-normal out-black")
for r in rows:
    print(f"{r[0]} {r[1]} {r[2]} {r[3]:.3f} ({r[4][0]:.3f},{r[4][1]:.3f},{r[4][2]:.3f}) {r[5]:.2f} {r[6]:.2f} {r[7]:.3f} {r[8]:.3f}"
          f" ({r[9][0]:.4f},{r[9][1]:.4f},{r[9][2]:.4f}) ({r[10][0]:.4f},{r[10][1]:.4f},{r[10][2]:.4f})")
for level in range(LEVELS):
    rs = [r for r in rows if r[0] == level]
    if not rs:
        continue
    area = sum(r[3] for r in rs)
    kept = sum(r[3] * r[5] * r[7] for r in rs) / max(sum(r[3] * r[5] for r in rs), 1e-12)
    alone = sum(r[3] * r[5] * r[8] for r in rs) / max(sum(r[3] * r[5] for r in rs), 1e-12)
    red = sum(r[3] * (r[9][0] - r[10][0]) for r in rs)
    print(f"level {level}: {len(rs)} faces, area {area:.2f} faces, helmet-weighted factor {kept:.3f} "
          f"(helmet alone {alone:.3f}), helmet red flux (faces x radiance) {red:.5f}")
