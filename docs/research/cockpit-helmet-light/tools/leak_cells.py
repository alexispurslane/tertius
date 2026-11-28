"""Diagnostic tool code (#47): where the nose's back-side glow travels in the light volume. From two dumps of the 1:41
frame (A with the back sides, B without, such as variant 4 and N1), it prints for the 1 m grid (level 2) a map in the
y-z plane of the shuttle frame: per cell row (y) and column (z), the difference A - B of the light that leaves the cells
(red, summed over the 32 directions and over the cells with shuttle |x| < 2.6). Beside it, per cell, the share of an
upward ray (direction +y) that the cell lets through, from its surface buckets as volume_source.comp computes it
(clear = 1 - min(1, max(front, back))), and the floor area that the cell holds. For levels 0 and 1, it prints the
summed difference in the cells above the cabin floor.
The camera sits at about (1.55, 1.18, -9.08) in the shuttle frame (from the ceiling patches' level-0 cells).
usage: leak_cells.py <dump A> <dump B>"""
import sys, os, math, struct
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vol2 import Vol, FIELD, FIRST, SIDES, DIRS, LEVELS

CAMERA = (1.55, 1.18, -9.08)
FLOOR_Y = -0.5


def step(d):
    octant, major = d // 4, d % 4
    v = [1, 1, 1]
    if major < 3:
        v[major] = 3
    sign = [1 - 2 * (octant & 1), 1 - 2 * ((octant >> 1) & 1), 1 - 2 * (octant >> 2)]
    return [v[i] * sign[i] for i in range(3)]


STEPS = [step(d) for d in range(DIRS)]


def half(h):
    return struct.unpack('<e', struct.pack('<H', h))[0]


def outgoing_red(v, level, local):
    total = 0.0
    for d in range(DIRS):
        o = FIELD + 16 * (DIRS * FIRST[level] + d * SIDES[level] ** 3 + local)
        total += half(struct.unpack_from('<I', v.b, o)[0] >> 16)
    return total


def clear_up(v, cell):
    """The share of light that crosses the cell along the most upward directions (the four with step y = +3, mean)."""
    shares = []
    for s in STEPS:
        if s[1] != 3:
            continue
        m = max(abs(x) for x in s)
        projected = [x / m for x in s]
        front = back = 0.0
        for side in range(6):
            an, _, _ = v.bucket(cell, side)
            if an[3] == 0:
                continue
            area = sum(an[i] * projected[i] for i in range(3)) / 4096.0
            back += max(-area, 0.0)
            front += max(area, 0.0)
        shares.append(1.0 - min(1.0, max(front, back)))
    return sum(shares) / len(shares)


def shuttle(v, level, c):
    centre = v.centre(level, c)
    return [centre[i] + CAMERA[i] for i in range(3)]


A, B = Vol(sys.argv[1]), Vol(sys.argv[2])
level = 2
side = SIDES[level]
rows = {}
for local in range(side ** 3):
    c = A.coords(level, local)
    p = shuttle(A, level, c)
    if abs(p[0]) > 2.6 or not (-4 < p[1] < 3) or not (-17 < p[2] < -7):
        continue
    key = (round(p[1], 2), round(p[2], 2))
    diff = outgoing_red(A, level, local) - outgoing_red(B, level, local)
    cell = FIRST[level] + local
    r = rows.setdefault(key, [0.0, [], 0.0])
    r[0] += diff
    r[1].append(clear_up(A, cell))
ys = sorted({k[0] for k in rows}, reverse=True)
zs = sorted({k[1] for k in rows})
print("level 2 (1 m): A - B outgoing red, summed over 32 directions and |x| < 2.6; [mean share an upward ray crosses]")
print("y \\ z  " + "".join(f"{z:>16.2f}" for z in zs))
for y in ys:
    line = f"{y:6.2f} "
    for z in zs:
        r = rows.get((y, z))
        line += f"{r[0]:>9.4f} [{sum(r[1]) / len(r[1]):.2f}]" if r else " " * 16
    print(line)
for level in (0, 1):
    total, cells = 0.0, 0
    for local in range(SIDES[level] ** 3):
        c = A.coords(level, local)
        p = shuttle(A, level, c)
        if p[1] > FLOOR_Y and abs(p[0]) < 2.6:
            total += outgoing_red(A, level, local) - outgoing_red(B, level, local)
            cells += 1
    print(f"level {level}: A - B outgoing red summed over {cells} cells above the cabin floor: {total:.4f}")
