// Material evaluation shared by the G-buffer pass and the transparent forward pass.
#ifndef TERTIUS_MATERIAL_GLSL
#define TERTIUS_MATERIAL_GLSL

#include "vectors.glsl"

// ShaderConstants.CutoutAlphaId: true in the cutout pipeline of an opaque pass, false in its solid pipeline and in every
// other, where the driver drops the test and the pipeline keeps its early depth writes (docs/plans/skin-and-hair.md, "The
// cutout test").
layout(constant_id = 1) const bool cutoutAlpha = false;

struct SurfaceSample
{
    vec4 baseColor;       // linear RGB and alpha
    vec3 normal;          // unit world-space normal after normal mapping
    float metallic;       // 0 to 1
    float roughness;      // r₀, 0 to 1, before the BRDF's floor
    float reflectance;    // 0 to 1: a non-metal's F0 is 0.16 times its square
    vec3 emissive;
};

vec3 anyPerpendicular(vec3 n)
{
    vec3 axis = abs(n.x) < 0.9 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
    return normalize(cross(axis, n));
}

vec4 sampleTexture(uint index, vec2 uv)
{
    // Different instances in one indirect draw use different materials, so the index is not dynamically uniform.
    return texture(sampler2D(textures[nonuniformEXT(index)], materialSampler), uv);
}

// Tangent space follows the vertex convention: tangent along +u, bitangent = cross(normal, tangent) × w.
// Normal maps store the tangent-space normal as RGB = xyz × 0.5 + 0.5, glTF's encoding. `n` is the unit vertex normal.
vec3 normalMapped(uint normalMap, vec2 uv, vec3 n, vec4 vertexTangent)
{
    vec3 t = vertexTangent.xyz - n * dot(n, vertexTangent.xyz);
    float tangentLengthSq = dot(t, t);
    t = tangentLengthSq > 1e-12 ? t * inversesqrt(tangentLengthSq) : anyPerpendicular(n);
    vec3 b = cross(n, t) * (vertexTangent.w < 0.0 ? -1.0 : 1.0);
    vec3 tangentNormal = sampleTexture(normalMap, uv).xyz * 2.0 - 1.0;
    vec3 mapped = t * tangentNormal.x + b * tangentNormal.y + n * tangentNormal.z;
    float mappedLengthSq = dot(mapped, mapped);
    return mappedLengthSq > 1e-12 ? mapped * inversesqrt(mappedLengthSq) : n;
}

// Whether a texel of alpha `alpha`, the base color's alpha times its map's, is covered under a cutout or fringed material
// of cutoff `cutoff`: the core. The alpha lies in [0, 1], from ShaderInput.Fraction and a UNORM map, and the cutoff in
// (0, 1], so the comparison sees finite values.
bool reachesCutoff(float alpha, float cutoff)
{
    return alpha >= cutoff;
}

// `fallbackNormal` is the unit normal of a surface whose interpolated vertex normal has no direction. In a cutout
// pipeline, a texel below the material's cutoff is discarded.
SurfaceSample sampleMaterial(uint materialIndex, vec2 uv, vec3 vertexNormal, vec4 vertexTangent, vec3 fallbackNormal)
{
    GpuMaterial m = materials[materialIndex];
    SurfaceSample s;
    s.baseColor = m.baseColor * sampleTexture(m.baseColorMap, uv);

    // A material without a normal map shades with its vertex normal exactly: no 8-bit UNORM texel decodes to a tangent
    // normal of +Z. The branch reads no texture for NoMap, which names no slot. The material comes from a flat input,
    // so every invocation of a quad takes the same branch, and the implicit derivatives of the sample inside it stay
    // defined.
    s.normal = normalizedOr(vertexNormal, fallbackNormal);
    if (m.normalMap != NoMap)
        s.normal = normalMapped(m.normalMap, uv, s.normal, vertexTangent);

    // glTF's layout: roughness in G, metallic in B. The reflectance has no map channel.
    vec4 metallicRoughness = sampleTexture(m.metallicRoughnessMap, uv);
    s.metallic = m.metallic * metallicRoughness.b;
    s.roughness = m.roughness * metallicRoughness.g;
    s.reflectance = m.reflectance;
    s.emissive = m.emissive;
    // After every sample, so the implicit derivatives of the quad stay defined: discard demotes to a helper (VulkanDevice).
    if (cutoutAlpha && !reachesCutoff(s.baseColor.a, m.alphaCutoff))
        discard;
    return s;
}

#endif
