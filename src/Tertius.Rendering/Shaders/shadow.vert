#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"

// Depth-only: the shadow pass draws casters from the same instance buffer and indirect commands as the G-buffer
// pass, with a cascade's matrix in draw.viewProj. Only the position attribute is bound.
layout(location = 0) in vec3 inPosition;

void main()
{
    GpuInstance instance = instances[gl_InstanceIndex];
    vec4 p = vec4(inPosition, 1.0);
    vec3 world = vec3(dot(instance.modelRow0, p), dot(instance.modelRow1, p), dot(instance.modelRow2, p));
    gl_Position = draw.viewProj * vec4(world, 1.0);
}
