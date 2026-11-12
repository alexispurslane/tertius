# A real candidate: the normal stop's power of 128 as seven squarings, the same value up to rounding.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; common.fixexp(root)
p = root + common.SH + 'diffuse_filter.glsl'
common.sub(p, "    float normal = pow(max(dot(a.normal, b.normal), 0.0), NormalPower);",
              "    float normal = max(dot(a.normal, b.normal), 0.0);\n    normal *= normal; normal *= normal; normal *= normal; normal *= normal; normal *= normal; normal *= normal; normal *= normal;")
