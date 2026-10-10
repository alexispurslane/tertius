// Push constants of every pass that draws meshes. Passing the matrix here, not a range index, lets the same
// vertex shader serve the depth ranges, the shadow cascades and the single-object impostor captures.
#ifndef TERTIUS_DRAW_GLSL
#define TERTIUS_DRAW_GLSL

layout(push_constant, std430) uniform DrawPush
{
    mat4 viewProj;   // clip space from world space
    vec4 eye;        // xyz eye position in world space (zero for the main view)
} draw;

#endif
