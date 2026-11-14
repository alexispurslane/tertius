#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"

// THROWAWAY (ter-kh4g, stage 1): the vertex stage of the back-face cost probe. It draws from the same instance buffer
// and indirect commands as the G-buffer pass, with a range's matrix in draw.viewProj, and gives the fragment stage the
// camera-relative position. Only the position attribute is bound.
layout(location = 0) in vec3 inPosition;

layout(location = 0) out vec3 outPosition;

void main()
{
    GpuInstance instance = instances[gl_InstanceIndex];
    vec4 p = vec4(inPosition, 1.0);
    vec3 world = vec3(dot(instance.modelRow0, p), dot(instance.modelRow1, p), dot(instance.modelRow2, p));
    outPosition = world;
    gl_Position = draw.viewProj * vec4(world, 1.0);
}
