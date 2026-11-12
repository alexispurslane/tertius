// The lighting model, shared by the deferred direct and ambient passes, the transparent forward pass, the impostor
// capture, the environment faces and the lit puffs.
//
// The PBR BRDF (docs/plans/pbr-brdf.md, "The BRDF"): Lambert diffuse, plus GGX with the height-correlated Smith
// visibility term and Schlick's Fresnel term, with Filament's multiple-scattering energy compensation from the DFG table:
//   f = c_diff / π + D · Vis · F · c,   c_diff = b (1 − m),   F0 = (1 − m) · 0.16 ρ² + m · b
// Each light of the one light list adds f · E · (N·L), where E is its irradiance (docs/plans/local-light-shadows.md, "The
// shaders"), and its specular part sees the light as a sphere of its radius (docs/plans/pbr-brdf.md, "Sized lights").
// A tube light is a capsule, whose diffuse part takes its vector irradiance and whose specular part its representative
// point (ARCHITECTURE.md, "Tube lights"). A rectangle light is a one-sided face, whose E is its face radiance: its diffuse
// part takes the edge sum cut at the horizon and its specular part the LTC of the lobe (ARCHITECTURE.md, "Rectangle
// lights").
// A hair pixel takes each light's response of a fiber instead (hair.glsl), from either side of its card, and its
// ambient light at its fiber normal, its shading normal (docs/plans/skin-and-hair.md, "The hair model"). A skin pixel
// shades as the standard model, with metallic 0. The ambient light comes from one ambient source,
// the scene environment map while it is on and the hemisphere while it is off, which gives a diffuse and a specular
// term (docs/plans/environment-map.md, "The one source of the ambient light"). The forward passes read the ambient
// light through ambientTerms, with every direction open. The deferred surfaces take their indirect light from the
// screen tracer's signals, or from the closed form of temporal.glsl (docs/plans/temporal-renderer.md, section 9).
// lobesRadiance pre-exposes the sum and caps it at MaxRadiance, the surfaces' share of the HDR image (exposure.glsl).
//
// A light's shadow slot says how its shadows dim it (shadows.glsl): a far light by its far shadows (a sphere per caster,
// tested analytically) and, with a cascade set, within 2 km by its cascaded shadow maps, the two multiplied; a near light
// by its atlas tiles.
#ifndef TERTIUS_LIGHTING_GLSL
#define TERTIUS_LIGHTING_GLSL

#include "shadows.glsl"
#include "hemisphere.glsl"
#include "brdf.glsl"
#include "exposure.glsl"
#include "atmosphere.glsl"
#include "environment.glsl"

// How a view samples every light's shadow maps (docs/plans/environment-map.md, "The shadow maps in a face").
const uint ShadowMapsNone = 0u;       // far shadows only: the impostor capture
const uint ShadowMapsMainView = 1u;   // the cascades by view depth with their blend bands, and the tiles: direct, transparent, lit puffs
const uint ShadowMapsHolding = 2u;    // the finest cascade whose box holds the point, and the tiles: the environment faces

// LightUnits.NearestDistance: the floor of the distance, in m, by which a light of radius 0 divides its irradiance.
const float NearestDistance = 0.01;

// RectView.PointDistanceRatio: the distance to a rectangle's center, in half diagonals, from which it shades as a point.
const float RectPointDistanceRatio = 8.0;
// RectView.PointRoughness: the roughness from which a far rectangle's shine is a point light's, not the LTC's.
const float RectPointRoughness = 0.5;

// ShaderInput.IrradianceLimit, 2^64: the most irradiance per channel in W/m² that one light gives a surface, and the most
// face radiance per channel in W/(m²·sr) of a rectangle light. A point, spot or tube light's irradiance before the cap is
// finite, at most 10^34 W/sr over the floor of 10^−4 m². A rectangle's face radiance can pass the float range before the
// cap, 10^34 W/sr over a face of 10^−12 m², and min(+∞, cap) is the cap. Below the cap, the sum over every light of its
// product with the response, its visibility and the exposure stays a finite single float: a rectangle's response per
// unit of face radiance is at most about its albedo (ARCHITECTURE.md, "Portable Vulkan").
const float IrradianceLimit = 18446744073709551616.0;

struct ShadingInput
{
    vec3 position;
    vec3 baseColor;
    vec3 normal;              // shading normal, after normal mapping
    vec3 geometricNormal;     // interpolated vertex normal: the surface the shadow map sees
    float metallic;
    float roughness;          // r₀, before the floor
    float reflectance;
    float skyFraction;        // s_n, the normal's sky share (skyFraction in hemisphere.glsl)
    float lobeSkyFraction;    // s_l, the lobe's sky share (lobeSkyFraction in hemisphere.glsl)
    float reflectionFootprint; // f, in rad, at least p: how far d moves to a neighboring pixel, set by the caller
    uint map;                 // the environment map that lights the surface, root first; only the map's ambient light reads it
    uint model;               // the shading model's code: ShadingStandard, ShadingSkin or ShadingHair
    vec3 strand;              // T, a hair's unit strand from root to tip; 0 for the other models
};

// The view of directLight that is the main view: every caller but an environment face, which passes its face's map.
const uint MainView = 0xFFFFFFFFu;

// The values that every light at one pixel shares, computed once per pixel.
struct SurfaceLobes
{
    vec3 diffuseColor;
    vec3 f0;
    float alpha;
    float NdotV;              // clamped to MinNdotV
    vec3 specularAlbedo;      // E = S + F0 (E₁ − S)
    vec3 energyCompensation;  // c = 1 + F0 (1 / E₁ − 1)
};

SurfaceLobes lobesOf(ShadingInput s, vec3 V)
{
    SurfaceLobes lobes;
    float roughness = flooredRoughness(s.roughness);
    lobes.alpha = roughness * roughness;
    lobes.diffuseColor = s.baseColor * (1.0 - s.metallic);
    lobes.f0 = vec3((1.0 - s.metallic) * 0.16 * s.reflectance * s.reflectance) + s.baseColor * s.metallic;
    lobes.NdotV = clamp(dot(s.normal, V), MinNdotV, 1.0);
    vec2 dfg = dfgAt(lobes.NdotV, roughness);
    lobes.specularAlbedo = vec3(dfg.x) + lobes.f0 * (dfg.y - dfg.x);
    lobes.energyCompensation = 1.0 + lobes.f0 * (1.0 / dfg.y - 1.0);
    return lobes;
}

// The light of one light toward V per unit irradiance, its diffuse part and its specular part
// (docs/plans/skin-and-hair.md, "The light of each model"). The diffuse part has the diffuse color in it, as
// AmbientTerms.diffuse has. DirectTerms in C# is the copy.
struct DirectTerms
{
    vec3 diffuse;
    vec3 specular;
};

// The unit half vector of L and V, or `fallback` where L = −V and the two have no half vector.
vec3 halfVectorOf(vec3 L, vec3 V, vec3 fallback)
{
    vec3 halfVector = L + V;
    float halfLengthSq = dot(halfVector, halfVector);
    return halfLengthSq > 1e-12 ? halfVector * inversesqrt(halfLengthSq) : fallback;
}

#include "hair.glsl"

// D · Vis · F · c · (N·l) of a light whose angular radius has the sine lightSine (docs/plans/pbr-brdf.md, "Sized
// lights"): l is the representative point of the sphere (sphereLightDirection), and D takes the widened lobe's
// normalization (sphereNormalization). A light of radius 0 is a point light: l = L, with the same operations as before
// the sized lights, so its image does not change.
vec3 specularResponse(SurfaceLobes lobes, vec3 N, vec3 V, vec3 L, float lightSine)
{
    bool sized = lightSine > 0.0;
    vec3 l = sized ? sphereLightDirection(L, reflect(-V, N), lightSine) : L;
    float NdotL = dot(N, l);
    if (NdotL <= 0.0)
        return vec3(0.0);
    vec3 H = halfVectorOf(l, V, N);
    float VdotH = dot(V, H);
    float distribution = ggxDistribution(N, H, lobes.alpha);
    if (sized)
        distribution *= sphereNormalization(lobes.alpha, lightSine, VdotH);
    float visibility = smithVisibility(NdotL, lobes.NdotV, lobes.alpha);
    vec3 lobe = schlickFresnel(lobes.f0, VdotH) * (distribution * visibility) * lobes.energyCompensation;
    return lobe * NdotL;
}

// D · Vis · F · c · (N·l) toward the representative point l of a tube light, with D times the normalization of the lobe
// that the ellipse of half-widths crossSine and alongSine widens (tubeNormalization).
vec3 tubeSpecularResponse(SurfaceLobes lobes, vec3 N, vec3 V, vec3 l, float crossSine, float alongSine)
{
    float NdotL = dot(N, l);
    if (NdotL <= 0.0)
        return vec3(0.0);
    vec3 H = halfVectorOf(l, V, N);
    float VdotH = dot(V, H);
    float distribution = ggxDistribution(N, H, lobes.alpha) * tubeNormalization(lobes.alpha, crossSine, alongSine, VdotH);
    float visibility = smithVisibility(NdotL, lobes.NdotV, lobes.alpha);
    vec3 lobe = schlickFresnel(lobes.f0, VdotH) * (distribution * visibility) * lobes.energyCompensation;
    return lobe * NdotL;
}

// f · max(N·L, 0) in its two parts: c_diff / π · (N·L) toward the light's center, and the specular response of the
// sphere light. A sphere above the horizon gives the irradiance of a point at its center (Karis 2013, p. 15), so only
// the specular part sees the light's size.
DirectTerms surfaceResponse(ShadingInput s, SurfaceLobes lobes, vec3 V, vec3 L, float lightSine)
{
    vec3 N = s.normal;
    float NdotL = dot(N, L);
    DirectTerms terms;
    if (NdotL <= 0.0)
    {
        terms.diffuse = vec3(0.0);
        terms.specular = vec3(0.0);
        return terms;
    }
    terms.diffuse = lobes.diffuseColor / PI * NdotL;
    terms.specular = specularResponse(lobes, N, V, L, lightSine);
    return terms;
}

// The radiance toward V per unit irradiance from direction L: a fiber's if `fiber`, else a surface's. `fiber` must be
// whether s shades as hair, which directLight alone decides and modelLight passes on (see directLight). lightSine is
// the sine of the light's angular radius (lightSineAt); a fiber shades every light as a point.
DirectTerms respond(bool fiber, ShadingInput s, SurfaceLobes lobes, vec3 V, vec3 L, float lightSine)
{
    return fiber ? hairResponse(s, lobes, V, L) : surfaceResponse(s, lobes, V, L, lightSine);
}

// Whether a light from L reaches the point at all, before its shadows: a fiber takes light from every side, a surface
// only from the side that both its normals face. geometricNdotL is the cosine of s.geometricNormal with L. A normal map
// can tilt the shading normal toward a light on a surface that itself faces away; that surface is in its own shadow.
// `fiber` as in respond. modelLight relies on the surface's rule: it passes a surface's geometric normal to the shadow
// lookups unturned, which lightVisibility takes only facing the light.
bool takesLight(bool fiber, ShadingInput s, vec3 L, float geometricNdotL)
{
    return fiber || (dot(s.normal, L) > 0.0 && geometricNdotL > 0.0);
}

// TubeView in C#: a tube light's capsule seen from a shaded point, per unit of its face radiance (ARCHITECTURE.md, "Tube
// lights"). The side counts as a line whose elements show the cylinder's width 2R sin ψ, and the two ends as half
// spheres at the segment's ends. Its solid angle Ω, in sr, is the scalar irradiance over the face radiance, which the
// specular part takes. Its cosine vector, in sr, is the vector irradiance over the face radiance: N · it is the
// irradiance on a surface of normal N where the whole capsule lies above it, which the diffuse part takes.
struct TubeView
{
    float solidAngle;
    vec3 cosineVector;
};

// The capsule around the segment from toCenter − halfSegment to toCenter + halfSegment, relative to the shaded point, in
// m, of `radius`, at least NearestDistance.
TubeView tubeView(vec3 toCenter, vec3 halfSegment, float radius)
{
    float halfLength = length(halfSegment);
    vec3 axis = halfLength > 0.0 ? halfSegment / halfLength : vec3(0.0, 1.0, 0.0);
    float along = dot(toCenter, axis);
    vec3 across = toCenter - along * axis;
    float axisDistance = length(across);
    // A point beside the segment and inside the capsule counts as on its surface, where the line's light is the side's
    // πL. Beyond an end the floor only keeps the angle's division finite, so the side's light falls to 0 on the axis.
    bool beside = (along - halfLength) * (along + halfLength) <= 0.0;
    float floored = max(axisDistance, beside ? radius : 1e-20);
    vec3 inward = axisDistance > 0.0 ? across / axisDistance : vec3(0.0);
    float near = along - halfLength;
    float far = along + halfLength;
    float nearSq = floored * floored + near * near;
    float farSq = floored * floored + far * far;
    // The angle between the directions to the two ends, without the cancellation of two arctangents. Its x is above 0
    // wherever its y is 0: beside the segment the floor keeps `floored` above 0, and beyond an end near · far is.
    float angle = atan(floored * (far - near), floored * floored + near * far);
    TubeView view;
    float nearDistance = sqrt(nearSq);
    float farDistance = sqrt(farSq);
    // Beyond an end the sines of the two ends lie close: their difference without the cancellation.
    view.solidAngle = beside
        ? 2.0 * radius / floored * (far / farDistance - near / nearDistance)
        : 2.0 * radius * floored * (far * far - near * near)
            / ((far * nearDistance + near * farDistance) * nearDistance * farDistance);
    view.cosineVector = radius * (inward * (angle / floored + far / farSq - near / nearSq)
        - axis * (floored * (1.0 / farSq - 1.0 / nearSq)));
    float halfSphere = 0.5 * PI * radius * radius;
    for (int end = -1; end <= 1; end += 2)
    {
        vec3 toEnd = toCenter + float(end) * halfSegment;
        float share = halfSphere / max(dot(toEnd, toEnd), radius * radius);
        view.solidAngle += share;
        view.cosineVector += normalizedOr(toEnd, vec3(0.0)) * share;
    }
    return view;
}

// TubeView.Representative: the unit direction toward Karis's representative point of a tube light's capsule (Karis
// 2013, p. 17), the point of the segment nearest the mirror ray R, then the point of the sphere of `radius` around it
// nearest the ray, with the sines of the half-widths of the widened lobe's ellipse: crossSine across the tube, and
// alongSine along it, which makes the ellipse's solid angle the capsule's solidAngle. toCenter and halfSegment as in
// tubeView.
vec3 tubeLightDirection(vec3 toCenter, vec3 halfSegment, float radius, float solidAngle, vec3 R,
    out float crossSine, out float alongSine)
{
    vec3 start = toCenter - halfSegment;
    vec3 segment = 2.0 * halfSegment;
    float mirrorAlong = dot(R, segment);
    float segmentSq = dot(segment, segment);
    float denominator = segmentSq - mirrorAlong * mirrorAlong;
    // A ray along the segment is as near to every point of it: the middle stands for them.
    float share = denominator > 1e-6 * segmentSq
        ? clamp((dot(R, start) * mirrorAlong - dot(start, segment)) / denominator, 0.0, 1.0)
        : 0.5;
    vec3 closest = start + share * segment;
    crossSine = min(radius / max(length(closest), 1e-6), 1.0);
    alongSine = min(solidAngle / (PI * crossSine), 1.0);
    return sphereLightDirection(normalizedOr(closest, normalizedOr(toCenter, R)), R, crossSine);
}

// RectView.SolidAngle: Ω, the solid angle in sr of a rectangle light's face centered toCenter from the shaded point with
// the half sides halfWidth and halfHeight, whose cross product points to its front, or 0 behind the face. Only a lit puff
// takes it: a surface's and a fiber's sums integrate the face radiance over the face themselves. The triangles
// (c0, c1, c2) and (c0, c2, c3) of Van Oosterom and Strackee each have the triple product 4 · height. The corners are
// taken in units of the center's distance, where Ω is the same and no product overflows.
float rectSolidAngle(vec3 toCenter, vec3 halfWidth, vec3 halfHeight)
{
    float scale = 1.0 / max(length(toCenter), 1e-6);
    vec3 c = toCenter * scale;
    vec3 w = halfWidth * scale;
    vec3 h = halfHeight * scale;
    // The point's height over the face's plane, times the half sides' lengths: in front where it is above 0.
    float height = -dot(c, cross(w, h));
    if (height <= 0.0)
        return 0.0;
    vec3 c0 = c - w - h;
    vec3 c1 = c - w + h;
    vec3 c2 = c + w + h;
    vec3 c3 = c + w - h;
    float l0 = length(c0);
    float l1 = length(c1);
    float l2 = length(c2);
    float l3 = length(c3);
    float first = l0 * l1 * l2 + dot(c0, c1) * l2 + dot(c0, c2) * l1 + dot(c1, c2) * l0;
    float second = l0 * l2 * l3 + dot(c0, c2) * l3 + dot(c0, c3) * l2 + dot(c2, c3) * l0;
    // atan's y is above 0, so it is defined for every x.
    return 2.0 * (atan(4.0 * height, first) + atan(4.0 * height, second));
}

// RectView.EdgeVector: θ n̂ of the arc from the unit direction a to the unit direction b, its angle times the unit normal
// of its plane; zero for an arc of no length, between opposite directions and when a or b is zero. θ / sin θ comes from
// Hill and Heitz's rational fit (Hill and Heitz 2016, "Real-Time Area Lighting: a Journey from Research to Production"),
// within 2·10⁻⁶ of it at every angle, with no arctangent. Past π / 2 it takes sin θ from the cross product, which keeps
// it exact near π, where 1 − cos² θ loses it in rounding.
vec3 edgeVector(vec3 a, vec3 b)
{
    vec3 normal = cross(a, b);
    float x = dot(a, b);
    float y = abs(x);
    // θ / sin θ / (2π) at the angle of cosine y, at most π / 2.
    float fit = (0.8543985 + (0.4965155 + 0.0145206 * y) * y) / (3.4175940 + (4.1616724 + y) * y);
    // Past π / 2, θ / sin θ = π / sin θ − (π − θ) / sin(π − θ). The floor keeps the division finite where the normal is
    // zero, and so is the product.
    float thetaOverSineOverTwoPi = x > 0.0 ? fit : 0.5 / max(length(normal), 1e-30) - fit;
    return normal * (2.0 * PI * thetaOverSineOverTwoPi);
}

// RectView.CutCosine: ∫ max(ω_z, 0) dω over the convex quad q seen from the origin, in sr: Lambert's edge sum over its
// part above z = 0. The cut leaves at most one way out and one way back in, which the horizon's arc joins. The quad winds
// clockwise about the direction from the origin toward it, so the sum is above 0.
float cutCosine(vec3 q[4])
{
    // The directions of the corners: a crossing between two of them lies on their arc, so it keeps the cut exact. A corner
    // that rounds to the origin adds no edge.
    for (int i = 0; i < 4; i++)
        q[i] = normalizedOr(q[i], vec3(0.0));
    float sum = 0.0;
    vec3 exitPoint = vec3(0.0);
    vec3 entryPoint = vec3(0.0);
    bool cut = false;
    for (int i = 0; i < 4; i++)
    {
        vec3 a = q[i];
        vec3 b = q[(i + 1) & 3];
        bool aAbove = a.z >= 0.0;
        bool bAbove = b.z >= 0.0;
        if (aAbove && bAbove)
            sum += edgeVector(a, b).z;
        else if (aAbove || bAbove)
        {
            // The signs of a.z and b.z differ, so the division is finite.
            vec3 crossing = normalizedOr(mix(a, b, a.z / (a.z - b.z)), vec3(0.0));
            cut = true;
            if (aAbove)
            {
                sum += edgeVector(a, crossing).z;
                exitPoint = crossing;
            }
            else
            {
                sum += edgeVector(crossing, b).z;
                entryPoint = crossing;
            }
        }
    }
    if (cut)
        sum += edgeVector(exitPoint, entryPoint).z;
    return max(0.5 * sum, 0.0);
}

// The path from a surface point to a light of the list, before its shadows. lightIrradiance gives its first part: the
// light's irradiance there, or a rectangle's face radiance, the direction of its diffuse light with that light's share and
// a tube's solid angle, L the unit direction toward the light's center and d its distance, and the inputs of a tube's or a
// rectangle's response. lightPath gives the rest, what its shadow lookup takes: the geometric normal turned toward the
// light and the sine of its angle with L.
struct LightPath
{
    // In W/m² per channel, or a rectangle's face radiance in W/(m²·sr) per channel, which its response integrates over
    // its face.
    vec3 irradiance;
    vec3 cosine;
    float solidAngle;
    vec3 L;
    float d;
    // From the shaded point to the light's center, in m.
    vec3 toCenter;
    // A tube's half segment and its radius, in m, at least NearestDistance. Only a tube with some irradiance sets them.
    vec3 halfSegment;
    float tubeRadius;
    // A rectangle's half width and half height, in m. Only a rectangle with some irradiance sets them.
    vec3 halfWidth;
    vec3 halfHeight;
    vec3 shadowNormal;
    float sinTheta;
};

// The first part of the path from `position` to the light at `index` of the list (LightPath). Its irradiance in W/m² per
// channel is color × intensity over max(d², R², NearestDistance²), times the window of Karis 2013 to the reach and a
// spot light's cone, and the air's tint for the atmosphere's sun, the light at frame.counts.y, at most IrradianceLimit.
// 0 at or beyond the reach. A tube light sends its scalar irradiance instead: its face radiance times its solid angle,
// with the window of its reach from its segment. A rectangle light sends its face radiance, with the window of its reach
// from its face, and none behind its face: its response, or a puff's solid angle, integrates it over the face. `tinted`
// may be false only when frame.counts.y is NoAirSun: a constant false leaves the tint's code out of the caller's loop. L
// is zero when the position is at the light's center. `cosine` is the direction of the diffuse light times its share of
// the irradiance: L, or a tube's cosine vector over its solid angle, `solidAngle`, which is 0 for every other light. A
// rectangle keeps L: rectResponse cuts its face itself. `shape` must be the light's shapeOf: a constant leaves the other
// shapes' code out of the caller's loop.
void lightIrradiance(uint index, bool tinted, uint shape, vec3 position, out LightPath p)
{
    GpuLight light = lights[index];
    p.toCenter = light.positionReach.xyz - position;
    // Finite: a kept light lies within 2^62 m of the eye, and so does every shaded point.
    float distanceSq = dot(p.toCenter, p.toCenter);
    // The floor keeps inversesqrt finite at the light's center, where L becomes zero.
    float inverseDistance = inversesqrt(max(distanceSq, 1e-8));
    p.L = p.toCenter * inverseDistance;
    p.d = distanceSq * inverseDistance;
    p.cosine = p.L;
    p.solidAngle = 0.0;
    p.irradiance = vec3(0.0);
    float reach = light.positionReach.w;
    float radius = light.spotRadius.y;
    float falloff;
    if (shape == RectShape)
    {
        // SceneBatcher.Shines keeps a rectangle with sides of at least 1 µm, so the squares of its half sides are normal
        // floats.
        vec3 halfWidth = light.directionCosOuter.xyz;
        vec3 halfHeight = light.halfHeight.xyz;
        float widthSq = dot(halfWidth, halfWidth);
        float heightSq = dot(halfHeight, halfHeight);
        // From the point to the face's nearest point: the center moved toward the point along each side, at most a half
        // side.
        vec3 toNearest = p.toCenter - clamp(dot(p.toCenter, halfWidth) / widthSq, -1.0, 1.0) * halfWidth
            - clamp(dot(p.toCenter, halfHeight) / heightSq, -1.0, 1.0) * halfHeight;
        float nearestSq = dot(toNearest, toNearest);
        // The reach holds the face: its peak intensity's reach from the face, plus half its diagonal.
        float faceReach = max(reach - sqrt(widthSq + heightSq), 0.0);
        if (nearestSq >= faceReach * faceReach)
            return;
        // A quarter of the face's area along its front.
        vec3 front = cross(halfWidth, halfHeight);
        // Behind the face, or in its plane, where it fills no solid angle: the point's height over the plane, times the
        // half sides' lengths, is not above 0.
        if (dot(p.toCenter, front) >= 0.0)
            return;
        float ratio = nearestSq / (faceReach * faceReach);
        float window = clamp(1.0 - ratio * ratio, 0.0, 1.0);
        p.halfWidth = halfWidth;
        p.halfHeight = halfHeight;
        // The peak intensity over the face's area is its face radiance.
        falloff = window * window / (4.0 * length(front));
    }
    else if (shape == TubeShape)
    {
        // GpuLight.From gives a tube a half segment longer than 10^−6 m, so its square is above 0.
        vec3 halfSegment = light.directionCosOuter.xyz;
        float halfLengthSq = dot(halfSegment, halfSegment);
        float halfLength = sqrt(halfLengthSq);
        vec3 toNearest = p.toCenter - clamp(dot(p.toCenter, halfSegment) / halfLengthSq, -1.0, 1.0) * halfSegment;
        float nearestSq = dot(toNearest, toNearest);
        // The reach holds the segment: its peak intensity's reach from the segment, plus half its length.
        float segmentReach = max(reach - halfLength, 0.0);
        if (nearestSq >= segmentReach * segmentReach)
            return;
        float ratio = nearestSq / (segmentReach * segmentReach);
        float window = clamp(1.0 - ratio * ratio, 0.0, 1.0);
        p.halfSegment = halfSegment;
        p.tubeRadius = max(radius, NearestDistance);
        TubeView view = tubeView(p.toCenter, halfSegment, p.tubeRadius);
        p.solidAngle = view.solidAngle;
        p.cosine = view.cosineVector / max(view.solidAngle, 1e-30);
        // The peak intensity over the area that the capsule shows across its segment is its face radiance.
        falloff = window * window * view.solidAngle
            / (4.0 * p.tubeRadius * halfLength + PI * p.tubeRadius * p.tubeRadius);
    }
    else
    {
        // Before the division by reach², which is then above distanceSq ≥ 0.
        if (distanceSq >= reach * reach)
            return;
        float ratio = distanceSq / (reach * reach);
        float window = clamp(1.0 - ratio * ratio, 0.0, 1.0);
        falloff = window * window / max(distanceSq, max(radius * radius, NearestDistance * NearestDistance));
        if (light.intensityKind.w == SpotKind)
            // Cone.Of keeps cos(inner) above cos(outer), so the edges of smoothstep differ.
            falloff *= smoothstep(light.directionCosOuter.w, light.spotRadius.x, dot(-p.L, light.directionCosOuter.xyz));
    }
    vec3 irradiance = light.intensityKind.rgb * falloff;
    // The atmosphere's sun takes the air's tint at the lit point (docs/plans/atmosphere.md, "The sun's tint"). The tint
    // is at most 1, so the product stays finite.
    if (tinted && index == frame.counts.y)
        irradiance *= sunTintAt(position);
    p.irradiance = min(irradiance, vec3(IrradianceLimit));
}

// sin ϕ of the angular radius ϕ of `light` at distance d from its center: R / d, at most 1 inside its sphere. The floor
// of d keeps the division finite at the center. 0 for a light of radius 0.
float lightSineAt(GpuLight light, float d)
{
    return min(light.spotRadius.y / max(d, 1e-6), 1.0);
}

// The share of `light` that its shadow slot lets through at `position`, by the rule of shadowMaps. L is the unit
// direction toward the light and d its distance. geometricNormal is the surface that the shadow maps see, turned toward
// the light, and sinTheta the sine of its angle with L: 1 for a hair card edge-on to the light.
// The impostor capture, ShadowMapsNone, takes the far shadows alone with weight 1, and no tiles.
float lightVisibility(GpuLight light, vec3 position, vec3 L, float d, vec3 geometricNormal, float sinTheta,
    uint shadowMaps)
{
    uint slot = light.shadow.x;
    if (slot == NoSlot)
        return 1.0;
    if (slot == AtlasTilesSlot)
        return shadowMaps == ShadowMapsNone ? 1.0 : tileShadow(light, position, L, d, geometricNormal, sinTheta);
    // The exact angle of a sphere, also inside it.
    float angularRadius = asin(lightSineAt(light, d));
    if (slot == FarShadowsOnlySlot || shadowMaps == ShadowMapsNone)
        return farShadowVisibility(position, L, angularRadius, light.occluders, 1.0);
    uint set = light.shadow.y;
    if (shadowMaps == ShadowMapsMainView)
    {
        float visibility = farShadowVisibility(position, L, angularRadius, light.occluders, beyondShadowMaps(position));
        if (visibility > 0.0)
            visibility *= cascadeShadow(set, position, geometricNormal, sinTheta);
        return visibility;
    }
    float visibility = farShadowVisibility(position, L, angularRadius, light.occluders, beyondShadowMapsHolding(set, position));
    if (visibility > 0.0)
        visibility *= cascadeShadowHolding(set, position, geometricNormal, sinTheta);
    return visibility;
}

// W = E · c, the compensated specular albedo: the weight of the light that arrives along the specular lobe in the light
// toward the eye, and the share of a uniform light that the specular part reflects. Every ambient source and the pass
// that adds the reflections' delta (the full-resolution trace, or the upsample at half resolution) weight specular
// light by it, and a pane of glass lets through what it leaves (transparent.frag).
vec3 specularWeight(SurfaceLobes lobes)
{
    return lobes.specularAlbedo * lobes.energyCompensation;
}

// The ambient light that a surface point sends toward the eye, before ambient occlusion, in two terms: the diffuse
// color times the incoming diffuse light, and the specular weight times the incoming specular light.
struct AmbientTerms
{
    vec3 diffuse;
    vec3 specular;
};

// The hemisphere as the ambient source: the diffuse term by the normal's sky share, the specular term by the lobe's.
AmbientTerms hemisphereAmbient(ShadingInput s, SurfaceLobes lobes)
{
    vec3 ground = frame.ambientGround.rgb;
    vec3 sky = frame.ambientSky.rgb;
    AmbientTerms terms;
    terms.diffuse = lobes.diffuseColor * mix(ground, sky, s.skyFraction);
    terms.specular = specularWeight(lobes) * mix(ground, sky, s.lobeSkyFraction);
    return terms;
}

// d: the specular dominant direction of the lobe of roughness r₀ about the shading normal, toward which it reflects the
// ambient light.
vec3 ambientSpecularDirection(vec3 normal, float roughness, vec3 V)
{
    float floored = flooredRoughness(roughness);
    return specularDominantDirection(normal, reflect(-V, normal), floored * floored);
}

// d of the surface's lobe.
vec3 ambientSpecularDirection(ShadingInput s, vec3 V)
{
    return ambientSpecularDirection(s.normal, s.roughness, V);
}

// The map as the ambient source: the diffuse term from the irradiance at the normal, the specular term from the
// specular map and the reflected stars at d.
AmbientTerms environmentAmbient(ShadingInput s, SurfaceLobes lobes, vec3 V)
{
    AmbientTerms terms;
    terms.diffuse = lobes.diffuseColor * environmentDiffuse(s.map, s.normal);
    vec3 incoming = environmentSpecular(
        s.map, s.position, ambientSpecularDirection(s, V), s.geometricNormal,
        flooredRoughness(s.roughness), s.reflectionFootprint);
    terms.specular = specularWeight(lobes) * incoming;
    return terms;
}

// The ambient light of a surface point in its two terms, with every direction open, from the frame's ambient source:
// the map while it is on and the hemisphere while it is off (docs/plans/environment-map.md, "The one source of the
// ambient light"). ambientLight reads the ambient light only through this function. V points from the surface to the
// eye.
AmbientTerms ambientTerms(ShadingInput s, SurfaceLobes lobes, vec3 V)
{
    return environmentMapOn ? environmentAmbient(s, lobes, V) : hemisphereAmbient(s, lobes);
}

// The ambient light reflected toward the eye: both terms of ambientTerms.
vec3 ambientLight(ShadingInput s, SurfaceLobes lobes, vec3 V)
{
    AmbientTerms terms = ambientTerms(s, lobes, V);
    return terms.diffuse + terms.specular;
}

// Whether a view keeps a light of the list (docs/plans/environment-map.md, decision 20): the main view the lights whose
// reach touches its frustum, and a face of map i the lights whose map is at most i
// (docs/plans/environment-maps-per-frame.md, decision 13).
bool viewKeeps(uint view, GpuLight light)
{
    return view == MainView ? light.inMainView != 0u : light.map <= view;
}

// The shape of `light` by its kind: RectShape, TubeShape, or PointShape for a point or a spot light.
uint shapeOf(GpuLight light)
{
    float kind = light.intensityKind.w;
    return kind == RectKind ? RectShape : kind == TubeKind ? TubeShape : PointShape;
}

// Whether some corner of the rectangle of path p lies above the plane through the shaded point with the normal n: then
// some part of the face does, because the face is convex.
bool rectAbove(vec3 n, LightPath p)
{
    return dot(n, p.toCenter) + abs(dot(n, p.halfWidth)) + abs(dot(n, p.halfHeight)) > 0.0;
}

// The path from a surface point to light `index`, or false where the point takes none of its light. `fiber` as in respond,
// `tinted` and `shape` as in lightIrradiance. The point takes the light that arrives along its diffuse direction, which is
// L but for a tube, and a surface takes a rectangle's light where some of its face lies above both of its horizons. A
// surface's geometric normal faces every point light that it takes (takesLight), so only a fiber's turns, or a surface's
// toward the middle of a tube that it takes from beyond its horizon. The sine of the angle to the light is the same on
// both sides of a card.
bool lightPath(uint index, bool fiber, bool tinted, uint shape, ShadingInput s, out LightPath p)
{
    lightIrradiance(index, tinted, shape, s.position, p);
    if (p.irradiance == vec3(0.0))
        return false;
    bool takes = shape == RectShape
        ? fiber || rectAbove(s.normal, p) && rectAbove(s.geometricNormal, p)
        : takesLight(fiber, s, p.cosine, dot(s.geometricNormal, p.cosine));
    if (!takes)
        return false;
    float geometricNdotL = dot(s.geometricNormal, p.L);
    p.shadowNormal = geometricNdotL < 0.0 ? -s.geometricNormal : s.geometricNormal;
    p.sinTheta = sqrt(max(1.0 - geometricNdotL * geometricNdotL, 0.0));
    return true;
}

// The share of light `index` that its shadows let through at a point, along the path that lightPath gave there.
float pathVisibility(uint index, LightPath p, vec3 position, uint shadowMaps)
{
    return lightVisibility(lights[index], position, p.L, p.d, p.shadowNormal, p.sinTheta, shadowMaps);
}

// MaterialSample.TubeTerms per unit of the tube's scalar irradiance: c_diff / π · (N · cosine), and the specular
// response toward the representative point (ARCHITECTURE.md, "Tube lights"). Nothing where the cosine points below the
// shading normal. A fiber shades the tube as a point light along its cosine, dimmed by the cosine's length.
DirectTerms tubeResponse(bool fiber, ShadingInput s, SurfaceLobes lobes, vec3 V, LightPath p)
{
    if (fiber)
    {
        float share = length(p.cosine);
        DirectTerms terms = hairResponse(s, lobes, V, normalizedOr(p.cosine, p.L));
        terms.diffuse *= share;
        terms.specular *= share;
        return terms;
    }
    vec3 N = s.normal;
    float cosine = dot(N, p.cosine);
    DirectTerms terms;
    terms.diffuse = vec3(0.0);
    terms.specular = vec3(0.0);
    if (cosine <= 0.0)
        return terms;
    terms.diffuse = lobes.diffuseColor / PI * cosine;
    float crossSine;
    float alongSine;
    vec3 l = tubeLightDirection(p.toCenter, p.halfSegment, p.tubeRadius, p.solidAngle, reflect(-V, N), crossSine,
        alongSine);
    terms.specular = tubeSpecularResponse(lobes, N, V, l, crossSine, alongSine);
    return terms;
}

// RectView.ShadesAsPointOn: whether the rectangle of path p shades a surface of normal n as a point light. It lies at
// least RectPointDistanceRatio half diagonals away, and every corner lies above the surface's horizon, which a point
// light cannot cut.
bool rectShadesAsPoint(vec3 n, LightPath p)
{
    float halfDiagonalSq = dot(p.halfWidth, p.halfWidth) + dot(p.halfHeight, p.halfHeight);
    bool far = p.d * p.d >= RectPointDistanceRatio * RectPointDistanceRatio * halfDiagonalSq;
    bool aboveHorizon = dot(n, p.toCenter) - abs(dot(n, p.halfWidth)) - abs(dot(n, p.halfHeight)) > 0.0;
    return far && aboveHorizon;
}

// MaterialSample.RectTerms per unit of the rectangle's face radiance (ARCHITECTURE.md, "Rectangle lights"): c_diff / π
// times the face's cosine integral cut at the shading normal's horizon, and the compensated specular albedo times the
// share of the lobe's LTC that the face covers, cut at the LTC's own horizon. Both sums take the corners in the LTC's
// frame, whose x points toward the view: the cosine integral does not depend on the tangents. A fiber shades the face as
// a point light along its vector irradiance per unit of face radiance, dimmed by that vector's length, as it shades a
// tube. A face that rectShadesAsPoint gives its diffuse light as a point light of its projected solid angle, and to a
// rough surface also its shine: the sums cost the direct pass most of its time (ARCHITECTURE.md, "Rectangle lights").
DirectTerms rectResponse(bool fiber, ShadingInput s, SurfaceLobes lobes, vec3 V, LightPath p)
{
    // In units of the center's distance, where the sums are the same and no product overflows.
    float scale = 1.0 / max(p.d, 1e-6);
    vec3 c = p.toCenter * scale;
    vec3 w = p.halfWidth * scale;
    vec3 h = p.halfHeight * scale;
    // −w −h, −w +h, +w +h, +w −h: clockwise about the front, seen from it.
    vec3 corners[4] = vec3[4](c - w - h, c - w + h, c + w + h, c + w - h);
    DirectTerms terms;
    terms.diffuse = vec3(0.0);
    terms.specular = vec3(0.0);
    if (fiber)
    {
        vec3 u[4] = vec3[4](normalizedOr(corners[0], vec3(0.0)), normalizedOr(corners[1], vec3(0.0)),
            normalizedOr(corners[2], vec3(0.0)), normalizedOr(corners[3], vec3(0.0)));
        vec3 vector = 0.5 * (edgeVector(u[0], u[1]) + edgeVector(u[1], u[2]) + edgeVector(u[2], u[3])
            + edgeVector(u[3], u[0]));
        terms = hairResponse(s, lobes, V, normalizedOr(vector, p.L));
        float share = length(vector);
        terms.diffuse *= share;
        terms.specular *= share;
        return terms;
    }
    vec3 N = s.normal;
    bool point = rectShadesAsPoint(N, p);
    // RectView.PointSolidAngle: c is a unit vector here, so this is the face's area times its cosine over d².
    float solidAngle = -4.0 * dot(c, cross(w, h));
    if (point && s.roughness >= RectPointRoughness)
    {
        terms = surfaceResponse(s, lobes, V, p.L, 0.0);
        terms.diffuse *= solidAngle;
        terms.specular *= solidAngle;
        return terms;
    }
    vec3 along = V - N * dot(V, N);
    // Head on the LTC is round about the normal, so any tangent serves.
    vec3 first = dot(along, along) > 1e-10
        ? normalize(along)
        : normalize(cross(abs(N.x) < 0.9 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0), N));
    vec3 second = cross(N, first);
    vec4 inverse = ltcAt(lobes.NdotV, flooredRoughness(s.roughness));
    vec3 local[4];
    vec3 lobe[4];
    for (int i = 0; i < 4; i++)
    {
        local[i] = vec3(dot(corners[i], first), dot(corners[i], second), dot(corners[i], N));
        lobe[i] = vec3(inverse.x * local[i].x + inverse.y * local[i].z, local[i].y,
            inverse.z * local[i].x + inverse.w * local[i].z);
    }
    float cosine = point ? dot(N, p.L) * solidAngle : cutCosine(local);
    if (cosine <= 0.0)
        return terms;
    terms.diffuse = lobes.diffuseColor / PI * cosine;
    terms.specular = specularWeight(lobes) * (cutCosine(lobe) / PI);
    return terms;
}

// The light of light `index` along its path from `s.position` before its shadows, in its two parts: its irradiance times
// the response, plain radiance. `fiber` as in respond, `shape` as in lightIrradiance.
DirectTerms pathLight(uint index, bool fiber, uint shape, ShadingInput s, SurfaceLobes lobes, vec3 V, LightPath p)
{
    DirectTerms terms;
    if (shape == RectShape)
        terms = rectResponse(fiber, s, lobes, V, p);
    else if (shape == TubeShape)
        terms = tubeResponse(fiber, s, lobes, V, p);
    else
        terms = respond(fiber, s, lobes, V, p.L, lightSineAt(lights[index], p.d));
    terms.diffuse *= p.irradiance;
    terms.specular *= p.irradiance;
    return terms;
}

// modelLight's loop over the lights of the list of one shape, `shape`, added to `sum`. `fiber` as in respond, `tinted`
// and `shape` as in lightIrradiance. modelLight is the one caller, and it passes constants. A light's response is
// evaluated only where its shadows let some of it through.
void shapeLight(bool fiber, bool tinted, uint shape, ShadingInput s, SurfaceLobes lobes, vec3 V, uint shadowMaps,
    uint view, inout DirectTerms sum)
{
    // frame.counts.w holds the shapes of the list: without a light of this shape, its loop would only skip every light.
    if ((frame.counts.w & shape) == 0u)
        return;
    for (uint i = 0u; i < frame.counts.x; i++)
    {
        if (!viewKeeps(view, lights[i]) || shapeOf(lights[i]) != shape)
            continue;
        LightPath p;
        if (!lightPath(i, fiber, tinted, shape, s, p))
            continue;
        float visibility = pathVisibility(i, p, s.position, shadowMaps);
        // THROWAWAY DIAGNOSTIC (ter-8esk): each light's visibility in its own channel, plus 10 for a light that passes.
        if (i < 3u)
            sum.specular[i] += visibility + 10.0;
    }
}

// directLight for one model: a fiber's light if `fiber`, else a surface's. `fiber` as in respond, `tinted` as in
// lightIrradiance. directLight is the one caller, and it passes constants, so each copy of the loop holds one model's
// code and only the tint that it needs. Each light shape takes a loop of its own, so a loop holds no other shape's code:
// in one loop with the point lights, the rectangle's code made the direct pass of a lamp hall of 64 bulbs and no
// rectangle about 1.7 ms slower (ticket t2-rhja).
DirectTerms modelLight(
    bool fiber, bool tinted, ShadingInput s, SurfaceLobes lobes, vec3 V, uint shadowMaps, uint view)
{
    DirectTerms sum;
    sum.diffuse = vec3(0.0);
    sum.specular = vec3(0.0);
    shapeLight(fiber, tinted, PointShape, s, lobes, V, shadowMaps, view, sum);
    shapeLight(fiber, tinted, TubeShape, s, lobes, V, shadowMaps, view, sum);
    shapeLight(fiber, tinted, RectShape, s, lobes, V, shadowMaps, view, sum);
    return sum;
}

// Every light of the list that the view keeps reflected toward V in its two parts, each dimmed by its shadows as
// shadowMaps says: a face of map i passes i, and every other caller MainView. The point and spot lights add in the order
// of the list, then the tubes, then the rectangles, each in the order of the list, so the sum rounds the same way on
// every run.
// A light adds only where takesLight says. A hair card's shadow lookups take its geometric normal turned toward the
// light, the side that the light sees (docs/plans/skin-and-hair.md, decision 27). The result is plain radiance: no ambient light, no emission and no exposure. The direct pass divides it
// by its material factors into the direct light's images (Shaders/direct.frag).
//
// A pixel has one model, so directLight chooses the model once per pixel, before the loop over the lights, and each
// model has a loop of its own. Chosen per light inside one loop, the hair's code made the direct pass of demo-exit's deck
// about 0.5 ms slower with no hair on screen. The trade-off: a subgroup that holds hair and other pixels runs both loops,
// so it pays each light's irradiance and shadow lookups twice. A view of hair under 16 shadowed lamps, about a third of
// whose pixel blocks hold both, showed no measurable cost of it (docs/plans/skin-and-hair.md, "The shading models'
// cost").
//
// For the same reason, the frame chooses between a loop with the air's tint and one without it: only a frame whose list
// holds the atmosphere's sun takes the loop that compares each light with it. In every frame, the test of the tint inside
// the one loop, never true without an atmosphere, made the direct pass of demo-exit's deck about 0.2 ms slower. The
// choice takes that off, and about 0.05 ms off its transparent pass (docs/plans/skin-and-hair.md, "What remains"). The
// choice is the same for every pixel of a frame.
DirectTerms directLight(ShadingInput s, SurfaceLobes lobes, vec3 V, uint shadowMaps, uint view)
{
    bool tinted = frame.counts.y != NoAirSun;
    if (!dropsFibers && s.model == ShadingHair)
        return tinted ? modelLight(true, true, s, lobes, V, shadowMaps, view)
                      : modelLight(true, false, s, lobes, V, shadowMaps, view);
    return tinted ? modelLight(false, true, s, lobes, V, shadowMaps, view)
                  : modelLight(false, false, s, lobes, V, shadowMaps, view);
}

// Both parts of directLight, added.
vec3 directSum(DirectTerms terms)
{
    return terms.diffuse + terms.specular;
}

// All light that leaves a surface point of the given lobes toward the eye, pre-exposed and capped at MaxRadiance, the
// surfaces' share of the HDR image (exposure.glsl): the reflected light times `exposure`, plus `preExposedEmission`,
// which the caller has already exposed and capped. The cap applies to the whole sum, because the share bounds what a
// surface writes, whatever the parts.
// The reflected light is capped before it is multiplied, so the product stays a finite single float (exposure.glsl).
// V points from the surface to the eye. Impostor captures pass ShadowMapsNone and an exposure of 1, because the atlas
// stores radiance. The main view's opaque surfaces split the same cap over the direct and the ambient pass
// (docs/plans/screen-space-illumination.md, "The cap at MaxRadiance").
vec3 lobesRadiance(
    ShadingInput s, SurfaceLobes lobes, vec3 preExposedEmission, vec3 V, uint shadowMaps, float exposure)
{
    vec3 reflected = ambientLight(s, lobes, V) + directSum(directLight(s, lobes, V, shadowMaps, MainView));
    return min(exposed(reflected, exposure) + preExposedEmission, vec3(MaxRadiance));
}

// lobesRadiance with the lobes of the material itself: every opaque surface.
vec3 surfaceRadiance(ShadingInput s, vec3 preExposedEmission, vec3 V, uint shadowMaps, float exposure)
{
    return lobesRadiance(s, lobesOf(s, V), preExposedEmission, V, shadowMaps, exposure);
}

// The light that the lights that the view keeps send from a surface point toward V, plus its emission, in plain
// radiance: no ambient light and no exposure, as the environment faces store it. Capped at MaxRadiance (exposure.glsl).
vec3 directRadiance(ShadingInput s, vec3 emission, vec3 V, uint shadowMaps, uint view)
{
    SurfaceLobes lobes = lobesOf(s, V);
    return min(directSum(directLight(s, lobes, V, shadowMaps, view)) + emission, vec3(MaxRadiance));
}

// The light that a lit puff at `position` sends toward the eye (docs/plans/particles.md, "Lit puffs"). A puff has no
// surface and no normal: it scatters the light that reaches its center equally in every direction, which is the mean over
// every facing of a matte surface lit by the same lights. The mean of the cosine over every facing is 1/4, so each light
// gives albedo / (4π) times its irradiance and visibility. The ambient light gives the albedo times the mean radiance of
// its source: its map's without the stars, or the mean of the sky and the ground color, whose shares average one half. A
// light's visibility samples its maps with L as the normal, which needs no normal offset.
// Only the main view draws puffs, and a puff's center can lie outside its frustum, so the puff takes only the lights that
// the main view keeps.
vec3 scatteredLight(vec3 albedo, vec3 position, uint map)
{
    vec3 meanAmbient = environmentMapOn
        ? environmentMeanRadiance(map)
        : 0.5 * (frame.ambientSky.rgb + frame.ambientGround.rgb);
    vec3 irradiance = vec3(0.0);
    for (uint i = 0u; i < frame.counts.x; i++)
    {
        GpuLight light = lights[i];
        if (!viewKeeps(MainView, light))
            continue;
        // A puff scatters a tube's or a rectangle's scalar irradiance, so it leaves the diffuse direction aside.
        LightPath p;
        // The puffs keep one loop for every frame, so they pay the compare with frame.counts.y for each light.
        uint shape = shapeOf(light);
        lightIrradiance(i, true, shape, position, p);
        // At the light's center L is a zero vector, which the far shadows' angle cannot take. A surface there takes
        // nothing either, by its dot(N, L) of 0.
        if (p.irradiance == vec3(0.0) || p.d == 0.0)
            continue;
        // A rectangle's path holds its face radiance, whose scalar irradiance is that times the face's solid angle.
        float spread = shape == RectShape ? rectSolidAngle(p.toCenter, p.halfWidth, p.halfHeight) : 1.0;
        irradiance += p.irradiance * (spread
            * lightVisibility(light, position, p.L, p.d, p.L, 0.0, ShadowMapsMainView));
    }
    return albedo * (meanAmbient + irradiance * (1.0 / (4.0 * PI)));
}

// Only fragment shaders include material.glsl, and only they have the screen derivatives that the footprint takes. The
// lit puffs' vertex shader and the compute passes include this file without it.
#ifdef TERTIUS_MATERIAL_GLSL
#include "reflection_footprint.glsl"

// The ShadingInput of a surface that a forward pass shades from its material's sample `s` at `position`: the glass and
// the fringe of the transparent pass, the environment faces and the impostor captures (docs/plans/skin-and-hair.md, "The
// fringe"). The reflection's footprint comes from the screen derivatives, so the caller calls this before any discard.
// The sky shares serve the hemisphere alone, and are 0 while the environment map is on. A forward surface is not in the
// G-buffer that the screen tracer reads, so it takes the ambient light with every direction open.
ShadingInput forwardShading(SurfaceSample s, vec3 position, vec4 hemisphere, uint map, vec3 V)
{
    ShadingInput shading;
    shading.position = position;
    shading.baseColor = s.baseColor.rgb;
    shading.normal = s.normal;
    shading.geometricNormal = s.geometricNormal;
    shading.metallic = s.metallic;
    shading.roughness = s.roughness;
    shading.reflectance = s.reflectance;
    shading.map = map;
    shading.reflectionFootprint = reflectionFootprint(ambientSpecularDirection(shading, V));
    shading.skyFraction = environmentMapOn ? 0.0 : skyFraction(hemisphere, position, s.normal);
    shading.lobeSkyFraction = environmentMapOn ? 0.0 : lobeSkyFraction(hemisphere, position, s.normal, V, s.roughness);
    shading.model = s.model;
    shading.strand = s.strand;
    return shading;
}
#endif

#endif
