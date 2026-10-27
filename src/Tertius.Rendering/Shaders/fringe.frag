#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "gbuffer_inputs.glsl"
#include "material.glsl"
#include "lighting.glsl"

// The fringe of a fringed material in the transparent pass (docs/plans/skin-and-hair.md, "The fringe"): transparent.frag
// with coverage in place of opacity. A texel whose alpha a is above 0 and below the cutoff covers a share a of its pixel,
// Porter and Duff's "over": the target becomes a · L + (1 − a) · target. The core drew in the G-buffer pass.

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inTexCoord;
layout(location = 4) flat in uvec2 inMaterialAndMap;   // x: the material. y: the instance's environment map
layout(location = 5) flat in vec4 inHemisphere;

// Dual-source blending (BlendMode.Transmittance): the target becomes outColor + target · outTransmittance.
layout(location = 0, index = 0) out vec4 outColor;
layout(location = 0, index = 1) out vec4 outTransmittance;

void main()
{
    vec3 V = towardEye(inPosition);
    // Before the discards below, so implicit derivatives stay defined for the whole quad.
    SurfaceSample s = sampleMaterial(inMaterialAndMap.x, inTexCoord, inNormal, inTangent, V);
    ShadingInput shading = forwardShading(s, inPosition, inHemisphere, inMaterialAndMap.y, V);
    float coverage = s.baseColor.a;
    // The core drew in the G-buffer pass, and a clear texel covers nothing.
    if (reachesCutoff(coverage, materials[inMaterialAndMap.x].alphaCutoff) || coverage <= 0.0)
        discard;
    // The G-buffer distance covers every depth range, as in transparent.frag.
    float opaqueDistance = texelFetch(gbufferDistance, ivec2(gl_FragCoord.xy), 0).r;
    if (opaqueDistance > 0.0 && length(inPosition - draw.eye.xyz) > opaqueDistance)
        discard;
    float exposure = exposureScale();
    // surfaceRadiance caps the light at MaxRadiance, so the fringe writes at most MaxRadiance · a = MaxRadiance · (1 − T):
    // the glass's bound, which keeps the blend inside the half float's range (ARCHITECTURE.md, "Portable Vulkan").
    vec3 radiance = surfaceRadiance(shading, exposed(s.emissive, exposure), inPosition, V, ShadowMapsMainView, exposure);
    outColor = vec4(radiance * coverage, 0.0);
    outTransmittance = vec4(vec3(1.0 - coverage), 0.0);
}
