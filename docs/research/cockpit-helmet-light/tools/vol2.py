"""Diagnostic tool code: reads a dump of the directional light volume's buffer in the layout of volume.glsl and
volume_layout.glsl after #22 (three grids, 16^3, 16^3 and 18^3 cells, 32 directions, 48-byte surface buckets), and
evaluates a receiver the way volume_receive.glsl does."""
import struct, math

LEVELS, REACH, DIRS = 3, 16, 32
SIDES = [16, 16, 18]
FIRST = [0, 4096, 8192]
CELLS = 14024
GRIDS, STATS = 0, 48
SURF = 64
FIELD = SURF + CELLS * 6 * 48
BOUNCE = FIELD + CELLS * DIRS * 16
FILT = BOUNCE + CELLS * 6 * 16
SIZE = FILT + CELLS * 12 * 16


def guard(level):
    return 1 if level == LEVELS - 1 else 0


class Vol:
    def __init__(self, path):
        self.b = open(path, 'rb').read()
        assert len(self.b) >= SIZE, (len(self.b), SIZE)
        self.grids = [struct.unpack_from('<4f', self.b, GRIDS + 16 * l) for l in range(LEVELS)]

    def cell(self, level, c):
        s = SIDES[level]
        return FIRST[level] + c[0] + s * (c[1] + s * c[2])

    def coords(self, level, local):
        s = SIDES[level]
        return (local % s, (local // s) % s, local // (s * s))

    def bucket(self, cell, side):
        o = SURF + 48 * (cell * 6 + side)
        an = struct.unpack_from('<4i', self.b, o)
        color = struct.unpack_from('<4I', self.b, o + 16)
        source = struct.unpack_from('<4f', self.b, o + 32)
        return an, color, source

    def filtered(self, cell, i):
        return struct.unpack_from('<4f', self.b, FILT + 16 * (cell * 12 + i))

    def centre(self, level, c):
        g = self.grids[level]
        return [g[i] + (c[i] + 0.5) * g[3] for i in range(3)]

    def lobes(self, cell, first, n):
        w = [x * x for x in n]
        out = [0.0] * 4
        for axis in range(3):
            v = self.filtered(cell, first + 2 * axis + (1 if n[axis] < 0 else 0))
            for k in range(4):
                out[k] += w[axis] * v[k]
        return out

    def at(self, level, p, n, r):
        last = SIDES[level] - 1
        local = [min(max(x - 0.5, 0.0), float(last)) for x in p]
        base = [int(math.floor(x)) for x in local]
        f = [local[i] - base[i] for i in range(3)]
        d, g = [0.0] * 4, [0.0] * 4
        for i in range(8):
            corner = (i % 2, (i // 2) % 2, i // 4)
            w = 1.0
            for a in range(3):
                w *= f[a] if corner[a] else 1 - f[a]
            c = self.cell(level, [min(base[a] + corner[a], last) for a in range(3)])
            ld, lg = self.lobes(c, 0, n), self.lobes(c, 6, r)
            for k in range(4):
                d[k] += w * ld[k]
                g[k] += w * lg[k]
        return d, g

    def level_weight(self, level, p):
        g = self.grids[level]
        side = float(SIDES[level])
        centre = [min(max(-g[i] / g[3], side / 2 - guard(level)), side / 2 + guard(level)) for i in range(3)]
        off = max(abs(p[i] - centre[i]) for i in range(3))
        return min(max(0.5 * REACH - 0.5 - off, 0.0), 1.0)

    def receive(self, pos, n, gn, r):
        """The volume's diffuse and broad specular radiance (rgb) and transmissions at a receiver, as applyLightVolume
        mixes the levels, with the receiver's offset of 0.45 cells along its geometric normal. Returns (d, g, levels)."""
        d, g = [0, 0, 0, 1.0], [0, 0, 0, 1.0]
        remaining = 1.0
        used = []
        for level in range(LEVELS):
            gr = self.grids[level]
            p = [(pos[i] - gr[i]) / gr[3] for i in range(3)]
            w = remaining * self.level_weight(level, p)
            if w == 0:
                continue
            ld, lg = self.at(level, [p[i] + 0.45 * gn[i] for i in range(3)], n, r)
            for k in range(4):
                d[k] += w * (ld[k] - (1 if k == 3 else 0))
                g[k] += w * (lg[k] - (1 if k == 3 else 0))
            used.append((level, w))
            remaining -= w
            if remaining <= 0:
                break
        return d, g, used
