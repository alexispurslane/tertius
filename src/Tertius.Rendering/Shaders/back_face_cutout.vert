#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"

// The back-face pass's cutout runs: back_face.vert, which also passes the texture coordinate and the material, so that
// back_face_cutout.frag can run the G-buffer's cutout test.
layout(location = 0) in vec3 inPosition;
layout(location = 3) in vec2 inTexCoord;

layout(location = 0) out vec3 outPosition;
layout(location = 1) out vec2 outTexCoord;
layout(location = 2) flat out uint outMaterial;

void main()
{
    GpuInstance instance = instances[gl_InstanceIndex];
    vec4 p = vec4(inPosition, 1.0);
    vec3 world = vec3(dot(instance.modelRow0, p), dot(instance.modelRow1, p), dot(instance.modelRow2, p));
    outPosition = world;
    outTexCoord = inTexCoord;
    outMaterial = instance.material;
    gl_Position = draw.viewProj * vec4(world, 1.0);
}
