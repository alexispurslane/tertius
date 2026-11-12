import re, sys
p = sys.argv[1]; s = open(p).read()
s, n = re.subn(r"farShadowVisibility\(position, L, angularRadius, light\.occluders, [^;]*\);", "1.0;", s)
assert n == 3, n
open(p, "w").write(s)
