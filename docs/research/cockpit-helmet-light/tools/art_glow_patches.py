"""Diagnostic tool code: the connected patches of the shuttle art's glow triangles in the shuttle's frame (the art's axes
scaled by ArtScale 1.6 and moved by ArtPlace (0, 0, 1), DemoScenes.Shuttle), with their area, facing and emitted power,
beside the demo's nine lamp lights.
usage (dev shell python): art_glow_patches.py <lunarshuttle model.glb>"""
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
    raw = np.frombuffer(b, dtype=np.uint8, count=stride * (a['count'] - 1) + comps * dt.itemsize, offset=off)
    out = np.empty((a['count'], comps), dt)
    for k in range(a['count']):
        out[k] = np.frombuffer(raw[k * stride:k * stride + comps * dt.itemsize].tobytes(), dt)
    return out


def components(tris):
    parent = {}

    def find(x):
        while parent.setdefault(x, x) != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x
    for t in tris:
        for v in t[1:]:
            parent[find(v)] = find(t[0])
    groups = {}
    for i, t in enumerate(tris):
        groups.setdefault(find(t[0]), []).append(i)
    return list(groups.values())


rows = []
for node in j['nodes']:
    if 'mesh' not in node:
        continue
    assert not any(k in node for k in ('translation', 'rotation', 'scale', 'matrix')), 'node transform not handled'
    for p in j['meshes'][node['mesh']]['primitives']:
        mat = j['materials'][p['material']]
        if 'glow' not in mat['name']:
            continue
        e = np.array(mat['emissiveFactor']) * mat.get('extensions', {}).get('KHR_materials_emissive_strength', {}).get('emissiveStrength', 1)
        pos = acc(p['attributes']['POSITION']).astype(np.float64) * SCALE + PLACE
        # Merge coincident vertices so that a patch split by its UV seams counts as one.
        key = {tuple(np.round(v, 4)): i for i, v in enumerate(pos)}
        canon = np.array([key[tuple(np.round(v, 4))] for v in pos])
        idx = canon[acc(p['indices']).reshape(-1, 3)]
        for comp in components([tuple(t) for t in idx]):
            t = idx[comp]
            a, b_, c = pos[t[:, 0]], pos[t[:, 1]], pos[t[:, 2]]
            cr = np.cross(b_ - a, c - a)
            area = 0.5 * np.linalg.norm(cr, axis=1).sum()
            normal = cr.sum(0) / max(np.linalg.norm(cr.sum(0)), 1e-12)
            pts = pos[np.unique(t)]
            rows.append((mat['name'], len(comp), area, pts.min(0), pts.max(0), normal, e, mat.get('doubleSided', False)))

print("material | triangles | area m² | min (x, y, z) | max (x, y, z) | facing | emission | per-channel power π·L·A (W) | double-sided")
for name, count, area, lo, hi, nrm, e, ds in rows:
    power = np.pi * e * area
    print(f"{name} | {count} | {area:.3f} | ({lo[0]:+.2f}, {lo[1]:+.2f}, {lo[2]:+.2f}) | ({hi[0]:+.2f}, {hi[1]:+.2f}, {hi[2]:+.2f})"
          f" | ({nrm[0]:+.2f}, {nrm[1]:+.2f}, {nrm[2]:+.2f}) | ({e[0]:.2f}, {e[1]:.2f}, {e[2]:.2f}) |"
          f" ({power[0]:.2f}, {power[1]:.2f}, {power[2]:.2f}) | {ds}")
