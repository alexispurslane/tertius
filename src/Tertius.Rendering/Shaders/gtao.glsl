// The GLSL port of XeGTAO: ground-truth ambient occlusion (Jimenez and others 2016) at the physical constants of
// docs/plans/ambient-occlusion.md, "XeGTAO's heuristics". It holds every function that the port takes from XeGTAO:
// the edges and their packing, the noise and the Hilbert index, the view position, the slice loop and its inner
// integral, and the denoise. HorizonSlice, HorizonSearch and OcclusionNoise in Shading/ are its C# copy.
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

// The view depth of sky, in m: beyond every radius, and finite in three squared 32-bit coordinates (decision 18).
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
    vec2 tangents = vec2(occlusion.cameraRight.w, occlusion.cameraUp.w);
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

// The cosine between the view vector and the direction from the center to the sample at a texel, or −1, which never
// raises a horizon. A texel outside the image hides nothing (decision 21), and nor does a sample at the radius or
// beyond it: the physical hard edge, where XeGTAO lerps by its falloff (lines 486 to 498). The clamp keeps acos defined
// where rounding puts the cosine of two unit vectors past 1.
float gtaoSampleHorizon(ivec2 texel, vec3 center, vec3 toEye)
{
    if (!insideHalf(texel))
        return -1.0;
    vec3 delta = gtaoTexelPosition(texel) - center;
    float sampleDistance = length(delta);
    if (!(sampleDistance < occlusion.radiusMeters))
        return -1.0;
    return clamp(dot(delta / sampleDistance, toEye), -1.0, 1.0);
}

// XeGTAO_MainPass (lines 245 to 576) at the physical constants, without the depth chain: the estimate of V at a
// half-resolution texel of view depth z, before the fade. radiusInTexels is ρ.
float gtaoEstimate(ivec2 texel, float z, float radiusInTexels)
{
    vec3 center = gtaoViewPosition(pixelScreenPosition(2 * texel), z * CenterBias);
    vec3 toEye = normalize(-center);
    vec3 normal = shadingNormalAt(2 * texel);
    vec2 noise = gtaoNoise(texel);
    // The nearest step lies beyond the center texel (line 367).
    float minS = NearestStepTexels / radiusInTexels;
    int slices = int(occlusion.slices);
    int stepsPerSide = int(occlusion.stepsPerSide);
    float visibility = 0.0;
    for (int slice = 0; slice < slices; slice++)
    {
        // Lines 5 and 6 of the paper's Algorithm 1.
        float phi = (float(slice) + noise.x) / float(slices) * GtaoPi;
        float cosPhi = cos(phi);
        float sinPhi = sin(phi);
        // The screen direction in texels: the image rows run down, so its y is −sin φ.
        vec2 omega = vec2(cosPhi, -sinPhi) * radiusInTexels;
        vec3 directionVec = vec3(cosPhi, sinPhi, 0.0);
        vec3 orthoDirectionVec = directionVec - dot(directionVec, toEye) * toEye;
        vec3 axisVec = normalize(cross(orthoDirectionVec, toEye));
        vec3 projectedNormalVec = normal - axisVec * dot(normal, axisVec);
        float signNorm = sign(dot(orthoDirectionVec, projectedNormalVec));
        float projectedNormalVecLength = length(projectedNormalVec);
        float cosNorm = clamp(dot(projectedNormalVec, toEye) / max(projectedNormalVecLength, MinProjectedNormalLength),
                              0.0, 1.0);
        float n = signNorm * acos(cosNorm);
        // Each side's lowest horizon is the tangent plane (line 409).
        float horizonCos0 = cos(n + GtaoHalfPi);
        float horizonCos1 = cos(n - GtaoHalfPi);
        for (int stepIndex = 0; stepIndex < stepsPerSide; stepIndex++)
        {
            // The R1 sequence (line 420).
            float stepBaseNoise = float(slice + stepIndex * stepsPerSide) * 0.6180339887498948482;
            float stepNoise = fract(noise.y + stepBaseNoise);
            float s = (float(stepIndex) + stepNoise) / float(stepsPerSide) + minS;
            // Snapped to a texel center (line 440).
            ivec2 sampleOffset = ivec2(round(s * omega));
            horizonCos0 = max(horizonCos0, gtaoSampleHorizon(texel + sampleOffset, center, toEye));
            horizonCos1 = max(horizonCos1, gtaoSampleHorizon(texel - sampleOffset, center, toEye));
        }
        // Line 27 of the paper's Algorithm 1, with the exact acos (decision 22).
        float h0 = -acos(horizonCos1);
        float h1 = acos(horizonCos0);
        visibility += projectedNormalVecLength * (gtaoSliceArc(h0, n) + gtaoSliceArc(h1, n));
    }
    return visibility / float(slices);
}

// ---- The denoise -------------------------------------------------------------------------------------------------

// XeGTAO_Denoise (lines 734 to 812) for one texel: XeGTAO's 3 × 3 edge-aware blur of the source term. Neighbors outside
// the image read the border texel, as XeGTAO's clamped sampler does.
float gtaoDenoise(ivec2 texel, bool lastPass)
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

    float ssaoValue = imageLoad(denoiseSource, texel).r;
    float ssaoValueL = imageLoad(denoiseSource, clamp(texel + ivec2(-1, 0), ivec2(0), last)).r;
    float ssaoValueR = imageLoad(denoiseSource, clamp(texel + ivec2(1, 0), ivec2(0), last)).r;
    float ssaoValueT = imageLoad(denoiseSource, clamp(texel + ivec2(0, -1), ivec2(0), last)).r;
    float ssaoValueB = imageLoad(denoiseSource, clamp(texel + ivec2(0, 1), ivec2(0), last)).r;
    float ssaoValueTL = imageLoad(denoiseSource, clamp(texel + ivec2(-1, -1), ivec2(0), last)).r;
    float ssaoValueTR = imageLoad(denoiseSource, clamp(texel + ivec2(1, -1), ivec2(0), last)).r;
    float ssaoValueBL = imageLoad(denoiseSource, clamp(texel + ivec2(-1, 1), ivec2(0), last)).r;
    float ssaoValueBR = imageLoad(denoiseSource, clamp(texel + ivec2(1, 1), ivec2(0), last)).r;

    float sumWeight = blurAmount;
    float sum = ssaoValue * sumWeight;
    sum += edgesC_LRTB.x * ssaoValueL;
    sum += edgesC_LRTB.y * ssaoValueR;
    sum += edgesC_LRTB.z * ssaoValueT;
    sum += edgesC_LRTB.w * ssaoValueB;
    sum += weightTL * ssaoValueTL;
    sum += weightTR * ssaoValueTR;
    sum += weightBL * ssaoValueBL;
    sum += weightBR * ssaoValueBR;
    sumWeight += dot(edgesC_LRTB, vec4(1.0)) + weightTL + weightTR + weightBL + weightBR;
    return sum / sumWeight;
}

#endif
