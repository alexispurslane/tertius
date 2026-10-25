// The exposure's meter (docs/plans/auto-exposure.md, "The math"). ExposureMeter.cs is the C# copy. Change both together.
#ifndef TERTIUS_METER_GLSL
#define TERTIUS_METER_GLSL

const uint HistogramBins = 256u;
const float BinsPerStop = 8.0;
const float LowestStops = -16.0;    // log2 of the lowest pre-exposed luminance with a bin of its own
const float MiddleGray = 0.18;
const vec3 LuminanceWeights = vec3(0.2126, 0.7152, 0.0722);

float luminance(vec3 radiance)
{
    return dot(radiance, LuminanceWeights);
}

// A luminance at or below 2^LowestStops, 0 included, falls in the first bin.
uint binOf(float y)
{
    float stops = log2(max(y, exp2(LowestStops)));
    return uint(clamp(floor((stops - LowestStops) * BinsPerStop), 0.0, float(HistogramBins - 1u)));
}

float binCenterStops(uint bin)
{
    return LowestStops + (float(bin) + 0.5) / BinsPerStop;
}

#endif
