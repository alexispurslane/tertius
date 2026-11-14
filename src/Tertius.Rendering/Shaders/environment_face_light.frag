#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "vectors.glsl"
#include "gbuffer_inputs.glsl"
#include "lighting.glsl"
#include "sky.glsl"
#include "face_surface.glsl"

// The light pass of an environment face (EnvironmentFacePass.cs): one fullscreen triangle over the face, after its
// surfaces wrote the surface record (face_surface.glsl). It writes every texel of the face once.
//
// A texel with a surface takes the light of impostor_capture.frag without the ambient light (docs/plans/environment-map.md,
// "How a face is lit"): every light, a cascade set by the finest cascade whose box holds the point, and the glow that
// the surface sends, in plain radiance, seen through the atmosphere's haze. Beside the light, it writes the surface's
// inverse distance from the eye to the distance cube (docs/plans/environment-map-distances.md, "How the capture writes
// it"). A texel without a surface shows the sky without the stars, with an open-sky share of 1. The readers add the
// stars for each pixel (docs/plans/environment-map.md, "What a face draws"). The sky is infinitely far, so its inverse
// distance is 0 (docs/plans/environment-map-distances.md, "The quantity").

// FaceLightPushConstants in GpuTypes.cs: the face's basis, forward.w the inverse of the face size, and the face's map.
layout(push_constant, std430) uniform FaceLightPush
{
    vec4 forward;
    vec4 right;
    vec4 up;
    uint map;
} face;

layout(location = 0) out vec4 outColor;              // RGBA16F: plain radiance, the open-sky share
layout(location = 1) out float outInverseDistance;   // R16F: 1000 m over the distance from the eye, in km⁻¹

const float NearPlane = 0.05;   // DepthRanges.NearPlane, in m

void main()
{
    ShadingInput s;
    vec3 glow;
    if (!faceSurfaceAt(ivec2(gl_FragCoord.xy), s, glow))
    {
        vec2 st = gl_FragCoord.xy * face.forward.w;
        vec3 direction = normalize(face.forward.xyz + (2.0 * st.x - 1.0) * face.right.xyz + (1.0 - 2.0 * st.y) * face.up.xyz);
        outColor = vec4(skyRadiance(direction), 1.0);
        outInverseDistance = 0.0;
        return;
    }

    // A face of map i keeps the lights of maps 0 to i: a map leaves out the lamps of the subtree that it leaves out
    // (docs/plans/environment-maps-per-frame.md, decision 13).
    // A face's eye is the camera's, at the origin. A surface there has no direction toward it (towardEye in draw.glsl).
    vec3 V = normalizedOr(vec3(0.0) - s.position, -frame.cameraForward.xyz);
    vec3 radiance = directRadiance(s, glow, V, ShadowMapsHolding, face.map);
    // The face sees the surface through the same haze as the main view, from the same eye (docs/plans/atmosphere.md,
    // "The haze"). Both parts are at most MaxRadiance, and so is their convex sum.
    outColor = vec4(throughAir(radiance, vec3(1.0), s.position, frame.atmosphere.haze.rgb), 0.0);
    // The radial distance along the texel's direction, not the depth along the face's axis, so that both sides of a seam
    // between two faces mean the same. Every surface lies at least NearPlane along its face's axis, so the max only keeps
    // a division by 0 out, and w is at most 20,000 km⁻¹, which a half float holds.
    outInverseDistance = 1000.0 / max(length(s.position), NearPlane);
}
