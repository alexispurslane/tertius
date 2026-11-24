#ifndef TERTIUS_BLUE_NOISE_GLSL
#define TERTIUS_BLUE_NOISE_GLSL
// The spatiotemporal blue-noise mask (BlueNoiseMask, ARCHITECTURE.md, "Randomness and determinism"): BlueNoiseFrames
// frames of BlueNoiseSide² cells, frame t in tile (t % 8, t / 8), each texel a radial number and an angle in bytes. The
// including shader declares the mask as `utexture2D blueNoiseMask` at its own binding before it includes this file: the
// screen tracer's kernels in the frame set, and the tone map in the post set.

const uint BlueNoiseSide = 128u;
const uint BlueNoiseFrames = 64u;
const uint BlueNoiseTilesAcross = 8u;
// The constant of pcg3d's third value for the mask's shift in each period, "SH".
const uint ShiftDraw = 0x5348u;

// Jarzynski and Olano 2020, "Hash Functions for GPU Rendering": three 32-bit words from three.
uvec3 pcg3d(uvec3 v)
{
    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    return v;
}

// The mask's two bytes, a radial number and an angle, for a cell at a draw: the draw's frame of the period, at the cell
// moved by the period's own toroidal shift, so that a still pixel's long mean averages new values in each period.
uvec2 blueNoiseBytesOf(ivec2 cell, uint drawIndex)
{
    uint maskFrame = drawIndex & (BlueNoiseFrames - 1u);
    uvec2 shift = pcg3d(uvec3(drawIndex / BlueNoiseFrames, 0u, ShiftDraw)).xy;
    uvec2 inFrame = (uvec2(cell) + shift) & (BlueNoiseSide - 1u);
    uvec2 tile = uvec2(maskFrame % BlueNoiseTilesAcross, maskFrame / BlueNoiseTilesAcross);
    return texelFetch(blueNoiseMask, ivec2(tile * BlueNoiseSide + inFrame), 0).rg;
}

#endif
