#version 460
#extension GL_EXT_samplerless_texture_functions : require
#extension GL_GOOGLE_include_directive : require
#include "srgb.glsl"

layout(set = 0, binding = 0) uniform texture2D hdrImage;

layout(push_constant, std430) uniform TonemapPush
{
    uint encodeSrgb;   // 1 when the output format is UNORM and the shader must apply the sRGB curve itself
    uint toneCurve;    // the ToneCurve enum: 0 ACES fitted, 1 Hable
} tonemap;

const uint toneCurveAcesFitted = 0u;

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

// John Hable's filmic curve from Uncharted 2 (GDC 2010, "Uncharted 2: HDR Lighting"), with his published constants.
vec3 hableF(vec3 x)
{
    const float a = 0.15, b = 0.50, c = 0.10, d = 0.20, e = 0.02, f = 0.30;
    return (x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f) - e / f;
}

vec3 hable(vec3 color)
{
    const float exposureBias = 2.0;
    const float white = 11.2;
    return clamp(hableF(exposureBias * color) / hableF(vec3(white)), 0.0, 1.0);
}

void main()
{
    // The passes that wrote the HDR image pre-exposed it, so the curve takes it as it is.
    vec3 hdr = max(texelFetch(hdrImage, ivec2(gl_FragCoord.xy), 0).rgb, vec3(0.0));
    vec3 ldr = tonemap.toneCurve == toneCurveAcesFitted ? acesFitted(hdr) : hable(hdr);
    if (tonemap.encodeSrgb != 0u)
        ldr = linearToSrgb(ldr);
    outColor = vec4(ldr, 1.0);
}
