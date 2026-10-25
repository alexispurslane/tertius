// The real spherical harmonics up to band 2, in world axes (docs/plans/environment-map.md, "The irradiance").
// Irradiance.cs is the C# copy.
#ifndef TERTIUS_SPHERICAL_HARMONICS_GLSL
#define TERTIUS_SPHERICAL_HARMONICS_GLSL

// Â_l of the clamped cosine by band: π, 2π / 3 and π / 4. Written out, so that a compute shader needs no frame.glsl.
const float ShClampedCosine[3] = float[](3.14159265358979, 2.09439510239320, 0.785398163397448);

// The band of coefficient i: 0 for coefficient 0, 1 for coefficients 1 to 3, 2 for coefficients 4 to 8.
uint shBand(uint i)
{
    return i == 0u ? 0u : (i < 4u ? 1u : 2u);
}

// The nine Y_lm at the unit `direction`, in the doc's order: Y_00, then y, z and x of band 1, then xy, yz, 3z² − 1, xz and
// x² − y² of band 2.
void shBasis(vec3 direction, out float y[9])
{
    float x = direction.x, yy = direction.y, z = direction.z;
    y[0] = 0.282094791773878;
    y[1] = 0.488602511902920 * yy;
    y[2] = 0.488602511902920 * z;
    y[3] = 0.488602511902920 * x;
    y[4] = 1.092548430592079 * x * yy;
    y[5] = 1.092548430592079 * yy * z;
    y[6] = 0.315391565252520 * (3.0 * z * z - 1.0);
    y[7] = 1.092548430592079 * x * z;
    y[8] = 0.546274215296040 * (x * x - yy * yy);
}

#endif
