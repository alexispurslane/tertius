// The lighting model, shared by the deferred direct and ambient passes, the transparent forward pass, the impostor
// capture, the environment faces and the lit puffs.
//
// The PBR BRDF (docs/plans/pbr-brdf.md, "The BRDF"): Lambert diffuse, plus GGX with the height-correlated Smith
// visibility term and Schlick's Fresnel term, with Filament's multiple-scattering energy compensation from the DFG table:
//   f = c_diff / π + D · Vis · F · c,   c_diff = b (1 − m),   F0 = (1 − m) · 0.16 ρ² + m · b
// Each light of the one light list adds f · E · (N·L), where E is its irradiance (docs/plans/local-light-shadows.md, "The
// shaders"). A hair pixel takes each light's response of a fiber instead (hair.glsl), from either side of its card, and
// its ambient light at its fiber normal, its shading normal (docs/plans/skin-and-hair.md, "The hair model"). A skin pixel
// shades as the standard model, with metallic 0. The ambient light comes from one ambient source,
// the scene environment map while it is on and the hemisphere while it is off, which gives a diffuse and a specular
// term (docs/plans/environment-map.md, "The one source of the ambient light"). The estimate of the light around each
// point, the occlusion V and the net bounce G − M, applies to the two ambient terms in occludedAmbient alone
// (docs/plans/screen-space-illumination.md, "The ambient pass"). Every reader of the ambient light reads it through
// ambientTerms (docs/plans/screen-space-reflections.md, "The one place where SSR reads the ambient light").
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
    vec3 baseColor;
    vec3 normal;              // shading normal, after normal mapping
    vec3 geometricNormal;     // interpolated vertex normal: the surface the shadow map sees
    float metallic;
    float roughness;          // r₀, before the floor
    float reflectance;
    float skyFraction;        // s_n, the normal's sky share (skyFraction in hemisphere.glsl)
    float lobeSkyFraction;    // s_l, the lobe's sky share (lobeSkyFraction in hemisphere.glsl)
    float reflectionFootprint; // f, in rad, at least p: how far d moves to a neighboring pixel, set by the caller
    float occlusion;          // V, the unhidden share of the cosine-weighted hemisphere: 1 when nothing hides it
    vec3 bounce;              // G − M, the net bounce, plain radiance: 0 where no estimate exists
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

// f · max(N·L, 0) in its two parts: c_diff / π · (N·L), and D · Vis · F · c · (N·L).
DirectTerms surfaceResponse(ShadingInput s, SurfaceLobes lobes, vec3 V, vec3 L)
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
    vec3 H = halfVectorOf(L, V, N);
    float distribution = ggxDistribution(dot(N, H), lobes.alpha);
    float visibility = smithVisibility(NdotL, lobes.NdotV, lobes.alpha);
    vec3 lobe = schlickFresnel(lobes.f0, dot(V, H)) * (distribution * visibility) * lobes.energyCompensation;
    terms.diffuse = lobes.diffuseColor / PI * NdotL;
    terms.specular = lobe * NdotL;
    return terms;
}

// The radiance toward V per unit irradiance from direction L: a fiber's if `fiber`, else a surface's. `fiber` must be
// whether s shades as hair, which directLight alone decides and modelLight passes on (see directLight).
DirectTerms respond(bool fiber, ShadingInput s, SurfaceLobes lobes, vec3 V, vec3 L)
{
    return fiber ? hairResponse(s, lobes, V, L) : surfaceResponse(s, lobes, V, L);
}

// Whether a light from L reaches the point at all, before its shadows: a fiber takes light from every side, a surface
// only from the side that both its normals face. geometricNdotL is the cosine of s.geometricNormal with L. A normal map
// can tilt the shading normal toward a light on a surface that itself faces away; that surface is in its own shadow.
// `fiber` as in respond. modelLight relies on the surface's rule: it passes a surface's geometric normal and cosine to
// the shadow lookups unturned, which lightVisibility takes only facing the light.
bool takesLight(bool fiber, ShadingInput s, vec3 L, float geometricNdotL)
{
    return fiber || (dot(s.normal, L) > 0.0 && geometricNdotL > 0.0);
}

// The irradiance in W/m² per channel that the light at `index` of the list sends to `position` before its shadows:
// color × intensity over max(d², R², NearestDistance²), times the window of Karis 2013 to the reach and a spot light's
// cone, and the air's tint for the atmosphere's sun, the light at frame.counts.y, at most IrradianceLimit. 0 at or beyond
// the reach. `tinted` may be false only when frame.counts.y is NoAirSun: a constant false leaves the tint's code out of
// the caller's loop. L is the unit direction toward the light, zero when the position is at the light's center, and d
// its distance.
vec3 lightIrradiance(uint index, bool tinted, vec3 position, out vec3 L, out float d)
{
    GpuLight light = lights[index];
    vec3 toLight = light.positionReach.xyz - position;
    // Finite: a kept light lies within 2^62 m of the eye, and so does every shaded point.
    float distanceSq = dot(toLight, toLight);
    // The floor keeps inversesqrt finite at the light's center, where L becomes zero.
    float inverseDistance = inversesqrt(max(distanceSq, 1e-8));
    L = toLight * inverseDistance;
    d = distanceSq * inverseDistance;
    float reach = light.positionReach.w;
    // Before the division by reach², which is then above distanceSq ≥ 0.
    if (distanceSq >= reach * reach)
        return vec3(0.0);
    float ratio = distanceSq / (reach * reach);
    float window = clamp(1.0 - ratio * ratio, 0.0, 1.0);
    float radius = light.spotRadius.y;
    float falloff = window * window / max(distanceSq, max(radius * radius, NearestDistance * NearestDistance));
    if (light.intensityKind.w > 0.5)
        // Cone.Of keeps cos(inner) above cos(outer), so the edges of smoothstep differ.
        falloff *= smoothstep(light.directionCosOuter.w, light.spotRadius.x, dot(-L, light.directionCosOuter.xyz));
    vec3 irradiance = light.intensityKind.rgb * falloff;
    // The atmosphere's sun takes the air's tint at the lit point (docs/plans/atmosphere.md, "The sun's tint"). The tint
    // is at most 1, so the product stays finite.
    if (tinted && index == frame.counts.y)
        irradiance *= sunTintAt(position);
    return min(irradiance, vec3(IrradianceLimit));
}

// The share of `light` that its shadow slot lets through at `position`, by the rule of shadowMaps. L is the unit
// direction toward the light and d its distance. geometricNormal is the surface that the shadow maps see, turned toward
// the light, and geometricNdotL its cosine with L, which must be positive, or 0 for a hair card edge-on to the light.
// The impostor capture, ShadowMapsNone, takes the far shadows alone with weight 1, and no tiles.
float lightVisibility(GpuLight light, vec3 position, vec3 L, float d, vec3 geometricNormal, float geometricNdotL,
    uint shadowMaps)
{
    uint slot = light.shadow.x;
    if (slot == NoSlot)
        return 1.0;
    if (slot == AtlasTilesSlot)
        return shadowMaps == ShadowMapsNone ? 1.0 : tileShadow(light, position, L, d, geometricNormal, geometricNdotL);
    // asin(min(R / d, 1)), the exact angle of a sphere, also inside it; the floor of d keeps the division finite.
    float angularRadius = asin(min(light.spotRadius.y / max(d, 1e-6), 1.0));
    if (slot == FarShadowsOnlySlot || shadowMaps == ShadowMapsNone)
        return farShadowVisibility(position, L, angularRadius, light.occluders, 1.0);
    uint set = light.shadow.y;
    if (shadowMaps == ShadowMapsMainView)
    {
        float visibility = farShadowVisibility(position, L, angularRadius, light.occluders, beyondShadowMaps(position));
        if (visibility > 0.0)
            visibility *= cascadeShadow(set, position, geometricNormal, geometricNdotL);
        return visibility;
    }
    float visibility = farShadowVisibility(position, L, angularRadius, light.occluders, beyondShadowMapsHolding(set, position));
    if (visibility > 0.0)
        visibility *= cascadeShadowHolding(set, position, geometricNormal, geometricNdotL);
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

// Lagarde and de Rousiers 2014, "Moving Frostbite to Physically Based Rendering 3.0", section 4.10.2,
// listing 26. The notes call α "roughness".
float specularOcclusion(float NdotV, float occlusion, float alpha)
{
    return clamp(pow(NdotV + occlusion, exp2(-16.0 * alpha - 1.0)) - 1.0 + occlusion, 0.0, 1.0);
}

// The one place where the estimate applies (docs/plans/screen-space-illumination.md, decision 41): the source's diffuse
// term less its light along the blocked directions, plus the net bounce under the diffuse color, and the specular term
// by V_s. The map's light along them is in the net bounce, G − M, so its whole term stays. The hemisphere has no
// direction at the estimate's texels, so it gives the blocked directions its mean, and the open share V keeps the rest.
// The max holds the map's term where the half-resolution M exceeds the irradiance at the pixel's own normal. It keeps
// the terms apart, because SSR replaces the occluded specular term where its ray hits.
AmbientTerms occludedAmbient(AmbientTerms terms, ShadingInput s, SurfaceLobes lobes)
{
    terms.diffuse = environmentMapOn ? max(terms.diffuse + lobes.diffuseColor * s.bounce, vec3(0.0))
                                     : terms.diffuse * s.occlusion + lobes.diffuseColor * s.bounce;
    terms.specular *= specularOcclusion(lobes.NdotV, s.occlusion, lobes.alpha);
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
        s.map, ambientSpecularDirection(s, V), flooredRoughness(s.roughness), s.reflectionFootprint);
    terms.specular = specularWeight(lobes) * incoming;
    return terms;
}

// The ambient light of a surface point in its two terms, from the frame's ambient source, the map while it is on and
// the hemisphere while it is off, then occluded (docs/plans/screen-space-reflections.md, "The one place where SSR
// reads the ambient light", and docs/plans/environment-map.md, "The one source of the ambient light"). ambientLight
// and the pass that adds the reflections' delta (the full-resolution trace, or the upsample at half resolution) read
// the ambient light only through this function. V points from the surface to the eye.
AmbientTerms ambientTerms(ShadingInput s, SurfaceLobes lobes, vec3 V)
{
    AmbientTerms source = environmentMapOn ? environmentAmbient(s, lobes, V) : hemisphereAmbient(s, lobes);
    return occludedAmbient(source, s, lobes);
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

// directLight for one model: a fiber's light if `fiber`, else a surface's. `fiber` as in respond, `tinted` as in
// lightIrradiance. directLight is the one caller, and it passes constants, so each copy of the loop holds one model's
// code and only the tint that it needs.
DirectTerms modelLight(
    bool fiber, bool tinted, ShadingInput s, SurfaceLobes lobes, vec3 position, vec3 V, uint shadowMaps, uint view)
{
    DirectTerms sum;
    sum.diffuse = vec3(0.0);
    sum.specular = vec3(0.0);
    for (uint i = 0u; i < frame.counts.x; i++)
    {
        GpuLight light = lights[i];
        if (!viewKeeps(view, light))
            continue;
        vec3 L;
        float d;
        vec3 irradiance = lightIrradiance(i, tinted, position, L, d);
        float geometricNdotL = dot(s.geometricNormal, L);
        if (irradiance == vec3(0.0) || !takesLight(fiber, s, L, geometricNdotL))
            continue;
        // A surface's geometric normal faces every light that it takes (takesLight), so only a fiber's turns.
        vec3 shadowNormal = fiber && geometricNdotL < 0.0 ? -s.geometricNormal : s.geometricNormal;
        float visibility = lightVisibility(
            light, position, L, d, shadowNormal, fiber ? abs(geometricNdotL) : geometricNdotL, shadowMaps);
        if (visibility > 0.0)
        {
            DirectTerms terms = respond(fiber, s, lobes, V, L);
            vec3 weight = irradiance * visibility;
            sum.diffuse += terms.diffuse * weight;
            sum.specular += terms.specular * weight;
        }
    }
    return sum;
}

// Every light of the list that the view keeps reflected toward V in its two parts, each dimmed by its shadows as
// shadowMaps says: a face of map i passes i, and every other caller MainView. The lights add in the order of the list,
// so the sum rounds the same way on every run. A light adds only where takesLight says. A hair card's shadow lookups
// take its geometric normal turned toward the light, the side that the light sees (docs/plans/skin-and-hair.md,
// decision 27). The result is plain radiance: no ambient light, no emission and no exposure. The direct pass writes it
// into the direct image (Shaders/direct_pass.glsl).
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
DirectTerms directLight(ShadingInput s, SurfaceLobes lobes, vec3 position, vec3 V, uint shadowMaps, uint view)
{
    bool tinted = frame.counts.y != NoAirSun;
    if (!dropsFibers && s.model == ShadingHair)
        return tinted ? modelLight(true, true, s, lobes, position, V, shadowMaps, view)
                      : modelLight(true, false, s, lobes, position, V, shadowMaps, view);
    return tinted ? modelLight(false, true, s, lobes, position, V, shadowMaps, view)
                  : modelLight(false, false, s, lobes, position, V, shadowMaps, view);
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
    ShadingInput s, SurfaceLobes lobes, vec3 preExposedEmission, vec3 position, vec3 V, uint shadowMaps, float exposure)
{
    vec3 reflected = ambientLight(s, lobes, V) + directSum(directLight(s, lobes, position, V, shadowMaps, MainView));
    return min(exposed(reflected, exposure) + preExposedEmission, vec3(MaxRadiance));
}

// lobesRadiance with the lobes of the material itself: every opaque surface.
vec3 surfaceRadiance(ShadingInput s, vec3 preExposedEmission, vec3 position, vec3 V, uint shadowMaps, float exposure)
{
    return lobesRadiance(s, lobesOf(s, V), preExposedEmission, position, V, shadowMaps, exposure);
}

// The light that the lights that the view keeps send from a surface point toward V, plus its emission, in plain
// radiance: no ambient light and no exposure, as the environment faces store it. Capped at MaxRadiance (exposure.glsl).
vec3 directRadiance(ShadingInput s, vec3 emission, vec3 position, vec3 V, uint shadowMaps, uint view)
{
    SurfaceLobes lobes = lobesOf(s, V);
    return min(directSum(directLight(s, lobes, position, V, shadowMaps, view)) + emission, vec3(MaxRadiance));
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
        // The puffs keep one loop for every frame, so they pay the compare with frame.counts.y for each light.
        vec3 incoming = lightIrradiance(i, true, position, L, d);
        // At the light's center L is a zero vector, which the far shadows' angle cannot take. A surface there takes
        // nothing either, by its dot(N, L) of 0.
        if (incoming == vec3(0.0) || d == 0.0)
            continue;
        irradiance += incoming * lightVisibility(light, position, L, d, L, 1.0, ShadowMapsMainView);
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
// G-buffer that the occlusion passes read, so it takes no estimate: the occlusion is 1 and the bounce 0, and the surface
// behind it takes the estimate at its pixel (docs/plans/screen-space-illumination.md, "The passes that keep V = 1").
ShadingInput forwardShading(SurfaceSample s, vec3 position, vec4 hemisphere, uint map, vec3 V)
{
    ShadingInput shading;
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
    shading.occlusion = 1.0;
    shading.bounce = vec3(0.0);
    shading.model = s.model;
    shading.strand = s.strand;
    return shading;
}
#endif

#endif
