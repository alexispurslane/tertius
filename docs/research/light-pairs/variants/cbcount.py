# The cascade bounds tree, colored by what each pixel's cascade lookups did: red where a lookup took the 16 taps, blue
# where the bounds settled every lookup dark, green where they settled every lookup lit, grey without a lookup.
import shutil, sys
root = sys.argv[1]
shutil.copyfile('/private/tmp/t2-mlvy/main-lighting.glsl', root + '/src/Tertius.Rendering/Shaders/lighting.glsl')
p = root + '/src/Tertius.Rendering/Shaders/shadows.glsl'
s = open(p).read()
def sub(a, b):
    global s
    assert s.count(a) == 1, a
    s = s.replace(a, b)
sub("#include \"cube_faces.glsl\"\n", "#include \"cube_faces.glsl\"\n\nuint debugCascade = 0u;\n")
sub("""    if (reference < bounds.x)
        return 0.0;
    if (reference >= bounds.y)
        return 1.0;
""", """    if (reference < bounds.x)
    {
        debugCascade |= 1u;
        return 0.0;
    }
    if (reference >= bounds.y)
    {
        debugCascade |= 2u;
        return 1.0;
    }
    debugCascade |= 4u;
""")
open(p, 'w').write(s)
p = root + '/src/Tertius.Rendering/Shaders/direct.frag'
s = open(p).read()
sub("""    outDiffuse = vec4(demodulated(direct.diffuse, factors.diffuse, k), 1.0);
    outSpecular = vec4(demodulated(direct.specular, factors.specular, k), 1.0);""", """    uint c = debugCascade;
    vec3 color = (c & 4u) != 0u ? vec3(1.0, 0.0, 0.0) : c == 1u ? vec3(0.0, 0.0, 1.0) : c == 2u ? vec3(0.0, 1.0, 0.0)
        : c == 3u ? vec3(0.0, 1.0, 1.0) : vec3(0.05);
    outDiffuse = vec4(color * 0.4, 1.0);
    outSpecular = vec4(0.0);""")
open(p, 'w').write(s)
