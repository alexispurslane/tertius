"""Diagnostic tool code: the scene-linear color, hue and saturation of named regions in one or more HDR dumps
(volume60.bin.hdr), and their 8-bit mean over a film folder. Regions are in 1280 x 720 output pixels.
usage (dev shell python): regions_linear.py <label>=<film folder> ..."""
import sys, glob, colorsys
import numpy as np
from PIL import Image

REGIONS = {"panelOrange": (770, 65, 120, 130), "consoleBesideDisplay": (852, 515, 30, 25),
           "floorBelowDisplay": (710, 562, 100, 25)}


def hdr(path):
    a = np.fromfile(path, dtype=np.float32)
    n = a.size // 4
    w = int(round((n * 16 / 9) ** 0.5))
    return a.reshape(n // w, w, 4)[..., :3], w


for arg in sys.argv[1:]:
    label, folder = arg.split("=", 1)
    h, w = hdr(f"{folder}/volume60.bin.hdr")
    s = w / 1280
    files = sorted(glob.glob(f"{folder}/cockpit-*.png"))
    m = sum(np.asarray(Image.open(f).convert("RGB"), dtype=np.float64) for f in files) / len(files)
    for name, (x, y, ww, hh) in REGIONS.items():
        lin = h[int(y * s):int((y + hh) * s), int(x * s):int((x + ww) * s)].reshape(-1, 3).mean(0)
        hue = colorsys.rgb_to_hls(*(lin / lin.max()))[0] * 360
        sat = (lin.max() - lin.min()) / lin.max()
        e = m[y:y + hh, x:x + ww].reshape(-1, 3).mean(0)
        print(f"{label:10s} {name:22s} linear ({lin[0]:.5f} {lin[1]:.5f} {lin[2]:.5f}) hue {hue:5.1f} sat {sat:.2f}"
              f"  8-bit ({e[0]:6.1f} {e[1]:6.1f} {e[2]:6.1f})")
