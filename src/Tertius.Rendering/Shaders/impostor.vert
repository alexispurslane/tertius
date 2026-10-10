#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"

// Impostor billboards in the G-buffer pass (ImpostorPass.cs). Each instance's model matrix maps quad space to
// camera-relative world space: x and y span half the captured square in the plane of the bounding sphere's center,
// z reaches from the center to the eye the capture was taken from, and the translation is the center. The
// material field holds the atlas tile. Six vertices make the quad's two triangles.

layout(location = 0) out vec2 outCorner;            // quad space, [-1, 1]²
layout(location = 1) flat out uint outInstance;

const vec2 corners[6] = vec2[](vec2(-1.0, -1.0), vec2(1.0, -1.0), vec2(-1.0, 1.0),
                               vec2(-1.0, 1.0), vec2(1.0, -1.0), vec2(1.0, 1.0));

void main()
{
    vec2 corner = corners[gl_VertexIndex];
    GpuInstance instance = instances[gl_InstanceIndex];
    vec4 p = vec4(corner, 0.0, 1.0);
    vec3 world = vec3(dot(instance.modelRow0, p), dot(instance.modelRow1, p), dot(instance.modelRow2, p));
    outCorner = corner;
    outInstance = gl_InstanceIndex;
    gl_Position = draw.viewProj * vec4(world, 1.0);
}
