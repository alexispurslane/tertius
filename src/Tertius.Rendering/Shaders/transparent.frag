#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "gbuffer_inputs.glsl"
#include "material.glsl"
#include "lighting.glsl"

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inTexCoord;
layout(location = 4) flat in uint inMaterial;

layout(location = 0) out vec4 outColor;

void main()
{
    // Sample before the discard below, so implicit derivatives stay defined for the whole quad.
    SurfaceSample s = sampleMaterial(inMaterial, inTexCoord, inNormal, inTangent);

    // The hardware depth buffer only holds the nearest depth range, so occlusion by opaque surfaces uses the
    // G-buffer distance, which covers all ranges.
    float opaqueDistance = texelFetch(gbufferDistance, ivec2(gl_FragCoord.xy), 0).r;
    float distanceToEye = length(inPosition - draw.eye.xyz);
    if (opaqueDistance > 0.0 && distanceToEye > opaqueDistance)
        discard;

    ShadingInput shading;
    shading.albedo = s.albedo.rgb;
    shading.normal = s.normal;
    shading.geometricNormal = normalize(inNormal);
    shading.specular = s.specular;
    shading.gloss = s.gloss;
    vec3 V = (draw.eye.xyz - inPosition) / max(distanceToEye, 1e-6);
    outColor = vec4(shadeSurface(shading, inPosition, V) + s.emissive, clamp(s.albedo.a, 0.0, 1.0));
}
