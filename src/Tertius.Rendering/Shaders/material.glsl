// Material evaluation shared by the G-buffer pass and the transparent forward pass.
#ifndef TERTIUS_MATERIAL_GLSL
#define TERTIUS_MATERIAL_GLSL

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
// Normal maps store the tangent-space normal as RGB = xyz × 0.5 + 0.5.
SurfaceSample sampleMaterial(uint materialIndex, vec2 uv, vec3 vertexNormal, vec4 vertexTangent)
{
    GpuMaterial m = materials[materialIndex];
    SurfaceSample s;
    s.baseColor = m.baseColor * sampleTexture(m.baseColorMap, uv);

    vec3 n = normalize(vertexNormal);
    vec3 t = vertexTangent.xyz - n * dot(n, vertexTangent.xyz);
    float tangentLengthSq = dot(t, t);
    t = tangentLengthSq > 1e-12 ? t * inversesqrt(tangentLengthSq) : anyPerpendicular(n);
    vec3 b = cross(n, t) * (vertexTangent.w < 0.0 ? -1.0 : 1.0);
    vec3 tangentNormal = sampleTexture(m.normalMap, uv).xyz * 2.0 - 1.0;
    vec3 mapped = t * tangentNormal.x + b * tangentNormal.y + n * tangentNormal.z;
    float mappedLengthSq = dot(mapped, mapped);
    s.normal = mappedLengthSq > 1e-12 ? mapped * inversesqrt(mappedLengthSq) : n;

    // glTF's layout: roughness in G, metallic in B. The reflectance has no map channel.
    vec4 metallicRoughness = sampleTexture(m.metallicRoughnessMap, uv);
    s.metallic = m.metallic * metallicRoughness.b;
    s.roughness = m.roughness * metallicRoughness.g;
    s.reflectance = m.reflectance;
    s.emissive = m.emissive;
    return s;
}

#endif
