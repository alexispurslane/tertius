"""Research tool (issue #42): the color shift of a still between today's rule and a variant, from the scene-linear HDR
readback (volume.bin.hdr, before the tone map). Prints the whole frame's mean RGB of both and their difference, and the
8 x 4 tiles of the frame with the largest change of mean RGB, as fractions of today's tile mean. With --image, it also
writes the absolute difference of the display PNGs (c.png) times 16.
usage: deck-shift.py <today folder> <variant folder> [--image <out.png>]"""
import argparse
import subprocess
from pathlib import Path

import numpy as np


def load(folder):
    a = np.fromfile(Path(folder) / "volume.bin.hdr", dtype=np.float32)
    n = a.size // 4
    w = int(round((n * 16 / 9) ** 0.5))
    return a.reshape(n // w, w, 4)[..., :3].astype(np.float64)


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("today")
parser.add_argument("variant")
parser.add_argument("--image")
args = parser.parse_args()
a, b = load(args.today), load(args.variant)
h, w, _ = a.shape
ma, mb = a.reshape(-1, 3).mean(0), b.reshape(-1, 3).mean(0)
print(f"{w}x{h}: today mean RGB {np.round(ma, 4)}, variant {np.round(mb, 4)}, change {np.round(mb - ma, 5)} "
      f"({np.round(100 * (mb - ma) / ma, 2)} %)")
tiles = []
for ty in range(4):
    for tx in range(8):
        ya, yb, xa, xb = ty * h // 4, (ty + 1) * h // 4, tx * w // 8, (tx + 1) * w // 8
        ta, tb = a[ya:yb, xa:xb].reshape(-1, 3).mean(0), b[ya:yb, xa:xb].reshape(-1, 3).mean(0)
        tiles.append((np.abs(tb - ta).max() / max(ta.max(), 1e-9), tx, ty, ta, tb))
tiles.sort(key=lambda t: -t[0])
for change, tx, ty, ta, tb in tiles[:4]:
    print(f"  tile column {tx} row {ty} (of 8 x 4): {100 * change:.1f} % of today's brightest channel, "
          f"RGB {np.round(ta, 4)} -> {np.round(tb, 4)}")
if args.image:
    subprocess.run(["magick", str(Path(args.today) / "c.png"), str(Path(args.variant) / "c.png"), "-alpha", "off", "-compose",
                    "difference", "-composite", "-evaluate", "multiply", "16", args.image], check=True)
