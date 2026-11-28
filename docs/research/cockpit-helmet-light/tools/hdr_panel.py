"""Diagnostic tool code: the helmet's added light on the panel in the scene-linear HDR target (before the tone map and
bloom), from the HDR dumps of one frame with the normal and the black helmet. It prints, per region, the black-helmet
light (the fill from every other source), the helmet's added light, the hue and saturation of the added light, and the
ratio of fill to added light (the dilution).
usage (dev shell python): hdr_panel.py <normal volume60.bin.hdr> <black volume60.bin.hdr>"""
import sys, colorsys
import numpy as np

REGIONS = {"panelOrange": (770, 65, 120, 130), "panelMid": (950, 120, 200, 120), "beamNearHelmet": (380, 230, 120, 80)}


def load(path):
    a = np.fromfile(path, dtype=np.float32)
    n = a.size // 4
    w = int(round((n * 16 / 9) ** 0.5))
    return a.reshape(n // w, w, 4)[..., :3], w


n, w = load(sys.argv[1])
b, _ = load(sys.argv[2])
s = w / 1280
print(f"HDR {w} x {n.shape[0]} (scale {s:.3f} of the 1280 x 720 output)")
for name, (x, y, ww, hh) in REGIONS.items():
    x0, y0, x1, y1 = int(x * s), int(y * s), int((x + ww) * s), int((y + hh) * s)
    fill = b[y0:y1, x0:x1].reshape(-1, 3).mean(0)
    add = (n - b)[y0:y1, x0:x1].reshape(-1, 3).mean(0)
    h, l, sat = colorsys.rgb_to_hls(*np.clip(add / max(add.max(), 1e-12), 0, 1))
    hf, lf, sf = colorsys.rgb_to_hls(*np.clip(fill / max(fill.max(), 1e-12), 0, 1))
    peak = add.max()
    print(f"{name:15s} fill ({fill[0]:.5f} {fill[1]:.5f} {fill[2]:.5f}) hue {hf*360:5.1f} sat {(fill.max()-fill.min())/fill.max():.2f} | "
          f"helmet ({add[0]:+.6f} {add[1]:+.6f} {add[2]:+.6f}) hue {h*360:5.1f} sat {(add.max()-add.min())/max(peak,1e-12):.2f} | "
          f"fill/helmet red {fill[0]/max(add[0],1e-12):.1f}")
