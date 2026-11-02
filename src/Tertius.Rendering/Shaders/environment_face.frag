#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "gbuffer_inputs.glsl"
#include "material.glsl"
#include "lighting.glsl"

// One surface of an environment face, drawn with mesh.vert in one of the face's three depth ranges
// (EnvironmentFacePass.cs). It is impostor_capture.frag without the ambient light (docs/plans/environment-map.md, "How a
// face is lit"): every light, a cascade set by the finest cascade whose box holds the point, and the emission of an
// entity that holds no light, in plain radiance, seen through the atmosphere's haze. The map is the light that arrives at
// the camera's eye, so V points toward the eye at the origin. Beside the light, the fragment writes its inverse distance
// from the eye to the distance cube (docs/plans/environment-map-distances.md, "How the capture writes it").

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inTexCoord;
layout(location = 4) flat in uvec4 inInstanceWords;  // x: the material. z: GpuInstance.holdsLight. A face reads no
                                                     // instance map and no card code (see mesh.vert)

layout(location = 0) out vec4 outColor;              // RGBA16F: plain radiance, and an open-sky share of 0
layout(location = 1) out float outInverseDistance;   // R16F: 1000 m over the distance from the eye, in km⁻¹

const float NearPlane = 0.05;   // DepthRanges.NearPlane, in m

void main()
{
    // A face's eye is the camera's, at the origin (draw.eye is 0). A surface there has no direction toward it, and one
    // whose vertex normal has no direction faces the eye.
    vec3 V = towardEye(inPosition);
    SurfaceSample s = sampleMaterial(inInstanceWords.x, inTexCoord, inNormal, inTangent, V);
    // A face takes no ambient light, so nothing reads the sky shares or the footprint that forwardShading gives it.
    ShadingInput shading = forwardShading(s, inPosition, vec4(0.0), draw.map, V);
    // The glow of an entity that holds a light adds nothing, as in the bounce source. Else the map's irradiance lights the
    // room with the lamp's power a second time. So the map shows such a glow dark, in its reflections too.
    vec3 glow = glowSent(s.emissive, inInstanceWords.z);
    // A face of map i keeps the lights of maps 0 to i: a map leaves out the lamps of the subtree that it leaves out
    // (docs/plans/environment-maps-per-frame.md, decision 13).
    vec3 radiance = directRadiance(shading, glow, inPosition, V, ShadowMapsHolding, draw.map);
    // The face sees the surface through the same haze as the main view, from the same eye (docs/plans/atmosphere.md,
    // "The haze"). Both parts are at most MaxRadiance, and so is their convex sum.
    outColor = vec4(throughAir(radiance, vec3(1.0), inPosition, frame.atmosphere.haze.rgb), 0.0);
    // The radial distance along the texel's direction, not the depth along the face's axis, so that both sides of a seam
    // between two faces mean the same. Every fragment lies at least NearPlane along its face's axis, so the max only keeps
    // a division by 0 out, and w is at most 20,000 km⁻¹, which a half float holds.
    outInverseDistance = 1000.0 / max(length(inPosition), NearPlane);
}
