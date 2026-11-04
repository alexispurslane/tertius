// Vector operations whose operands stay in their domain (ARCHITECTURE.md, "Portable Vulkan").
#ifndef TERTIUS_VECTORS_GLSL
#define TERTIUS_VECTORS_GLSL

// The smallest normal single float. A driver may flush a smaller value to 0.
const float SmallestNormal = 1.17549435e-38;

// The smallest normal half float, 2^-14. A driver may flush a smaller value to 0 when it stores it in a 16-bit image or
// loads it, and the rounding of the conversion is undefined.
const float SmallestNormalHalf = 6.103515625e-5;

// v at unit length, or `fallback` when v is too short to have a direction. normalize of a zero vector divides by 0,
// which gives NaN, and an interpolated normal is zero where the normals of a triangle's corners cancel.
vec3 normalizedOr(vec3 v, vec3 fallback)
{
    float lengthSq = dot(v, v);
    return lengthSq >= SmallestNormal ? v * inversesqrt(lengthSq) : fallback;
}

// n_f: across a hair's strand toward the eye. A strand along the line of sight takes the card's normal, which is across
// it. HairFiber.FiberNormal is the C# copy. Here, so that the compute passes, which include no material.glsl, read it too.
vec3 fiberNormal(vec3 strand, vec3 V, vec3 cardNormal)
{
    return normalizedOr(V - strand * dot(strand, V), cardNormal);
}

// n, or −n where it faces away from `direction`: the normal of the side of a surface that `direction` sees.
vec3 turnedToward(vec3 n, vec3 direction)
{
    return dot(n, direction) < 0.0 ? -n : n;
}

#endif
