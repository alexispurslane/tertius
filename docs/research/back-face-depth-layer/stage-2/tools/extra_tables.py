#!/usr/bin/env python3
"""Throwaway (t1-3j2j): the tables of shadow-leak.md that no other tool prints, and the box sheet.

  python3 -I extra_tables.py box <root> [<sheet.png>]
  python3 -I extra_tables.py ticks <root>
  python3 -I extra_tables.py cabin <root>

<root> is /private/tmp/t1-3j2j.

box: part 3's two tables of bounce-raised-box. The stills are <root>/stills/box/<run>/s-000239.png at -6 stops, for the
runs full, filter2, filter1 and map4096. The floor is rows 560 and below, and a lit floor pixel is above 0.8 times the
floor's median luminance in the default still. A level is one of 255 in any channel. The second table judges the CPU
copy's lit share (<root>/cast/eng-box-<run>.npz, from engine_shadow.py on the cast <root>/cast/box-edges.npz, with
ENGINE_FILTER or ENGINE_DILATE) against the cast's sunlit share. With <sheet.png>, it also writes the box sheet: the crop
x 600 to 959, y 575 to 654 at twice its size, of the four stills and the path trace <root>/ref/box-edges.npy.

ticks: the other line's table. For each tick t060, t300 and t306, the line casts <root>/ticks/<t>/<strip>.npz and the CPU
copy <root>/ticks/<t>/eng-<strip>.npz. The engine's lit part runs from its first lit point to the hull's outer face, the
cast's last point. Its size in render pixels at 1280 x 720 comes from the export's camera, which sits at the origin and
looks along -z.

cabin: part 1's counts of the cabin, from the cast <root>/cast/cabin-map8.npz (every eighth pixel, 16 lines to the sun),
the cast <root>/cast/cabin-map2.npz (every second pixel), and the CPU copy <root>/cast/eng-cabin-map8.npz. A point faces
the sun when its cosine to the sun is above 0.01.
"""
import collections
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
import tone  # noqa: E402

mode, root = sys.argv[1], Path(sys.argv[2])


def box():
    def still(run):
        return np.asarray(Image.open(root / f"stills/box/{run}/s-000239.png").convert("RGB")).astype(int)

    full = still("full")
    lum = tone.luminance(tone.scene_from_display(full.astype(np.uint8), -6))
    floor = np.zeros(lum.shape, bool)
    floor[560:] = True
    lit = floor & (lum > 0.8 * np.median(lum[floor]))
    print(f"floor pixels {int(floor.sum())}, lit floor pixels {int(lit.sum())}")
    print("run, lit floor pixels darker by more than 2 levels, floor pixels changed by more than 2 levels, largest change")
    for run in ("filter2", "filter1", "map4096"):
        other = still(run)
        change = np.abs(other - full).max(2)
        darker = (full - other).max(2)
        print(run, int((darker[lit] > 2).sum()), int((change[floor] > 2).sum()), int(change.max()))
    cast = np.load(root / "cast/box-edges.npz")
    truth = np.nan_to_num(cast["share"])
    print("run, mean |lit share - truth|, points darker by more than 0.1, points brighter by more than 0.1")
    for run in ("full", "filter2", "filter1", "dilate2"):
        share = np.nan_to_num(np.load(root / f"cast/eng-box-{run}.npz")["lit"])
        print(run, f"{np.abs(share - truth).mean():.3f}", int((truth - share > 0.1).sum()), int((share - truth > 0.1).sum()))
    if len(sys.argv) > 3:
        x0, y0, x1, y1, zoom = 600, 575, 960, 655, 2
        w, h = (x1 - x0) * zoom, (y1 - y0) * zoom
        crops = [still(run)[y0:y1, x0:x1].astype(np.uint8) for run in ("full", "filter2", "filter1", "map4096")]
        crops.append(tone.display(np.load(root / "ref/box-edges.npy")[..., :3], -6))
        sheet = Image.new("RGB", (w, len(crops) * (h + 4) - 4), (255, 255, 255))
        for i, crop in enumerate(crops):
            sheet.paste(Image.fromarray(crop).resize((w, h), Image.NEAREST), (0, i * (h + 4)))
        sheet.save(sys.argv[3])


def ticks():
    print("tick, strip, engine lit part m, render pixels, mean lit share over it, peak | truth: lit length m, peak")
    for tick in ("t060", "t300", "t306"):
        folder = root / "ticks" / tick
        scene = json.loads((folder / "export/scene.json").read_text())
        camera = scene["camera"]
        tan_x, tan_y = camera["tanHalfX"], camera["tanHalfY"]

        def render(q):
            return np.array([(q[0] / -q[2] / tan_x + 1) / 2 * 1280, (1 - q[1] / -q[2] / tan_y) / 2 * 720])

        for strip, axis, across in (("endsill-0", 2, 1), ("endsill-2", 2, 1), ("jamb", 0, 0), ("sidesill", 0, 0)):
            if not (folder / f"eng-{strip}.npz").exists():
                continue
            cast = np.load(folder / f"{strip}.npz")
            engine = np.load(folder / f"eng-{strip}.npz")
            position = cast["position"]
            lit = np.nonzero(engine["lit"] > 0)[0]
            first, outer = position[lit[0]], position[-1]
            length = abs(float(outer[axis] - first[axis]))
            pixels = abs(float(render(outer)[across] - render(first)[across]))
            true_lit = np.nonzero(cast["share"] > 0)[0]
            true_length = abs(float(outer[axis] - position[true_lit[0]][axis])) if true_lit.size else None
            print(tick, strip, f"{length:.3f}", f"{pixels:.3f}", f"{engine['lit'][lit].mean():.3f}", f"{engine['lit'].max():.3f}",
                  "|", true_length, f"{cast['share'].max():.3f}")


def cabin():
    coarse = np.load(root / "cast/cabin-map8.npz")
    fine = np.load(root / "cast/cabin-map2.npz")
    engine = np.load(root / "cast/eng-cabin-map8.npz")
    print(f"every second pixel: {fine['instance'].size} points, sunlit share above 0 at {int(np.nansum(fine['share'] > 0))}")
    facing = coarse["ndotl"] > 0.01
    print(f"every eighth pixel: {int(facing.sum())} points face the sun; the blockers of the line to the sun's center:",
          collections.Counter(coarse["blocker"][facing].tolist()).most_common(4))
    gap = engine["gap_m"][facing]
    print(f"CPU copy on the points that face the sun: lit share above 0 at {int(np.nansum(engine['lit'][facing] > 0))},"
          f" cascades {sorted(collections.Counter(engine['cascade'][facing].astype(int).tolist()).items())},"
          f" stored caster's gap toward the sun {np.nanmin(gap):.2f} to {np.nanmax(gap):.2f} m")
    print(f"CPU copy on every point: lit share above 0 at {int(np.nansum(engine['lit'] > 0))}")


{"box": box, "ticks": ticks, "cabin": cabin}[mode]()
