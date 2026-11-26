// Screen-space diagnosis. Each undirected edge gets one fixed, decorrelated blue-noise
// decision. The endpoint anchoring makes the decision identical in both directions.
const ivec2 ProbeWideOffsets[12] = ivec2[12](
    ivec2(3, 1), ivec2(-1, 3), ivec2(4, -3), ivec2(5, 1),
    ivec2(-2, 6), ivec2(7, 4), ivec2(-5, 7), ivec2(9, -2),
    ivec2(3, 10), ivec2(-8, 8), ivec2(11, 4), ivec2(0, -12));

vec3 probeExchange(ivec2 pixel, ivec2 neighbor, TracingPixel center,
    MaterialFactors factors, vec3 outgoing, float share)
{
    if (any(lessThan(neighbor, ivec2(0)))
        || any(greaterThanEqual(neighbor, ivec2(frame.viewport.xy))) || !tracedSurface(neighbor))
        return vec3(0.0);
    TracingPixel other = tracingPixelAt(neighbor);
    if (!probeCompatible(center, other) || !probeConnection(pixel, neighbor, center, other))
        return vec3(0.0);
    MaterialFactors otherFactors = materialFactors(other.s, lobesOf(other.s, other.V));
    if (any(lessThan(otherFactors.specular, vec3(LeastFactor))))
        return vec3(0.0);
    return share * (probeOutgoingAt(neighbor, other, otherFactors) - outgoing);
}

vec3 probeIrregularSpread(ivec2 pixel, TracingPixel center, MaterialFactors factors, vec3 source)
{
    if (handOverOf(center) <= 0.0 || any(lessThan(factors.specular, vec3(LeastFactor))))
        return source;
    vec3 outgoing = factors.specular * source;
    vec3 moved = vec3(0.0);
    // Eight one-pixel connections join all integer-coordinate phases. Total share <= 1/2.
    for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
            if (x != 0 || y != 0)
                moved += probeExchange(pixel, pixel + ivec2(x, y), center, factors, outgoing, 1.0 / 16.0);
    // 24 candidate wide connections have distinct directions and lengths. Total share <= 1/2.
    for (int index = 0; index < 12; index++)
        for (int side = -1; side <= 1; side += 2)
        {
            ivec2 neighbor = pixel + side * ProbeWideOffsets[index];
            ivec2 anchor = side > 0 ? pixel : neighbor;
            ivec2 noiseCell = anchor + ivec2(37 * index, 53 * index);
            uint choice = blueNoiseBytesOf(noiseCell, 32u + uint(index)).r;
            if (choice < 192u)
                moved += probeExchange(pixel, neighbor, center, factors, outgoing, 1.0 / 48.0);
        }
    return (outgoing + moved) / factors.specular;
}
