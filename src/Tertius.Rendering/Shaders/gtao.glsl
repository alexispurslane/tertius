// The GLSL port of XeGTAO: ground-truth ambient occlusion (Jimenez and others 2016) at the physical constants of
// docs/plans/ambient-occlusion.md, "XeGTAO's heuristics". It holds every function that the port takes from XeGTAO: the
// edges and their packing, the noise and the Hilbert index, the view position, the texel's setup (gtaoTexelSetup), the
// slice's geometry (gtaoSliceGeometry) and its inner integral, the step offsets (gtaoExactStepOffset), and the denoise.
// The slice loop is occlusion_sectors.comp's. HorizonSlice, HorizonSearch and OcclusionNoise in Shading/ are its C#
// copy.
//
// Source: https://github.com/GameTechDev/XeGTAO, commit a5b1686c7ea37788eeb3576b5be47f7c03db532c,
// Source/Rendering/Shaders/XeGTAO.hlsli, XeGTAO.h and vaGTAO.hlsl (copies in ThirdParty/XeGTAO).
//
// MIT License
//
// Copyright (C) 2016-2021, Intel Corporation
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
#ifndef TERTIUS_GTAO_GLSL
#define TERTIUS_GTAO_GLSL

#include "occlusion.glsl"

const float GtaoPi = 3.1415926535897932384626433832795;
const float GtaoHalfPi = 1.5707963267948966192313216916398;

// The view depth of sky, in m: beyond every radius (decision 18). No pass squares a sky texel's view position, which a
// wide field of view or a wide image puts past 10^19 m across (occlusion_sectors.comp).
const float SkyDepth = 1e18;
// Moves the center toward the eye against self-occlusion from the precision of the depths: XeGTAO's value for 32-bit
// depths (XeGTAO.hlsli line 281).
const float CenterBias = 0.99999;
// The nearest step from the center, in half-resolution texels (XeGTAO's pixelTooCloseThreshold, line 367).
const float NearestStepTexels = 1.3;
// The weight of the center in the last denoise, XeGTAO's DenoiseBlurBeta (XeGTAO.h line 189). The first denoise
// takes a fifth of it (XeGTAO.hlsli line 736).
const float DenoiseBlurBeta = 1.2;
// The guard against the 0 / 0 of a normal with no part in the slice plane: GLSL's clamp of NaN is undefined, where
// HLSL's saturate gives 0 (docs/plans/ambient-occlusion.md, "The occlusion horizons pass").
const float MinProjectedNormalLength = 1e-6;

// ---- The view position -------------------------------------------------------------------------------------------

// XeGTAO_ComputeViewspacePosition (line 104): the view position at a screen position and a view depth, with
// NDCToViewMul = (2 t_x, −2 t_y) and NDCToViewAdd = (−t_x, t_y) (XeGTAO.h lines 181 and 182). A view depth of 1 gives
// the view ray r.
vec3 gtaoViewPosition(vec2 screenPosition, float z)
{
    vec2 tangents = cameraTangents();
    vec2 ndcToViewMul = vec2(2.0, -2.0) * tangents;
    vec2 ndcToViewAdd = vec2(-1.0, 1.0) * tangents;
    return vec3((ndcToViewMul * screenPosition + ndcToViewAdd) * z, z);
}

// P(t): the view position of the representative pixel of a half-resolution texel, at the texel's view depth.
vec3 gtaoTexelPosition(ivec2 texel)
{
    return gtaoViewPosition(pixelScreenPosition(2 * texel), viewDepthAt(texel));
}

// ---- The edges ---------------------------------------------------------------------------------------------------

// XeGTAO_CalculateEdges (line 120): per side, 1 joins the neighbor and 0 cuts, from the depth differences with the
// slope of the two opposite sides taken out.
vec4 gtaoEdges(float centerZ, float leftZ, float rightZ, float topZ, float bottomZ)
{
    vec4 edgesLRTB = vec4(leftZ, rightZ, topZ, bottomZ) - centerZ;
    float slopeLR = (edgesLRTB.y - edgesLRTB.x) * 0.5;
    float slopeTB = (edgesLRTB.w - edgesLRTB.z) * 0.5;
    vec4 edgesLRTBSlopeAdjusted = edgesLRTB + vec4(slopeLR, -slopeLR, slopeTB, -slopeTB);
    edgesLRTB = min(abs(edgesLRTB), abs(edgesLRTBSlopeAdjusted));
    return clamp(1.25 - edgesLRTB / (centerZ * 0.011), 0.0, 1.0);
}

// XeGTAO_PackEdges (line 133): two bits per side, L · 64 + R · 16 + T · 4 + B, over 255, for an R8 UNORM image.
float gtaoPackEdges(vec4 edgesLRTB)
{
    edgesLRTB = round(clamp(edgesLRTB, 0.0, 1.0) * 2.9);
    return dot(edgesLRTB, vec4(64.0 / 255.0, 16.0 / 255.0, 4.0 / 255.0, 1.0 / 255.0));
}

// XeGTAO_UnpackEdges (line 686).
vec4 gtaoUnpackEdges(float packedEdges)
{
    uint packedValue = uint(packedEdges * 255.5);
    vec4 edgesLRTB = vec4(float((packedValue >> 6) & 3u), float((packedValue >> 4) & 3u),
                          float((packedValue >> 2) & 3u), float(packedValue & 3u)) / 3.0;
    return clamp(edgesLRTB, 0.0, 1.0);
}

// The unpacked edges of a half-resolution texel. A texel outside the image reads the border texel, as XeGTAO's clamped
// sampler does.
vec4 gtaoEdgesAt(ivec2 texel)
{
    return gtaoUnpackEdges(imageLoad(occlusionEdges, clamp(texel, ivec2(0), halfSize() - 1)).r);
}

// ---- The noise ---------------------------------------------------------------------------------------------------

// HilbertIndex (XeGTAO.h lines 120 to 142), level 6: the place of a texel on the Hilbert curve through its 64 × 64
// tile. The curve reads only the low 6 bits, so the tile repeats, as XeGTAO's lookup image reads it.
uint gtaoHilbertIndex(uint posX, uint posY)
{
    const uint hilbertWidth = 64u;
    posX %= hilbertWidth;
    posY %= hilbertWidth;
    uint index = 0u;
    for (uint curLevel = hilbertWidth / 2u; curLevel > 0u; curLevel /= 2u)
    {
        uint regionX = (posX & curLevel) > 0u ? 1u : 0u;
        uint regionY = (posY & curLevel) > 0u ? 1u : 0u;
        index += curLevel * curLevel * ((3u * regionX) ^ regionY);
        if (regionY == 0u)
        {
            if (regionX == 1u)
            {
                posX = hilbertWidth - 1u - posX;
                posY = hilbertWidth - 1u - posY;
            }
            uint temp = posX;
            posX = posY;
            posY = temp;
        }
    }
    return index;
}

// SpatioTemporalNoise (vaGTAO.hlsl lines 74 to 91) at the temporal index 0: the Hilbert index drives the R2 sequence.
// x turns the slices, y shifts the steps. A fixed function of the texel, so a still camera sees the same noise.
vec2 gtaoNoise(ivec2 texel)
{
    uint index = gtaoHilbertIndex(uint(texel.x), uint(texel.y));
    return fract(0.5 + float(index) * vec2(0.75487766624669276005, 0.5698402909980532659114));
}

// ---- The slices --------------------------------------------------------------------------------------------------

// Half of the paper's inner integral (equation 7): ∫ from 0 to h of cos(θ − γ) · |sin θ| dθ (lines 542 and 543).
float gtaoSliceArc(float horizon, float normalAngle)
{
    return (cos(normalAngle) + 2.0 * horizon * sin(normalAngle) - cos(2.0 * horizon - normalAngle)) / 4.0;
}

// What every slice of a half-resolution texel shares (XeGTAO_MainPass, lines 245 to 380, at the physical constants).
struct GtaoTexel
{
    vec3 center;          // c: the representative pixel's view position, moved toward the eye by CenterBias
    vec3 toEye;           // V̂
    vec3 normal;          // the view-space shading normal of the representative pixel
    vec3 geometricNormal; // n_g: its view-space geometric normal, whose plane blocks what lies below it (decision 39)
    vec2 noise;           // ξ: x turns the slices, y shifts the steps
    float radiusInTexels; // ρ
};

// One slice around a texel, in view space with XeGTAO's axes (docs/plans/screen-space-illumination.md, "The slice and its
// sectors"). HorizonSlice and SliceSectors are the C# copy.
struct SliceGeometry
{
    vec3 toEye;        // V̂
    vec3 side;         // ô: in the slice plane, at right angles to V̂, toward the plus side
    float normalAngle; // γ
    float arc;         // T = cos γ + γ sin γ
    float weight;      // W = |n_p| T
};

// The setup of the texel at view depth z, whose radius spans radiusInTexels texels.
GtaoTexel gtaoTexelSetup(ivec2 texel, float z, float radiusInTexels)
{
    GtaoTexel t;
    t.center = gtaoViewPosition(pixelScreenPosition(2 * texel), z * CenterBias);
    t.toEye = normalize(-t.center);
    t.normal = shadingNormalAt(2 * texel);
    t.geometricNormal = geometricNormalAt(2 * texel);
    t.noise = gtaoNoise(texel);
    t.radiusInTexels = radiusInTexels;
    return t;
}

// φ = (k + ξ_x) / S · π: the screen angle of a slice (lines 5 and 6 of the paper's Algorithm 1).
float gtaoSliceAngle(GtaoTexel t, int slice)
{
    return (float(slice) + t.noise.x) / float(occlusion.slices) * GtaoPi;
}

// The geometry of a slice of the texel (lines 386 to 408).
SliceGeometry gtaoSliceGeometry(GtaoTexel t, int slice)
{
    float phi = gtaoSliceAngle(t, slice);
    vec3 directionVec = vec3(cos(phi), sin(phi), 0.0);
    vec3 orthoDirectionVec = directionVec - dot(directionVec, t.toEye) * t.toEye;
    vec3 axisVec = normalize(cross(orthoDirectionVec, t.toEye));
    vec3 projectedNormalVec = t.normal - axisVec * dot(t.normal, axisVec);
    float signNorm = sign(dot(orthoDirectionVec, projectedNormalVec));
    float projectedNormalVecLength = length(projectedNormalVec);
    float cosNorm = clamp(dot(projectedNormalVec, t.toEye) / max(projectedNormalVecLength, MinProjectedNormalLength),
                          0.0, 1.0);
    SliceGeometry g;
    g.toEye = t.toEye;
    // V̂ has a view-space z of at least NearPlane over the distance in size, so the screen direction is never along it.
    g.side = normalize(orthoDirectionVec);
    g.normalAngle = signNorm * acos(cosNorm);
    g.arc = cos(g.normalAngle) + g.normalAngle * sin(g.normalAngle);
    g.weight = projectedNormalVecLength * g.arc;
    return g;
}

// The offset in half-resolution texels of a step of a slice, on its plus side, at its exact place on the slice line:
// an even place in its share of the radius, shifted by the R1 sequence, beyond the nearest step (lines 410 to 440). The
// step reads the texel nearest that place, but its surface point stays on the slice line (sectors.glsl,
// stepSurfacePoint): XeGTAO measures the snapped texel's own point, which lies off the slice.
vec2 gtaoExactStepOffset(GtaoTexel t, int slice, int stepIndex)
{
    int stepsPerSide = int(occlusion.stepsPerSide);
    float phi = gtaoSliceAngle(t, slice);
    // The screen direction in texels: the image rows run down, so its y is −sin φ.
    vec2 omega = vec2(cos(phi), -sin(phi)) * t.radiusInTexels;
    // The R1 sequence (line 420).
    float stepBaseNoise = float(slice + stepIndex * stepsPerSide) * 0.6180339887498948482;
    float stepNoise = fract(t.noise.y + stepBaseNoise);
    // The nearest step lies beyond the center texel (line 367).
    float s = (float(stepIndex) + stepNoise) / float(stepsPerSide) + NearestStepTexels / t.radiusInTexels;
    // A step beyond the image stays one texel past its longer side, where it still blocks nothing (decision 21): the
    // conversion of a float past the range of an int is undefined, and ρ reaches 10^10 at the largest radius.
    vec2 stepBound = vec2(float(max(halfSize().x, halfSize().y) + 1));
    return clamp(s * omega, -stepBound, stepBound);
}

// ---- The denoise -------------------------------------------------------------------------------------------------

// XeGTAO_Denoise (lines 734 to 812) for one texel: XeGTAO's 3 × 3 edge-aware blur of the source term's estimate, every
// channel with the same weights (estimate.glsl). Neighbors outside the image read the border texel, as XeGTAO's clamped
// sampler does.
Estimate gtaoDenoise(ivec2 texel, bool lastPass)
{
    const float diagWeight = 0.85 * 0.5;
    float blurAmount = lastPass ? DenoiseBlurBeta : DenoiseBlurBeta / 5.0;
    ivec2 last = halfSize() - 1;

    vec4 edgesL_LRTB = gtaoEdgesAt(texel + ivec2(-1, 0));
    vec4 edgesR_LRTB = gtaoEdgesAt(texel + ivec2(1, 0));
    vec4 edgesT_LRTB = gtaoEdgesAt(texel + ivec2(0, -1));
    vec4 edgesB_LRTB = gtaoEdgesAt(texel + ivec2(0, 1));
    vec4 edgesC_LRTB = gtaoEdgesAt(texel);

    // Each edge must agree from both sides.
    edgesC_LRTB *= vec4(edgesL_LRTB.y, edgesR_LRTB.x, edgesT_LRTB.w, edgesB_LRTB.z);

    // A small leak from the neighbors where 3 or 4 sides cut (line 773).
    const float leakThreshold = 2.5;
    const float leakStrength = 0.5;
    float edginess = (clamp(4.0 - leakThreshold - dot(edgesC_LRTB, vec4(1.0)), 0.0, 1.0) / (4.0 - leakThreshold))
                   * leakStrength;
    edgesC_LRTB = clamp(edgesC_LRTB + edginess, 0.0, 1.0);

    float weightTL = diagWeight * (edgesC_LRTB.x * edgesL_LRTB.z + edgesC_LRTB.z * edgesT_LRTB.x);
    float weightTR = diagWeight * (edgesC_LRTB.z * edgesT_LRTB.y + edgesC_LRTB.y * edgesR_LRTB.z);
    float weightBL = diagWeight * (edgesC_LRTB.w * edgesB_LRTB.x + edgesC_LRTB.x * edgesL_LRTB.w);
    float weightBR = diagWeight * (edgesC_LRTB.y * edgesR_LRTB.w + edgesC_LRTB.w * edgesB_LRTB.y);

    // The texel and its eight neighbors with their weights, in XeGTAO's order of the sum.
    const ivec2 offsets[9] = ivec2[9](ivec2(0, 0), ivec2(-1, 0), ivec2(1, 0), ivec2(0, -1), ivec2(0, 1), ivec2(-1, -1),
                                      ivec2(1, -1), ivec2(-1, 1), ivec2(1, 1));
    float weights[9] = float[9](blurAmount, edgesC_LRTB.x, edgesC_LRTB.y, edgesC_LRTB.z, edgesC_LRTB.w, weightTL,
                                weightTR, weightBL, weightBR);
    Estimate sum = Estimate(vec3(0.0), 0.0, vec3(0.0));
    for (int i = 0; i < 9; i++)
    {
        Estimate value = unpackEstimate(imageLoad(denoiseSource, clamp(texel + offsets[i], ivec2(0), last)));
        sum = addWeighted(sum, value, weights[i]);
    }
    float sumWeight = blurAmount + dot(edgesC_LRTB, vec4(1.0)) + weightTL + weightTR + weightBL + weightBR;
    return dividedEstimate(sum, sumWeight);
}

#endif
