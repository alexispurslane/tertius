# The cascade lookup with a fast path: one raw depth fetch at the lookup's texel, and where the reference lies clearly
# off it, one compare tap in place of the sixteen. A timing proxy for a bounds image of the cascades, not an exact filter.
import sys
p = sys.argv[1] + '/src/Tertius.Rendering/Shaders/shadows.glsl'
s = open(p).read()
a = """    float lit = 0.0;
    for (int y = 0; y < 4; y++)
    {
        for (int x = 0; x < 4; x++)
        {
            vec2 offset = vec2(float(x) - 1.5, float(y) - 1.5) * invSize;"""
assert s.count(a) == 1
s = s.replace(a, """    float rawSize = float(textureSize(shadowCascades, 0).x);
    float raw = texelFetch(shadowCascades, ivec3(ivec2(clamp(uv * rawSize, vec2(0.0), vec2(rawSize - 1.0))), int(layer)), 0).r;
    if (abs(reference - raw) > 0.01)
        return textureGrad(sampler2DArrayShadow(shadowCascades, shadowSampler), vec4(uv, layer, reference), vec2(0.0), vec2(0.0));
""" + a)
open(p, 'w').write(s)
