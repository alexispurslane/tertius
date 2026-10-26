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

#endif
