"""Diagnostic tool code (#48): the panel receiver of the 1:41 cockpit evaluated three ways from volume dumps, at level 0
with the trilinear weights of volume_receive.glsl and the receiver's offset of 0.45 cells along its normal:
  stored  = the three axial lobes of `filtered` weighted by n * n, as volumeLobes does today;
  rebuilt = the same lobes rebuilt from the field's 32 incoming directions, as volume_filter.comp builds them (a check
            that this tool reads the field right);
  cut     = one lobe around the receiver's own normal, from the 32 incoming directions with the cosine to the normal
            clamped at zero, so that no light from behind the surface counts. Same normalization as a stored lobe.
It prints each for two dumps and their difference, and splits the cut lobe's difference by the half sphere (along x, y
and z) that each direction comes from.
usage: receiver_cut.py <dump A> <dump B>  (prints A - B)"""
import sys, os, math, struct
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vol2 import Vol, FIELD, FIRST, SIDES, DIRS

POS, N = (-0.32, 0.19, -1.11), (-0.39, -0.84, 0.38)


def step(d):
    octant, major = d // 4, d % 4
    v = [1, 1, 1]
    if major < 3:
        v[major] = 3
    sign = [1 - 2 * (octant & 1), 1 - 2 * ((octant >> 1) & 1), 1 - 2 * (octant >> 2)]
    s = [v[i] * sign[i] for i in range(3)]
    length = math.sqrt(sum(x * x for x in s))
    return [x / length for x in s]


DIR = [step(d) for d in range(DIRS)]


def half(h):
    return struct.unpack('<e', struct.pack('<H', h))[0]


def incoming(v, level, d, local):
    o = FIELD + 16 * (DIRS * FIRST[level] + d * SIDES[level] ** 3 + local)
    words = struct.unpack_from('<4I', v.b, o)
    return [half(w & 0xFFFF) for w in words[:3]]


def corners(v, level=0):
    g = v.grids[level]
    p = [(POS[i] - g[i]) / g[3] + 0.45 * N[i] for i in range(3)]
    last = SIDES[level] - 1
    local = [min(max(x - 0.5, 0.0), float(last)) for x in p]
    base = [int(math.floor(x)) for x in local]
    f = [local[i] - base[i] for i in range(3)]
    for i in range(8):
        corner = (i % 2, (i // 2) % 2, i // 4)
        w = 1.0
        for a in range(3):
            w *= f[a] if corner[a] else 1 - f[a]
        c = [min(base[a] + corner[a], last) for a in range(3)]
        yield w, c[0] + SIDES[level] * (c[1] + SIDES[level] * c[2])


def lobe(v, local, axis_n, level=0):
    acc, total = [0.0] * 3, 0.0
    for d in range(DIRS):
        cosine = -sum(DIR[d][i] * axis_n[i] for i in range(3))
        if cosine <= 0:
            continue
        L = incoming(v, level, d, local)
        for k in range(3):
            acc[k] += cosine * L[k]
        total += cosine
    return [x / total for x in acc]


def evaluate(v):
    stored, rebuilt, cut = [0.0] * 3, [0.0] * 3, [0.0] * 3
    by_half = {}
    for w, local in corners(v):
        cell = FIRST[0] + local
        for a in range(3):
            side = 2 * a + (1 if N[a] < 0 else 0)
            axis = [0.0] * 3
            axis[a] = -1.0 if N[a] < 0 else 1.0
            s = v.filtered(cell, side)
            r = lobe(v, local, axis)
            for k in range(3):
                stored[k] += w * N[a] ** 2 * s[k]
                rebuilt[k] += w * N[a] ** 2 * r[k]
        c = lobe(v, local, N)
        for k in range(3):
            cut[k] += w * c[k]
        total = sum(max(0.0, -sum(DIR[d][i] * N[i] for i in range(3))) for d in range(DIRS))
        for d in range(DIRS):
            cosine = -sum(DIR[d][i] * N[i] for i in range(3))
            if cosine <= 0:
                continue
            red = w * cosine * incoming(v, 0, d, local)[0] / total
            for a, name in enumerate("xyz"):
                key = f"from {'+' if -DIR[d][a] > 0 else '-'}{name}"
                by_half[key] = by_half.get(key, 0.0) + red
    return stored, rebuilt, cut, by_half


A, B = Vol(sys.argv[1]), Vol(sys.argv[2])
ea, eb = evaluate(A), evaluate(B)
for i, name in enumerate(("stored lobes, n*n", "rebuilt lobes, n*n", "cut at the surface")):
    a, b = ea[i], eb[i]
    print(f"{name:20s} A ({a[0]:.5f}, {a[1]:.5f}, {a[2]:.5f})  B ({b[0]:.5f}, {b[1]:.5f}, {b[2]:.5f})"
          f"  A-B red {a[0] - b[0]:+.5f}")
print("cut lobe, A-B red by the half sphere each direction comes from (the halves overlap):")
for key in sorted(ea[3]):
    print(f"  {key}: {ea[3][key] - eb[3].get(key, 0.0):+.5f}")
