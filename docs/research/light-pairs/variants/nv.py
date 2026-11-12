"""Scratch edit of ticket t2-mlvy, variant "no corner shadows": the corner pass of key and fill, version 2, ranks the
lamps by their loss without a shadow lookup. Usage: nv.py <root of a copy of the tree>"""
import sys

p = f"{sys.argv[1]}/src/Tertius.Rendering/Shaders/key_fill_corners.frag"
s = open(p).read()
a = """        float visibility = pathVisibility(i, p, s.position, ShadowMapsMainView);
        if (visibility <= 0.0)
            continue;
"""
assert s.count(a) == 1
open(p, "w").write(s.replace(a, "        float visibility = 1.0;\n"))
