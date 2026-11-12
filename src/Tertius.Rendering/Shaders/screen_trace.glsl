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
// The last frame's diffuse light that a hit reads: the diffuse filter's output, or with the filter off the fast history
// (ScreenTraceImages.HitDiffuse); and the last frame's specular fast history.
layout(set = 3, binding = 6, rgba16f) uniform readonly image2D lastDiffuseLight;
layout(set = 3, binding = 7, rgba16f) uniform readonly image2D lastFastSpecular;
layout(set = 3, binding = 8, rgba16f) uniform readonly image2D slowDiffuse;    // the last frame's slow histories: a the
#include "specular_history.glsl"
layout(set = 3, binding = 9, SPECULAR_HISTORY_FORMAT) uniform readonly image2D slowSpecular;
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
// The light pyramid (ARCHITECTURE.md, "Guided diffuse rays", light_pyramid.glsl): each pixel's guide light, and the sums
// of levels 1 to L, mip m level m + 1, whose padding holds no light. Bindings 19 to 23 are the light age's.
layout(set = 3, binding = 24) uniform texture2D lightFinest;    // R32F
layout(set = 3, binding = 25) uniform texture2D lightLevels;    // R32F
// The hit light (ticket ter-67y3, ScreenTraceImages.HitLight), layer KindReflection and KindDiffuse: rgb the light that
// leaves each pixel that a walk can hit toward a ray of the kind (leavingLightOf). The plan writes it, and a hit reads it.
layout(set = 3, binding = 26, rgba16f) uniform image2DArray hitLight;

#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
// The light-change age of a precise specular history (tickets tiunr-py6q and tiunr-7haj, ScreenTraceImages.LightAge): r
// the exact light at the pixel, g the frames since it last changed, or for a light that the record never saw change −(N + 1), where N counts the frames since the
// frame that first showed the pixel. The fraction of g holds a pending change (ticket tiunr-okzm, recordAgeOf). The plan
// writes this frame's record from the last frame's, and a hit reads this frame's record at its pixel.
layout(set = 3, binding = 19, rg32f) uniform readonly image2D lastLightAge;
layout(set = 3, binding = 20, rg32f) uniform image2D lightAge;
// The specular sample's light age (TemporalSignal.SampleAge), which the resolve writes: an age code (ageCodeOf).
layout(set = 3, binding = 21, r16ui) uniform writeonly uimage2D specularSampleAge;
// FSR 3.1's transparency and composition mask (ScreenTraceImages.LightChangeMask), which the resolve writes from this
// frame's records (lightChangeMaskAt): where the direct light changed, FSR's history keeps to this frame's light (ticket
// t1-4hij).
layout(set = 3, binding = 22, r8) uniform writeonly image2D lightChangeMask;

// The age of a pixel whose light the record never saw change, in the frame that first shows it: no evidence. Each later
// frame that shows it unchanged lowers it by one, down to −(MostLightAge + 1).
const float NoLightAge = -1.0;

// The largest age that a record holds: its code, the age plus 1, fills the 15 bits of a ray record's w above RayTraced.
const float MostLightAge = 32766.0;

// The draws in a row that a light must stay outside the range of the last exact light to count as a change
// (ARCHITECTURE.md, the light-change age, and t2-hvez).
const float LightChangeDraws = 4.0;

// The code of an age in a ray record and in the sample age: 0 for no evidence, else the age plus 1.
uint ageCodeOf(float age)
{
    return age < 0.0 ? 0u : uint(min(age, MostLightAge)) + 1u;
}

// A record's g: the age, whose integer part it keeps, plus, away from 0, the draws that a pending change has stayed
// outside the range, in steps of 1 / LightChangeDraws. A single float holds both exactly: the age's magnitude stays below
// 2^15, so 8 bits of fraction remain, and LightChangeDraws is a power of 2.
float recordAgeOf(float age, float pendingDraws)
{
    float pending = pendingDraws / LightChangeDraws;
    return age < 0.0 ? age - pending : age + pending;
}

// The age of a record's g (recordAgeOf). A hit stores g as it is: ageCodeOf and rayFieldOf round toward 0 as this does.
float ageOfRecord(float g)
{
    return trunc(g);
}

// The draws of a record's pending change (recordAgeOf): 0 for none.
float pendingDrawsOfRecord(float g)
{
    return round(abs(g - trunc(g)) * LightChangeDraws);
}

// The frames after a change of the pixel's light in which FSR's history keeps to this frame's light.
const float LightChangeMaskFrames = 8.0;

// Whether a record's g shows its pixel's light changing (ticket t1-4hij): while a change is pending and for
// LightChangeMaskFrames frames after it counts.
bool changingLight(float g)
{
    bool recent = g >= 0.0 && ageOfRecord(g) < LightChangeMaskFrames;
    return recent || pendingDrawsOfRecord(g) > 0.0;
}

// FSR 3.1's transparency and composition mask at the pixel (ticket t1-4hij), from this frame's light-age records: 1 where
// the opening of the changing pixels by a 3 × 3 block holds the pixel, that is where some 3 × 3 block around it changes
// whole, else 0. Under the mask FSR clamps its history to the pixel's neighbors in this frame, so a still surface under a
// moving light does not keep the light of the light's earlier places. A moving light or caster changes the light of a
// band many pixels wide, which the opening keeps. The jitter that moves a pixel on and off a still feature thinner than a
// pixel changes its record too, in a line one or two pixels wide, which the opening removes: under the mask FSR's clamp
// would follow that aliasing. The resolve writes it, after the plan wrote every record.
float lightChangeMaskAt(ivec2 pixel)
{
    // An opening holds only pixels that change: one read decides the still pixels.
    if (!changingLight(imageLoad(lightAge, pixel).g))
        return 0.0;
    ivec2 last = ivec2(frame.viewport.xy) - 1;
    bool changing[5][5];
    for (int y = 0; y < 5; y++)
        for (int x = 0; x < 5; x++)
            changing[y][x] = changingLight(imageLoad(lightAge, clamp(pixel + ivec2(x - 2, y - 2), ivec2(0), last)).g);
    for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
        {
            bool whole = true;
            for (int dy = 0; dy < 3; dy++)
                for (int dx = 0; dx < 3; dx++)
                    whole = whole && changing[y + dy][x + dx];
            if (whole)
                return 1.0;
        }
    return 0.0;
}

#ifdef TERTIUS_LIGHT_AGE_DIAGNOSTIC
// The diagnostic of uncovered surfaces (TemporalSignal.UncoveredDiagnostic), which only the capture tool and the tests
// read: the resolve writes r, the fewest frames seen among the never-changed surfaces that the specular sample's rays
// hit, or −1 for none. The temporal dispatch writes the other channels.
layout(set = 3, binding = 23, rgba32f) uniform writeonly image2D uncoveredDiagnostic;

// The age of a ray that hit nothing: below every record's age, so that a miss differs from a surface first shown.
const float MissLightAge = -65536.0;

// The diagnostic's field of a ray record, the 15 bits above RayTraced: an age code below SeenField, or SeenField plus a
// never-changed hit's frames seen. Its age codes stop at MostFieldCode, age 16,382, which changes no cut while the
// history holds fewer frames.
const uint SeenField = 0x4000u;
const uint MostFieldCode = 0x3FFFu;

uint rayFieldOf(float age)
{
    if (age >= 0.0)
        return min(ageCodeOf(age), MostFieldCode);
    if (age > MissLightAge)
        return SeenField | uint(min(-age - 1.0, float(MostFieldCode)));
    return 0u;
}

// The seen code of a ray record: 0 for none, else the frames seen of its never-changed hit plus 1.
uint seenCodeOfRecord(uvec4 ray)
{
    uint field = ray.w >> 1;
    return (field & SeenField) != 0u ? (field & MostFieldCode) + 1u : 0u;
}
#endif

// The age code of a ray record's hit (ageCodeOf).
uint ageCodeOfRecord(uvec4 ray)
{
#ifdef TERTIUS_LIGHT_AGE_DIAGNOSTIC
    uint field = ray.w >> 1;
    return (field & SeenField) != 0u ? 0u : field;
#else
    return ray.w >> 1;
#endif
}
#endif

HISTORY_READER(lastDiffuseLightAt, lastDiffuseLight)
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
    float guidedShare;       // LightPyramid.GuidedShare, or 0 with the guiding off (TERTIUS_GUIDED_RAYS)
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
// The guided draw: the descent's levels, three per draw from "GD" on, and the target's place in its slab, "GT".
const uint GuideDraw = 0x4744u;
const uint TargetDraw = 0x4754u;

// How deep a pixel's target box reaches behind its center, the front of its slab, as a share of its view depth: κ / 2
// (LightPyramid.TargetDepthShare).
const float TargetDepthShare = 0.5 * ThicknessShare;

// The reflection rays' blue-noise mask (BlueNoiseMask): BlueNoiseFrames frames of BlueNoiseSide² cells, frame t in tile
// (t % 8, t / 8), each texel a radial number and an angle in bytes.
layout(set = 0, binding = 20) uniform utexture2D blueNoiseMask;
const uint BlueNoiseSide = 128u;
const uint BlueNoiseFrames = 64u;
const uint BlueNoiseTilesAcross = 8u;
// The constant of pcg3d's third value for the mask's shift in each period, "SH".
const uint ShiftDraw = 0x5348u;

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

// The mask's two bytes, a radial number and an angle, for a cell at a draw: the draw's frame of the period, at the cell
// moved by the period's own toroidal shift, so that a still pixel's long mean averages new values in each period.
uvec2 blueNoiseBytesOf(ivec2 cell, uint drawIndex)
{
    uint maskFrame = drawIndex & (BlueNoiseFrames - 1u);
    uvec2 shift = pcg3d(uvec3(drawIndex / BlueNoiseFrames, 0u, ShiftDraw)).xy;
    uvec2 inFrame = (uvec2(cell) + shift) & (BlueNoiseSide - 1u);
    uvec2 tile = uvec2(maskFrame % BlueNoiseTilesAcross, maskFrame / BlueNoiseTilesAcross);
    return texelFetch(blueNoiseMask, ivec2(tile * BlueNoiseSide + inFrame), 0).rg;
}

// The two numbers of a reflection ray's GGX draw, (angle, radial), from the mask: each byte picks a step of 1/256, and
// 16 bits of the draw place the number inside it. The sum is an integer below 2^24, which a float holds exactly, so a
// number stays below 1.
vec2 reflectionNumbersOf(ivec2 cell, uint drawIndex, uvec3 draw)
{
    uvec2 bytes = blueNoiseBytesOf(cell, drawIndex);
    uvec2 numbers = (bytes.yx << 16) | (draw.xy >> 16);
    return vec2(numbers) * (1.0 / 16777216.0);
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
    p.point = p.s.position;
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
            : environmentSpecular(p.s.map, p.s.position, direction, p.s.geometricNormal, sqrt(p.alpha),
                withAmbientFootprint(p.s, pixel).reflectionFootprint);
    else
        light = mix(frame.ambientGround.rgb, frame.ambientSky.rgb,
            kind == KindDiffuse ? p.s.skyFraction : p.s.lobeSkyFraction);
    return exposed(light, exposureScale());
}

// The last frame's light of the signal of `kind` at a reprojection that holds one (lastDiffuseLight, lastFastSpecular),
// rescaled to its weights and into this frame's exposure.
vec3 lastLightOf(Reprojection r, uint kind)
{
    vec3 light = kind == KindDiffuse ? lastDiffuseLightAt(r).rgb : lastFastSpecularAt(r).rgb;
    // As the accumulator rescales its histories (accumulate_temporal.comp): within 2^±64 and at most MaxRadiance.
    return exposed(light, historyRatio(exposureStops, previousStops));
}

// The pixel's place in the last frame's image, from its motion.
vec2 lastPlaceAt(ivec2 pixel)
{
    return readPlace(vec2(pixel) + 0.5, imageLoad(motionTarget, pixel));
}

// The light that leaves a pixel toward a ray of each kind, pre-exposed and at most MaxRadiance.
struct LeavingLight
{
    vec3 reflection;   // toward a reflection ray
    vec3 diffuse;      // toward a diffuse ray, which leaves out the pixel's specular part
};

// The reprojection of a pixel into the last frame for each signal, by the surface test's pass bits at the pixel.
struct SignalReprojections
{
    Reprojection diffuse;
    Reprojection specular;
};

SignalReprojections reprojectionsAt(ivec2 pixel)
{
    uint bits = imageLoad(surfaceTest, pixel).r;
    vec2 place = lastPlaceAt(pixel);
    return SignalReprojections(reprojectionOf(place, bits & 0xFu), reprojectionOf(place, (bits >> 4) & 0xFu));
}

// The glow that the pixel sends: its emission, or 0 where its entity holds a light.
vec3 glowAt(ivec2 pixel)
{
    return glowSent(texelFetch(gbufferEmissive, pixel, 0).rgb, lightMarkOfWord(texelFetch(gbufferShading, pixel, 0).b));
}

// The light that leaves the pixel q of a traced surface toward a ray of each kind (section 6, "Where a hit's light comes
// from"), from q's material s, its view direction V and its reprojections r: this frame's direct light and the last
// frame's light of both signals at q's place, times q's material factors, and the glow that q sends. A diffuse ray leaves
// out q's specular part. Where q fails the surface test, and so in a reset frame, a history is the environment's closed
// form at q. The plan writes it into the hit light image (ticket ter-67y3), and a hit reads it there (hitLightOf).
LeavingLight leavingLightOf(ivec2 q, ShadingInput s, vec3 V, SignalReprojections r)
{
    MaterialFactors factors = materialFactors(s, lobesOf(s, V));
    IndirectLight closedLight = IndirectLight(vec3(0.0), vec3(0.0));
    if (!holdsHistory(r.diffuse) || !holdsHistory(r.specular))
        closedLight = closedForm(withAmbientFootprint(s, q), V);
    vec3 diffuseHistory = holdsHistory(r.diffuse) ? lastLightOf(r.diffuse, KindDiffuse) : closedLight.diffuse;
    vec3 light = factors.diffuse * (imageLoad(directDiffuseLight, q).rgb + diffuseHistory) + glowAt(q);
    vec3 specularHistory = holdsHistory(r.specular) ? lastLightOf(r.specular, KindReflection) : closedLight.specular;
    vec3 reflection = light + factors.specular * (imageLoad(directSpecularLight, q).rgb + specularHistory);
    return LeavingLight(min(reflection, vec3(MaxRadiance)), min(light, vec3(MaxRadiance)));
}

// The light that leaves the pixel q toward a ray of each kind (leavingLightOf). An impostor's light is its emission,
// which holds its ambient light.
LeavingLight leavingLightAt(ivec2 q)
{
    if (texelFetch(gbufferBaseColor, q, 0).a == 0.0)
        return LeavingLight(glowAt(q), glowAt(q));
    return leavingLightOf(q, materialAt(q), -normalize(viewRayThrough(vec2(q) + 0.5)), reprojectionsAt(q));
}

// Whether a walk can hit the pixel, inside the viewport: its finest depth lies in front of the eye (walkScreen). The sky's
// is 0.
bool hittable(ivec2 pixel)
{
    return texelFetch(depthFinest, pixel, 0).r > 0.0;
}

// The light of a hit at the pixel q toward a ray of the kind, pre-exposed: the plan's hit light (leavingLightAt). The
// pixel must be hittable.
vec3 hitLightOf(ivec2 q, uint kind)
{
    return imageLoad(hitLight, ivec3(q, kind)).rgb;
}

// The guide light of a cell of the light pyramid's level, 0 beyond the level's image, whose fetch is undefined.
float pyramidLightAt(int level, ivec2 cell)
{
    if (level == 0)
        return all(lessThan(cell, textureSize(lightFinest, 0))) ? texelFetch(lightFinest, cell, 0).r : 0.0;
    return all(lessThan(cell, textureSize(lightLevels, level - 1))) ? texelFetch(lightLevels, cell, level - 1).r : 0.0;
}

// The guide light of the whole screen: the top level's one cell. 0 when no lit pixel is in view.
float guideTotal()
{
    return texelFetch(lightLevels, ivec2(0), textureQueryLevels(lightLevels) - 1).r;
}

// The pixel at which a guided ray from `pixel` aims, each pixel in proportion to its guide light: from the top, each level
// takes a child in proportion to its light (LightPyramid.Drawn). The total must be above 0.
ivec2 guidedTargetOf(ivec2 pixel)
{
    int top = textureQueryLevels(lightLevels);
    ivec2 cell = ivec2(0);
    // Each draw gives the random units of three levels.
    uvec3 draw = uvec3(0u);
    for (int level = top; level >= 1; level--)
    {
        int index = top - level;
        if (index % 3 == 0)
            draw = drawOf(pixel, trace.drawIndex, GuideDraw + uint(index / 3));
        float unit = unitOf(draw[index % 3]);
        float lights[4];
        float total = 0.0;
        for (int child = 0; child < 4; child++)
        {
            lights[child] = pyramidLightAt(level - 1, 2 * cell + ivec2(child & 1, child >> 1));
            total += lights[child];
        }
        float reach = unit * total;
        int chosen = 0;
        float below = 0.0;
        for (int child = 0; child < 4; child++)
        {
            if (lights[child] <= 0.0)
                continue;
            chosen = child;
            below += lights[child];
            if (reach < below)
                break;
        }
        cell = 2 * cell + ivec2(chosen & 1, chosen >> 1);
    }
    return cell;
}

// The point at which a guided ray from `pixel` aims in its target pixel: a uniform point of the target's box, its volume
// from its center's depth to TargetDepthShare behind it (LightPyramid.TargetOf).
vec3 guidedPointOf(ivec2 pixel, ivec2 target)
{
    uvec3 draw = drawOf(pixel, trace.drawIndex, TargetDraw);
    vec2 place = vec2(target) + vec2(unitOf(draw.x), unitOf(draw.y));
    return viewRayThrough(place) * ((1.0 + TargetDepthShare * unitOf(draw.z)) / texelFetch(depthFinest, target, 0).r);
}

// The end of a chord that no side of the slab ends (LightPyramid.NoEnd).
const float NoChordEnd = 1e30;

// The chord (t from x to y) where also α + β t ≥ 0 (LightPyramid.Chord.Inside).
vec2 chordInside(vec2 chord, float alpha, float beta)
{
    if (beta > 0.0)
        return vec2(max(chord.x, -alpha / beta), chord.y);
    if (beta < 0.0)
        return vec2(chord.x, min(chord.y, -alpha / beta));
    return alpha < 0.0 ? chord.xx : chord;
}

// (t / z)² at the distance t along the ray, z the point's view depth.
float squareOverDepth(float t, float originDepth, float along)
{
    float ratio = t / (originDepth + t * along);
    return ratio * ratio;
}

// The density per steradian of the directions of the guided rays from `origin` that aim at the pixel q, along
// `direction`, times the screen's guide light: q's guide light times the integral of t² over q's target box along the
// ray, over the box's volume element, and 0 where the ray misses the box or q has no light (LightPyramid.PixelDensity).
// A pixel without light, most of the 3 × 3 around a hit in a dark region, returns before its depth's fetch.
float pixelDensityOf(vec3 origin, vec3 direction, ivec2 q)
{
    bool inside = all(greaterThanEqual(q, ivec2(0))) && all(lessThan(q, textureSize(lightFinest, 0)));
    float light = inside ? texelFetch(lightFinest, q, 0).r : 0.0;
    if (light <= 0.0)
        return 0.0;
    float w = texelFetch(depthFinest, q, 0).r;
    if (w <= 0.0)
        return 0.0;
    float front = 1.0 / w;
    float back = (1.0 + TargetDepthShare) / w;
    vec3 forward = frame.cameraForward.xyz;
    float originDepth = dot(origin, forward);
    float along = dot(direction, forward);
    // The chord of the ray through the box: t from 0 where every side of the box holds the ray's point.
    vec2 chord = chordInside(chordInside(vec2(0.0, NoChordEnd), originDepth - front, along), back - originDepth, -along);
    vec3 right = frame.cameraRight.xyz / dot(frame.cameraRight.xyz, frame.cameraRight.xyz);
    vec3 up = frame.cameraUp.xyz / dot(frame.cameraUp.xyz, frame.cameraUp.xyz);
    for (int side = 0; side < 2; side++)
    {
        // The pixel's columns and rows, each edge a plane through the eye, as pixelOf places the jittered pixels.
        vec2 edge = (vec2(q + side) + frame.renderParams.yz) * frame.viewport.zw * 2.0 - 1.0;
        float facing = side == 0 ? 1.0 : -1.0;
        vec3 acrossX = (right - edge.x * forward) * facing;
        vec3 acrossY = (-up - edge.y * forward) * facing;
        chord = chordInside(chord, dot(origin, acrossX), dot(direction, acrossX));
        chord = chordInside(chord, dot(origin, acrossY), dot(direction, acrossY));
    }
    if (chord.y <= chord.x)
        return 0.0;
    // Simpson's rule over the chord: z changes by at most TargetDepthShare inside the box, so t² / z² is nearly a
    // quadratic in t, which the rule integrates exactly.
    float squares = (chord.y - chord.x) / 6.0 * (squareOverDepth(chord.x, originDepth, along)
        + 4.0 * squareOverDepth(0.5 * (chord.x + chord.y), originDepth, along) + squareOverDepth(chord.y, originDepth, along));
    // The pixel's area at depth 1, 4 tan h tan v / (width · height), times the box's depth: a uniform point of the box
    // has the density 1 / (A z² dz).
    float areaDepth = 4.0 * length(frame.cameraRight.xyz) * length(frame.cameraUp.xyz) * frame.viewport.z
        * frame.viewport.w * (back - front);
    return light * squares / areaDepth;
}

// The density per steradian with which a guided ray from `origin` runs along `direction` and counts, and whether one that
// aims at `target` counts: it counts when the walk hits a pixel of the 3 × 3 around its target, and it crosses the
// target's box. So the density sums pixelDensityOf over the 3 × 3 pixels around the hit, in rows from the top, over the
// screen's guide light, which must be above 0 (ARCHITECTURE.md, "Guided diffuse rays", LightPyramid.GuidedDensity and
// Counts).
float guidedDensityOf(vec3 origin, vec3 direction, ivec2 hit, ivec2 target, out bool counts)
{
    float density = 0.0;
    counts = false;
    for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            ivec2 q = hit + ivec2(x, y);
            float pixel = pixelDensityOf(origin, direction, q);
            density += pixel;
            counts = counts || all(equal(q, target)) && pixel > 0.0;
        }
    return density / guideTotal();
}

// The density of the one-sample mixture of the cosine draw and the guided draw (LightPyramid.MixtureDensity).
float mixtureDensity(float cosineDensity, float guidedDensity)
{
    return (1.0 - trace.guidedShare) * cosineDensity + trace.guidedShare * guidedDensity;
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
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
    float age;        // the light age of the hit's pixel, NoLightAge for a miss (MissLightAge under the diagnostic)
#endif
};

// Traces the ray of the kind from the pixel that starts it: its draw, its walk to the kind's cap, and its light. A
// direction below the geometric plane brings no light and traces nothing (section 6, "The ray kinds"). `cell` is the
// ray's cell of the blue-noise mask: its block for a block's ray, its pixel for a pixel's own ray.
TracedRay traceFrom(ivec2 pixel, ivec2 cell, TracingPixel p, uint kind)
{
    TracedRay result;
    uvec3 draw = drawOf(pixel, trace.drawIndex, DirectionDraw[kind]);
    // A reflection ray draws from the blue-noise mask, a diffuse ray from pcg3d (ARCHITECTURE.md, "Randomness and
    // determinism").
    vec2 u = kind == KindReflection ? reflectionNumbersOf(cell, trace.drawIndex, draw)
                                    : vec2(unitOf(draw.x), unitOf(draw.y));
    // A diffuse ray takes the guided draw by the draw's third word, while a lit pixel is in view (ARCHITECTURE.md,
    // "Guided diffuse rays").
    bool guiding = kind == KindDiffuse && trace.guidedShare > 0.0 && guideTotal() > 0.0;
    bool guided = guiding && unitOf(draw.z) < trace.guidedShare;
    ivec2 target = ivec2(-1);
    vec3 drawn;
    if (guided)
    {
        target = guidedTargetOf(pixel);
        drawn = normalize(guidedPointOf(pixel, target) - p.origin);
    }
    else
        drawn = kind == KindReflection ? reflectionDirection(p, u) : diffuseDirection(p, u);
    // The record holds the direction's code, and the resolve weighs the ray by the code's direction. So a reflection ray
    // walks that direction, and its density is that direction's: at the roughness floor, the rounding of the code alone
    // moves D(h) by several percent. A diffuse ray walks its drawn direction: the guided density is a box function of
    // the direction, and a rounded direction near a box's edge would count where its draw did not land, which loses
    // light. Its cosine density and weight are smooth, so the code's rounding moves them by far less than a percent.
    result.direction = kind == KindReflection ? directionOfCode(directionCodeOf(drawn)) : drawn;
    result.density = densityOf(p, kind, result.direction);
    result.light = vec3(0.0);
    result.distance = 0.0;
    result.traced = dot(result.direction, p.s.geometricNormal) > 0.0;
#ifdef TERTIUS_LIGHT_AGE_DIAGNOSTIC
    result.age = MissLightAge;
#elif defined(TERTIUS_PRECISE_SPECULAR_HISTORY)
    result.age = NoLightAge;
#endif
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
    {
        // A miss has no guided density. A guided ray counts only where it hits a pixel near its target, so a miss brings
        // nothing.
        if (guiding)
            result.density = mixtureDensity(result.density, 0.0);
        if (guided)
            result.light = vec3(0.0);
        return result;
    }
    if (guiding)
    {
        bool counts;
        result.density = mixtureDensity(result.density, guidedDensityOf(p.origin, result.direction, hitPixel, target, counts));
        if (guided && !counts)
        {
            result.light = vec3(0.0);
            return result;
        }
    }
    // The hit's surface point from its own distance, not from the ray's inverse depth, which is coarse far away.
    vec3 hitPoint = normalize(viewRayThrough(hit)) * texelFetch(gbufferDistance, hitPixel, 0).r;
    result.distance = length(hitPoint - p.origin);
    float fade = edgeFade(hit) * (1.0 - entryShare);
    result.light = mix(missLight, hitLightOf(hitPixel, kind), fade);
#ifdef TERTIUS_LIGHT_AGE_DIAGNOSTIC
    // THROWAWAY DIAGNOSTIC (t8-pkag): the hit pixel and the hit distance in 0.1 mm.
    result.light = vec3(vec2(hitPixel), min(result.distance * 10000.0, 60000.0));
#endif
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
    if (fade > 0.0)
        result.age = imageLoad(lightAge, hitPixel).g;
#endif
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
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
    // The hit's age code in the 15 bits above RayTraced.
#ifdef TERTIUS_LIGHT_AGE_DIAGNOSTIC
    record.ray.w |= rayFieldOf(traced.age) << 1;
#else
    record.ray.w |= ageCodeOf(traced.age) << 1;
#endif
#endif
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
