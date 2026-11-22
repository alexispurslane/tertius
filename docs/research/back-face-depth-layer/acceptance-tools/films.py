#!/usr/bin/env python3
"""Tool code (t1-8b4w): the flicker of a film, per tile, and its comparison between two sides.

  python3 -I films.py measure <frame folder> <first> <last> <out.npy>
  python3 -I films.py compare <main.npy> <branch.npy> [<x0>,<y0>,<x1>,<y1> ...]

The measure reads s-<first>.png to s-<last>.png of a headless film. A frame's luminance is 0.2126 R + 0.7152 G +
0.0722 B of its 8-bit sRGB values, in units of 1/255. The flicker of a tile of 16 x 16 output pixels is the mean
absolute change of that luminance between consecutive frames, averaged over the tile's pixels and the frame pairs. A
still view's flicker is the ray noise that the histories let through, plus any blinking. The compare prints the tiles
where the branch's flicker passes main's by more than main's own spread allows, as connected regions. Boxes leave
their tiles out: a part of the view that moves, such as the deck's walking person.
"""
import sys
from pathlib import Path

import numpy as np

TILE = 16


def luma(rgb8):
    rgb = np.asarray(rgb8, dtype=np.float64)
    return rgb[..., 0] * 0.2126 + rgb[..., 1] * 0.7152 + rgb[..., 2] * 0.0722


def tile_mean(image, tile):
    rows, cols = image.shape[0] // tile, image.shape[1] // tile
    cut = np.asarray(image, dtype=np.float64)[: rows * tile, : cols * tile]
    return cut.reshape(rows, tile, cols, tile).mean(axis=(1, 3))


def flicker(frames, tile):
    total, previous, pairs = None, None, 0
    for frame in frames:
        current = luma(frame)
        if previous is not None:
            change = tile_mean(np.abs(current - previous), tile)
            total = change if total is None else total + change
            pairs += 1
        previous = current
    return total / pairs


def _frames(folder, first, last):
    from PIL import Image
    for index in range(first, last + 1):
        yield np.asarray(Image.open(Path(folder) / f"s-{index:06d}.png").convert("RGB"))


def _excluded_mask(shape, boxes):
    mask = np.zeros(shape, dtype=bool)
    for x0, y0, x1, y1 in boxes:
        mask[y0 // TILE: -(-y1 // TILE), x0 // TILE: -(-x1 // TILE)] = True
    return mask


def compare(main, branch, boxes):
    from scipy import ndimage
    keep = ~_excluded_mask(main.shape, boxes)
    print(f"tiles {keep.sum()}, main mean {main[keep].mean():.3f}, branch mean {branch[keep].mean():.3f} (1/255 per draw)")
    for name, values in (("main", main), ("branch", branch)):
        print(f"{name}: 99th percentile {np.percentile(values[keep], 99):.3f}, largest {values[keep].max():.3f}")
    # A tile flickers more on the branch when its flicker is at least twice main's and 2/255 above it.
    worse = keep & (branch >= 2.0 * main) & (branch - main >= 2.0)
    labels, count = ndimage.label(worse, structure=np.ones((3, 3)))
    found = []
    for index in range(1, count + 1):
        ys, xs = np.nonzero(labels == index)
        found.append((len(ys), xs.min() * TILE, ys.min() * TILE, (xs.max() + 1) * TILE, (ys.max() + 1) * TILE,
                      main[labels == index].mean(), branch[labels == index].mean()))
    print(f"tiles that flicker more on the branch: {worse.sum()} in {count} regions")
    for size, x0, y0, x1, y1, m, b in sorted(found, reverse=True)[:12]:
        print(f"  {size} tiles in x {x0}-{x1}, y {y0}-{y1}: main {m:.2f}, branch {b:.2f}")


def main(argv):
    if argv[:1] == ["measure"] and len(argv) == 5:
        np.save(argv[4], flicker(_frames(argv[1], int(argv[2]), int(argv[3])), TILE))
        return 0
    if argv[:1] == ["compare"] and len(argv) >= 3:
        boxes = [tuple(int(v) for v in box.split(",")) for box in argv[3:]]
        compare(np.load(argv[1]), np.load(argv[2]), boxes)
        return 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
