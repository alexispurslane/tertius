# A real candidate: a ray reads its miss light only where it needs it, on a miss or where the hit fades into it. The
# same light: mix(miss, hit, 1) is the hit's light.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; common.fixexp(root)
p = root + common.SH + 'screen_trace.glsl'
common.sub(p, """    vec3 missLight = missLightOf(p, pixel, kind, result.direction);
    result.light = missLight;
    if (walkScreen(depthFinest, depthPyramid, walk, steps, hit, hitPixel, entryShare) != WalkHit)
    {
        // A miss has no guided density. A guided ray counts only where it hits a pixel near its target, so a miss brings
        // nothing.
        if (guiding)
            result.density = mixtureDensity(result.density, 0.0);
        if (guided)
            result.light = vec3(0.0);
        return result;
    }
""", """    if (walkScreen(depthFinest, depthPyramid, walk, steps, hit, hitPixel, entryShare) != WalkHit)
    {
        // A miss has no guided density. A guided ray counts only where it hits a pixel near its target, so a miss brings
        // nothing.
        if (guiding)
            result.density = mixtureDensity(result.density, 0.0);
        result.light = guided ? vec3(0.0) : missLightOf(p, pixel, kind, result.direction);
        return result;
    }
""")
common.sub(p, """    result.light = mix(missLight, hitLightOf(hitPixel, kind), fade);
""", """    vec3 hitLight = hitLightOf(hitPixel, kind);
    result.light = fade >= 1.0 ? hitLight : mix(missLightOf(p, pixel, kind, result.direction), hitLight, fade);
""")
