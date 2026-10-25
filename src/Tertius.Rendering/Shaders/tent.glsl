// The 3x3 tent filter of the bloom's upsample (Jimenez, SIGGRAPH 2014): nine bilinear taps one texel of the smaller
// image apart, weighted 1 2 1, 2 4 2, 1 2 1 over 16. The output pixel's center at gl_FragCoord lies at half that in the
// smaller image's texels.
vec3 tent(texture2D image, sampler linear)
{
    vec2 texel = 1.0 / vec2(textureSize(sampler2D(image, linear), 0));
    vec2 center = 0.5 * gl_FragCoord.xy * texel;
    vec3 sum = vec3(0.0);
    for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            float weight = float((2 - abs(x)) * (2 - abs(y)));
            sum += weight * textureLod(sampler2D(image, linear), center + vec2(x, y) * texel, 0.0).rgb;
        }
    return sum / 16.0;
}
