// The painted atmosphere per point (docs/plans/atmosphere.md, "The model"): the air's density share at a point, the
// sun's tint there, and the haze between the eye and a surface. frame.atmosphere holds the planet's shape and the look,
// which PaintedAtmosphere.cs prepares at the interpolated eye. Without an atmosphere, 1 / H and 1 / the haze distance are
// 0 and the tint is 1, so the haze lets everything through and no light changes. Positions are camera-relative world
// positions, so the eye is at the origin. Needs frame.glsl.
#ifndef TERTIUS_ATMOSPHERE_GLSL
#define TERTIUS_ATMOSPHERE_GLSL

#include "frame.glsl"

// h(p): the height in m of `position` above the planet's surface, 0 at and below it, where the density stays at the
// surface's. Finite: the position and the center lie within 2^62 m of the eye, so their difference's square is below
// 2^127.
float airHeight(vec3 position)
{
    return max(length(position - frame.atmosphere.centerRadius.xyz) - frame.atmosphere.centerRadius.w, 0.0);
}

// s(p) = e^(−h(p) / H): the air's density at `position` over its surface density, from 0 to 1. A height of at most 2^63 m
// times 1 / H of at most 2^62 per m stays finite, and exp of a large negative number is 0.
float airShare(vec3 position)
{
    return exp(-airHeight(position) * frame.atmosphere.sky.w);
}

// The sun's tint at `position`: the planet's tint where the air is at its surface density, none where there is no air,
// mix(1, SunTint, s(p)). lightIrradiance applies it to the light that GpuLight.airSun marks.
vec3 sunTintAt(vec3 position)
{
    return mix(vec3(1.0), frame.atmosphere.sunTint.rgb, airShare(position));
}

// T(p): the share of the light from a surface at `position` that the haze lets through to the eye, from 0 to 1. The
// optical depth is the distance over the haze distance times m, the mean density share along the straight ray from the
// eye, in an air whose density falls by e per scale height: (s_eye − s_p) / x with x = (h_p − h_eye) / H. Near x = 0 the
// quotient loses its digits, and the mean of the two ends differs from it by less than x² / 12 of s_eye. Both shares lie
// from 0 to 1, so m does too.
float airTransmittance(vec3 position)
{
    float inverseHazeDistance = frame.atmosphere.haze.w;
    if (inverseHazeDistance == 0.0)
        return 1.0;
    float inverseScaleHeight = frame.atmosphere.sky.w;
    float eyeHeight = airHeight(vec3(0.0));
    float pointHeight = airHeight(position);
    float eyeShare = exp(-eyeHeight * inverseScaleHeight);
    float pointShare = exp(-pointHeight * inverseScaleHeight);
    float x = (pointHeight - eyeHeight) * inverseScaleHeight;
    float meanShare = abs(x) < 0.01 ? 0.5 * (eyeShare + pointShare) : (eyeShare - pointShare) / x;
    // The distance and the inverse are each at most 2^62, so the depth stays finite.
    return exp(-length(position) * inverseHazeDistance * meanShare);
}

// The light of a surface at `position` as the eye sees it through the haze: `light` times T, plus the haze's light
// `airLight` times (1 − T) times `cover`. `cover` is the share of the haze's light that the surface stands in for, per
// channel: 1 for an opaque surface, 1 − t for a pane that lets through t, and 0 for an additive puff. With it, a pane and
// the hazed surface behind it add to one haze over the whole ray. `airLight` is the haze's light in the units of `light`:
// frame.atmosphere.haze.rgb in plain radiance, or exposed. The result is at most the larger of light and airLight · cover.
vec3 throughAir(vec3 light, vec3 cover, vec3 position, vec3 airLight)
{
    float transmittance = airTransmittance(position);
    return light * transmittance + airLight * ((1.0 - transmittance) * cover);
}

#endif
