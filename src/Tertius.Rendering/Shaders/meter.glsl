// The exposure's meter (https://github.com/alexispurslane/tertius/wiki/auto-exposure, "The math"). ExposureMeter.cs is the C# copy. Change both together.
#ifndef TERTIUS_METER_GLSL
#define TERTIUS_METER_GLSL

const uint HistogramBins = 2304u;
const float BinsPerStop = 8.0;
const float LowestStops = -160.0;    // log2 of the lowest scene luminance with a bin of its own
const float MiddleGray = 0.18;
const vec3 LuminanceWeights = vec3(0.2126, 0.7152, 0.0722);

float luminance(vec3 radiance)
{
    return dot(radiance, LuminanceWeights);
}

// Black and positive subnormal luminance use the first bin, also on GPUs that flush subnormals.
uint binOf(float y)
{
    if (y <= 0.0 || floatBitsToUint(y) < 0x00800000u)
        return 0u;
    float stops = log2(y);
    return uint(clamp(floor((stops - LowestStops) * BinsPerStop), 0.0, float(HistogramBins - 1u)));
}

float binCenterStops(uint bin)
{
    return LowestStops + (float(bin) + 0.5) / BinsPerStop;
}

#endif
