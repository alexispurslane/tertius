import sys
p = sys.argv[1]; s = open(p).read()
a = "        float visibility = pathVisibility(i, p, s.position, shadowMaps);\n        if (visibility > 0.0)\n"
assert s.count(a) == 1
s = s.replace(a, "        sum.diffuse += p.irradiance * 1e-6;\n        continue;\n" + a)
open(p, "w").write(s)
