import sys
root = sys.argv[1]
p = root + '/src/Tertius.Rendering/Shaders/lighting.glsl'
s = open(p).read()
def sub(a, b):
    global s
    assert s.count(a) == 1, a
    s = s.replace(a, b)
sub("// shapeLight's loop under a visible floor", "uint debugPairs = 0u;\n\n// shapeLight's loop under a visible floor")
sub("""        if (!lightPath(i, fiber, tinted, shape, s, p))
            continue;
        bool bounded = shape == RectShape && !fiber;""", """        if (!lightPath(i, fiber, tinted, shape, s, p))
            continue;
        float debugMode = frame.renderParams.w;
        if (debugMode == 0.0625)
            debugPairs++;
        bool bounded = shape == RectShape && !fiber;""")
sub("""        if (share <= 0.0)
            continue;
        float visibility = pathVisibility(i, p, s.position, shadowMaps);
        if (visibility <= 0.0)
            continue;
        if (bounded)""", """        float visibility = pathVisibility(i, p, s.position, shadowMaps);
        if (debugMode == 0.03125 && visibility > 0.0)
            debugPairs++;
        uint debugSlot = lights[i].shadow.x;
        if (debugMode == 0.015625 && debugSlot == AtlasTilesSlot)
            debugPairs++;
        if (debugMode == 0.0078125 && debugSlot != AtlasTilesSlot && debugSlot != NoSlot)
            debugPairs++;
        if (debugMode == 0.00390625 && visibility > 0.0 && visibility < 1.0)
            debugPairs++;
        if (share <= 0.0)
            continue;
        if (visibility <= 0.0)
            continue;
        if (debugMode < 0.003)
            debugPairs++;
        if (bounded)""")
open(p, 'w').write(s)
p = root + '/src/Tertius.Rendering/Shaders/direct.frag'
s = open(p).read()
sub("""    outDiffuse = vec4(demodulated(direct.diffuse, factors.diffuse, k), 1.0);
    outSpecular = vec4(demodulated(direct.specular, factors.specular, k), 1.0);""", """    uint c = debugPairs;
    vec3 color = c == 0u ? vec3(0.03) : c <= 2u ? vec3(0.0, 0.0, 1.0) : c <= 4u ? vec3(0.0, 1.0, 1.0)
        : c <= 6u ? vec3(0.0, 1.0, 0.0) : c <= 8u ? vec3(1.0, 1.0, 0.0) : c <= 12u ? vec3(1.0, 0.45, 0.0) : vec3(1.0, 0.0, 0.0);
    outDiffuse = vec4(color * 0.4, 1.0);
    outSpecular = vec4(0.0);""")
open(p, 'w').write(s)
