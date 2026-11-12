# The cascade bounds with the taps in loops whose count the bounds give at run time, so that no compiler can run the
# taps of a settled lookup ahead of the test.
import shutil, sys
root = sys.argv[1]
shutil.copyfile('/private/tmp/t2-mlvy/main-lighting.glsl', root + '/src/Tertius.Rendering/Shaders/lighting.glsl')
p = root + '/src/Tertius.Rendering/Shaders/shadows.glsl'
s = open(p).read()
a = """    if (reference < bounds.x)
        return 0.0;
    if (reference >= bounds.y)
        return 1.0;

    float lit = 0.0;
    for (int y = 0; y < 4; y++)
    {
        for (int x = 0; x < 4; x++)
        {"""
assert s.count(a) == 1, a
s = s.replace(a, """    bool settledLit = reference >= bounds.y;
    int taps = settledLit || reference < bounds.x ? 0 : 4;
    float lit = settledLit ? 16.0 : 0.0;
    for (int y = 0; y < taps; y++)
    {
        for (int x = 0; x < taps; x++)
        {""")
open(p, 'w').write(s)
