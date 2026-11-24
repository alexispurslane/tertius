#!/usr/bin/env python3
# Tool code of t1-3er4: compares takes of the real cuts under the fix's stencils. Reads the crops of extract.py.
#   ROI lags: roi.py's measure on the nose crop.
#   brightness: the mean 601 luma and the mean linear luminance of the ROI and of the cone box.
#   specks: ter-fe63's speck count (luma more than 12 above its 7 x 7 median, outside the lamps widened by 13 pixels),
#           here on the cone box (output 640..1279 x 0..359), not on ter-fe63's wider box.
#   block seams: for the cone box, the mean absolute horizontal and vertical luma step by the output column's and row's
#           phase modulo 12 (one block of 8 render pixels at the scale 2/3); the block borders fall at column phase 8
#           and row phase 0. "seam_ratio" is the step at the border phase over the mean step of the other phases.
#   shimmer: the frame-to-frame RMS of the nose box's luma over d0..d14 and d15..d29 (and of the floor box for lamps).
#   floor grain: ter-fe63's grain, the RMS of the floor box's linear luminance less its Gaussian blur of sigma 2, over its mean.
# Usage: python3 -I analyze.py nose|lamps <out dir> <label>=<take dir> ...
import json, subprocess, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter, maximum_filter, median_filter
sys.path.insert(0, str(Path(__file__).parent))
from roi import measure, luma, ROI

KIND, OUT = sys.argv[1], Path(sys.argv[2])
takes = {a.split("=")[0]: Path(a.split("=")[1]) for a in sys.argv[3:]}
OUT.mkdir(parents=True, exist_ok=True)
L = np.array([0.2126, 0.7152, 0.0722])
def lin(rgb):
    a = rgb.astype(np.float64) / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ L
def specks(rgb):
    g = luma(rgb.astype(np.float32)); lamps = maximum_filter(g > 200.0, size=13)
    return int(((g - median_filter(g, size=7) > 12.0) & ~lamps).sum())
def seams(rgb):
    g = luma(rgb.astype(np.float64))
    sx = np.abs(np.diff(g, axis=1)).mean(0); sy = np.abs(np.diff(g, axis=0)).mean(1)
    # a step between columns c and c+1 sits on the border when c+1 has phase 8
    px = np.array([sx[(np.arange(len(sx)) + 1) % 12 == k].mean() for k in range(12)])
    py = np.array([sy[(np.arange(len(sy)) + 1) % 12 == k].mean() for k in range(12)])
    return {"col_seam_ratio": round(float(px[8] / np.delete(px, 8).mean()), 3),
            "row_seam_ratio": round(float(py[0] / np.delete(py, 0).mean()), 3)}
def grain(rgb):
    y = lin(rgb); return float(np.sqrt(((y - gaussian_filter(y, 2)) ** 2).mean()) / y.mean())
def shimmer(frames):
    g = np.stack([luma(f.astype(np.float64)) for f in frames])
    return round(float(np.sqrt((np.diff(g, axis=0) ** 2).mean())), 3)

data = {k: np.load(v / "crops.npz") for k, v in takes.items()}
hashes = {k: json.loads((v / "hashes.json").read_text())["sha256"] for k, v in takes.items()}
summary = {}
for k, d in data.items():
    nose, cone, floor = d["nose"], d["cone"], d["floor"]
    s = {"hash_d0": hashes[k][61], "hash_d29": hashes[k][90], "hash_d64": hashes[k][125]}
    if KIND == "nose":
        s["roi"] = {f"d{n}": measure(nose[n]) for n in (0, 1, 2, 3, 5, 10, 29, 64)}
        s["roi_linear_mean"] = {f"d{n}": round(float(lin(nose[n])[ROI].mean()), 5) for n in (0, 1, 2, 3, 5, 10, 29, 64)}
        s["cone_linear_mean"] = {f"d{n}": round(float(lin(cone[n]).mean()), 5) for n in (0, 1, 2, 3, 5, 10, 29, 64)}
        s["specks_cone"] = [specks(cone[n]) for n in range(len(cone))]
        s["seams_cone"] = {f"d{n}": seams(cone[n]) for n in (0, 1, 2, 3)}
        s["shimmer_nose_d0_d14"] = shimmer(nose[0:15]); s["shimmer_nose_d15_d29"] = shimmer(nose[15:30])
        s["shimmer_nose_d30_d64"] = shimmer(nose[30:65])
    else:
        s["floor_grain"] = [round(grain(floor[n]), 5) for n in range(len(floor))]
        s["floor_linear_mean"] = {f"d{n}": round(float(lin(floor[n]).mean()), 5) for n in (0, 1, 2, 3, 5, 10, 29, 64)}
        s["seams_floor"] = {f"d{n}": seams(floor[n]) for n in (0, 1, 2, 3)}
        s["shimmer_floor_d0_d14"] = shimmer(floor[0:15]); s["shimmer_floor_d15_d29"] = shimmer(floor[15:30])
        s["shimmer_floor_d30_d64"] = shimmer(floor[30:65])
    summary[k] = s
labels = list(takes)
ref = labels[0]
for k in labels[1:]:
    box = data[k]["nose" if KIND == "nose" else "floor"].astype(np.int16) - data[ref]["nose" if KIND == "nose" else "floor"].astype(np.int16)
    summary[k]["vs_" + ref] = {f"d{n}": {"mean_abs": round(float(np.abs(box[n]).mean()), 3), "max_abs": int(np.abs(box[n]).max()),
                                       "identical": bool((box[n] == 0).all())} for n in (0, 3, 4, 10, 29, 64)}
(OUT / f"{KIND}-summary.json").write_text(json.dumps(summary, indent=1))

# 1:1 strip: rows = takes, columns = draws.
crop = "nose" if KIND == "nose" else "floor"
draws = [0, 1, 2, 5, 10, 29]
def sub(a):  # the floor strip box of ter-fe63 (760..1159 x 660..959) inside the floor box
    return a if KIND == "nose" else a[40:340, 160:560]
h, w = sub(data[ref][crop][0]).shape[:2]
pad, head = 6, 18
canvas = Image.new("RGB", (len(draws) * (w + pad) - pad + 90, len(labels) * (h + head + pad)), "white")
dr = ImageDraw.Draw(canvas)
for r, k in enumerate(labels):
    top = r * (h + head + pad)
    dr.text((4, top + h // 2 + head), k, fill="black")
    for c, n in enumerate(draws):
        canvas.paste(Image.fromarray(np.ascontiguousarray(sub(data[k][crop][n]))), (90 + c * (w + pad), top + head))
        note = f"lag6 {summary[k]['roi'][f'd{n}']['lag6']}" if KIND == "nose" and f"d{n}" in summary[k]["roi"] else \
               (f"grain {summary[k]['floor_grain'][n]:.4f}" if KIND != "nose" else "")
        dr.text((90 + c * (w + pad) + 3, top + 3), f"{k} d{n}  {note}", fill="black")
canvas.save(OUT / f"{KIND}-strip-1x.png")

# Side-by-side film of the cone box (nose) or the floor box (lamps), d0..d64, 60 fps, H.264 CRF 12.
box = "cone" if KIND == "nose" else "floor"
fh, fw = data[ref][box][0].shape[:2]
width = len(labels) * fw + (len(labels) - 1) * 8; width += width % 2; height = fh + 22 + (fh + 22) % 2
enc = subprocess.Popen(["ffmpeg", "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{width}x{height}",
                        "-r", "60", "-i", "-", "-c:v", "libx264", "-crf", "12", "-pix_fmt", "yuv420p",
                        str(OUT / f"{KIND}-film-baseline-a-b.mp4")], stdin=subprocess.PIPE)
for n in range(len(data[ref][box])):
    cv = Image.new("RGB", (width, height), "white"); dr = ImageDraw.Draw(cv)
    for j, k in enumerate(labels):
        cv.paste(Image.fromarray(np.ascontiguousarray(data[k][box][n])), (j * (fw + 8), 22))
        dr.text((j * (fw + 8) + 4, 5), f"{k}  d{n} after the cut", fill="black")
    enc.stdin.write(cv.tobytes())
enc.stdin.close(); assert enc.wait() == 0
print(json.dumps({k: {kk: vv for kk, vv in v.items() if kk in ("roi", "seams_cone", "seams_floor", "shimmer_nose_d0_d14",
      "shimmer_floor_d0_d14", "shimmer_floor_d15_d29")} for k, v in summary.items()})[:3000])
