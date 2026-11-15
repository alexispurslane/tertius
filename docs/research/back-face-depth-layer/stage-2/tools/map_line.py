#!/usr/bin/env python3
"""Throwaway (t1-3j2j): the shadow map's stored depth along a line across a strip, beside the receiver's depth.

  python3 -I map_line.py <dump folder> <scene.json> <cascade> <x y z> <x y z> <count>

It takes <count> receiver points on the world line between the two points (camera-relative, m), projects each into the
cascade without the normal offset, and prints the texel that holds it, the receiver's reversed depth, the stored depth of
that texel, and their gap in m along the sun: positive where the stored caster lies nearer the sun than the receiver.
A stored depth of 0 is the cleared map: no caster. The dump is ShadowProbe's, as in engine_shadow.py.
"""
import json
import sys
from pathlib import Path

import numpy as np

dump = Path(sys.argv[1])
fit = json.loads((dump / "cascades.json").read_text())
cascade = int(sys.argv[3])
a = np.array(list(map(float, sys.argv[4:7])))
b = np.array(list(map(float, sys.argv[7:10])))
count = int(sys.argv[10])
size = fit["mapSize"]
info = fit["cascades"][cascade]
m = np.array(info["viewProjectionRows"], dtype=np.float64).reshape(4, 4)
depth = np.fromfile(dump / f"cascade-{cascade}.f32", "<f4").reshape(size, size)
metres = 2 * info["depthHalfRange"]
print(f"cascade {cascade}, texel {info['texel']:.5f} m, one unit of depth {metres:.1f} m")
print("point (m), texel (column, row), receiver depth, stored depth, gap (m)")
for i in range(count):
    p = a + (b - a) * i / max(count - 1, 1)
    clip = np.append(p, 1.0) @ m
    u, v = (clip[0] * 0.5 + 0.5) * size, (clip[1] * 0.5 + 0.5) * size
    stored = float(depth[int(np.floor(v)), int(np.floor(u))])
    gap = (stored - clip[2]) * metres
    print(np.round(p, 4).tolist(), (int(np.floor(u)), int(np.floor(v))), f"{clip[2]:.7f}", f"{stored:.7f}",
          "no caster" if stored == 0 else f"{gap:.3f}")
