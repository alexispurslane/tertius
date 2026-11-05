#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "gbuffer_inputs.glsl"
#include "exposure.glsl"
#include "atmosphere.glsl"

// The haze over the opaque surfaces of the main view (HazePass.cs, docs/plans/atmosphere.md, "The haze"): after the
// reflections, so that it covers each surface's whole light, and before the transparent pass, whose panes and puffs take
// their own haze. The blend makes the lit image throughAir of itself with a cover of 1: the haze's exposed light times the
// day factor at the surface times (1 − T), plus the image times T. A sky pixel keeps the painted sky.

layout(location = 0) in vec3 inRay;

// Dual-source blending (BlendMode.Transmittance): the target becomes outColor + target · outTransmittance.
layout(location = 0, index = 0) out vec4 outColor;
layout(location = 0, index = 1) out vec4 outTransmittance;

void main()
{
    // As in the ambient pass, every read stays inside the image, also for a helper invocation past its edge.
    ivec2 pixel = min(ivec2(gl_FragCoord.xy), ivec2(frame.viewport.xy) - 1);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    if (distanceToEye <= 0.0)
    {
        outColor = vec4(0.0);
        outTransmittance = vec4(1.0);
        return;
    }

    vec3 position = normalize(inRay) * distanceToEye;
    vec3 airLight = exposed(frame.atmosphere.haze.rgb, exposureScale());
    // throughAir of a black surface is the haze's own share. The image's share, T, goes to the blend. The convex sum of
    // the image and the haze's light, each at most MaxRadiance, stays at most MaxRadiance.
    outColor = vec4(throughAir(vec3(0.0), vec3(1.0), position, airLight), 0.0);
    outTransmittance = vec4(vec3(airTransmittance(position)), 0.0);
}
