import sys
p = sys.argv[1]; s = open(p).read()
a = "        float visibility = pathVisibility(i, p, s.position, shadowMaps);\n"
assert s.count(a) == 1
s = s.replace(a, "        float visibility = 1.0;\n")
open(p, "w").write(s)
