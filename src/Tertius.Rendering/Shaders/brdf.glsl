// The terms of the PBR BRDF and the reads of its tables (docs/plans/pbr-brdf.md, "The BRDF"). Each function has a C#
// twin in Tertius.Rendering/Shading, which BrdfShaderTests pins.
#ifndef TERTIUS_BRDF_GLSL
#define TERTIUS_BRDF_GLSL

#include "vectors.glsl"

const float MinRoughness = 0.045;   // GgxLobe.MinRoughness
const float MinNdotV = 1e-4;        // MaterialSample's floor on μ

const float DfgSize = 32.0;         // BrdfTables.DfgSize
const float LobeSkyWidth = 64.0;    // BrdfTables.LobeSkyWidth
const float LobeSkyHeight = 32.0;   // BrdfTables.LobeSkyHeight

layout(set = 0, binding = 8) uniform texture2D dfgTable;       // RG16F: (S, E₁) over μ across and r down
layout(set = 0, binding = 9) uniform texture2D lobeSkyTable;   // R16F: s_l over (d·up + 1) / 2 across and r down
layout(set = 0, binding = 10) uniform sampler linearClamp;    // linear, clamped to the edge, one level

// GgxLobe's constructor: the one place of the roughness floor.
float flooredRoughness(float roughness)
{
    return clamp(roughness, MinRoughness, 1.0);
}

// D: the GGX distribution of the microfacet normals, per steradian.
float ggxDistribution(float NdotH, float alpha)
{
    float alphaSq = alpha * alpha;
    float denominator = NdotH * NdotH * (alphaSq - 1.0) + 1.0;
    return alphaSq / (PI * denominator * denominator);
}

// Vis = G₂ / (4 NdotL NdotV), with the height-correlated Smith masking-shadowing term.
float smithVisibility(float NdotL, float NdotV, float alpha)
{
    float alphaSq = alpha * alpha;
    float lightPart = NdotL * sqrt(NdotV * NdotV * (1.0 - alphaSq) + alphaSq);
    float viewPart = NdotV * sqrt(NdotL * NdotL * (1.0 - alphaSq) + alphaSq);
    return 0.5 / (lightPart + viewPart);
}

// F: Schlick's approximation, which reaches 1 at a grazing angle for every F0.
vec3 schlickFresnel(vec3 f0, float VdotH)
{
    return f0 + (1.0 - f0) * pow(1.0 - clamp(VdotH, 0.0, 1.0), 5.0);
}

// d: the direction between the mirror direction R and the normal N where the lobe gathers most of its light. Seen
// straight from behind, R is −N, and the mix is zero where towardMirror is one half: d is then N.
vec3 specularDominantDirection(vec3 N, vec3 R, float alpha)
{
    float towardMirror = (1.0 - alpha) * (sqrt(1.0 - alpha) + alpha);
    return normalizedOr(mix(N, R, towardMirror), N);
}

// BrdfTables.TexelCoordinate: texel i of n holds the value i / (n − 1), so both ends read exact values.
float tableCoordinate(float value, float texelCount)
{
    return ((texelCount - 1.0) * value + 0.5) / texelCount;
}

// (S, E₁) at μ and the floored roughness.
vec2 dfgAt(float NdotV, float roughness)
{
    vec2 uv = vec2(tableCoordinate(NdotV, DfgSize), tableCoordinate(roughness, DfgSize));
    return textureLod(sampler2D(dfgTable, linearClamp), uv, 0.0).rg;
}

// s_l at d·up and the floored roughness.
float lobeSkyAt(float cosine, float roughness)
{
    vec2 uv = vec2(tableCoordinate(cosine * 0.5 + 0.5, LobeSkyWidth), tableCoordinate(roughness, LobeSkyHeight));
    return textureLod(sampler2D(lobeSkyTable, linearClamp), uv, 0.0).r;
}

#endif
