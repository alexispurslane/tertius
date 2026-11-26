#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"

// The back-face pass's solid runs (https://github.com/alexispurslane/tertius/wiki/back-face-depth-layer, decision 4): the G-buffer's instances and
// indirect commands, with a range's jittered matrix in draw.viewProj. Only the position attribute is bound.
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
