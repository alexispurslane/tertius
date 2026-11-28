"""Read receiver RGB and identify full-art pixels that sample the 0.5 m grid."""
import argparse
import statistics
import struct
from pathlib import Path


def floats(path):
    data = Path(path).read_bytes()
    return struct.unpack(f"<{len(data)//4}f", data)


def weight(grid, outer, position):
    origin, spacing = grid[:3], grid[3]
    side, guard = (18, 1) if outer else (16, 0)
    center = [min(side/2 + guard, max(side/2 - guard, -lo/spacing)) for lo in origin]
    distance = max(abs((p-lo)/spacing - c) for p, lo, c in zip(position, origin, center))
    return min(1, max(0, 7.5-distance))


class Frame:
    def __init__(self, directory):
        path = Path(directory) / "volume.bin"
        data = path.read_bytes()
        self.grids = [struct.unpack_from("<4f", data, i*16) for i in range(3)]
        camera = floats(str(path) + ".camera")
        self.width, self.height = map(int, camera[:2])
        self.forward, self.right, self.up = camera[2:5], camera[5:8], camera[8:11]
        self.jitter_x, self.jitter_y = camera[11:13]
        self.hdr = floats(str(path) + ".hdr")
        depth = Path(str(path) + ".depth")
        self.depth = floats(depth) if depth.is_file() else None

    def position(self, x, y):
        inverse_depth = self.depth[2*(x + y*self.width)]
        if inverse_depth <= 0:
            return None
        nx = 2*(x + 0.5 + self.jitter_x)/self.width - 1
        ny = 2*(y + 0.5 + self.jitter_y)/self.height - 1
        return tuple((f + nx*r - ny*u)/inverse_depth for f, r, u in zip(self.forward, self.right, self.up))

    def rgb(self, x, y):
        start = 4*(x + y*self.width)
        return self.hdr[start:start+3]

    def tint(self, x, y):
        r, g, _ = self.rgb(x, y)
        return r-g

    def middle_only(self, x, y):
        position = self.position(x, y)
        return position and weight(self.grids[0], False, position) == 0 and weight(self.grids[1], False, position) == 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline")
    parser.add_argument("candidate")
    parser.add_argument("--pixel", nargs=2, type=int)
    args = parser.parse_args()
    before, after = Frame(args.baseline), Frame(args.candidate)
    if args.pixel:
        x, y = args.pixel
        for frame in (before, after):
            print(f"pixel ({x}, {y}) RGB {frame.rgb(x,y)} and red minus green {frame.tint(x,y):.6f}")
            if frame.depth:
                print(f"  camera-relative position {frame.position(x,y)}, exclusive 0.5 m receiver: {frame.middle_only(x,y)}")
        return
    if before.depth:
        for y in range(36, before.height-36, 24):
            for x in range(36, before.width-36, 24):
                if before.middle_only(x,y) and after.middle_only(x,y):
                    print(f"({x}, {y}) position {before.position(x,y)} m, "
                          f"RGB {before.rgb(x,y)} → {after.rgb(x,y)} W/(m² sr)")
        return
    for frame in (before, after):
        x, y = frame.width//2, frame.height//2
        values = [frame.tint(i,j) for i in range(x-12,x+13) for j in range(y-12,y+13)]
        print(f"center RGB {frame.rgb(x,y)}, center red minus green {frame.tint(x,y):.6f}, "
              f"central 25×25 median tint {statistics.median(values):.6f}")


if __name__ == "__main__":
    main()
