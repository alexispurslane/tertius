"""Diagnostic tool code: the two small glow0 patches near the cockpit ceiling against the overhead panel of the 1:41
shot. Part 1 (art geometry): what each side of each patch faces, and the distance and cosines from each patch to the
panel receiver, with an occlusion test. Part 2 (volume dumps): the light that the patches add to the panel receiver
(N0 minus today), split by the receiver's three axial lobes (volume_receive.glsl, volumeLobes, weights n * n).
The panel receiver is the one of README.md, "Direction count": (-0.32, +0.19, -1.11) m from the camera, normal
(-0.39, -0.84, +0.38). With the camera at about (1.55, 1.18, -9.08) in the shuttle frame (from the patches' level-0
cells), it lies at (1.23, 1.37, -10.19).
usage (dev shell python): ceiling_patches.py <lunarshuttle model.glb> <cases root>"""
import sys, os, math
import numpy as np
here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, here)
from vol2 import Vol

glb, root = sys.argv[1], sys.argv[2]
g = {}
sys.argv = [sys.argv[0], glb]
exec(open(os.path.join(here, 'nose_floor_rays.py')).read().split('print("horizontal')[0], g)
tris, mats, c = g['tris'], g['mats'], g['c']
e1, e2 = tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0]
nrm = np.cross(e1, e2); nrm /= np.linalg.norm(nrm, axis=1)[:, None]
def first(o, d):
    h = np.cross(d, e2); det = (e1 * h).sum(1); ok = np.abs(det) > 1e-12
    inv = np.where(ok, 1 / np.where(ok, det, 1), 0); s = o - tris[:, 0]
    u = (s * h).sum(1) * inv; q = np.cross(s, e1); v = (q * d).sum(1) * inv; t = (q * e2).sum(1) * inv
    hit = ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 1e-3)
    if not hit.any(): return None
    k = np.nonzero(hit)[0][np.argmin(t[hit])]
    return round(float(t[k]), 3), mats[k], nrm[k].round(2)
sel = np.nonzero((mats == 'lunarshuttle_fuselage.glow0') & (c[:, 1] > 1))[0]
for k in sel:
    o, n = c[k], nrm[k]
    print('patch', o.round(2), 'front', n.round(2))
    for name, d in (('front', n), ('back', -n), ('up', np.array([0, 1.0, 0]))):
        print('  ', name, 'first hit (distance, material, its normal):', first(o + 1e-3 * d, d))
p = np.array([1.23, 1.37, -10.19]); pn = np.array([-0.39, -0.84, 0.38])
for k in sel:
    v = p - c[k]; dist = np.linalg.norm(v); d = v / dist
    print('patch', c[k].round(2), 'to panel: distance %.2f, cos at patch front %.3f, cos at panel %.3f' % (dist, d @ nrm[k], -d @ pn),
          'occluded' if (first(c[k] + 1e-3 * d, d) or (99,))[0] < dist - 0.01 else 'clear')

pos, n = (-0.32, 0.19, -1.11), (-0.39, -0.84, 0.38)
a, b = Vol(f"{root}/glowN0/normal/volume60.bin"), Vol(f"{root}/today/normal/volume60.bin")
level, last = 0, 15
gr = a.grids[level]
q = [(pos[i] - gr[i]) / gr[3] + 0.45 * n[i] for i in range(3)]
local = [min(max(x - 0.5, 0.0), float(last)) for x in q]
base = [int(math.floor(x)) for x in local]
f = [local[i] - base[i] for i in range(3)]
names = ['+x', '-x', '+y', '-y', '+z', '-z']
added = [[0.0] * 3 for _ in range(3)]
for i in range(8):
    corner = (i % 2, (i // 2) % 2, i // 4)
    w = 1.0
    for ax in range(3):
        w *= f[ax] if corner[ax] else 1 - f[ax]
    cell = a.cell(level, [min(base[ax] + corner[ax], last) for ax in range(3)])
    for ax in range(3):
        idx = 2 * ax + (1 if n[ax] < 0 else 0)
        va, vb = a.filtered(cell, idx), b.filtered(cell, idx)
        for k in range(3):
            added[ax][k] += w * n[ax] ** 2 * (va[k] - vb[k])
for name, cs in (('today', b), ('N0', a)):
    d, _, used = cs.receive(pos, n, n, n)
    print(f"{name}: volume diffuse light at the panel receiver (R, G, B) ({d[0]:.5f}, {d[1]:.5f}, {d[2]:.5f}), levels {used}")
for ax in range(3):
    idx = 2 * ax + (1 if n[ax] < 0 else 0)
    print(f"lobe {names[idx]} weight {n[ax] ** 2:.2f}: added red {added[ax][0]:.5f}")
