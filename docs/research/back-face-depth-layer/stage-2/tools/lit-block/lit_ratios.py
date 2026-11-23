#!/usr/bin/env python3
"""Tool code (t1-r9vj): the ratio table of one view of the lit block, from t1-g4p1's ratios.py with one change: every
column gets its own frame noise, the change of each ratio from frame 231 to frame 239 of that column's film.

  python3 -I lit_ratios.py <views.json> <view> <set>

views.json has the layout of t1-g4p1's tools/views.json (see ../ratios.py). Every column needs <column>-000231.webp and
<column>-000239.webp. A set without "reference" prints the engine's columns alone. The table's cells are a region's ratio
± the column's frame noise, and for the reference ± half the difference of its two seeds' ratios. Then the nearest
column per region, and the largest difference between the columns main and layer against their frame noise.

A set may name "neutral_from": another set of the same view whose neutral region stands for its own, column by column
and seed by seed. The lit back's neutral floor patch is clipped in its 8-bit frames, and the lit front's is not.
"""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, str(Path(__file__).parent.parent))
import ratio_rows  # noqa: E402
import regions  # noqa: E402
import tone  # noqa: E402

views = json.loads(Path(sys.argv[1]).read_text())
view = views[sys.argv[2]]
lights = view["sets"][sys.argv[3]]
normal = np.load(view["geometry"]["normal"])
position = np.load(view["geometry"]["position"])
neutral = view["neutral"]


masks = {name: regions.region_mask(spec, normal, position) for name, spec in view["regions"].items()}


def engine_means(column, frame):
    """The mean of each region in a column's frame. With "neutral_from" in the set, the neutral region's mean comes from
    the same column and frame of that other set, at its own exposure."""
    def means(of):
        path = Path(of["columns"]) / f"{column}-{frame}.webp"
        lum = tone.luminance(tone.scene_from_display(np.asarray(Image.open(path).convert("RGB")), of["stops"]))
        return {name: float(lum[m].mean()) for name, m in masks.items()}
    result = means(lights)
    if "neutral_from" in lights:
        result[neutral] = means(view["sets"][lights["neutral_from"]])[neutral]
    return result


def reference_means(crops, index=None):
    """The mean of each region in the path trace's crops of one seed. With "neutral_from", the neutral region's mean
    comes from that other set's seed of the same index."""
    if index is not None and "neutral_from" in lights:
        means = reference_means(crops)
        means[neutral] = reference_means(view["sets"][lights["neutral_from"]]["reference"][index])[neutral]
        return means
    canvas = np.full(normal.shape[:2], np.nan)
    for path, x, y in crops:
        lum = tone.luminance(np.load(path))
        canvas[y:y + lum.shape[0], x:x + lum.shape[1]] = lum
    means = {}
    for name, m in masks.items():
        values = canvas[m]
        if np.isnan(values).any():
            raise SystemExit(f"region {name}: the reference's crops do not cover it")
        means[name] = float(values.mean())
    return means


late = {c: engine_means(c, "000239") for c in lights["order"]}
noise = {c: ratio_rows.frame_noise(engine_means(c, "000231"), late[c], neutral) for c in lights["order"]}
rows = {c: ratio_rows.ratios(late[c], neutral) for c in lights["order"]}
names = [r for r in masks if r != neutral]

print("Pixels per region: " + ", ".join(f"{r} {int(masks[r].sum())}" for r in masks))
source = f" of the set {lights['neutral_from']}" if "neutral_from" in lights else ""
print("| Column | " + " | ".join(names) + f" | {neutral}{source}, W/(m² sr) |")
print("|---|" + "---:|" * (len(names) + 1))
for c in lights["order"]:
    cells = [f"{rows[c][r]:.3g} ± {noise[c][r]:.2g}" for r in names]
    print(f"| `{c}` | " + " | ".join(cells) + f" | {late[c][neutral]:.4g} |")

if "reference" in lights:
    seeds = [reference_means(crops, i) for i, crops in enumerate(lights["reference"])]
    mean = {r: float(np.mean([s[r] for s in seeds])) for r in masks}
    truth = ratio_rows.ratios(mean, neutral)
    seed_noise = {r: abs(seeds[0][r] / seeds[0][neutral] - seeds[1][r] / seeds[1][neutral]) / 2 for r in names}
    cells = [f"{truth[r]:.3g} ± {seed_noise[r]:.2g}" for r in names]
    print("| Path trace | " + " | ".join(cells) + f" | {mean[neutral]:.4g} |")
    print()
    print("| Region | Nearest | Next | Path trace |")
    print("|---|---|---|---:|")
    for r, (best, nxt) in ratio_rows.nearest(rows, truth).items():
        print(f"| {r} | `{best}` {rows[best][r]:.3g} | `{nxt}` {rows[nxt][r]:.3g} | {truth[r]:.3g} |")

if "main" in rows and "layer" in rows:
    print()
    for r in names:
        gap = abs(rows["main"][r] - rows["layer"][r])
        print(f"main against layer, {r}: {gap:.2g}; frame noise main {noise['main'][r]:.2g}, layer {noise['layer'][r]:.2g}")
