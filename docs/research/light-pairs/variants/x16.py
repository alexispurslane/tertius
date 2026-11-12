# Only the lights with cascades or far shadows are shaded: what the pass costs without its lamps.
import sys
p = sys.argv[1]; s = open(p).read()
a = "        if (!viewKeeps(view, lights[i]) || shapeOf(lights[i]) != shape)\n            continue;\n"
assert s.count(a) == 1
s = s.replace(a, a + "        if (lights[i].shadow.x == NoSlot || lights[i].shadow.x == AtlasTilesSlot)\n            continue;\n")
open(p, "w").write(s)
