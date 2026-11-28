"""Diagnostic tool code: the shuttle art's geometry between the nose glow patches and the cabin floor, in the shuttle's
frame (art axes x ArtScale 1.6 + ArtPlace (0, 0, 1)). It prints the horizontal surfaces under the cockpit (their height,
facing and material), then casts a segment from each nose patch's centre to points on the cabin floor and counts the
triangles that each segment crosses.
usage (dev shell python): nose_floor_rays.py <lunarshuttle model.glb>"""
import sys, json, struct
import numpy as np

SCALE, PLACE = 1.6, np.array([0.0, 0.0, 1.0])
b = open(sys.argv[1], 'rb').read()
n = struct.unpack_from('<I', b, 12)[0]
j = json.loads(b[20:20 + n])
bin0 = 20 + n + 8


def acc(i):
    a = j['accessors'][i]
    v = j['bufferViews'][a['bufferView']]
    off = bin0 + v.get('byteOffset', 0) + a.get('byteOffset', 0)
    comps = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
    dt = np.dtype({5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}[a['componentType']])
    stride = v.get('byteStride', 0) or comps * dt.itemsize
    rows = [np.frombuffer(b, dt, comps, off + k * stride) for k in range(a['count'])]
    return np.array(rows)


tris, mats, glow = [], [], []
for node in j['nodes']:
    if 'mesh' not in node:
        continue
    for p in j['meshes'][node['mesh']]['primitives']:
        name = j['materials'][p['material']]['name']
        pos = acc(p['attributes']['POSITION']).astype(np.float64) * SCALE + PLACE
        t = pos[acc(p['indices']).reshape(-1, 3)]
        tris.append(t)
        mats += [name] * len(t)
tris = np.concatenate(tris)
mats = np.array(mats)
c = tris.mean(1)
nrm = np.cross(tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0])
area = 0.5 * np.linalg.norm(nrm, axis=1)
nrm /= np.maximum(2 * area[:, None], 1e-12)

print("horizontal surfaces (|ny| > 0.7) with centre |x| < 2.2, -15.5 < z < -8, -3 < y < 1, by height:")
sel = (np.abs(nrm[:, 1]) > 0.7) & (np.abs(c[:, 0]) < 2.2) & (c[:, 2] > -15.5) & (c[:, 2] < -8) & (c[:, 1] > -3) & (c[:, 1] < 1)
for y in np.unique(np.round(c[sel, 1], 1)):
    s = sel & (np.round(c[:, 1], 1) == y)
    up = (nrm[s, 1] > 0).sum()
    print(f"  y {y:+.1f}: {s.sum():4d} triangles, {area[s].sum():.2f} m2, {up} face up, {s.sum() - up} face down,"
          f" z {c[s, 2].min():.2f}..{c[s, 2].max():.2f}, materials {sorted(set(mats[s]))}")

glowsel = np.array(['glow' in m for m in mats]) & (c[:, 2] < -14) & (c[:, 1] < -1)
print(f"nose glow triangles: {glowsel.sum()}")
starts = c[glowsel]


def crossings(a, d):
    """The triangles that the segment a -> a + d crosses (Moller-Trumbore), excluding its two ends."""
    e1, e2 = tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0]
    h = np.cross(d, e2)
    det = (e1 * h).sum(1)
    ok = np.abs(det) > 1e-12
    inv = np.where(ok, 1 / np.where(ok, det, 1), 0)
    s = a - tris[:, 0]
    u = (s * h).sum(1) * inv
    q = np.cross(s, e1)
    v = (q * d).sum(1) * inv
    t = (q * e2).sum(1) * inv
    hit = ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 1e-3) & (t < 1 - 1e-3)
    return np.nonzero(hit)[0], t


targets = [(x, y, z) for x in (-0.6, 0.0, 0.6) for z in (-10.5, -11.5, -12.5) for y in (float(sys.argv[2]) if len(sys.argv) > 2 else 0.05,)]
print("segments from each nose glow triangle centre to cabin floor points (just above the floor height given):")
counts = {}
for tgt in targets:
    tgt = np.array(tgt)
    ks = []
    for a in starts[::max(1, len(starts) // 24)]:
        hits, t = crossings(a, tgt - a)
        ks.append(len(hits))
        for h in hits:
            counts[(round(c[h, 1], 1), mats[h])] = counts.get((round(c[h, 1], 1), mats[h]), 0) + 1
    print(f"  to {tuple(tgt)}: crossings min {min(ks)} max {max(ks)} mean {np.mean(ks):.1f}")
print("crossed surfaces (height, material): count")
for k, v in sorted(counts.items(), key=lambda kv: -kv[1])[:12]:
    print(f"  {k}: {v}")
