// Push constants of every pass that draws meshes. Needs frame.glsl. Passing the matrix here, not a range index, lets the same
// vertex shader serve the depth ranges, the shadow cascades and the single-object impostor captures.
#ifndef TERTIUS_DRAW_GLSL
#define TERTIUS_DRAW_GLSL

layout(push_constant, std430) uniform DrawPush
{
    mat4 viewProj;   // clip space from world space
    vec4 eye;        // xyz eye position in world space (zero for the main view)
} draw;

#include "vectors.glsl"

// The unit direction from a camera-relative world position toward the eye of this draw. A position at the eye has no
// such direction, and gets the view's backward axis, which faces the eye.
vec3 towardEye(vec3 position)
{
    return normalizedOr(draw.eye.xyz - position, -frame.cameraForward.xyz);
}

#endif
