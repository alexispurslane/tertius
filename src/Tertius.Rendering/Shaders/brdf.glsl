// The terms of the PBR BRDF and the reads of its tables (docs/plans/pbr-brdf.md, "The BRDF"). Each function has a C#
// twin in Tertius.Rendering/Shading, which BrdfShaderTests pins.
#ifndef TERTIUS_BRDF_GLSL
#define TERTIUS_BRDF_GLSL

#include "vectors.glsl"

const float MinRoughness = 0.045;   // GgxLobe.MinRoughness
const float MinNdotV = 1e-4;        // MaterialSample's floor on μ
const float MinHalfCosine = 1e-4;   // GgxLobe.MinHalfCosine, the floor of v·h in sphereNormalization

const float DfgSize = 32.0;         // BrdfTables.DfgSize
const float LobeSkyWidth = 64.0;    // BrdfTables.LobeSkyWidth
const float LobeSkyHeight = 32.0;   // BrdfTables.LobeSkyHeight
const float LtcSize = 64.0;         // LtcTable.Size

layout(set = 0, binding = 8) uniform texture2D dfgTable;       // RG16F: (S, E₁) over μ across and r down
layout(set = 0, binding = 9) uniform texture2D lobeSkyTable;   // R16F: s_l over (d·up + 1) / 2 across and r down
layout(set = 0, binding = 10) uniform sampler linearClamp;    // linear, clamped to the edge, one level
layout(set = 0, binding = 19) uniform texture2D ltcTable;      // RGBA16F: LtcTable's m00, m02, m20, m22 over √(1 − μ) across and r down

// GgxLobe's constructor: the one place of the roughness floor.
float flooredRoughness(float roughness)
{
    return clamp(roughness, MinRoughness, 1.0);
}

// D: the GGX distribution of the microfacet normals, per steradian, at the unit half vector H of the unit normal N.
// The denominator (n·h)² (α² − 1) + 1 is |n × h|² + (n·h)² α², as Filament's D_GGX computes it: near n·h = 1, a 32-bit
// 1 − (n·h)² cancels to a few float steps of n·h, which move D by several percent at the floor's α² = 4.1e-6, and the
// cross product keeps its precision. GgxLobe.Distribution, in doubles, is the C# copy.
float ggxDistribution(vec3 N, vec3 H, float alpha)
{
    vec3 NcrossH = cross(N, H);
    float NdotH = dot(N, H);
    float alphaSq = alpha * alpha;
    float denominator = dot(NcrossH, NcrossH) + NdotH * NdotH * alphaSq;
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

// GgxLobe.SphereLightDirection: the unit direction toward the representative point of a sphere light, its point nearest
// the mirror ray R (Karis 2013, "Real Shading in Unreal Engine 4", p. 15, equation 11). L is the unit direction toward
// the light's center, and lightSine the sine of its angular radius, R / d: the vectors are in units of d. The notes print
// centerToRay as L − (L·R) R, which points away from the ray; the closest point needs (L·R) R − L. L if the closest point
// is the shading point itself.
vec3 sphereLightDirection(vec3 L, vec3 R, float lightSine)
{
    vec3 centerToRay = dot(L, R) * R - L;
    float centerToRaySq = dot(centerToRay, centerToRay);
    // A ray through the sphere has its closest point on itself, saturate's 1. The test also keeps inversesqrt from 0.
    vec3 closest = centerToRaySq <= lightSine * lightSine
        ? L + centerToRay
        : L + centerToRay * (lightSine * inversesqrt(centerToRaySq));
    return normalizedOr(closest, L);
}

// GgxLobe.SphereNormalization: (α / α′)², the normalization of the lobe that a sphere light widens (Karis 2013, p. 16,
// equation 14), with α′ = min(α + sin ϕ / (2 √(v·h)), 1) (docs/plans/pbr-brdf.md, "Sized lights"). Karis's equation 10
// adds sin ϕ / 3. The half vectors of the light's disc span 1 / (4 v·h) of its solid angle, so a mirror shows the light's
// own radiance when the radius of their cone, sin ϕ / (2 √(v·h)), widens α.
float sphereNormalization(float alpha, float lightSine, float VdotH)
{
    float widened = min(alpha + lightSine * 0.5 * inversesqrt(max(VdotH, MinHalfCosine)), 1.0);
    float share = alpha / widened;
    return share * share;
}

// GgxLobe.TubeNormalization: (α / α′_c) (α / α′_a), the normalization of the lobe that a tube light widens by the
// half-widths of the ellipse of its representative point, crossSine across the tube and alongSine along it, each
// widened as sphereNormalization widens α (ARCHITECTURE.md, "Tube lights").
float tubeNormalization(float alpha, float crossSine, float alongSine, float VdotH)
{
    float spread = 0.5 * inversesqrt(max(VdotH, MinHalfCosine));
    return alpha / min(alpha + crossSine * spread, 1.0) * (alpha / min(alpha + alongSine * spread, 1.0));
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

// LtcTable.Sample: the inverse matrix of the LTC of the lobe at μ and the floored roughness, as (m00, m02, m20, m22) with
// m11 = 1, in the frame whose x points toward the view and whose z is the normal (ARCHITECTURE.md, "Rectangle lights").
vec4 ltcAt(float NdotV, float roughness)
{
    vec2 uv = vec2(tableCoordinate(sqrt(1.0 - NdotV), LtcSize), tableCoordinate(roughness, LtcSize));
    return textureLod(sampler2D(ltcTable, linearClamp), uv, 0.0);
}

// s_l at d·up and the floored roughness.
float lobeSkyAt(float cosine, float roughness)
{
    vec2 uv = vec2(tableCoordinate(cosine * 0.5 + 0.5, LobeSkyWidth), tableCoordinate(roughness, LobeSkyHeight));
    return textureLod(sampler2D(lobeSkyTable, linearClamp), uv, 0.0).r;
}

#endif
