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
layout(location = 4) flat in uvec4 inInstanceWords;  // x: the material. y: the instance's map. z: GpuInstance.holdsLight. w:
                                                     // the card code (mesh.vert)
layout(location = 5) flat in vec4 inHemisphere;

// Dual-source blending (BlendMode.Transmittance): the target becomes outColor + target · outTransmittance.
layout(location = 0, index = 0) out vec4 outColor;
layout(location = 0, index = 1) out vec4 outTransmittance;

void main()
{
    vec3 V = towardEye(inPosition);
    // Before the discard below, so implicit derivatives stay defined for the whole quad.
    SurfaceSample s = sampleMaterial(inInstanceWords.x, inTexCoord, inNormal, inTangent, V);
    ShadingInput shading = forwardShading(s, inPosition, inHemisphere, inInstanceWords.y, V);

    // The hardware depth buffer only holds the nearest depth range, so occlusion by opaque surfaces uses the
    // G-buffer distance, which covers all ranges.
    float opaqueDistance = texelFetch(gbufferDistance, ivec2(gl_FragCoord.xy), 0).r;
    float distanceToEye = length(inPosition - draw.eye.xyz);
    if (opaqueDistance > 0.0 && distanceToEye > opaqueDistance)
        discard;

    // The reflection happens at the surface, whatever the glass lets through, so the specular part and the emission add
    // in full. Only the opacity's share of the light enters the material and scatters, so only the diffuse part scales.
    float opacity = clamp(s.baseColor.a, 0.0, 1.0);
    SurfaceLobes lobes = lobesOf(shading, V);
    lobes.diffuseColor *= opacity;
    // The glass lets through what it neither reflects nor scatters: T = (1 − opacity)(1 − E · c), per channel.
    vec3 transmittance = clamp((1.0 - opacity) * (1.0 - specularWeight(lobes)), 0.0, 1.0);
    // The pane's pre-exposed radiance takes at most MaxRadiance · (1 − T), so that over a target v the blend's
    // MaxRadiance · (1 − T) + T · v keeps the surfaces' share and the puffs' inside the half float's range: past it, a
    // device may store infinity, which the tone curve turns into NaN (ARCHITECTURE.md, "Portable Vulkan").
    float exposure = exposureScale();
    // The emission is capped before the exposure multiplies it, as every writer's light is (exposure.glsl).
    vec3 radiance = min(
        lobesRadiance(shading, lobes, exposed(s.emissive, exposure), inPosition, V, ShadowMapsMainView, exposure),
        MaxRadiance * (1.0 - transmittance));
    // outColor is radiance, so it takes the exposure that every pass's radiance takes. The transmittance is a fraction
    // of the radiance behind, and never takes such a factor. The blend keeps the target's alpha, so neither alpha
    // matters.
    outColor = vec4(radiance, 0.0);
    outTransmittance = vec4(transmittance, 0.0);
}
