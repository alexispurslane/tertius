// The diffuse filter's input (diffuse_filter.glsl), which the spatial dispatch of the diffuse signal's accumulation writes
// beside its slow history, because both read the same images: the slow history's light and the standard deviation of
// the luminance of the pixel's samples. A history of at least YoungFrames frames takes the deviation that the temporal
// dispatch keeps in the fast history's alpha. A younger one estimates it from the fast histories of the same surface
// around it, raised by YoungFrames over its length, as the paper does, because a few samples give no temporal estimate.
// Needs accumulate.glsl and meter.glsl.
#ifndef TERTIUS_DIFFUSE_FILTER_INPUT_GLSL
#define TERTIUS_DIFFUSE_FILTER_INPUT_GLSL

// The filter's input: rgb the light, a the deviation.
layout(set = 3, binding = 13, rgba16f) uniform writeonly image2D filterInput;

// The length below which a history estimates its deviation from its neighbors: the paper's four frames.
const float YoungFrames = 4.0;

// The half width of the window of the young estimate: the paper's 7 × 7 pixels.
const int YoungRadius = 3;

// The deviation of the luminance of the fast histories on the pixel's surface over the window, raised by YoungFrames over
// the history's length.
float youngDeviation(ivec2 pixel, SurfaceKey key, float historyLength)
{
    float sum = 0.0;
    float squares = 0.0;
    float count = 0.0;
    for (int y = -YoungRadius; y <= YoungRadius; y++)
        for (int x = -YoungRadius; x <= YoungRadius; x++)
        {
            ivec2 neighbor = pixel + ivec2(x, y);
            if (!insideImages(neighbor) || !sameSurface(key, surfaceKeyAt(neighbor)))
                continue;
            float l = luminance(imageLoad(fastHistory, neighbor).rgb);
            sum += l;
            squares += l * l;
            count += 1.0;
        }
    // The pixel's own key passes its own test, so the count is at least 1.
    float mean = sum / count;
    float variance = max(squares / count - mean * mean, 0.0) * YoungFrames / max(historyLength, 1.0);
    return min(sqrt(variance), MaxRadiance);
}

// Writes the filter's input of a pixel that shows a surface, whose slow history the dispatch writes as `slow` and
// `historyLength`. An unlit surface, such as an impostor's, whose light is its emission, takes a deviation of 0, which the
// filter never reads: its wavelet dispatches pass the pixel's light on unfiltered.
void storeFilterInput(ivec2 pixel, SurfaceKey key, vec3 slow, float historyLength)
{
    float deviation = 0.0;
    if (texelFetch(gbufferBaseColor, pixel, 0).a > 0.0)
        deviation = historyLength >= YoungFrames ? imageLoad(fastHistory, pixel).a
                                                 : youngDeviation(pixel, key, historyLength);
    imageStore(filterInput, pixel, vec4(slow, deviation));
}

#endif
