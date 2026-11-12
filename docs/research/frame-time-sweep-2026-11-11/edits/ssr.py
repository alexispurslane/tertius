# The resolve of Stachowiak 2015 as a prototype: a pixel that shares takes the reflection rays of several blocks around
# it, each weighted by the pixel's own BRDF over the ray's density, and divides by the sum of the weights. argv[2] is 4
# (the 2 x 2 nearest blocks) or 9 (the 3 x 3 blocks).
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; count = sys.argv[2]; common.envexp(root)
p = root + common.SH + 'screen_trace_resolve.comp'
old = """        RayRecord reflection = recordAt(pixel, KindReflection, plan);
        WeighedRay reflectionRay = weighedRayOf(p, reflection);
        if (reflectionRay.brings)
        {
"""
new = """        RayRecord reflection = recordAt(pixel, KindReflection, plan);
        WeighedRay reflectionRay = weighedRayOf(p, reflection);
        if ((plan & PlanOwnReflection) == 0u)
        {
            // The prototype: the rays of the blocks around the pixel, in a ratio estimator.
            ivec2 block = pixel / 2;
            ivec2 blocks = ivec2(ceil(frame.viewport.xy * 0.5));
            SurfaceKey key = surfaceKeyAt(pixel);
            vec3 reusedLight = vec3(0.0);
            float reusedWeight = 0.0;
#if REUSE == 4 || REUSE == 1
            // REUSE 1: the block's own ray alone, so the estimate is the ray's light without a weight.
            ivec2 side = ivec2((pixel.x & 1) == 1 ? 1 : -1, (pixel.y & 1) == 1 ? 1 : -1);
            for (int j = 0; j <= (REUSE == 4 ? 1 : 0); j++)
                for (int i = 0; i <= (REUSE == 4 ? 1 : 0); i++)
                {
                    ivec2 other = block + ivec2(i, j) * side;
#else
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    ivec2 other = block + ivec2(i, j);
#endif
                    if (any(lessThan(other, ivec2(0))) || any(greaterThanEqual(other, blocks)))
                        continue;
                    if (i != 0 || j != 0)
                    {
                        // The other block's first pixel of its turn stands for the pixel that traced its ray.
                        uint corner = (trace.drawIndex + drawOf(other, 0u, TurnDraw[KindReflection]).x) & 3u;
                        ivec2 origin = 2 * other + ivec2(corner & 1u, corner >> 1);
                        if (any(greaterThanEqual(vec2(origin), frame.viewport.xy)) || !sameSurface(key, surfaceKeyAt(origin)))
                            continue;
                    }
                    RayRecord record;
                    record.light = imageLoad(blockLight, ivec3(other, KindReflection));
                    record.ray = imageLoad(blockRay, ivec3(other, KindReflection));
                    WeighedRay ray = weighedRayOf(p, record);
#if HITPOINT
                    // Stachowiak 2015 reuses the hit point: the direction from this pixel to the point that the other
                    // block's ray hit. A miss has no point, so it keeps its direction.
                    if (ray.brings && record.light.a > 0.0)
                    {
                        uint turnCorner = (trace.drawIndex + drawOf(other, 0u, TurnDraw[KindReflection]).x) & 3u;
                        ivec2 from = min(2 * other + ivec2(turnCorner & 1u, turnCorner >> 1), ivec2(frame.viewport.xy) - 1);
                        vec3 fromPoint = viewRayThrough(vec2(from) + 0.5) * surfaceKeyAt(from).depth;
                        ray.direction = normalize(fromPoint + ray.direction * record.light.a - p.point);
                        ray.brings = dot(ray.direction, p.s.geometricNormal) > 0.0;
                    }
#endif
                    if (!ray.brings)
                        continue;
                    vec3 response = p.s.model == ShadingHair ? hairResponse(p.s, lobes, p.V, ray.direction).specular
                        : specularResponse(lobes, p.s.normal, p.V, ray.direction, 0.0);
                    float weight = dot(response / (ray.density * max(factors.specular, vec3(LeastFactor))), vec3(1.0 / 3.0));
                    if (!(weight > 0.0) || isinf(weight))
                        continue;
                    reusedLight += record.light.rgb * weight;
                    reusedWeight += weight;
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
                    uint reusedCode = ageCodeOfRecord(record.ray);
                    if (handOver < 1.0 && reusedCode != 0u)
                        ageCode = ageCode == 0u ? reusedCode : min(ageCode, reusedCode);
#endif
                }
            if (reusedWeight > 0.0)
                specularLight += (1.0 - handOver) * reusedLight / reusedWeight;
        }
        else if (reflectionRay.brings)
        {
"""
common.sub(p, old, new)
common.sub(p, "layout(local_size_x = 8, local_size_y = 8) in;\n", "layout(local_size_x = 8, local_size_y = 8) in;\n#define REUSE " + count + "\n#define HITPOINT " + ("1" if len(sys.argv) > 3 and sys.argv[3] == "hit" else "0") + "\n")
