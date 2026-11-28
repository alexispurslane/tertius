#version 460
#extension GL_EXT_samplerless_texture_functions : require
#extension GL_GOOGLE_include_directive : require
#include "bloom.glsl"

#include "frame.glsl"
#include "exposure.glsl"

// Threshold each original scene texel before interpolation. Exposure affects only this derived response.
// Ordinary excess light keeps its amplitude. Above 128, a C1 rational response approaches 256.
// This is an artistic extreme-glare response, separate from the optical reference for normal glow.
vec3 thresholded(vec3 sourceColor)
{
    vec3 color = displayExposed(sourceColor, exposureStops);
    float brightness = max(color.r, max(color.g, color.b));
    float width = bloom.threshold * bloom.knee;
    float delta = brightness - bloom.threshold;
    float excess = max(delta, 0.0);
    // A subnormal knee cannot reach a half-float bloom texel. Skip it before any driver can flush its divisor to zero.
    if (width >= 1.17549435e-38 && delta > -width && delta < width)
    {
        float normalized = delta / width + 1.0;
        float soft = (width / (4.0 + 1e-5 / width)) * normalized * normalized;
        excess = max(excess, min(soft, brightness));
    }
    if (excess > 128.0)
    {
        float extreme = excess - 128.0;
        excess = 128.0 + 128.0 * (extreme / (128.0 + extreme));
    }
    return (color / max(brightness, 1e-5)) * excess;
}

// Bilinear interpolation of already thresholded source texels. Clamp each fetch to the active rectangle,
// which can be smaller than the source allocation after a render-scale change.
vec4 mappedTap(vec2 outputPosition)
{
    vec2 sourcePosition = outputPosition * vec2(bloom.scaleX, bloom.scaleY)
        - vec2(bloom.jitterX, bloom.jitterY) - 0.5;
    ivec2 first = ivec2(floor(sourcePosition));
    vec2 fraction = fract(sourcePosition);
    ivec2 last = ivec2(bloom.sourceWidth, bloom.sourceHeight) - 1;
    vec3 a = thresholded(texelFetch(source, clamp(first, ivec2(0), last), 0).rgb);
    vec3 b = thresholded(texelFetch(source, clamp(first + ivec2(1, 0), ivec2(0), last), 0).rgb);
    vec3 c = thresholded(texelFetch(source, clamp(first + ivec2(0, 1), ivec2(0), last), 0).rgb);
    vec3 d = thresholded(texelFetch(source, clamp(first + ivec2(1, 1), ivec2(0), last), 0).rgb);
    return vec4(mix(mix(a, b, fraction.x), mix(c, d, fraction.x), fraction.y), 1.0);
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
    if (bloom.scaleX != 1.0 || bloom.scaleY != 1.0 || bloom.jitterX != 0.0 || bloom.jitterY != 0.0)
    {
        vec2 center = 2.0 * gl_FragCoord.xy;
        outColor = vec4(jimenez13(
            mappedTap(center + vec2(-2, -2)), mappedTap(center + vec2(0, -2)), mappedTap(center + vec2(2, -2)),
            mappedTap(center + vec2(-1, -1)), mappedTap(center + vec2(1, -1)),
            mappedTap(center + vec2(-2, 0)), mappedTap(center), mappedTap(center + vec2(2, 0)),
            mappedTap(center + vec2(-1, 1)), mappedTap(center + vec2(1, 1)),
            mappedTap(center + vec2(-2, 2)), mappedTap(center + vec2(0, 2)), mappedTap(center + vec2(2, 2))).rgb
            / float(bloom.levels), 1.0);
        return;
    }
    ivec2 last = ivec2(bloom.sourceWidth, bloom.sourceHeight) - 1;
    // The output pixel's center lies between source texels 2p and 2p + 1, so its taps cover texels 2p - 2 to 2p + 3.
    ivec2 first = ivec2(gl_FragCoord.xy) * 2 - 2;
    vec3 sum = vec3(0.0);
    for (int y = 0; y < 6; y++)
        for (int x = 0; x < 6; x++)
        {
            vec3 texel = texelFetch(source, clamp(first + ivec2(x, y), ivec2(0), last), 0).rgb;
            sum += texelWeights[y * 6 + x] * thresholded(texel);
        }
    // The response is at most 256. Divide before the additive up chain; half-float rounding leaves ample room.
    outColor = vec4(sum / (128.0 * float(bloom.levels)), 1.0);
}
