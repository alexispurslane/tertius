#version 460

layout(location = 0) in vec3 inRay;
layout(location = 0) out vec4 outColor;

void main()
{
    outColor = vec4(inRay, 1.0);
}
