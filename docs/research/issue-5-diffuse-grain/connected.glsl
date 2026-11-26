// Research: a dense disk, positive at every integer offset strictly inside radius 11.
// The full-disk normalizer includes the center. Rejected shares stay at the center.
bool probeDenseConnection(ivec2 pixel, ivec2 neighbor, TracingPixel first, TracingPixel second)
{
    ivec2 delta = abs(neighbor - pixel);
    int steps = max(delta.x, delta.y);
    for (int i = 1; i < steps; i++)
    {
        // Reversing endpoints visits the exact same 8-connected raster path.
        ivec2 cell = (pixel * (steps - i) + neighbor * i + ivec2(steps / 2)) / steps;
        if (!tracedSurface(cell))
            return false;
        TracingPixel middle = tracingPixelAt(cell);
        if (!probeCompatible(first, middle) || !probeCompatible(second, middle))
            return false;
    }
    return true;
}

vec3 probeConnectedSpread(ivec2 pixel, TracingPixel center, MaterialFactors factors, vec3 source)
{
    if (handOverOf(center) <= 0.0 || any(lessThan(factors.specular, vec3(LeastFactor))))
        return source;
    vec3 outgoing = factors.specular * source;
    vec3 moved = vec3(0.0);
    for (int y = -10; y <= 10; y++)
        for (int x = -10; x <= 10; x++)
        {
            int distanceSquared = x * x + y * y;
            if (distanceSquared == 0 || distanceSquared >= 121)
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
            float falloff = 1.0 - float(distanceSquared) / 121.0;
            float share = falloff * falloff / 126.71545659449492;
            moved += share * (probeOutgoingAt(neighbor, other, otherFactors) - outgoing);
        }
    return (outgoing + moved) / factors.specular;
}
