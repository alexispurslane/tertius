# The rectangle's two edge sums without the clip at the horizon: the vector sum of the four edges, then the form factor
# of a sphere cut by the horizon (Hill and Heitz 2016, with Unity HDRP's rational fit). A timing proxy for that change.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; common.fixexp(root)
p = root + common.SH + 'lighting.glsl'
s = open(p).read()
i = s.index("float cutCosine(vec3 q[4])\n{"); j = s.index("// The path from a surface point to a light of the list, before its shadows.")
new = """float cutCosine(vec3 q[4])
{
    for (int i = 0; i < 4; i++)
        q[i] = normalizedOr(q[i], vec3(0.0));
    vec3 F = 0.5 * (edgeVector(q[0], q[1]) + edgeVector(q[1], q[2]) + edgeVector(q[2], q[3]) + edgeVector(q[3], q[0]));
    float f2 = clamp(dot(F, F), 1e-12, 1.0);
    float x = min(sqrt(f2), 0.999);
    float y = clamp(F.z * inversesqrt(f2), -1.0, 1.0);
    float fit = x * (0.9245867471551246 + y) * (0.5 + (-0.9245867471551246 + y) * (0.5359050373687144 + x * (-1.0054221851257754 + x * (1.8199061187417047 - x * 1.3172081704209504))));
    return PI * clamp(fit, 0.0, 1.0);
}

"""
open(p, 'w').write(s[:i] + new + s[j:])
