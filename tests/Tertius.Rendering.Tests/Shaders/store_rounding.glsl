// The values of the store-rounding probes (StoreRoundingTests): value c of 36 lies between two neighboring half floats,
// the base and the next one away from zero, at a quarter, a half or three quarters of their spacing. The bases are 1,
// 1 + 2^−10, 0.296875, 100, 100.0625 and 3000; the second and the fifth have an odd last bit, so that a tie shows whether a
// store rounds ties to even. Cases 18 to 35 are the negatives of cases 0 to 17. Every value is exact in a 32-bit float.
#ifndef TERTIUS_STORE_ROUNDING_GLSL
#define TERTIUS_STORE_ROUNDING_GLSL

const int RoundingCases = 36;
const float RoundingBases[6] = float[6](1.0, 1.0009765625, 0.296875, 100.0, 100.0625, 3000.0);
const float RoundingSpacings[6] = float[6](1.0 / 1024.0, 1.0 / 1024.0, 1.0 / 4096.0, 1.0 / 16.0, 1.0 / 16.0, 2.0);
const float RoundingShares[3] = float[3](0.25, 0.5, 0.75);

// The half float of case c, with its sign.
float roundingBase(int c)
{
    float base = RoundingBases[c % 6];
    return c >= 18 ? -base : base;
}

// The step of case c from its base, away from zero.
float roundingStep(int c)
{
    float step = RoundingShares[(c / 6) % 3] * RoundingSpacings[c % 6];
    return c >= 18 ? -step : step;
}

// The value of case c, between its base and the next half float away from zero.
float roundingValue(int c)
{
    return roundingBase(c) + roundingStep(c);
}

#endif
