#version 460
layout(location = 0) in vec3 position;
layout(location = 3) in vec2 texCoord;
layout(location = 0) out vec3 world;
layout(location = 1) out vec2 uv;
layout(location = 2) flat out uint axis;
layout(push_constant) uniform VolumeDraw
{
    vec4 row0;
    vec4 row1;
    vec4 row2;
    vec4 grid;
    uvec4 words;
}
draw;
void main()
{
    vec4 p = vec4(position, 1);
    world = vec3(dot(draw.row0, p), dot(draw.row1, p), dot(draw.row2, p));
    uv = texCoord;
    vec3 local = (world - draw.grid.xyz) / (16.0 * draw.grid.w);
    axis = uint(gl_InstanceIndex);
    gl_Position = vec4(2.0 * vec2(local[(axis + 1u) % 3u], local[(axis + 2u) % 3u]) - 1.0, local[axis], 1);
}
