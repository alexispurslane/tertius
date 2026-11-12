# Every rectangle lamp responds as a small sphere light: what the rectangle's edge sums cost against a point's response.
import sys
p = sys.argv[1]; s = open(p).read()
a = "    if (shape == RectShape)\n        terms = rectResponse(fiber, s, lobes, V, p);\n"
assert s.count(a) == 1
s = s.replace(a, "    if (shape == RectShape)\n    {\n        terms = respond(fiber, s, lobes, V, p.L, 0.1);\n        terms.diffuse *= 0.05;\n        terms.specular *= 0.05;\n    }\n")
open(p, "w").write(s)
