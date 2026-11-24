// The tone map pass (ARCHITECTURE.md, "Tone mapping"). tonemap.frag reads the HDR image alone. tonemap_bloom.frag
// defines BLOOM and adds the summed bloom levels, so that a renderer without bloom runs the shader that never reads them.

layout(set = 0, binding = 0) uniform texture2D hdrImage;
// The blue-noise mask of the output's dither (DescriptorLayouts.PostMaskBinding).
layout(set = 0, binding = 3) uniform utexture2D blueNoiseMask;
#include "blue_noise.glsl"
#ifdef BLOOM
#include "tent.glsl"
layout(set = 0, binding = 1) uniform texture2D glowImage;   // bloom level 0, which holds every level summed
layout(set = 0, binding = 2) uniform sampler linearClamp;
#endif

layout(push_constant, std430) uniform TonemapPush
{
    uint encodeSrgb;   // 1 when the output format is UNORM and the shader must apply the sRGB curve itself
    uint toneCurve;    // the ToneCurve enum: 0 ACES fitted, 1 Hable
    float glowStrength; // the bloom's strength; unread without bloom
    uint drawIndex;    // the frame's draw index, which picks the dither's frame of the blue-noise mask
    float code;        // one code of the output's encoding, as a share of 1: 1/255 at 8 bits; 0 turns the dither off
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

// The encoded color with the output's dither: a triangular offset of at most one code up or down, the same for the three
// channels, from the blue-noise mask at the pixel and the draw. Its size is `code` in the middle and falls to 0 over the
// last code at each end, so it is 0 at an encoded value of 0 and of 1: black stays black and white stays white. Each
// offset is as likely as its negative, so a region's mean code is the unrounded value (ARCHITECTURE.md, "Tone mapping").
vec3 dithered(vec3 encoded, ivec2 pixel, uint drawIndex, float code)
{
    // The mask's radial byte as a number in (0, 1), uniform over the pixels of a frame.
    float u = (float(blueNoiseBytesOf(pixel, drawIndex).x) + 0.5) * (1.0 / 256.0);
    // The inverse of the triangular distribution on [-1, 1]: the sum of two uniform numbers, from one.
    float offset = u < 0.5 ? sqrt(2.0 * u) - 1.0 : 1.0 - sqrt(2.0 - 2.0 * u);
    vec3 size = clamp(min(encoded, 1.0 - encoded), 0.0, code);
    return encoded + offset * size;
}

void main()
{
    vec3 light = texelFetch(hdrImage, ivec2(gl_FragCoord.xy), 0).rgb;
#ifdef BLOOM
    // The glow joins the light before the tone curve, so the curve shapes both alike.
    light += tonemap.glowStrength * tent(glowImage, linearClamp);
#endif
    // The passes that wrote the HDR image pre-exposed it, so the curve takes it as it is.
    vec3 hdr = max(light, vec3(0.0));
    vec3 ldr = tonemap.toneCurve == toneCurveAcesFitted ? acesFitted(hdr) : hable(hdr);
    // The dither works on the encoded value, whose codes the target rounds. An sRGB target encodes what the shader writes,
    // so the shader takes the dithered value back through the inverse curve.
    vec3 encoded = dithered(linearToSrgb(ldr), ivec2(gl_FragCoord.xy), tonemap.drawIndex, tonemap.code);
    outColor = vec4(tonemap.encodeSrgb != 0u ? encoded : srgbToLinear(encoded), 1.0);
}
