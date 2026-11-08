// The lighting model, shared by the deferred direct and ambient passes, the transparent forward pass, the impostor
// capture, the environment faces and the lit puffs.
//
// The PBR BRDF (docs/plans/pbr-brdf.md, "The BRDF"): Lambert diffuse, plus GGX with the height-correlated Smith
// visibility term and Schlick's Fresnel term, with Filament's multiple-scattering energy compensation from the DFG table:
//   f = c_diff / π + D · Vis · F · c,   c_diff = b (1 − m),   F0 = (1 − m) · 0.16 ρ² + m · b
// Each light of the one light list adds f · E · (N·L), where E is its irradiance (docs/plans/local-light-shadows.md, "The
// shaders"), and its specular part sees the light as a sphere of its radius (docs/plans/pbr-brdf.md, "Sized lights").
// A tube light is a capsule, whose diffuse part takes its vector irradiance and whose specular part its representative
// point (ARCHITECTURE.md, "Tube lights").
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

// ShaderInput.IrradianceLimit, 2^64 W/m²: the most irradiance per channel that one light gives a surface. A kept light's
// irradiance before the cap is finite, at most 10^34 W/sr over the floor of 10^−4 m², so min sees no infinity. Below the
// cap, the sum over every light of its product with the BRDF, its visibility and the exposure stays a finite single float
// (ARCHITECTURE.md, "Portable Vulkan").
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

// The irradiance in W/m² per channel that the light at `index` of the list sends to `position` before its shadows:
// color × intensity over max(d², R², NearestDistance²), times the window of Karis 2013 to the reach and a spot light's
// cone, and the air's tint for the atmosphere's sun, the light at frame.counts.y, at most IrradianceLimit. 0 at or beyond
// the reach. A tube light sends its scalar irradiance instead: its face radiance times its solid angle, with the window
// of its reach from its segment. `tinted` may be false only when frame.counts.y is NoAirSun: a constant false leaves the
// tint's code out of the caller's loop. L is the unit direction toward the light, zero when the position is at the
// light's center, and d its distance. `cosine` is the direction of the diffuse light times its share of the irradiance:
// L, or a tube's cosine vector over its solid angle, `solidAngle`, which is 0 for every other light.
vec3 lightIrradiance(uint index, bool tinted, vec3 position, out vec3 L, out float d, out vec3 cosine,
    out float solidAngle)
{
    GpuLight light = lights[index];
    vec3 toLight = light.positionReach.xyz - position;
    // Finite: a kept light lies within 2^62 m of the eye, and so does every shaded point.
    float distanceSq = dot(toLight, toLight);
    // The floor keeps inversesqrt finite at the light's center, where L becomes zero.
    float inverseDistance = inversesqrt(max(distanceSq, 1e-8));
    L = toLight * inverseDistance;
    d = distanceSq * inverseDistance;
    cosine = L;
    solidAngle = 0.0;
    float reach = light.positionReach.w;
    float radius = light.spotRadius.y;
    float falloff;
    if (light.intensityKind.w == TubeKind)
    {
        // GpuLight.From gives a tube a half segment longer than 10^−6 m, so its square is above 0.
        vec3 halfSegment = light.directionCosOuter.xyz;
        float halfLengthSq = dot(halfSegment, halfSegment);
        float halfLength = sqrt(halfLengthSq);
        vec3 toNearest = toLight - clamp(dot(toLight, halfSegment) / halfLengthSq, -1.0, 1.0) * halfSegment;
        float nearestSq = dot(toNearest, toNearest);
        // The reach holds the segment: its peak intensity's reach from the segment, plus half its length.
        float segmentReach = max(reach - halfLength, 0.0);
        if (nearestSq >= segmentReach * segmentReach)
            return vec3(0.0);
        float ratio = nearestSq / (segmentReach * segmentReach);
        float window = clamp(1.0 - ratio * ratio, 0.0, 1.0);
        float tubeRadius = max(radius, NearestDistance);
        TubeView view = tubeView(toLight, halfSegment, tubeRadius);
        solidAngle = view.solidAngle;
        cosine = view.cosineVector / max(view.solidAngle, 1e-30);
        // The peak intensity over the area that the capsule shows across its segment is its face radiance.
        falloff = window * window * view.solidAngle
            / (4.0 * tubeRadius * halfLength + PI * tubeRadius * tubeRadius);
    }
    else
    {
        // Before the division by reach², which is then above distanceSq ≥ 0.
        if (distanceSq >= reach * reach)
            return vec3(0.0);
        float ratio = distanceSq / (reach * reach);
        float window = clamp(1.0 - ratio * ratio, 0.0, 1.0);
        falloff = window * window / max(distanceSq, max(radius * radius, NearestDistance * NearestDistance));
        if (light.intensityKind.w == SpotKind)
            // Cone.Of keeps cos(inner) above cos(outer), so the edges of smoothstep differ.
            falloff *= smoothstep(light.directionCosOuter.w, light.spotRadius.x, dot(-L, light.directionCosOuter.xyz));
    }
    vec3 irradiance = light.intensityKind.rgb * falloff;
    // The atmosphere's sun takes the air's tint at the lit point (docs/plans/atmosphere.md, "The sun's tint"). The tint
    // is at most 1, so the product stays finite.
    if (tinted && index == frame.counts.y)
        irradiance *= sunTintAt(position);
    return min(irradiance, vec3(IrradianceLimit));
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

// The path from a surface point to light `index` of the list, before its shadows: the light's irradiance there, the
// direction of its diffuse light with that light's share and a tube's solid angle (lightIrradiance), and what its
// shadow lookup takes, L the unit direction toward the light, d its distance, the geometric normal turned toward the
// light and the sine of its angle with L.
struct LightPath
{
    vec3 irradiance;
    vec3 cosine;
    float solidAngle;
    vec3 L;
    float d;
    vec3 shadowNormal;
    float sinTheta;
};

// The path from a surface point to light `index`, or false where the point takes none of its light. `fiber` as in respond,
// `tinted` as in lightIrradiance. The point takes the light that arrives along its diffuse direction, which is L but for a
// tube. A surface's geometric normal faces every point light that it takes (takesLight), so only a fiber's turns, or a
// surface's toward the middle of a tube that it takes from beyond its horizon. The sine of the angle to the light is the
// same on both sides of a card.
bool lightPath(uint index, bool fiber, bool tinted, ShadingInput s, out LightPath p)
{
    p.irradiance = lightIrradiance(index, tinted, s.position, p.L, p.d, p.cosine, p.solidAngle);
    if (p.irradiance == vec3(0.0) || !takesLight(fiber, s, p.cosine, dot(s.geometricNormal, p.cosine)))
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
// shading normal. A fiber shades the tube as a point light along its cosine, dimmed by the cosine's length. toCenter
// runs from the shaded point to the tube's middle.
DirectTerms tubeResponse(bool fiber, ShadingInput s, SurfaceLobes lobes, vec3 V, LightPath p, GpuLight light,
    vec3 toCenter)
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
    vec3 l = tubeLightDirection(toCenter, light.directionCosOuter.xyz, max(light.spotRadius.y, NearestDistance),
        p.solidAngle, reflect(-V, N), crossSine, alongSine);
    terms.specular = tubeSpecularResponse(lobes, N, V, l, crossSine, alongSine);
    return terms;
}

// The light of light `index` along its path from `s.position` before its shadows, in its two parts: its irradiance times
// the response, plain radiance. `fiber` as in respond.
DirectTerms pathLight(uint index, bool fiber, ShadingInput s, SurfaceLobes lobes, vec3 V, LightPath p)
{
    GpuLight light = lights[index];
    DirectTerms terms = light.intensityKind.w == TubeKind
        ? tubeResponse(fiber, s, lobes, V, p, light, light.positionReach.xyz - s.position)
        : respond(fiber, s, lobes, V, p.L, lightSineAt(light, p.d));
    terms.diffuse *= p.irradiance;
    terms.specular *= p.irradiance;
    return terms;
}

// directLight for one model: a fiber's light if `fiber`, else a surface's. `fiber` as in respond, `tinted` as in
// lightIrradiance. directLight is the one caller, and it passes constants, so each copy of the loop holds one model's
// code and only the tint that it needs. A light's response is evaluated only where its shadows let some of it through.
DirectTerms modelLight(
    bool fiber, bool tinted, ShadingInput s, SurfaceLobes lobes, vec3 V, uint shadowMaps, uint view)
{
    DirectTerms sum;
    sum.diffuse = vec3(0.0);
    sum.specular = vec3(0.0);
    for (uint i = 0u; i < frame.counts.x; i++)
    {
        if (!viewKeeps(view, lights[i]))
            continue;
        LightPath p;
        if (!lightPath(i, fiber, tinted, s, p))
            continue;
        float visibility = pathVisibility(i, p, s.position, shadowMaps);
        if (visibility > 0.0)
        {
            DirectTerms terms = pathLight(i, fiber, s, lobes, V, p);
            sum.diffuse += terms.diffuse * visibility;
            sum.specular += terms.specular * visibility;
        }
    }
    return sum;
}

// Every light of the list that the view keeps reflected toward V in its two parts, each dimmed by its shadows as
// shadowMaps says: a face of map i passes i, and every other caller MainView. The lights add in the order of the list,
// so the sum rounds the same way on every run. A light adds only where takesLight says. A hair card's shadow lookups
// take its geometric normal turned toward the light, the side that the light sees (docs/plans/skin-and-hair.md,
// decision 27). The result is plain radiance: no ambient light, no emission and no exposure. The direct pass divides it
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
        vec3 L;
        float d;
        // A puff scatters a tube's scalar irradiance, so it leaves the diffuse direction aside.
        vec3 cosine;
        float solidAngle;
        // The puffs keep one loop for every frame, so they pay the compare with frame.counts.y for each light.
        vec3 incoming = lightIrradiance(i, true, position, L, d, cosine, solidAngle);
        // At the light's center L is a zero vector, which the far shadows' angle cannot take. A surface there takes
        // nothing either, by its dot(N, L) of 0.
        if (incoming == vec3(0.0) || d == 0.0)
            continue;
        irradiance += incoming * lightVisibility(light, position, L, d, L, 0.0, ShadowMapsMainView);
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
