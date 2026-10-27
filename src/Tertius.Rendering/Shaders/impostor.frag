#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "octahedral.glsl"
#include "exposure.glsl"

// Impostor billboards into the G-buffer (ImpostorPass.cs). The capture's lighting is baked, so the pixel is
// written as emission with base color alpha 0, which tells the direct and ambient passes to add nothing else. The captured relative
// depth rebuilds the surface point on the ray from the capture's eye through the quad, which gives the depth test
// and the distance target the object's real shape.

layout(set = 3, binding = 0) uniform texture2DArray impostorColor;        // premultiplied color, coverage
layout(set = 3, binding = 1) uniform texture2DArray impostorDepthOffset;  // (depth / center depth − 1) × coverage
layout(set = 3, binding = 2) uniform sampler impostorSampler;              // trilinear, clamped

layout(location = 0) in vec2 inCorner;
layout(location = 1) flat in uint inInstance;

layout(location = 0) out vec4 outBaseColor;
layout(location = 1) out vec4 outNormal;
layout(location = 2) out vec4 outSpecular;
layout(location = 3) out vec4 outEmissive;
layout(location = 4) out float outDistance;

void main()
{
    GpuInstance instance = instances[inInstance];
    // Quad +y is the capture's up, which is the top row of the tile.
    vec3 uv = vec3(inCorner.x * 0.5 + 0.5, 0.5 - inCorner.y * 0.5, float(instance.material));
    // Both samples before the discard, so the implicit derivatives are defined for the whole quad.
    vec4 color = texture(sampler2DArray(impostorColor, impostorSampler), uv);
    float offset = texture(sampler2DArray(impostorDepthOffset, impostorSampler), uv).r;
    if (color.a < 0.5)
        discard;
    float coverage = color.a;

    // The quad point Q in the center's plane, and the capture's eye E (quad z = 1). The surface lies on the ray
    // from E through Q, at the captured fraction of Q's depth.
    vec4 q = vec4(inCorner, 0.0, 1.0);
    vec3 quad = vec3(dot(instance.modelRow0, q), dot(instance.modelRow1, q), dot(instance.modelRow2, q));
    vec3 eye = vec3(instance.modelRow0.w, instance.modelRow1.w, instance.modelRow2.w) +
               vec3(instance.modelRow0.z, instance.modelRow1.z, instance.modelRow2.z);
    vec3 world = eye + (quad - eye) * (1.0 + clamp(offset / coverage, -0.99, 1.0));
    vec4 clip = draw.viewProj * vec4(world, 1.0);
    // At or behind the eye: clip w is 0 or less, and the depth would divide by it.
    if (clip.w <= 0.0)
        discard;
    float depth = clip.z / clip.w;
    // In front of this range's near plane: the nearer range draws that part.
    if (depth > 1.0)
        discard;
    gl_FragDepth = depth;

    vec3 toward = normalize(vec3(instance.modelRow0.z, instance.modelRow1.z, instance.modelRow2.z));
    outBaseColor = vec4(0.0);
    outNormal = vec4(octEncode(toward), octEncode(toward));
    outSpecular = vec4(0.0);
    // The atlas holds radiance, which the billboard pre-exposes with this frame's exposure.
    outEmissive = vec4(preExposed(color.rgb / coverage), 1.0);
    outDistance = length(world - draw.eye.xyz);
}
