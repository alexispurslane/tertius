// The scene environment maps' light, for the ambient source of lighting.glsl and the lit puffs
// (docs/plans/environment-map.md, "Reading the map", and docs/plans/environment-maps-per-frame.md, "The readers"). Each
// chain frame's map holds plain radiance in the axes of its frame, so every reader names its map and turns its direction
// into the map's axes first.
#ifndef TERTIUS_ENVIRONMENT_GLSL
#define TERTIUS_ENVIRONMENT_GLSL

#include "cube_faces.glsl"
#include "frame.glsl"
#include "sky.glsl"
#include "specular_cone.glsl"
#include "thickness.glsl"

// ShaderConstants.EnvironmentMap: true while the renderer's setting is SceneEnvironmentMap, fixed for its life.
layout(constant_id = 0) const bool environmentMapOn = false;

// Whether the ambient light skips the work of its terms that add nothing to the image (docs/plans/environment-map.md,
// "Cost"). A test build defines TERTIUS_EVERY_AMBIENT_TERM for its reference image, which computes every term
// (AmbientSkipsTests); the engine's build never does.
#ifdef TERTIUS_EVERY_AMBIENT_TERM
const bool skipsTermsThatAddNothing = false;
#else
const bool skipsTermsThatAddNothing = true;
#endif

// Whether the ambient specular term reads each map where its ray ends (docs/plans/environment-map-distances.md, "The
// search"). A test build defines TERTIUS_MAP_DIRECTION_ONLY for its reference image, which reads every map along d, as
// every reader did before the search (MapSearchRenderTests); the engine's build never does.
#ifdef TERTIUS_MAP_DIRECTION_ONLY
const bool readsMapsWhereRaysEnd = false;
#else
const bool readsMapsWhereRaysEnd = true;
#endif

// RGBA16F, a cube array whose cube i is map i: level k holds roughness k / (L − 1), alpha the open-sky share. Partially
// bound: empty with the map off.
layout(set = 0, binding = 12) uniform textureCubeArray environmentSpecularMap;
// Trilinear, clamped, every level.
layout(set = 0, binding = 13) uniform sampler environmentSampler;
// RGBA16F, a cube array whose cube i is map i's irradiance cube: level 0 holds D at each texel's direction, 16 texels on a
// side, alpha the open-sky share that a Lambert surface sees, and each later level the mean of its children over their
// solid angles, down to 2 texels on a side. Sampled with environmentSampler. Partially bound: empty with the map off.
layout(set = 0, binding = 14) uniform textureCubeArray environmentIrradianceMap;
// R16F, a cube array whose cube i is map i's distance cube (docs/plans/environment-map-distances.md, "The distance store"):
// level 0 holds w = 1000 m over the distance from the eye to the surface that the radiance map's texel shows, along the
// texel's direction, in km⁻¹, and 0 for the sky. Each later level holds the mean of its children over their solid angles,
// down to 1 texel on a side, as the radiance map's mips do. Sampled with environmentSampler. Partially bound: empty with
// the map off.
layout(set = 0, binding = 16) uniform textureCubeArray environmentDistanceMap;

// A world direction in the axes of map `map`: the inverse of R_i, frame.mapAxes[map], by the quaternion sandwich with the
// conjugate. The batcher keeps R_i a unit quaternion, so the turn keeps a unit vector unit, and the identity turns v to
// itself exactly, because its vector part is 0.
vec3 inMapAxes(uint map, vec3 v)
{
    vec4 axes = frame.mapAxes[map];
    vec3 u = -axes.xyz;
    return v + 2.0 * cross(u, cross(u, v) + axes.w * v);
}

// D(n): the ambient diffuse radiance of map `map` on a surface with the unit normal n, from level 0 of its irradiance cube
// at n, filtered across the cube's texels and faces.
vec3 environmentDiffuse(uint map, vec3 normal)
{
    return textureLod(
        samplerCubeArray(environmentIrradianceMap, environmentSampler),
        vec4(inMapAxes(map, normal), float(map)),
        0.0).rgb;
}

// Map `map`'s specular map along the lobe of roughness r, from 0 to 1, at the direction v in the map's axes, which need
// not be unit: the light in rgb and the open-sky share in a. The layer coordinate float(map) is an integer, which Vulkan
// rounds to itself.
vec4 environmentLobeInMapAxes(uint map, vec3 v, float roughness)
{
    float levels = float(textureQueryLevels(samplerCubeArray(environmentSpecularMap, environmentSampler)));
    return textureLod(samplerCubeArray(environmentSpecularMap, environmentSampler), vec4(v, float(map)),
        roughness * (levels - 1.0));
}

// environmentLobeInMapAxes at the unit world direction d.
vec4 environmentLobe(uint map, vec3 d, float roughness)
{
    return environmentLobeInMapAxes(map, inMapAxes(map, d), roughness);
}

// Where the ray from the shaded point `position` along the unit direction d first goes behind the stored surfaces of map
// `map` (docs/plans/environment-map-distances.md, "The search"). MapSearch.Hit is the C# twin. MapHit is its result.
struct MapHit
{
    bool found;       // false where the ray goes behind no stored surface
    vec3 direction;   // L(t*), the eye's direction to the hit in the map's axes, not unit; d in the map's axes without a hit
    float reach;      // ρ(t*): the hit's distance from position over its distance from the eye; 1 without a hit
};

// The march's settings and its guards: MapSearch.Samples, NearSample and SecantSteps, and its private constants.
const uint MapSearchSamples = 8u;           // N, the even samples, at t = k / N for k = 1 to N
const float MapSearchNearSample = 1.0 / 64.0; // t₀, the sweep of the first sample, about one level-0 texel from x at W = 128
const uint MapSearchSecantSteps = 3u;       // K, the regula falsi steps in the first bracket
const float ShortestLookup = 1.0 / 1024.0;  // a shorter L(t) passes through the eye's neighborhood, in front of every surface
const float KilometersPerMeter = 0.001;     // w is in km⁻¹ and the distances in m

// The ray x + s d in the sweep t, s = |x| t / (1 − t), in map `map`'s axes with the eye at the origin
// (docs/plans/environment-map-distances.md, "The ray in the map's sweep"). MapSearch.SweptRay is the C# twin.
struct SweptRay
{
    uint map;
    float distance;       // |x|, in m
    vec3 toPoint;         // x̂, the eye's unit direction to the shaded point
    vec3 direction;       // d
    float tanHalfAngle;   // tan θ_c of the lobe's cone (specular_cone.glsl)
    float texelAngle;     // 2 / W, a level-0 texel's angle at a face's center, in rad
    float lastLevel;      // log₂ W, the level of 1 × 1
};

// One sample of the march: its sweep t and its clearance c(t).
struct SweptSample
{
    float sweep;
    float clearance;
};

// L(t) = (1 − t) x̂ + t d: the eye's direction to the ray's point at the sweep t, not unit.
vec3 mapLookup(SweptRay ray, float sweep)
{
    precise vec3 lookup = (1.0 - sweep) * ray.toPoint + sweep * ray.direction;
    return lookup;
}

// c(t) = (1 − t) − 0.001 |x| |L(t)| w(L(t), λ(t)): above 0 where the ray's point at t lies in front of the stored surface
// in its direction, below 0 behind it. λ(t) is the level whose texel side is the cone's radius as the eye sees it,
// ρ(t) tan θ_c with ρ(t) = t / |L(t)|, by the level rule of SSR's coneLevel (coneLevelAt, ReflectionCone.Level).
float mapClearance(SweptRay ray, float sweep)
{
    precise vec3 lookup = mapLookup(ray, sweep);
    precise float lookupLength = length(lookup);
    // In front, with no read: the limit of c as |L| goes to 0, because w is bounded, and a read along 0 is undefined.
    if (lookupLength < ShortestLookup)
    {
        precise float open = 1.0 - sweep;
        return open;
    }
    float level = coneLevelAt(ray.tanHalfAngle, sweep, lookupLength, ray.texelAngle, ray.lastLevel);
    float inverseDistance = textureLod(
        samplerCubeArray(environmentDistanceMap, environmentSampler), vec4(lookup, float(ray.map)), level).r;
    precise float clearance = 1.0 - sweep - KilometersPerMeter * ray.distance * lookupLength * inverseDistance;
    return clearance;
}

// Where the line through a bracket's two ends crosses c = 0: tA + (tB − tA) cA / (cA − cB). The front's cA ≥ 0 > cB, the
// back's, so the divisor is above 0.
float secantPlace(SweptSample front, SweptSample back)
{
    precise float place =
        front.sweep + (back.sweep - front.sweep) * front.clearance / (front.clearance - back.clearance);
    return place;
}

// The hit at the sweep t, with no read: its direction L(t) and its reach t / |L(t)|.
MapHit mapHitAt(SweptRay ray, float sweep)
{
    precise vec3 lookup = mapLookup(ray, sweep);
    // The floor keeps the division finite where the hit lies at the eye, which a ray through the eye can approach.
    precise float reach = sweep / max(length(lookup), ShortestLookup);
    return MapHit(true, lookup, reach);
}

// The hit in the bracket from `front` to `back`: after K regula falsi steps, which always run so that each pixel's reads
// have one bound, the last secant place, with no read.
MapHit refinedMapHit(SweptRay ray, SweptSample front, SweptSample back)
{
    for (uint refinement = 0u; refinement < MapSearchSecantSteps; refinement++)
    {
        precise float sweep = secantPlace(front, back);
        SweptSample next = SweptSample(sweep, mapClearance(ray, sweep));
        if (next.clearance >= 0.0)
            front = next;
        else
            back = next;
    }
    return mapHitAt(ray, secantPlace(front, back));
}

// position is relative to the eye, in m, and it, d and geometricNormal, the unit normal n_g of the shaded point's
// geometric surface, are in world axes; position is not at the eye. A ray with d · n_g ≤ 0 points into that surface and
// finds no hit, as SSR's trace takes no such ray (docs/plans/skin-and-hair.md, decision 35). alpha, the floored roughness
// squared, sets the lobe's cone, which chooses the level of each read. The search reads the distance
// cube at most N + K + 1 = 12 times. Its arithmetic is precise, so that the ambient pass, a fragment shader, and the
// reflections' delta, a compute shader, decide alike ("The same bits in a fragment and a compute stage").
MapHit mapHit(uint map, vec3 position, vec3 d, vec3 geometricNormal, float alpha)
{
    // A ray that points into its own surface, as a normal map can turn d, would find that surface at once, which no
    // sample can see (docs/plans/environment-map-distances.md, "Error cases"). d is today's read, bit for bit.
    if (dot(d, geometricNormal) <= 0.0)
        return MapHit(false, d, 1.0);
    SweptRay ray;
    ray.map = map;
    precise vec3 x = inMapAxes(map, position);
    ray.distance = length(x);
    precise vec3 toPoint = x / ray.distance;
    ray.toPoint = toPoint;
    ray.direction = inMapAxes(map, d);
    ray.tanHalfAngle = lobeConeTangent(alpha);
    // The cube has log₂ W + 1 levels, so 2 / W = 2^(1 − log₂ W), which ldexp gives exactly.
    int lastLevel = textureQueryLevels(samplerCubeArray(environmentDistanceMap, environmentSampler)) - 1;
    ray.lastLevel = float(lastLevel);
    ray.texelAngle = ldexp(1.0, 1 - lastLevel);
    // The last sample in front: none before the first one. Front first: a sample behind counts only after a sample in
    // front. The first bracket holds the first crossing, and later samples cannot change it.
    bool seenFront = false;
    SweptSample front = SweptSample(0.0, 0.0);
    // Whether the near sample lies inside the thickness of its stored surface: behind it, by at most the share κ of the
    // surface's distance D that a surface fills behind it, as for SSR's trace (thickness.glsl). The clearance is (1 − t)(1 − |p| / D) for
    // the point p, so c + κ (1 − t) ≥ 0 means |p| ≤ (1 + κ) D.
    bool nearInSlab = false;
    for (uint k = 0u; k <= MapSearchSamples; k++)
    {
        // k / N by a product with the exact 1 / N, because a division may round on the device.
        float sweep = k == 0u ? MapSearchNearSample : float(k) * (1.0 / float(MapSearchSamples));
        SweptSample next = SweptSample(sweep, mapClearance(ray, sweep));
        // The near entry: a near sample just behind its surface and a first even sample behind too mean that the ray
        // entered a surface nearer than the near sample. A shaded point that the filter puts a little behind its own
        // surface has its first even sample in front, and the point beside a nearer object lies beyond the slab.
        if (k == 1u && nearInSlab && next.clearance < 0.0)
            return mapHitAt(ray, MapSearchNearSample);
        if (k == 0u)
        {
            precise float slabBack = next.clearance + ThicknessShare * (1.0 - sweep);
            nearInSlab = next.clearance < 0.0 && slabBack >= 0.0;
        }
        // A sample with c = 0 counts as in front, so the sky at t = 1 is never a crossing.
        if (next.clearance >= 0.0)
        {
            front = next;
            seenFront = true;
        }
        else if (seenFront)
            return refinedMapHit(ray, front, next);
    }
    return MapHit(false, ray.direction, 1.0);
}

// S at the ray's end: map `map`'s specular light that reaches the shaded point `position` along the lobe of the floored
// roughness r about the unit direction d, plus the reflected stars at d for the footprint f, times the open-sky share
// around the hit (docs/plans/environment-map-distances.md, "The usage code"). A ray that meets no stored surface reads
// the map at d with r, exactly as before the search. The stars stay in world axes at d, because they lie at infinity.
vec3 environmentSpecular(uint map, vec3 position, vec3 d, vec3 geometricNormal, float roughness, float footprint)
{
    MapHit hit = readsMapsWhereRaysEnd
        ? mapHit(map, position, d, geometricNormal, roughness * roughness)
        : MapHit(false, d, 1.0);
    // r √ρ* at most 1: the lobe's footprint at the hit as the eye sees it ("A rough lobe"). MapHit.ReadRoughness in C#.
    vec4 lobe = hit.found
        ? environmentLobeInMapAxes(map, hit.direction, min(roughness * sqrt(hit.reach), 1.0))
        : environmentLobe(map, d, roughness);
    // Where the lobe sees no open sky, its share is 0, and 0 times the reflected stars, which are finite, is a zero,
    // whose sum with lobe.rgb equals lobe.rgb, so the stars' cells are not hashed.
    if (skipsTermsThatAddNothing && lobe.a == 0.0)
        return lobe.rgb;
    return lobe.rgb + lobe.a * reflectedStars(d, starWidth(footprint, roughness * roughness));
}

// α of the specular map's lobe per rad of the width of the part of the sky that a read stands for: GgxLobe.AlphaPerWidth.
const float LobeAlphaPerWidth = 0.12108;

// The lower bound on the roughness of a read for a part, GgxLobe.MinPieceRoughness: a sharper read lets a small bright
// part of the map show in some texels and not in their neighbors (docs/plans/screen-space-illumination.md, decision 42).
const float MinPieceRoughness = 0.5;

// The roughness of the specular map's lobe that blurs a read over a part of the sky `width` rad wide:
// max(sqrt(LobeAlphaPerWidth · width), MinPieceRoughness) (docs/plans/screen-space-illumination.md, decisions 41 and
// 42). Only a part wider than 2.07 rad keeps 95 % of the lobe's weight on itself. A narrower part takes the bound's
// lobe, which puts up to 11 % of its weight beyond either end of a part 0.95 rad wide and 24 % beyond a part 0.5 rad
// wide. GgxLobe.RoughnessForWidth is the C# copy.
float roughnessForWidth(float width)
{
    return max(sqrt(LobeAlphaPerWidth * width), MinPieceRoughness);
}

// The mean of map `map`'s radiance over every direction, without the stars: the lit puffs' ambient light. It is the mean
// of D over every direction, which the six faces of the irradiance cube's last level hold, a face's mean at its center:
// there the four texels of a face of 2 texels meet, so the filter weighs each by a quarter, and each spans a quarter of
// the face. The mean over the sphere does not depend on the map's axes, so the reads stay in them.
vec3 environmentMeanRadiance(uint map)
{
    float last = float(textureQueryLevels(samplerCubeArray(environmentIrradianceMap, environmentSampler)) - 1);
    vec3 sum = vec3(0.0);
    for (uint face = 0u; face < 6u; face++)
        sum += textureLod(
            samplerCubeArray(environmentIrradianceMap, environmentSampler),
            vec4(faceBasis(face).forward, float(map)),
            last).rgb;
    return sum / 6.0;
}

#endif
