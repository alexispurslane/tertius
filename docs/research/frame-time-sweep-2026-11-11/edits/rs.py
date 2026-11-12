# The resolve study, second round: a pixel that shares takes the reflection rays of 1 or 4 blocks, each through the
# point that the ray hit. argv: <root> <count 1|4> <weight ratio|own> <rays all|half>.
#   ratio: Stachowiak 2015, weight = the pixel's response over the ray's stored density, divided by the sum of weights.
#   own:   each ray counts as the pixel's own ray: its density is the pixel's own density along the ray, so the weight is
#          the smooth F G2 / (G1 E), and the sample is the plain mean over the rays that traced.
#   half:  a block's ray counts only on every second draw, in a checkerboard of blocks: the image of half the rays. The
#          rays are still traced, so this build says nothing about time.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root, count, weight, rays = sys.argv[1:5]; common.envexp(root)
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
            ivec2 block = pixel / 2;
            ivec2 blocks = ivec2(ceil(frame.viewport.xy * 0.5));
            SurfaceKey key = surfaceKeyAt(pixel);
            vec3 reusedLight = vec3(0.0);
            float reusedWeight = 0.0;
            float reusedCount = 0.0;
            ivec2 side = ivec2((pixel.x & 1) == 1 ? 1 : -1, (pixel.y & 1) == 1 ? 1 : -1);
            for (int j = 0; j <= SPAN; j++)
                for (int i = 0; i <= SPAN; i++)
                {
                    ivec2 other = block + ivec2(i, j) * side;
                    if (any(lessThan(other, ivec2(0))) || any(greaterThanEqual(other, blocks)))
                        continue;
#if HALF
                    if (((uint(other.x + other.y) + trace.drawIndex) & 1u) != 0u)
                        continue;
#endif
                    // The pixel of the other block that traced its ray in this draw.
                    uint corner = (trace.drawIndex + drawOf(other, 0u, TurnDraw[KindReflection]).x) & 3u;
                    ivec2 origin = min(2 * other + ivec2(corner & 1u, corner >> 1), ivec2(frame.viewport.xy) - 1);
                    if ((i != 0 || j != 0) && !sameSurface(key, surfaceKeyAt(origin)))
                        continue;
                    RayRecord record;
                    record.light = imageLoad(blockLight, ivec3(other, KindReflection));
                    record.ray = imageLoad(blockRay, ivec3(other, KindReflection));
                    if ((record.ray.w & RayTraced) == 0u)
                        continue;
                    reusedCount += 1.0;
                    WeighedRay ray = weighedRayOf(p, record);
                    // The direction from this pixel to the point that the ray hit. A miss keeps its direction.
                    if (ray.brings && record.light.a > 0.0)
                    {
                        vec3 fromPoint = viewRayThrough(vec2(origin) + 0.5) * surfaceKeyAt(origin).depth;
                        ray.direction = normalize(fromPoint + ray.direction * record.light.a - p.point);
                        ray.brings = dot(ray.direction, p.s.geometricNormal) > 0.0;
                    }
                    if (!ray.brings)
                        continue;
#if OWNWEIGHT
                    ray.density = reflectionDensity(p, ray.direction);
                    if (!(ray.density > 0.0))
                        continue;
                    reusedLight += record.light.rgb * specularWeightOf(p, lobes, factors, ray);
#else
                    vec3 response = p.s.model == ShadingHair ? hairResponse(p.s, lobes, p.V, ray.direction).specular
                        : specularResponse(lobes, p.s.normal, p.V, ray.direction, 0.0);
                    float weight = dot(response / (ray.density * max(factors.specular, vec3(LeastFactor))), vec3(1.0 / 3.0));
                    if (!(weight > 0.0) || isinf(weight))
                        continue;
                    reusedLight += record.light.rgb * weight;
                    reusedWeight += weight;
#endif
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
                    uint reusedCode = ageCodeOfRecord(record.ray);
                    if (handOver < 1.0 && reusedCode != 0u)
                        ageCode = ageCode == 0u ? reusedCode : min(ageCode, reusedCode);
#endif
                }
#if OWNWEIGHT
            if (reusedCount > 0.0)
                specularLight += (1.0 - handOver) * reusedLight / reusedCount;
#else
            if (reusedWeight > 0.0)
                specularLight += (1.0 - handOver) * reusedLight / reusedWeight;
#endif
        }
        else if (reflectionRay.brings)
        {
"""
common.sub(p, old, new)
defines = f"#define SPAN {1 if count == '4' else 0}\n#define OWNWEIGHT {1 if weight == 'own' else 0}\n#define HALF {1 if rays == 'half' else 0}\n"
common.sub(p, "layout(local_size_x = 8, local_size_y = 8) in;\n", "layout(local_size_x = 8, local_size_y = 8) in;\n" + defines)
