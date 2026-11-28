"""Diagnostic tool code: each light source's share of the scene-linear light on the dark canopy and the panel of the
1:41 cockpit under #42 variant 4, from the frame-60 HDR dumps of four cases with the normal helmet: today (glow
restrained to 2% for gray), glow (variant 4), glowN0 (nose sends no glow) and glowN1 (nose sends its front side only).
The lamps' share is today's light (it also holds the display and 2% of the gray glow). The ceiling glow0 patches are
glowN0 - today, the nose's front sides glowN1 - glowN0, and its back sides glow - glowN1.
usage (dev shell python): source_shares.py <cases root>"""
import sys
import numpy as np

REGIONS = {"darkCanopy": (1100, 150, 150, 100), "panelOrange": (770, 65, 120, 130)}


def hdr(path):
    a = np.fromfile(path, dtype=np.float32)
    n = a.size // 4
    w = int(round((n * 16 / 9) ** 0.5))
    return a.reshape(n // w, w, 4)[..., :3], w / 1280


def region(img, r, s):
    x, y, w, h = r
    return img[int(y * s):int((y + h) * s), int(x * s):int((x + w) * s)].reshape(-1, 3).mean(0)


root = sys.argv[1]
cases = {c: hdr(f"{root}/{c}/normal/volume60.bin.hdr") for c in ("today", "glow", "glowN0", "glowN1")}
for name, r in REGIONS.items():
    v = {c: region(img, r, s) for c, (img, s) in cases.items()}
    parts = {"lamps (today)": v["today"], "ceiling glow0 patches": v["glowN0"] - v["today"],
             "nose, front sides": v["glowN1"] - v["glowN0"], "nose, back sides": v["glow"] - v["glowN1"]}
    total = v["glow"]
    print(f"{name}: variant 4 linear ({total[0]:.5f}, {total[1]:.5f}, {total[2]:.5f})")
    for k, p in parts.items():
        print(f"  {k:24s} ({p[0]:+.5f}, {p[1]:+.5f}, {p[2]:+.5f})  share of red {p[0] / total[0]:6.1%}  of luminance"
              f" {np.dot(p, [0.2126, 0.7152, 0.0722]) / np.dot(total, [0.2126, 0.7152, 0.0722]):6.1%}")
