#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inTexCoord;

layout(location = 0) out vec3 outPosition;
layout(location = 1) out vec3 outNormal;
layout(location = 2) out vec4 outTangent;
layout(location = 3) out vec2 outTexCoord;
layout(location = 4) flat out uint outMaterial;

void main()
{
    // gl_InstanceIndex includes the indirect command's firstInstance, so it indexes the instance buffer directly.
    GpuInstance instance = instances[gl_InstanceIndex];
    vec4 p = vec4(inPosition, 1.0);
    vec3 world = vec3(dot(instance.modelRow0, p), dot(instance.modelRow1, p), dot(instance.modelRow2, p));
    mat3 linear = transpose(mat3(instance.modelRow0.xyz, instance.modelRow1.xyz, instance.modelRow2.xyz));

    outPosition = world;
    outNormal = linear * (inNormal * instance.inverseScaleSq);
    outTangent = vec4(linear * inTangent.xyz, inTangent.w);
    outTexCoord = inTexCoord;
    outMaterial = instance.material;
    gl_Position = draw.viewProj * vec4(world, 1.0);
}
