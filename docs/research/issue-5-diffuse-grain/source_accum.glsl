// Research only: isolate diffuse-ray-sourced specular RGB and accumulate it at the
// exact same physical surface points over genuinely fresh draws via motion reprojection
// and specularPassBits (rejecting depth/normal/roughness discontinuities such as the
// windshield boundary). Leaves diffuse light, dedicated reflection rays, and glossy
// surfaces (handOver == 0) untouched.

layout(set = 3, binding = 30, rgba32f) uniform readonly image2D prevProbeSourceAccum;
layout(set = 3, binding = 31, rgba32f) uniform writeonly image2D currProbeSourceAccum;

vec4 probeReadPrevAccum(Reprojection r)
{
    vec4 total = vec4(0.0);
    for (uint tap = 0u; tap < 4u; tap++)
        if (r.weights[tap] > 0.0)
            total += r.weights[tap] * imageLoad(prevProbeSourceAccum, tapTexel(r, tap));
    return total;
}

vec3 probeAccumulateIsolatedSource(ivec2 pixel, TracingPixel center, MaterialFactors factors, vec3 source, bool applySmallSpread)
{
    float ho = handOverOf(center);
    if (ho <= 0.0 || any(lessThan(factors.specular, vec3(LeastFactor))))
    {
        imageStore(currProbeSourceAccum, pixel, vec4(0.0));
        return source;
    }

    vec3 rawOutgoing = factors.specular * source;
    if (applySmallSpread)
    {
        // Gentle reciprocal connected spread (radius 5 render pixels, exact outgoing-RGB exchange).
        // Sum of (1 - d^2 / 25)^2 over 0 <= d^2 < 25 is 26.1968 (including center = 1.0).
        vec3 moved = vec3(0.0);
        for (int y = -4; y <= 4; y++)
            for (int x = -4; x <= 4; x++)
            {
                int d2 = x * x + y * y;
                if (d2 == 0 || d2 >= 25)
                    continue;
                ivec2 neighbor = pixel + ivec2(x, y);
                if (any(lessThan(neighbor, ivec2(0)))
                    || any(greaterThanEqual(neighbor, ivec2(frame.viewport.xy))) || !tracedSurface(neighbor))
                    continue;
                TracingPixel other = tracingPixelAt(neighbor);
                if (!probeCompatible(center, other) || !probeDenseConnection(pixel, neighbor, center, other))
                    continue;
                MaterialFactors otherFactors = materialFactors(other.s, lobesOf(other.s, other.V));
                if (any(lessThan(otherFactors.specular, vec3(LeastFactor))))
                    continue;
                float falloff = 1.0 - float(d2) / 25.0;
                float share = (falloff * falloff) / 26.1968;
                moved += share * (probeOutgoingAt(neighbor, other, otherFactors) - rawOutgoing);
            }
        rawOutgoing += moved;
    }

    uint bits = imageLoad(surfaceTest, pixel).r;
    vec4 motion = imageLoad(motionTarget, pixel);
    vec2 surfacePlace = readPlace(vec2(pixel) + 0.5, motion);
    Reprojection r = reprojectionOf(surfacePlace, specularPassBits(bits));

    vec3 accumOutgoing = rawOutgoing;
    float count = 1.0;
    if (holdsHistory(r))
    {
        vec4 prev = probeReadPrevAccum(r);
        float prevCount = prev.a;
        if (prevCount > 0.0)
        {
            float ratio = historyRatio(exposureStops, previousStops);
            vec3 prevRgb = exposed(prev.rgb, ratio);
            count = min(prevCount + 1.0, PROBE_ACCUM_CAP);
            accumOutgoing = prevRgb + (rawOutgoing - prevRgb) / count;
        }
    }

    imageStore(currProbeSourceAccum, pixel, vec4(accumOutgoing, count));
    return accumOutgoing / factors.specular;
}
