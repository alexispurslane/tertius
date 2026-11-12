import sys
p = sys.argv[1]; s = open(p).read()
a = "            DirectTerms terms = pathLight(i, fiber, shape, s, lobes, V, p);\n"
assert s.count(a) == 1
s = s.replace(a, "            DirectTerms terms;\n            terms.diffuse = p.irradiance * 1e-3;\n            terms.specular = vec3(0.0);\n")
open(p, "w").write(s)
