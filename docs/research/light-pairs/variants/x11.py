# One compare tap at the lookup in place of the sixteen: what the taps themselves cost.
import sys
p = sys.argv[1] + '/src/Tertius.Rendering/Shaders/shadows.glsl'
s = open(p).read()
a = """    for (int y = 0; y < 4; y++)
    {
        for (int x = 0; x < 4; x++)
        {
            vec2 offset = vec2(float(x) - 1.5, float(y) - 1.5) * invSize;"""
assert s.count(a) == 1
s = s.replace(a, """    for (int y = 0; y < 1; y++)
    {
        for (int x = 0; x < 1; x++)
        {
            vec2 offset = vec2(0.0) * invSize;""")
a = "    return lit * (1.0 / 16.0);\n}\n\n// How far a world position is beyond the shadow maps"
assert s.count(a) == 1
s = s.replace(a, "    return lit;\n}\n\n// How far a world position is beyond the shadow maps")
open(p, 'w').write(s)
