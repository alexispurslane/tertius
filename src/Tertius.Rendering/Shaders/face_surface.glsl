// The surface record of an environment face (EnvironmentFacePass.cs, FaceSurface.cs): what the nearest opaque surface
// of each texel is, as the values that the light loop reads. The face's surfaces write it (environment_face.frag), and
// the face's light pass lights each texel once from it (environment_face_light.frag). A forward pass would run the
// light loop for every fragment of every small triangle, which cost the deck 0.11 ms more of its mean frame
// (ticket t2-zcr4).
//
// The six layers of one RGBA32F image hold the record. Every value is the full float that the forward pass computed, so
// the light pass reads the same numbers:
//
//   0  xyz the position from the eye in m, w the shading model's code plus 1. A w of 0 marks a texel with no surface.
//   1  xyz the shading normal, w the metallic share
//   2  xyz the geometric normal, w the roughness r₀
//   3  rgb the base color, w the reflectance
//   4  rgb the glow that the surface sends (glowSent), w 0
//   5  xyz a hair's strand, w 0
//
// A shader that writes the record defines FACE_SURFACE_WRITES before the include, after material.glsl. A shader that
// reads it includes this file after lighting.glsl.
#ifndef TERTIUS_FACE_SURFACE_GLSL
#define TERTIUS_FACE_SURFACE_GLSL

#ifdef FACE_SURFACE_WRITES

layout(location = 0) out vec4 outPositionModel;
layout(location = 1) out vec4 outNormalMetallic;
layout(location = 2) out vec4 outGeometricNormalRoughness;
layout(location = 3) out vec4 outBaseColorReflectance;
layout(location = 4) out vec4 outGlow;
layout(location = 5) out vec4 outStrand;

// Writes the record of the material sample `s` at `position`, which sends `glow`.
void writeFaceSurface(vec3 position, SurfaceSample s, vec3 glow)
{
    // The model's codes are small whole numbers, which a float holds exactly.
    outPositionModel = vec4(position, float(s.model + 1u));
    outNormalMetallic = vec4(s.normal, s.metallic);
    outGeometricNormalRoughness = vec4(s.geometricNormal, s.roughness);
    outBaseColorReflectance = vec4(s.baseColor.rgb, s.reflectance);
    outGlow = vec4(glow, 0.0);
    outStrand = vec4(s.strand, 0.0);
}

#else

#extension GL_EXT_samplerless_texture_functions : require

// Set 3 of the face light layout (DescriptorLayouts.FaceSurfaceSet): the record, one layer per output above, read with
// texelFetch at gl_FragCoord.
layout(set = 3, binding = 0) uniform texture2DArray faceSurface;

const int FacePositionModel = 0;
const int FaceNormalMetallic = 1;
const int FaceGeometricNormalRoughness = 2;
const int FaceBaseColorReflectance = 3;
const int FaceGlow = 4;
const int FaceStrand = 5;

// The record of `texel` as the light loop's input, and the glow that its surface sends. False where no surface lies:
// the texel shows the sky. A face takes no ambient light, so the fields that only the ambient light reads are 0.
bool faceSurfaceAt(ivec2 texel, out ShadingInput s, out vec3 glow)
{
    vec4 positionModel = texelFetch(faceSurface, ivec3(texel, FacePositionModel), 0);
    glow = vec3(0.0);
    if (positionModel.w == 0.0)
        return false;
    vec4 normalMetallic = texelFetch(faceSurface, ivec3(texel, FaceNormalMetallic), 0);
    vec4 geometricNormalRoughness = texelFetch(faceSurface, ivec3(texel, FaceGeometricNormalRoughness), 0);
    vec4 baseColorReflectance = texelFetch(faceSurface, ivec3(texel, FaceBaseColorReflectance), 0);
    s.position = positionModel.xyz;
    s.baseColor = baseColorReflectance.rgb;
    s.normal = normalMetallic.xyz;
    s.geometricNormal = geometricNormalRoughness.xyz;
    s.metallic = normalMetallic.w;
    s.roughness = geometricNormalRoughness.w;
    s.reflectance = baseColorReflectance.w;
    s.skyFraction = 0.0;
    s.lobeSkyFraction = 0.0;
    s.reflectionFootprint = 0.0;
    s.map = 0u;
    s.model = uint(positionModel.w) - 1u;
    s.strand = texelFetch(faceSurface, ivec3(texel, FaceStrand), 0).xyz;
    glow = texelFetch(faceSurface, ivec3(texel, FaceGlow), 0).rgb;
    return true;
}

#endif

#endif
