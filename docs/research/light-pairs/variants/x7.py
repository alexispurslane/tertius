import sys
p = sys.argv[1]; s = open(p).read()
for a in ["visibility *= cascadeShadow(set, position, geometricNormal, sinTheta);",
          "visibility *= cascadeShadowHolding(set, position, geometricNormal, sinTheta);"]:
    assert s.count(a) == 1
    s = s.replace(a, "visibility *= 1.0;")
open(p, "w").write(s)
