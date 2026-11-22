#!/usr/bin/env python3
"""Tool code (ter-ukw6): the sill census's samples of one box, split by class and by the place of their hit pixel.

  python3 -I sources.py <box> <regions> <run folder> ...

<regions> is "name:x0:y0:x1:y1;...", in render pixels, x1 and y1 excluded. The first region that holds a hit pixel names
its source, and a hit pixel in no region is "other". The census's sillpixel lines give each sample's hit pixel and class
(0 an opening, 1 the hit surface, 2 a nearer surface, 3 undetermined). For each run it prints samples and light per class
and source."""
import sys
from collections import defaultdict
from pathlib import Path


def source_of(x, y, regions):
    """The name of the first region that holds the hit pixel (x, y), or "other"."""
    for name, x0, y0, x1, y1 in regions:
        if x0 <= x < x1 and y0 <= y < y1:
            return name
    return "other"


def tally(lines, box, regions):
    """{(class, source): [samples, light]} over the census lines "sillpixel-<box> x y class sheet samples light"."""
    sums = defaultdict(lambda: [0, 0.0])
    for line in lines:
        parts = line.split()
        if parts and parts[0] == f"sillpixel-{box}":
            cell = sums[(parts[3], source_of(int(parts[1]), int(parts[2]), regions))]
            cell[0] += int(parts[5])
            cell[1] += float(parts[6])
    return dict(sums)


def parse_regions(text):
    return [(n, int(a), int(b), int(c), int(d)) for n, a, b, c, d in (r.split(":") for r in text.split(";") if r)]


if __name__ == "__main__":
    box, regions = sys.argv[1], parse_regions(sys.argv[2])
    for run in map(Path, sys.argv[3:]):
        sums = tally((run / "census.txt").read_text().splitlines(), box, regions)
        total = sum(v[1] for v in sums.values())
        print(f"{run.name} ({box}): total light {total:,.0f}")
        for (cls, source), (samples, light) in sorted(sums.items(), key=lambda kv: -kv[1][1]):
            print(f"  class {cls} {source:12} {samples:>9,} / {light:>13,.0f}  {100 * light / total:6.2f} %")
