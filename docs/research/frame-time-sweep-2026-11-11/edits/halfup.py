# The look of a half-resolution reflection path, as a prototype in the composite: each pixel takes the mean of the specular
# history over each 2 x 2 block, and the edge-aware bilinear blend of the four nearest blocks' means. Not its cost.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; common.envexp(root)
p = root + common.SH + 'composite_pass.glsl'
common.sub(p, "void main()\n{\n    // A helper invocation of a 2 × 2 quad can lie past", """vec3 coarseSpecularAt(ivec2 pixel)
{
    vec3 full = imageLoad(indirectSpecularLight, pixel).rgb;
    SurfaceKey key = surfaceKeyAt(pixel);
    float roughness = texelFetch(gbufferSpecular, pixel, 0).g;
    ivec2 block = pixel / 2;
    ivec2 side = ivec2((pixel.x & 1) == 1 ? 1 : -1, (pixel.y & 1) == 1 ? 1 : -1);
    ivec2 last = ivec2(frame.viewport.xy) - 1;
    vec3 sum = vec3(0.0);
    float weights = 0.0;
    for (int j = 0; j < 2; j++)
        for (int i = 0; i < 2; i++)
        {
            ivec2 other = block + ivec2(i, j) * side;
            vec3 mean = vec3(0.0);
            float count = 0.0;
            for (int c = 0; c < 4; c++)
            {
                ivec2 q = 2 * other + ivec2(c & 1, c >> 1);
                if (any(lessThan(q, ivec2(0))) || any(greaterThan(q, last)) || !sameSurface(key, surfaceKeyAt(q))
                    || abs(texelFetch(gbufferSpecular, q, 0).g - roughness) > 0.02)
                    continue;
                mean += imageLoad(indirectSpecularLight, q).rgb;
                count += 1.0;
            }
            if (count == 0.0)
                continue;
            float weight = (i == 0 ? 0.75 : 0.25) * (j == 0 ? 0.75 : 0.25);
            sum += weight * mean / count;
            weights += weight;
        }
    return weights > 0.0 ? sum / weights : full;
}

void main()
{
    // A helper invocation of a 2 × 2 quad can lie past""")
common.sub(p, "? IndirectLight(imageLoad(indirectDiffuseLight, pixel).rgb, imageLoad(indirectSpecularLight, pixel).rgb)",
              "? IndirectLight(imageLoad(indirectDiffuseLight, pixel).rgb, coarseSpecularAt(pixel))")
