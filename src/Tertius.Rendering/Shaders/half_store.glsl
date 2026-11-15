// The rounding of a value that a shader stores in a 16-bit float history (ticket t8-69v7, ARCHITECTURE.md, "Portable
// Vulkan"). A history is a mean that feeds back: each draw reads the stored value, moves it by 1 / L of its distance to the
// sample and stores it again. The Vulkan spec lets a store into a 16-bit float image round toward zero or to the nearest
// half float. A store that rounds one way loses a share b of the value in every draw, and the history settles L · b below
// its samples' mean: about 1 % at L = 32 on a driver that rounds toward zero. So every store of a history's value takes
// nearestHalf first. The value is then a half float already, and every driver stores it exactly.
#ifndef TERTIUS_HALF_STORE_GLSL
#define TERTIUS_HALF_STORE_GLSL

// The bits of the half float nearest to the 32-bit float whose bits are `bits`, as a 32-bit float, with ties to even. A
// half float keeps 10 bits of mantissa down to the least normal half, 2^−14, and below it a fixed spacing of 2^−24. So the
// rounding clears the last `dropped` bits of the 32-bit float, 13 for a normal half and more below 2^−14, and adds one
// unit of the kept bits where the cleared bits pass half a unit, or equal it with an odd kept bit. A carry out of the
// mantissa raises the exponent, which gives the next power of two, as it must. The sign bit takes no part.
uint nearestHalfBits(uint bits)
{
    uint magnitude = bits & 0x7FFFFFFFu;
    // The common case, at or above 2^−14: adding 0x0FFF, and 1 more for an odd kept bit, carries into the kept bits
    // exactly where the 13 cleared bits pass half a unit or equal it with an odd kept bit. Five operations, because the
    // stores of the histories run it about 25 million times a draw at 1280 × 720.
    if (magnitude >= 0x38800000u)
        return (bits + 0x0FFFu + ((bits >> 13) & 1u)) & ~0x1FFFu;
    uint sign = bits & 0x80000000u;
    // The biased exponent of the 32-bit float: 113 is 2^−14, and 103 is 2^−24.
    uint exponent = magnitude >> 23;
    if (exponent < 103u)
        // Below 2^−24: the nearest half float is 2^−24 above the tie at 2^−25, and 0 at or below it.
        return sign | (magnitude > 0x33000000u ? 0x33800000u : 0u);
    uint dropped = 126u - exponent;
    uint unit = 1u << dropped;
    uint rest = magnitude & (unit - 1u);
    uint kept = magnitude - rest;
    uint tie = unit >> 1;
    bool up = rest > tie || (rest == tie && (kept & unit) != 0u);
    return sign | (up ? kept + unit : kept);
}

// The half float nearest to x, as a 32-bit float, with ties to even. A store of the result in a 16-bit float image is exact
// on every driver, because the value is a half float already. x is finite and at most the largest half float, 65,504, in
// size: a larger x gives 65,536, which no half float holds. Integer arithmetic on the bits only: no log2, which the spec
// lets be inexact at a power of two, no isnan, which a driver can fold away, and no packHalf2x16, whose rounding is the
// driver's.
float nearestHalf(float x)
{
    return uintBitsToFloat(nearestHalfBits(floatBitsToUint(x)));
}

vec2 nearestHalf(vec2 v)
{
    return vec2(nearestHalf(v.x), nearestHalf(v.y));
}

vec3 nearestHalf(vec3 v)
{
    return vec3(nearestHalf(v.x), nearestHalf(v.y), nearestHalf(v.z));
}

vec4 nearestHalf(vec4 v)
{
    return vec4(nearestHalf(v.xyz), nearestHalf(v.w));
}

// The value that the accumulation stores in a signal's slow history or its scratch image, whose format
// SPECULAR_HISTORY_FORMAT gives (specular_history.glsl): rgb the light, a the length. It is the nearest half float in the
// 16-bit floats of every history but the precise specular one, and the value itself in that history's 32-bit floats,
// which store it exactly. The length is a whole number until the anti-lag shortens it by the clamp's move, which gives
// it a fraction, so it takes the same rule as the light.
vec4 slowHistoryValue(vec4 value)
{
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
    return value;
#else
    return nearestHalf(value);
#endif
}

#endif
