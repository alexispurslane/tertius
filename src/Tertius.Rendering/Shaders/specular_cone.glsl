#ifndef TERTIUS_SPECULAR_CONE_GLSL
#define TERTIUS_SPECULAR_CONE_GLSL

const float ConeTangentPerAlpha = 1.7602; // MapSearchCone.TangentPerAlpha: sqrt((1 − 0.244) / 0.244)

// tan θ_c of the cone of the lobe of alpha, the floored roughness squared: MapSearchCone.OfAlpha.
float lobeConeTangent(float alpha)
{
    return ConeTangentPerAlpha * alpha;
}

// The level of a chain whose level-0 texel spans unitAngle rad that a cone of tangent tanHalfAngle selects, rayLength m
// along its ray, at a point hitDistance m from the eye: log₂ of the cone's radius there in level-0 texels, clamped to 0
// to topLevel: MapSearchCone.Level. The floor keeps log2 finite for a hit at the origin; any level below 0 reads level
// 0. The arithmetic is precise, so that a fragment and a compute stage choose the same level from the same inputs.
float coneLevelAt(float tanHalfAngle, float rayLength, float hitDistance, float unitAngle, float topLevel)
{
    precise float radiusTexels = rayLength * tanHalfAngle / (hitDistance * unitAngle);
    precise float level = clamp(log2(max(radiusTexels, 1e-6)), 0.0, topLevel);
    return level;
}

#endif
