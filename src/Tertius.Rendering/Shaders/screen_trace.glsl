// The screen tracer (docs/plans/temporal-renderer.md, section 6): the one owner of which pixels start a ray of each kind,
// of a ray's start, its direction and density, its random numbers, its hit point, the light of a hit and of a miss, and
// the block records. Its four kernels include it: screen_trace_plan.comp, screen_trace_block_rays.comp and
// screen_trace_own_rays.comp (each of both kinds, by a specialization constant) and screen_trace_resolve.comp. Set 3 is
// the tracer set (DescriptorLayouts.ScreenTraceSet, ScreenTraceImages): every image holds the render size, the block
// records a quarter of it, and each stays in the General layout.
#ifndef TERTIUS_SCREEN_TRACE_GLSL
#define TERTIUS_SCREEN_TRACE_GLSL

#extension GL_GOOGLE_include_directive : require
#extension GL_EXT_samplerless_texture_functions : require
#include "frame.glsl"
#include "gbuffer_surface.glsl"
#include "temporal.glsl"
#include "signals.glsl"
#include "screen_walk.glsl"
#include "own_rays.glsl"

layout(set = 3, binding = 0) uniform texture2D depthFinest;                    // R32F: each pixel's center w, 0 for sky
layout(set = 3, binding = 1) uniform texture2D depthPyramid;                   // RG32F, mip m = level m + 1
layout(set = 3, binding = 2, rgba16f) uniform readonly image2D directDiffuseLight;   // demodulated and pre-exposed
layout(set = 3, binding = 3, rgba16f) uniform readonly image2D directSpecularLight;
layout(set = 3, binding = 4, r8ui) uniform readonly uimage2D surfaceTest;      // the pass bits (signals.glsl)
layout(set = 3, binding = 5, rgba16f) uniform readonly image2D motionTarget;   // the G-buffer's motion target
layout(set = 3, binding = 6, rgba16f) uniform readonly image2D lastFastDiffuse;      // the last frame's fast histories
layout(set = 3, binding = 7, rgba16f) uniform readonly image2D lastFastSpecular;
layout(set = 3, binding = 8, rgba16f) uniform readonly image2D slowDiffuse;    // the last frame's slow histories: a the
layout(set = 3, binding = 9, rgba16f) uniform readonly image2D slowSpecular;   // history length in frames
layout(set = 3, binding = 10, r8ui) uniform uimage2D rayPlan;                  // the plan's bits (PlanOwnRay)
// The records of the rays, layer KindReflection and KindDiffuse: rgb the ray's light, pre-exposed, a its hit distance in
// m, 0 for a miss; and x, y the direction's octahedral code in 16-bit units, z the half bits of log2 of its density per
// steradian, w RayTraced. A block's record at the block, an own ray's at its pixel.
layout(set = 3, binding = 11, rgba16f) uniform image2DArray blockLight;
layout(set = 3, binding = 12, rgba16ui) uniform uimage2DArray blockRay;
layout(set = 3, binding = 13, rgba16f) uniform image2DArray ownLight;
layout(set = 3, binding = 14, rgba16ui) uniform uimage2DArray ownRay;
// The two samples of the frame, which the resolve writes (TemporalSignal.Sample): rgb the demodulated sample, a 1 for the
// diffuse sample and the hit distance for the specular sample.
layout(set = 3, binding = 15, rgba16f) uniform writeonly image2D diffuseSample;
layout(set = 3, binding = 16, rgba16f) uniform writeonly image2D specularSample;
OWN_RAY_LIST(3, 17, reflectionOwnRays);
OWN_RAY_LIST(3, 18, diffuseOwnRays);

HISTORY_READER(lastFastDiffuseAt, lastFastDiffuse)
HISTORY_READER(lastFastSpecularAt, lastFastSpecular)
HISTORY_READER(slowDiffuseAt, slowDiffuse)
HISTORY_READER(slowSpecularAt, slowSpecular)

// ScreenTracePushConstants.
layout(push_constant, std430) uniform ScreenTracePush
{
    uint drawIndex;          // the draw's index since the last reset (section 6, "Randomness")
    float maxRoughness;      // ScreenTrace.MaxRoughness: above it, a pixel traces no reflection ray
    float handOverStart;     // MaxRoughness − HandOverWidth: from it, the specular sample blends in the diffuse rays
    uint reflectionSteps;    // the caps of decision 41
    uint diffuseSteps;
} trace;

// The two ray kinds, which index the record layers and the constants of the draws.
const uint KindReflection = 0u;
const uint KindDiffuse = 1u;

// The plan's bits at a pixel: whether it traces its own ray of each kind, bit 0 for a reflection ray and bit 1 for a
// diffuse ray.
const uint PlanOwnReflection = 1u;
const uint PlanOwnDiffuse = 2u;

// A record's w: whether its ray traced. A ray below its pixel's geometric plane, and a block that no pixel starts, trace
// nothing.
const uint RayTraced = 1u;

// A pixel traces its own ray of a kind while its history of that kind is shorter than this (section 6, "The ray budget").
const float FullRateFrames = 8.0;

// The largest weight of a ray at a pixel, which trades a small bias for no fireflies (section 6, "The resolve").
const float MostRayWeight = 4.0;

// The least cosine of a block ray at its chosen pixel, 2^-14, by which the resolve divides.
const float LeastRayCosine = 0.00006103515625;

// The constants of pcg3d's third value, one per kind and use: a direction takes two dimensions from one draw, its x and y.
const uint DirectionDraw[2] = uint[2](0x5245u, 0x4446u);   // "RE", "DF"
const uint TurnDraw[2] = uint[2](0x5254u, 0x4454u);        // the block's turn

// Jarzynski and Olano 2020, "Hash Functions for GPU Rendering": three 32-bit words from three.
uvec3 pcg3d(uvec3 v)
{
    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    return v;
}

// The draw of a pixel or a block: pcg3d of its packed position, the draw index and a constant per kind and use.
uvec3 drawOf(ivec2 position, uint drawIndex, uint constant)
{
    return pcg3d(uvec3(packedPixel(position), drawIndex, constant));
}

// A word as a float in [0, 1), from its 24 high bits, which a float holds exactly.
float unitOf(uint word)
{
    return float(word >> 8) * (1.0 / 16777216.0);
}

// A pixel that starts a ray, and what its rays need.
struct TracingPixel
{
    ShadingInput s;
    vec3 normal;            // the normal of the rays' lobes: the shading normal, a hair pixel's fiber normal
    vec3 V;                 // toward the eye
    vec3 point;             // the surface point, camera-relative
    vec3 origin;            // the rays' start: the surface point off the geometric surface, as today's trace starts
    float viewDepth;
    float alpha;            // the floored roughness squared
    uint ownCard;           // a hair pixel's card code, NoCard for another model
};

// Whether the pixel holds a lit surface whose rays the tracer traces: not sky and not an impostor, whose light the atlas
// holds.
bool tracedSurface(ivec2 pixel)
{
    return texelFetch(gbufferDistance, pixel, 0).r > 0.0 && texelFetch(gbufferBaseColor, pixel, 0).a != 0.0;
}

// The pixel's surface for its rays. The pixel must hold a traced surface.
TracingPixel tracingPixelAt(ivec2 pixel)
{
    TracingPixel p;
    p.s = materialAt(pixel);
    vec3 ray = viewRayThrough(vec2(pixel) + 0.5);
    vec3 toSurface = normalize(ray);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    p.V = -toSurface;
    p.normal = p.s.model == ShadingHair ? fiberNormal(p.s.strand, p.V, p.s.normal) : p.s.normal;
    p.viewDepth = distanceToEye / length(ray);
    p.point = toSurface * distanceToEye;
    p.origin = p.point + p.s.geometricNormal * originOffset(p.viewDepth);
    float roughness = flooredRoughness(p.s.roughness);
    p.alpha = roughness * roughness;
    uvec4 shading = texelFetch(gbufferShading, pixel, 0);
    p.ownCard = cardOfWords(shading.b, shading.a);
    return p;
}

// Whether a pixel of a traced surface starts a ray of the kind: every one starts a diffuse ray, and one of a floored
// roughness up to MaxRoughness a reflection ray.
bool startsKind(TracingPixel p, uint kind)
{
    return kind == KindDiffuse || sqrt(p.alpha) <= trace.maxRoughness;
}

// The block's chosen pixel for the kind: the first pixel of the block's turn that starts the ray (section 6, "The ray
// budget"), or false when none does. The plan, the block's ray and the resolve call it, so all three choose the same
// pixel.
bool chosenPixelOf(ivec2 block, uint kind, out ivec2 chosen, out TracingPixel p)
{
    uint turn = trace.drawIndex + drawOf(block, 0u, TurnDraw[kind]).x;
    for (uint k = 0u; k < 4u; k++)
    {
        uint corner = (turn + k) & 3u;
        chosen = 2 * block + ivec2(corner & 1u, corner >> 1);
        if (all(lessThan(vec2(chosen), frame.viewport.xy)) && tracedSurface(chosen))
        {
            p = tracingPixelAt(chosen);
            if (startsKind(p, kind))
                return true;
        }
    }
    return false;
}

// The unit tangent and bitangent of a unit normal (Duff et al. 2017), with no branch on the pole.
void basisOf(vec3 n, out vec3 tangent, out vec3 bitangent)
{
    float s = n.z >= 0.0 ? 1.0 : -1.0;
    float a = -1.0 / (s + n.z);
    float b = n.x * n.y * a;
    tangent = vec3(1.0 + s * n.x * n.x * a, s * b, -s * n.x);
    bitangent = vec3(b, s + n.y * n.y * a, -n.y);
}

// The density per steradian of a reflection ray along `direction` from the pixel: GGX's visible normals at the pixel's
// alpha, D(h) G1(v) / (4 n·v), with n·v floored at MinNdotV.
float reflectionDensity(TracingPixel p, vec3 direction)
{
    float nv = max(dot(p.normal, p.V), MinNdotV);
    vec3 h = halfVectorOf(direction, p.V, p.normal);
    float alphaSq = p.alpha * p.alpha;
    float lambdaPart = nv + sqrt(alphaSq + (1.0 - alphaSq) * nv * nv);
    return ggxDistribution(p.normal, h, p.alpha) / (2.0 * lambdaPart);
}

// A reflection ray: the view direction reflected about a half vector from GGX's visible normals at alpha, sampled by
// spherical caps (Dupuy and Benyoub 2023).
vec3 reflectionDirection(TracingPixel p, vec2 u)
{
    vec3 tangent, bitangent;
    basisOf(p.normal, tangent, bitangent);
    // A normal map can turn the shading normal from the eye: the floor keeps the sample in the upper hemisphere.
    float nv = max(dot(p.normal, p.V), MinNdotV);
    vec3 v = normalize(vec3(dot(p.V, tangent), dot(p.V, bitangent), nv));
    vec3 stretched = normalize(vec3(v.xy * p.alpha, v.z));
    float phi = 2.0 * PI * u.x;
    float z = (1.0 - u.y) * (1.0 + stretched.z) - stretched.z;
    float sine = sqrt(clamp(1.0 - z * z, 0.0, 1.0));
    vec3 cap = vec3(sine * cos(phi), sine * sin(phi), z) + stretched;
    vec3 h = normalize(vec3(cap.xy * p.alpha, max(cap.z, 0.0)));
    return reflect(-p.V, h.x * tangent + h.y * bitangent + h.z * p.normal);
}

// A diffuse ray: cosine-weighted around the lobes' normal, of density cos θ / π.
vec3 diffuseDirection(TracingPixel p, vec2 u)
{
    vec3 tangent, bitangent;
    basisOf(p.normal, tangent, bitangent);
    float radius = sqrt(u.x);
    float phi = 2.0 * PI * u.y;
    float cosine = sqrt(max(1.0 - u.x, 0.0));
    return radius * cos(phi) * tangent + radius * sin(phi) * bitangent + cosine * p.normal;
}

// The ray's density per steradian along `direction`, for the kind.
float densityOf(TracingPixel p, uint kind, vec3 direction)
{
    return kind == KindReflection ? reflectionDensity(p, direction) : max(dot(p.normal, direction), 0.0) / PI;
}

// The light of a miss along `direction` from the pixel, pre-exposed (section 6, "Misses"): with the map, its lobe at
// roughness 0.5 for a diffuse ray, and its specular term at the pixel's roughness and footprint for a reflection ray.
// Without the map, the hemisphere has no direction at a pixel, so a miss reads the pixel's closed form, the hemisphere
// by the normal's or the lobe's sky share.
vec3 missLightOf(TracingPixel p, ivec2 pixel, uint kind, vec3 direction)
{
    vec3 light;
    if (environmentMapOn)
        light = kind == KindDiffuse ? environmentLobe(p.s.map, direction, 0.5).rgb
            : environmentSpecular(p.s.map, direction, sqrt(p.alpha),
                withAmbientFootprint(p.s, pixel).reflectionFootprint);
    else
        light = mix(frame.ambientGround.rgb, frame.ambientSky.rgb,
            kind == KindDiffuse ? p.s.skyFraction : p.s.lobeSkyFraction);
    return exposed(light, exposureScale());
}

// The last frame's fast history in `history` at a reprojection that holds one, rescaled to its weights and into this
// frame's exposure.
vec3 lastFastOf(Reprojection r, uint kind)
{
    vec3 fast = kind == KindDiffuse ? lastFastDiffuseAt(r).rgb : lastFastSpecularAt(r).rgb;
    // As the accumulator rescales its histories (accumulate_temporal.comp): within 2^±64 and at most MaxRadiance.
    return exposed(fast, historyRatio(exposureStops, previousStops));
}

// The pixel's place in the last frame's image, from its motion.
vec2 lastPlaceAt(ivec2 pixel)
{
    return (vec2(pixel) + 0.5) * frame.viewport.zw + imageLoad(motionTarget, pixel).xy;
}

// The light of a hit at the pixel q, pre-exposed (section 6, "Where a hit's light comes from"): this frame's direct
// light and the last frame's fast histories at q's place, times q's material factors, and the glow that q sends. A diffuse
// ray leaves out q's specular part. Where q fails the surface test, and so in a reset frame, a history is the
// environment's closed form at q. An impostor's light is its emission, which holds its ambient light.
vec3 hitLightOf(ivec2 q, uint kind)
{
    vec3 emissive = texelFetch(gbufferEmissive, q, 0).rgb;
    vec3 glow = glowSent(emissive, lightMarkOfWord(texelFetch(gbufferShading, q, 0).b));
    if (texelFetch(gbufferBaseColor, q, 0).a == 0.0)
        return glow;
    ShadingInput s = materialAt(q);
    vec3 V = -normalize(viewRayThrough(vec2(q) + 0.5));
    MaterialFactors factors = materialFactors(s, lobesOf(s, V));
    uint bits = imageLoad(surfaceTest, q).r;
    vec2 place = lastPlaceAt(q);
    Reprojection diffuse = reprojectionOf(place, bits & 0xFu);
    Reprojection specular = reprojectionOf(place, (bits >> 4) & 0xFu);
    bool closed = !holdsHistory(diffuse) || kind == KindReflection && !holdsHistory(specular);
    IndirectLight closedLight = IndirectLight(vec3(0.0), vec3(0.0));
    if (closed)
        closedLight = closedForm(withAmbientFootprint(s, q), V);
    vec3 diffuseHistory = holdsHistory(diffuse) ? lastFastOf(diffuse, KindDiffuse) : closedLight.diffuse;
    vec3 light = factors.diffuse * (imageLoad(directDiffuseLight, q).rgb + diffuseHistory) + glow;
    if (kind == KindReflection)
    {
        vec3 specularHistory = holdsHistory(specular) ? lastFastOf(specular, KindReflection) : closedLight.specular;
        light += factors.specular * (imageLoad(directSpecularLight, q).rgb + specularHistory);
    }
    return min(light, vec3(MaxRadiance));
}

// The 16-bit units of a unit direction's octahedral code.
uvec2 directionCodeOf(vec3 direction)
{
    return uvec2(clamp(octEncode(direction) * 0.5 + 0.5, 0.0, 1.0) * 65535.0 + 0.5);
}

vec3 directionOfCode(uvec2 code)
{
    return octDecode(vec2(code) * (2.0 / 65535.0) - 1.0);
}

// The result of one ray.
struct TracedRay
{
    vec3 direction;
    float density;
    vec3 light;       // the hit's light faded into the miss's, pre-exposed; 0 for a ray that traced nothing
    float distance;   // from the ray's start to the hit, in m; 0 for a miss
    bool traced;      // false for a direction below the pixel's geometric plane, which brings no light
};

// Traces the ray of the kind from the pixel that starts it: its draw, its walk to the kind's cap, and its light. A
// direction below the geometric plane brings no light and traces nothing (section 6, "The ray kinds").
TracedRay traceFrom(ivec2 pixel, TracingPixel p, uint kind)
{
    TracedRay result;
    uvec3 draw = drawOf(pixel, trace.drawIndex, DirectionDraw[kind]);
    vec2 u = vec2(unitOf(draw.x), unitOf(draw.y));
    vec3 drawn = kind == KindReflection ? reflectionDirection(p, u) : diffuseDirection(p, u);
    // The record holds the direction's code, and the resolve weighs the ray by the code's direction. So the ray walks
    // that direction, and its density is that direction's: at the roughness floor, the rounding of the code alone moves
    // D(h) by several percent.
    result.direction = directionOfCode(directionCodeOf(drawn));
    result.density = densityOf(p, kind, result.direction);
    result.light = vec3(0.0);
    result.distance = 0.0;
    result.traced = dot(result.direction, p.s.geometricNormal) > 0.0;
    if (!result.traced)
        return result;
    ScreenWalk walk = screenWalkOf(screenRayOf(p.origin, result.direction, p.viewDepth), result.direction, p.ownCard);
    vec2 hit;
    ivec2 hitPixel;
    float entryShare;
    uint steps = kind == KindReflection ? trace.reflectionSteps : trace.diffuseSteps;
    vec3 missLight = missLightOf(p, pixel, kind, result.direction);
    result.light = missLight;
    if (walkScreen(depthFinest, depthPyramid, walk, steps, hit, hitPixel, entryShare) != WalkHit)
        return result;
    // The hit's surface point from its own distance, not from the ray's inverse depth, which is coarse far away.
    vec3 hitPoint = normalize(viewRayThrough(hit)) * texelFetch(gbufferDistance, hitPixel, 0).r;
    result.distance = length(hitPoint - p.origin);
    float fade = edgeFade(hit) * (1.0 - entryShare);
    result.light = mix(missLight, hitLightOf(hitPixel, kind), fade);
    return result;
}

// The half bits of log2 of a density: a reflection ray's density passes 65,504, the largest half float, at the roughness
// floor below an n·v of about 0.3, and its logarithm stays near 17 there. A density below LeastDensity, such as a diffuse
// ray along its pixel's plane, codes as ZeroDensityCode, which decodes to exactly 0. Both ends test for it: the Vulkan
// spec leaves log2 of 0 undefined, and exp2's error at −65,504 allows a denormal, which the resolve's weights would
// divide by. A hair pixel's diffuse ray can have a density of 0 above its card, where hairResponse is not 0.
const float LeastDensity = 7.888609052210118e-31;  // 2^-100, a normal float
const uint ZeroDensityCode = 0xFBFFu;              // the half bits of −65,504, the least half

uint densityCodeOf(float density)
{
    return density >= LeastDensity ? packHalf2x16(vec2(log2(density), 0.0)) & 0xffffu : ZeroDensityCode;
}

float densityOfCode(uint code)
{
    return code == ZeroDensityCode ? 0.0 : exp2(unpackHalf2x16(code).x);
}

// A ray's record: its light and hit distance, and its direction, density and RayTraced.
struct RayRecord
{
    vec4 light;
    uvec4 ray;
};

// The farthest hit distance that a record holds, in m: the largest half float, past which a GPU that is not Metal stores
// infinity. A farther hit, such as a planet's, stores it. At 1080p and 60°, its reflected place then lies about 2 px off
// the true one when the camera moves 117 m per frame, an orbit's speed. The same-hit test compares capped distances alike.
const float MostHitDistance = 65504.0;

RayRecord recordOf(TracedRay traced)
{
    RayRecord record;
    record.light = vec4(traced.light, min(traced.distance, MostHitDistance));
    record.ray = uvec4(directionCodeOf(traced.direction), densityCodeOf(traced.density), traced.traced ? RayTraced : 0u);
    return record;
}

// The record of a block with no ray: no pixel of it starts the kind.
RayRecord noRay()
{
    RayRecord record;
    record.light = vec4(0.0);
    record.ray = uvec4(0u);
    return record;
}

#endif
