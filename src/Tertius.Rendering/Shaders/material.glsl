// Material evaluation shared by the G-buffer pass and the transparent forward pass.
#ifndef TERTIUS_MATERIAL_GLSL
#define TERTIUS_MATERIAL_GLSL

struct SurfaceSample
{
    vec4 albedo;          // linear RGB and alpha
    vec3 normal;          // unit world-space normal after normal mapping
    float specular;       // specular intensity in [0, 1]
    float gloss;          // gloss in [0, 1]
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
    s.albedo = m.baseColor * sampleTexture(m.albedoTexture, uv);

    vec3 n = normalize(vertexNormal);
    vec3 t = vertexTangent.xyz - n * dot(n, vertexTangent.xyz);
    float tangentLengthSq = dot(t, t);
    t = tangentLengthSq > 1e-12 ? t * inversesqrt(tangentLengthSq) : anyPerpendicular(n);
    vec3 b = cross(n, t) * (vertexTangent.w < 0.0 ? -1.0 : 1.0);
    vec3 tangentNormal = sampleTexture(m.normalTexture, uv).xyz * 2.0 - 1.0;
    vec3 mapped = t * tangentNormal.x + b * tangentNormal.y + n * tangentNormal.z;
    float mappedLengthSq = dot(mapped, mapped);
    s.normal = mappedLengthSq > 1e-12 ? mapped * inversesqrt(mappedLengthSq) : n;

    vec2 specularGloss = sampleTexture(m.specularGlossTexture, uv).rg;
    s.specular = m.specularIntensity * specularGloss.x;
    s.gloss = m.gloss * specularGloss.y;
    s.emissive = m.emissive;
    return s;
}

#endif
