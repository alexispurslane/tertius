#version 460
#extension GL_EXT_samplerless_texture_functions : require

layout(set = 0, binding = 0) uniform texture2D hdrImage;

layout(push_constant, std430) uniform TonemapPush
{
    float exposure;
    uint encodeSrgb;   // 1 when the output format is UNORM and the shader must apply the sRGB curve itself
} tonemap;

layout(location = 0) out vec4 outColor;

// ACES fitted, by Stephen Hill (BakingLab): sRGB → ACES AP1 (with the RRT saturation), a rational fit of the
// RRT + ODT curve, then back to linear sRGB. GLSL matrices are column-major, so these are the transposes of the
// row-major HLSL originals.
const mat3 acesInput = mat3(
    0.59719, 0.07600, 0.02840,
    0.35458, 0.90834, 0.13383,
    0.04823, 0.01566, 0.83777);

const mat3 acesOutput = mat3(
     1.60475, -0.10208, -0.00327,
    -0.53108,  1.10813, -0.07276,
    -0.07367, -0.00605,  1.07602);

vec3 rrtAndOdtFit(vec3 v)
{
    vec3 a = v * (v + 0.0245786) - 0.000090537;
    vec3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return a / b;
}

vec3 acesFitted(vec3 color)
{
    return clamp(acesOutput * rrtAndOdtFit(acesInput * color), 0.0, 1.0);
}

vec3 linearToSrgb(vec3 c)
{
    return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c));
}

void main()
{
    vec3 hdr = texelFetch(hdrImage, ivec2(gl_FragCoord.xy), 0).rgb * tonemap.exposure;
    vec3 ldr = acesFitted(max(hdr, vec3(0.0)));
    if (tonemap.encodeSrgb != 0u)
        ldr = linearToSrgb(ldr);
    outColor = vec4(ldr, 1.0);
}
