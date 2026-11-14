#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "material.glsl"
#define FACE_SURFACE_WRITES
#include "face_surface.glsl"

// One surface of an environment face, drawn with mesh.vert in one of the face's three depth ranges
// (EnvironmentFacePass.cs). It lights nothing. It writes the face's surface record (face_surface.glsl): the surface
// that the texel shows, as the values that the light loop reads. The face's light pass then lights each texel once
// (environment_face_light.frag). The map is the light that arrives at the camera's eye, so V points toward the eye at
// the origin.

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inTexCoord;
layout(location = 4) flat in uvec4 inInstanceWords;  // x: the material. z: GpuInstance.holdsLight. A face reads no
                                                     // instance map and no card code (see mesh.vert)

void main()
{
    // A face's eye is the camera's, at the origin (draw.eye is 0). A surface there has no direction toward it, and one
    // whose vertex normal has no direction faces the eye.
    vec3 V = towardEye(inPosition);
    SurfaceSample s = sampleMaterial(inInstanceWords.x, inTexCoord, inNormal, inTangent, V);
    // The glow of an entity that holds a light adds nothing, as in the bounce source. Else the map's irradiance lights the
    // room with the lamp's power a second time. So the map shows such a glow dark, in its reflections too.
    writeFaceSurface(inPosition, s, glowSent(s.emissive, inInstanceWords.z));
}
