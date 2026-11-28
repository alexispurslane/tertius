"""Diagnostic tool code: one table row per case of the cockpit set, from the folders that variant-cases.sh writes, and
the change of each case against the reference case in regions that #20 must keep, in 8-bit levels of the 120-frame
mean with the normal helmet. It also writes <root>/<case>-vs-<ref>-x4.png: the difference x 4 around mid-gray.
usage (dev shell python): cases_table.py <root> <reference case> <case>..."""
import sys, glob, colorsys
import numpy as np
from PIL import Image

PANEL = (770, 65, 120, 130)
KEEP = {"darkCanopy": (1100, 150, 150, 100), "panelOrange": PANEL, "wallLeft": (20, 350, 200, 150),
        "floor": (700, 600, 200, 80), "consoleBesideDisplay": (852, 515, 30, 25)}


def mean(folder):
    files = sorted(glob.glob(f"{folder}/cockpit-*.png"))
    return sum(np.asarray(Image.open(f).convert("RGB"), dtype=np.float64) for f in files) / len(files)


def hdr(path):
    a = np.fromfile(path, dtype=np.float32)
    n = a.size // 4
    w = int(round((n * 16 / 9) ** 0.5))
    return a.reshape(n // w, w, 4)[..., :3], w / 1280


def region(img, r, s=1.0):
    x, y, w, h = r
    return img[int(y * s):int((y + h) * s), int(x * s):int((x + w) * s)].reshape(-1, 3).mean(0)


root, ref, cases = sys.argv[1], sys.argv[2], sys.argv[3:]
means = {}
print("| Case | Panel, normal helmet (8-bit) | Helmet light on panel (8-bit) | Helmet light, linear | Hue, saturation |"
      " Other light / helmet red | Pixels > 1 / > 4 levels |")
print("|---|---|---|---|---|---|---|")
for c in [ref] + [c for c in cases if c != ref]:
    n, b = mean(f"{root}/{c}/normal"), mean(f"{root}/{c}/black")
    means[c] = n
    d = n - b
    hn, s = hdr(f"{root}/{c}/normal/volume60.bin.hdr")
    hb, _ = hdr(f"{root}/{c}/black/volume60.bin.hdr")
    fill, add = region(hb, PANEL, s), region(hn - hb, PANEL, s)
    hue = colorsys.rgb_to_hls(*np.clip(add / max(add.max(), 1e-12), 0, 1))[0] * 360
    sat = (add.max() - add.min()) / max(add.max(), 1e-12)
    helmet = (np.abs(d) > 24).any(axis=2)
    for _ in range(6):
        g = helmet.copy()
        g[1:] |= helmet[:-1]; g[:-1] |= helmet[1:]; g[:, 1:] |= helmet[:, :-1]; g[:, :-1] |= helmet[:, 1:]
        helmet = g
    red = np.where(helmet, 0, d[..., 0])
    pn, pd = region(n, PANEL), region(d, PANEL)
    print(f"| {c} | ({pn[0]:.1f}, {pn[1]:.1f}, {pn[2]:.1f}) | ({pd[0]:+.2f}, {pd[1]:+.2f}, {pd[2]:+.2f}) |"
          f" ({add[0]:+.6f}, {add[1]:+.6f}, {add[2]:+.6f}) | {hue:.0f}°, {sat:.2f} | {fill[0] / max(add[0], 1e-12):.1f} |"
          f" {(red > 1).sum():,} / {(red > 4).sum():,} |")
print()
print("| Case | " + " | ".join(f"{k} change" for k in KEEP) + " |")
print("|---|" + "---|" * len(KEEP))
for c, n in means.items():
    if c == ref:
        continue
    cells = []
    for r in KEEP.values():
        v = region(n, r) - region(means[ref], r)
        cells.append(f"({v[0]:+.1f}, {v[1]:+.1f}, {v[2]:+.1f})")
    print(f"| {c} | " + " | ".join(cells) + " |")
    Image.fromarray(np.clip(128 + 4 * (n - means[ref]), 0, 255).astype(np.uint8)).save(f"{root}/{c}-vs-{ref}-x4.png")
