# A timing proxy for a ray plan per 2 x 2 block: the first pixel of each block reads its four pixels once, picks each
# kind's chosen pixel once and plans all four. The other three invocations write only their light age.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; common.fixexp(root)
p = root + common.SH + 'screen_trace_plan.comp'
s = open(p).read()
i = s.index("void main()\n{")
new = """struct PlanPixel
{
    vec3 point;
    vec3 geometricNormal;
    vec3 normal;
    vec3 V;
    float alpha;
    float viewDepth;
};

bool fitsPlan(PlanPixel p, PlanPixel c, uint kind)
{
    bool fit = abs(dot(p.point - c.point, c.geometricNormal)) <= ThicknessShare * p.viewDepth
        && dot(p.geometricNormal, c.geometricNormal) >= SameNormalCosine;
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
    fit = fit && (kind != KindReflection
        || distance(reflect(-p.V, p.normal), reflect(-c.V, c.normal)) <= 2.0 * LobeFitShare * atan(p.alpha));
#endif
    return fit;
}

void main()
{
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(vec2(pixel), frame.viewport.xy)))
        return;
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
    imageStore(lightAge, pixel, vec4(lightAgeOf(pixel), 0.0, 0.0));
#endif
    if (((pixel.x | pixel.y) & 1) != 0)
        return;
    ivec2 block = pixel / 2;
    PlanPixel ps[4];
    bool traced[4];
    uint plans[4];
    for (int k = 0; k < 4; k++)
    {
        ivec2 q = 2 * block + ivec2(k & 1, k >> 1);
        plans[k] = 0u;
        traced[k] = all(lessThan(vec2(q), frame.viewport.xy)) && tracedSurface(q);
        if (traced[k])
        {
            TracingPixel t = tracingPixelAt(q);
            ps[k] = PlanPixel(t.point, t.s.geometricNormal, t.normal, t.V, t.alpha, t.viewDepth);
        }
    }
    for (uint kind = KindReflection; kind <= KindDiffuse; kind++)
    {
        uint turn = trace.drawIndex + drawOf(block, 0u, TurnDraw[kind]).x;
        int chosen = -1;
        for (uint k = 0u; k < 4u && chosen < 0; k++)
        {
            int corner = int((turn + k) & 3u);
            if (traced[corner] && (kind == KindDiffuse || sqrt(ps[corner].alpha) <= trace.maxRoughness))
                chosen = corner;
        }
        if (chosen < 0)
            continue;
        for (int k = 0; k < 4; k++)
        {
            if (k == chosen || !traced[k] || !(kind == KindDiffuse || sqrt(ps[k].alpha) <= trace.maxRoughness))
                continue;
            ivec2 q = 2 * block + ivec2(k & 1, k >> 1);
            if (historyLengthOf(q, kind) >= FullRateFrames && fitsPlan(ps[k], ps[chosen], kind))
                continue;
            if (kind == KindReflection)
            {
                plans[k] |= PlanOwnReflection;
                APPEND_OWN_RAY(reflectionOwnRays, q)
            }
            else
            {
                plans[k] |= PlanOwnDiffuse;
                APPEND_OWN_RAY(diffuseOwnRays, q)
            }
        }
    }
    for (int k = 0; k < 4; k++)
    {
        ivec2 q = 2 * block + ivec2(k & 1, k >> 1);
        if (all(lessThan(vec2(q), frame.viewport.xy)))
            imageStore(rayPlan, q, uvec4(plans[k]));
    }
}
"""
open(p, 'w').write(s[:i] + new)
