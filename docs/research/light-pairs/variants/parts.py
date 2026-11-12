"""Scratch edit of ticket t2-mlvy, cost only: the corner pass of key and fill, version 2, without one or both of its two
loops, to find what its time is made of. The image of these variants is wrong by design.
Usage: parts.py <root of a copy of the tree>, with PARTS=ring (no walk of the rings), lights (no loop over the lights)
or both in the environment."""
import os
import sys

parts = os.environ["PARTS"]
p = f"{sys.argv[1]}/src/Tertius.Rendering/Shaders/key_fill_corners.frag"
s = open(p).read()
ring = "    for (int r = 1; r < TileSize; r++)\n"
lights = "    for (uint i = 0u; i < frame.counts.x; i++)\n"
assert s.count(ring) == 1 and s.count(lights) == 1
if parts in ("ring", "both"):
    s = s.replace(ring, "    for (int r = 1; r < min(TileSize, 1); r++)\n")
if parts in ("lights", "both"):
    s = s.replace(lights, "    for (uint i = 0u; i < min(frame.counts.x, 0u); i++)\n")
open(p, "w").write(s)
