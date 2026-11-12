import sys
p = sys.argv[1]; s = open(p).read()
a = """    for (uint i = 0u; i < frame.counts.x; i++)
    {
        if (!viewKeeps(view, lights[i]) || shapeOf(lights[i]) != shape)
            continue;
        LightPath p;
        if (!lightPath(i, fiber, tinted, shape, s, p))
            continue;
        float visibility = pathVisibility(i, p, s.position, shadowMaps);
        if (visibility > 0.0)
        {
            DirectTerms terms = pathLight(i, fiber, shape, s, lobes, V, p);
            sum.diffuse += terms.diffuse * visibility;
            sum.specular += terms.specular * visibility;
        }
    }
"""
assert s.count(a) == 1
s = s.replace(a, """    for (uint base = 0u; base < frame.counts.x; base += 8u)
    {
        float seen[8];
        for (uint j = 0u; j < 8u; j++)
        {
            uint i = base + j;
            seen[j] = 0.0;
            if (i >= frame.counts.x || !viewKeeps(view, lights[i]) || shapeOf(lights[i]) != shape)
                continue;
            LightPath p;
            if (!lightPath(i, fiber, tinted, shape, s, p))
                continue;
            seen[j] = pathVisibility(i, p, s.position, shadowMaps);
        }
        for (uint j = 0u; j < 8u; j++)
        {
            if (seen[j] <= 0.0)
                continue;
            uint i = base + j;
            LightPath p;
            lightPath(i, fiber, tinted, shape, s, p);
            DirectTerms terms = pathLight(i, fiber, shape, s, lobes, V, p);
            sum.diffuse += terms.diffuse * seen[j];
            sum.specular += terms.specular * seen[j];
        }
    }
""")
open(p, "w").write(s)
