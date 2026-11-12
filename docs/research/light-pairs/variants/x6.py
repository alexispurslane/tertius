import sys
p = sys.argv[1]; s = open(p).read()
a = "        return shadowMaps == ShadowMapsNone ? 1.0 : tileShadow(light, position, L, d, geometricNormal, sinTheta);\n"
assert s.count(a) == 1
s = s.replace(a, "        return 1.0;\n")
open(p, "w").write(s)
