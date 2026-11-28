"""Read surface area and outgoing radiance from the renderer's volume buffer."""
import argparse
from itertools import product
from math import floor, isclose
from pathlib import Path
from struct import unpack_from

SIDE = [16, 16, 18]
FIRST = [0, 4096, 8192]
HEADER = 64
CELL_COUNT = sum(n**3 for n in SIDE)
FACE_UNITS = 4096


class Volume:
    def __init__(self, path, baseline):
        self.data = Path(path).read_bytes()
        self.baseline = baseline
        self.grids = [unpack_from("<4f", self.data, i*16) for i in range(3)]
        self.evaluations = unpack_from("<I", self.data, 48)[0]

    def bucket(self, level, coordinates, face):
        x, y, z = coordinates
        side = SIDE[level]
        cell = FIRST[level] + x + side*(y + side*z)
        offset = HEADER + (6*cell + face)*48
        area = unpack_from("<i", self.data, offset + 12)[0]
        if not self.baseline:
            return area, unpack_from("<3f", self.data, offset + 32)
        if area == 0:
            return 0, (0, 0, 0)
        color = unpack_from("<3I", self.data, offset + 16)
        emission = unpack_from("<3I", self.data, offset + 32)
        tiles = 6 if level == 2 else 4
        shift = 2*tiles - side//2
        x, y, z = ((c + shift)//4 for c in coordinates)
        tile = 64*level + x + tiles*(y + tiles*z)
        light_offset = HEADER + CELL_COUNT*6*48 + (tile*6 + face)*16
        light = unpack_from("<3f", self.data, light_offset)
        diffuse = [c*l for c, l in zip(color, light)]
        peak = max(diffuse)
        saturation = (peak - min(diffuse)) / max(peak, area*1e-12)
        chroma = max(0, min(1, (saturation - 0.58)/0.20))
        restraint = 0.02 + 0.98*chroma**2*(3 - 2*chroma)
        return area, tuple(4*e + d*restraint for e, d in zip(emission, diffuse))

    def children(self, coarse, coordinates):
        parent = self.grids[coarse]
        child = self.grids[coarse-1]
        first = [2*c + round((p-q)/child[3]) for c, p, q in zip(coordinates, parent, child)]
        for offsets in product(range(2), repeat=3):
            coordinates = tuple(c+d for c, d in zip(first, offsets))
            if all(0 <= c < SIDE[coarse-1] for c in coordinates):
                yield coordinates

    def inherited(self, coarse, children, face):
        buckets = [self.bucket(coarse-1, child, face) for child in children]
        area = sum(area for area, _ in buckets)/4
        rgb = tuple(sum(rgb[i] for _, rgb in buckets)/4 for i in range(3))
        return area, rgb

    def transfer(self, coarse):
        area_count = lit_count = equal_count = 0
        for coordinates in product(range(SIDE[coarse]), repeat=3):
            children = list(self.children(coarse, coordinates))
            if not children:
                continue
            for face in range(6):
                area, rgb = self.inherited(coarse, children, face)
                if area <= 0:
                    continue
                area_count += 1
                if max(rgb) <= 0:
                    continue
                lit_count += 1
                parent_area, parent_rgb = self.bucket(coarse, coordinates, face)
                if len(children) == 8 and abs(parent_area - area) <= 0.5 and all(
                    isclose(p, c, rel_tol=1e-5, abs_tol=1e-7) for p, c in zip(parent_rgb, rgb)
                ):
                    equal_count += 1
        return area_count, lit_count, equal_count


def scaled(rgb):
    return tuple(c/FACE_UNITS for c in rgb)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dump")
    parser.add_argument("--baseline", action="store_true")
    parser.add_argument("--camera-x", type=float)
    args = parser.parse_args()
    volume = Volume(args.dump, args.baseline)
    print(f"{volume.evaluations} directional-volume light evaluations")
    if args.camera_x is None:
        for level in (1, 2):
            print(f"grid {level-1}→{level}: {volume.transfer(level)} "
                  "(parent buckets inheriting area, inheriting light, preserving pure inherited light)")
        return
    target = (2.25 - args.camera_x, -0.75, -1.1)
    grid = volume.grids[1]
    cell = tuple(floor((p - g)/grid[3]) for p, g in zip(target, grid))
    area, source = volume.bucket(1, cell, 5)
    fine_area, fine_source = volume.inherited(1, list(volume.children(1, cell)), 5)
    print(f"0.5 m cell {cell}, -Z face")
    print(f"area per parent face: {area/FACE_UNITS:.6f}, inherited area: {fine_area/FACE_UNITS:.6f}")
    print(f"outgoing RGB times area per parent face: {scaled(source)}, inherited contribution: {scaled(fine_source)}")
    if not args.baseline:
        print(f"own shell contribution: {scaled(tuple(p-c for p,c in zip(source,fine_source)))}")


if __name__ == "__main__":
    main()
