import sys
p = sys.argv[1]; s = open(p).read()
a = "        if (!viewKeeps(view, lights[i]) || shapeOf(lights[i]) != shape)\n            continue;\n"
assert s.count(a) == 1
s = s.replace(a, a + "        if (i % 3u != 0u)\n            continue;\n")
open(p, "w").write(s)
