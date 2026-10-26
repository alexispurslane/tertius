#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "gbuffer_inputs.glsl"
#include "lighting.glsl"
#include "exposure.glsl"

// Puffs in the transparent pass (TransparentPass.cs). Each instance is one GpuPuff, drawn as a square centered on the
// puff that faces the eye, with the puff's radius as half its side. Six vertices make the square's two triangles.

layout(location = 0) out vec3 outPosition;        // camera-relative world position
layout(location = 1) out vec2 outCorner;          // square space, [-1, 1]²: the offset from the center over the radius
layout(location = 2) flat out vec3 outRadiance;   // the light that the puff sends to the eye at its center
layout(location = 3) flat out float outRadius;    // meters

const vec2 corners[6] = vec2[](vec2(-1.0, -1.0), vec2(1.0, -1.0), vec2(-1.0, 1.0),
                               vec2(-1.0, 1.0), vec2(1.0, -1.0), vec2(1.0, 1.0));

void main()
{
    GpuPuff puff = puffs[gl_InstanceIndex];
    vec3 center = puff.positionRadius.xyz;
    float radius = puff.positionRadius.w;

    // The square lies in the plane through the center across the line of sight. A puff whose center is at the eye faces
    // along the camera's forward axis.
    vec3 toCenter = center - draw.eye.xyz;
    float distanceToCenter = length(toCenter);
    vec3 forward = distanceToCenter > 1e-6 * radius ? toCenter / distanceToCenter : frame.cameraForward.xyz;
    vec3 upHint = normalize(frame.cameraUp.xyz);
    if (abs(dot(forward, upHint)) > 0.999)
        upHint = normalize(frame.cameraRight.xyz);
    vec3 right = normalize(cross(forward, upHint));
    vec3 up = cross(right, forward);

    vec2 corner = corners[gl_VertexIndex];
    vec3 world = center + (corner.x * right + corner.y * up) * radius;
    outPosition = world;
    outCorner = corner;
    // The case: 0 glow, 1 lit. A glow puff emits its color and takes no light. A lit puff is lit once, at its center
    // (lighting.glsl, scatteredLight), so each of its six vertices computes the same value.
    vec3 radiance = puff.colorKind.w < 0.5 ? puff.colorKind.rgb : scatteredLight(puff.colorKind.rgb, center, puff.map);
    // Puffs that overlap add in the blend unit, so each takes an equal share of the puffs' budget, and all the puffs over
    // a pixel add at most ShaderInput.PuffBudget, half of MaxRadiance, before the blend rounds (ARCHITECTURE.md,
    // "Portable Vulkan").
    outRadiance = exposed(radiance, exposureScale(), frame.params.x);
    outRadius = radius;
    gl_Position = draw.viewProj * vec4(world, 1.0);
}
