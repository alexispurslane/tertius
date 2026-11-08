#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "material.glsl"

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inTexCoord;
layout(location = 4) flat in uvec4 inInstanceWords;

layout(location = 0) out vec2 outPositionXY;
layout(location = 1) out vec2 outPositionZDistance;
layout(location = 2) out vec2 outNormalXY;
layout(location = 3) out vec2 outNormalZMaterial;

void main()
{
    vec3 V = towardEye(inPosition);
    SurfaceSample s = sampleMaterial(inInstanceWords.x, inTexCoord, inNormal, inTangent, V);
    outPositionXY = inPosition.xy;
    outPositionZDistance = vec2(inPosition.z, length(inPosition - draw.eye.xyz));
    outNormalXY = s.normal.xy;
    outNormalZMaterial = vec2(s.normal.z, float(inInstanceWords.x));
}
