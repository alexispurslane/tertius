#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "gbuffer_inputs.glsl"

// Puffs in the transparent pass, added to the HDR image: color × (1 − ρ²)² × fade, where ρ is the distance of the
// fragment's point from the puff's center over the radius. The fade is one rule: a puff fades within one radius of
// anything opaque, and the eye counts as an opaque point at distance 0. So a puff that cuts into a wall shows no hard
// line, a puff behind a wall draws nothing, and a puff around the eye fades.

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec2 inCorner;
layout(location = 2) flat in vec3 inRadiance;
layout(location = 3) flat in float inRadius;

layout(location = 0) out vec4 outColor;

void main()
{
    // Outside the circle ρ is above 1 and the profile is 0.
    float rhoSquared = dot(inCorner, inCorner);
    float edge = max(1.0 - rhoSquared, 0.0);
    float profile = edge * edge;

    // The G-buffer distance covers every depth range. It is 0 for sky, which counts as no surface.
    float opaqueDistance = texelFetch(gbufferDistance, ivec2(gl_FragCoord.xy), 0).r;
    float distanceToEye = length(inPosition - draw.eye.xyz);
    // Each fade clamps its distance to [0, r] before it divides by r, so a small radius cannot carry the quotient past
    // the largest single float. The renderer draws no puff of a radius below the smallest normal float.
    float surfaceFade = opaqueDistance > 0.0
        ? clamp(opaqueDistance - distanceToEye, 0.0, inRadius) / inRadius
        : 1.0;
    float eyeFade = min(distanceToEye, inRadius) / inRadius;

    // Additive blending keeps the target's alpha, so the alpha written here is not used.
    outColor = vec4(inRadiance * (profile * surfaceFade * eyeFade), 0.0);
}
