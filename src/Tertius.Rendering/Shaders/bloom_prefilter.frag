#version 460
#extension GL_EXT_samplerless_texture_functions : require
#extension GL_GOOGLE_include_directive : require
#include "bloom.glsl"

// The first downsample, from the HDR image into level 0, of the thresholded light. The threshold applies to each HDR
// texel before any averaging, so a source of one pixel glows by its own brightness. Each bilinear tap of Jimenez's
// filter lies on a texel corner and averages the four texels around it, so the filter of a thresholded image is a fixed
// weight for each of the 6x6 texels under its 13 taps: the sum of a quarter of the weight of each tap that holds the
// texel. The draw sums the 36 weighted texels itself, so no draw has to write the thresholded image.

// The largest finite half float. A blend that overflows writes infinity, which would spread NaN through every level.
const float halfMax = 65504.0;

// Unity's quadratic soft threshold (PostProcessing v2, QuadraticThreshold in Colors.hlsl): the brightness is the
// largest channel of the HDR image, which the shading passes pre-exposed. Below threshold - knee width nothing glows, above threshold + knee width the light
// above the threshold glows, and between them a quadratic joins the two, so the glow fades in.
vec3 thresholded(vec3 color)
{
    color = clamp(color, 0.0, halfMax);
    float brightness = max(color.r, max(color.g, color.b));
    float width = bloom.threshold * bloom.knee;
    float soft = clamp(brightness - bloom.threshold + width, 0.0, 2.0 * width);
    soft = soft * soft / (4.0 * width + 1e-5);
    return color * max(soft, brightness - bloom.threshold) / max(brightness, 1e-5);
}

// The weights of the 6x6 texels, times 128, row by row. They sum to 128.
const float texelWeights[36] = float[36](
    1, 1, 2, 2, 1, 1,
    1, 5, 6, 6, 5, 1,
    2, 6, 8, 8, 6, 2,
    2, 6, 8, 8, 6, 2,
    1, 5, 6, 6, 5, 1,
    1, 1, 2, 2, 1, 1);

void main()
{
    ivec2 last = textureSize(source, 0) - 1;
    // The output pixel's center lies between source texels 2p and 2p + 1, so its taps cover texels 2p - 2 to 2p + 3.
    ivec2 first = ivec2(gl_FragCoord.xy) * 2 - 2;
    vec3 sum = vec3(0.0);
    for (int y = 0; y < 6; y++)
        for (int x = 0; x < 6; x++)
        {
            vec3 texel = texelFetch(source, clamp(first + ivec2(x, y), ivec2(0), last), 0).rgb;
            sum += texelWeights[y * 6 + x] * thresholded(texel);
        }
    // Each level holds at most the largest half float over the levels, so their sum in level 0 stays finite.
    outColor = vec4(sum / (128.0 * float(bloom.levels)), 1.0);
}
