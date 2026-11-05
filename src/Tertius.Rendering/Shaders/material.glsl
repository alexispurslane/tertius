// Material evaluation shared by the G-buffer pass and the transparent forward pass.
#ifndef TERTIUS_MATERIAL_GLSL
#define TERTIUS_MATERIAL_GLSL

#include "vectors.glsl"

// ShaderConstants.CutoutAlphaId: true in the cutout pipeline of an opaque pass, false in its solid pipeline and in every
// other, where the driver drops the test and the pipeline keeps its early depth writes (docs/plans/skin-and-hair.md, "The
// cutout test").
layout(constant_id = 1) const bool cutoutAlpha = false;
layout(constant_id = 8) const bool mainView = false;

struct SurfaceSample
{
    vec4 baseColor;       // linear RGB and alpha
    vec3 normal;          // unit world-space normal after normal mapping
    vec3 geometricNormal; // the unit interpolated vertex normal: the surface that the shadow maps see. For hair, turned
                          // toward the eye (docs/plans/skin-and-hair.md, "Both sides of a hair card")
    float metallic;       // 0 to 1
    float roughness;      // r₀, 0 to 1, before the BRDF's floor
    float reflectance;    // 0 to 1: a non-metal's F0 is 0.16 times its square
    vec3 emissive;
    uint model;           // the shading model's code: ShadingStandard, ShadingSkin or ShadingHair
    vec3 strand;          // T, a hair's unit strand from root to tip; 0 for the other models
};

vec3 anyPerpendicular(vec3 n)
{
    vec3 axis = abs(n.x) < 0.9 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
    return normalize(cross(axis, n));
}

vec4 sampleTexture(uint index, vec2 uv)
{
    // Different instances in one indirect draw use different materials, so the index is not dynamically uniform.
    return texture(sampler2D(textures[nonuniformEXT(index)], materialSampler), uv, mainView ? frame.renderParams.x : 0.0);
}

// The unit tangent of the shading frame of the unit normal `n`: the vertex tangent made perpendicular to it, or any
// perpendicular where the vertex has no tangent.
vec3 frameTangent(vec3 n, vec4 vertexTangent)
{
    vec3 t = vertexTangent.xyz - n * dot(n, vertexTangent.xyz);
    float tangentLengthSq = dot(t, t);
    return tangentLengthSq > 1e-12 ? t * inversesqrt(tangentLengthSq) : anyPerpendicular(n);
}

// Tangent space follows the vertex convention: tangent along +u, bitangent = cross(normal, tangent) × w.
// Normal maps store the tangent-space normal as RGB = xyz × 0.5 + 0.5, glTF's encoding. `n` is the unit vertex normal.
vec3 normalMapped(uint normalMap, vec2 uv, vec3 n, vec4 vertexTangent)
{
    vec3 t = frameTangent(n, vertexTangent);
    vec3 b = cross(n, t) * (vertexTangent.w < 0.0 ? -1.0 : 1.0);
    vec3 tangentNormal = sampleTexture(normalMap, uv).xyz * 2.0 - 1.0;
    vec3 mapped = t * tangentNormal.x + b * tangentNormal.y + n * tangentNormal.z;
    float mappedLengthSq = dot(mapped, mapped);
    return mappedLengthSq > 1e-12 ? mapped * inversesqrt(mappedLengthSq) : n;
}

// T, the strand from root to tip (docs/plans/skin-and-hair.md, "The strand and the fiber normal"): the axis of the shading
// frame of the unit shading normal `n`, after the normal map, that the strand direction's code names: 0 +u, 1 −u, 2 +v,
// 3 −v. v shrinks along the frame's bitangent, as in glTF, so +v is the bitangent's opposite. HairFiber.Strand is the C#
// copy.
vec3 strandOf(uint direction, vec3 n, vec4 vertexTangent)
{
    vec3 t = frameTangent(n, vertexTangent);
    vec3 alongV = cross(t, n) * (vertexTangent.w < 0.0 ? -1.0 : 1.0);
    vec3 axis = direction < 2u ? t : alongV;
    return (direction & 1u) == 0u ? axis : -axis;
}

// Whether a texel of alpha `alpha`, the base color's alpha times its map's, is covered under a cutout or fringed material
// of cutoff `cutoff`: the core. The alpha lies in [0, 1], from ShaderInput.Fraction and a UNORM map, and the cutoff in
// (0, 1], so the comparison sees finite values.
bool reachesCutoff(float alpha, float cutoff)
{
    return alpha >= cutoff;
}

// V is the unit direction toward the eye, and also the normal of a surface whose interpolated vertex normal has no
// direction. A hair's shading normal is its fiber normal, so every pass that shades the sample sees the fiber, not the
// card's plane (docs/plans/skin-and-hair.md, "The strand and the fiber normal"). In a cutout pipeline, a texel below the
// material's cutoff is discarded.
SurfaceSample sampleMaterial(uint materialIndex, vec2 uv, vec3 vertexNormal, vec4 vertexTangent, vec3 V)
{
    GpuMaterial m = materials[materialIndex];
    SurfaceSample s;
    s.baseColor = m.baseColor * sampleTexture(m.baseColorMap, uv);

    // A material without a normal map shades with its vertex normal exactly: no 8-bit UNORM texel decodes to a tangent
    // normal of +Z. The branch reads no texture for NoMap, which names no slot. The material comes from a flat input,
    // so every invocation of a quad takes the same branch, and the implicit derivatives of the sample inside it stay
    // defined.
    s.geometricNormal = normalizedOr(vertexNormal, V);
    s.normal = s.geometricNormal;
    if (m.normalMap != NoMap)
        s.normal = normalMapped(m.normalMap, uv, s.normal, vertexTangent);

    // glTF's layout: roughness in G, metallic in B. The reflectance has no map channel.
    vec4 metallicRoughness = sampleTexture(m.metallicRoughnessMap, uv);
    s.metallic = m.metallic * metallicRoughness.b;
    s.roughness = m.roughness * metallicRoughness.g;
    s.reflectance = m.reflectance;
    s.emissive = m.emissive;
    s.model = m.shading & 3u;
    s.strand = vec3(0.0);
    if (!dropsFibers && s.model == ShadingHair)
    {
        // The strand first, from the frame that the mesh gives, so that it runs the same way seen from either side.
        s.strand = strandOf((m.shading >> 2) & 3u, s.normal, vertexTangent);
        // A hair card has no back, and the passes that shade draw its back faces (docs/plans/skin-and-hair.md, "Both sides
        // of a hair card"): seen from behind, its normals turn toward the eye, as those of the card seen from the front.
        s.geometricNormal = turnedToward(s.geometricNormal, V);
        s.normal = fiberNormal(s.strand, V, turnedToward(s.normal, V));
    }
    // After every sample, so the implicit derivatives of the quad stay defined: discard demotes to a helper (VulkanDevice).
    if (cutoutAlpha && !reachesCutoff(s.baseColor.a, m.alphaCutoff))
        discard;
    return s;
}

#endif
