#ifndef TERTIUS_NEIGHBOR_LIMIT_PROTOTYPE_GLSL
#define TERTIUS_NEIGHBOR_LIMIT_PROTOTYPE_GLSL

layout(constant_id = 9) const bool neighborLimitPrototypeOn = false;

vec3 limitIncomingEstimate(ivec2 pixel, vec3 sampled)
{
    if (!neighborLimitPrototypeOn || !finiteSample(sampled) || any(lessThan(sampled, vec3(0.0))))
        return sampled;

    SurfaceKey center = surfaceKeyAt(pixel);
    float centerRoughness = roughnessAt(pixel);
    float neighborBrightness[8];
    int support = 0;
    for (int row = -1; row <= 1; row++)
    for (int column = -1; column <= 1; column++)
    {
        if (row == 0 && column == 0)
            continue;
        ivec2 neighbor = pixel + 2 * ivec2(column, row);
        if (!insideImages(neighbor) || !sameSurface(center, surfaceKeyAt(neighbor)))
            continue;
        if (parameters.light == SpecularLight && abs(roughnessAt(neighbor) - centerRoughness) > 0.1)
            continue;
        vec3 reference = imageLoad(sampleImage, neighbor).rgb;
        if (!finiteSample(reference) || any(lessThan(reference, vec3(0.0))))
            continue;
        float brightness = max(reference.r, max(reference.g, reference.b));
        int position = support;
        while (position > 0 && neighborBrightness[position - 1] > brightness)
        {
            neighborBrightness[position] = neighborBrightness[position - 1];
            position--;
        }
        neighborBrightness[position] = brightness;
        support++;
    }

    if (support < 4)
        return sampled;
    float medianBrightness = 0.5 * neighborBrightness[(support - 1) / 2] + 0.5 * neighborBrightness[support / 2];
    float brightness = max(sampled.r, max(sampled.g, sampled.b));
    float knee = 4.0 * medianBrightness;
    if (brightness <= knee)
        return sampled;
    float ratio = knee / brightness;
    return sampled * (ratio * (2.0 - ratio));
}

#endif
