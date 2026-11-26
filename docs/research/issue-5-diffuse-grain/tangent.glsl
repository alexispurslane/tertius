// Research only. A circular distance bound in both geometric tangent planes.
// The existing screen-space candidates and blue-noise anchor stay unchanged.
float probeSurfaceRadius(TracingPixel p)
{
    // Geometric mean of the horizontal and vertical front-facing pixel sizes, in m.
    vec2 pixelSize = 2.0 * vec2(length(frame.cameraRight.xyz), length(frame.cameraUp.xyz))
        * frame.viewport.zw * p.viewDepth;
    return 12.0 * sqrt(pixelSize.x * pixelSize.y);
}

bool probeTangentPair(TracingPixel first, TracingPixel second)
{
    vec3 apart = second.point - first.point;
    vec3 firstTangent = apart - dot(apart, first.s.geometricNormal) * first.s.geometricNormal;
    vec3 secondTangent = apart - dot(apart, second.s.geometricNormal) * second.s.geometricNormal;
    float distanceSquared = max(dot(firstTangent, firstTangent), dot(secondTangent, secondTangent));
    float radius = min(probeSurfaceRadius(first), probeSurfaceRadius(second));
    return distanceSquared <= radius * radius;
}
