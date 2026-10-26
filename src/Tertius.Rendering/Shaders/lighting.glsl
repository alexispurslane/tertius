// The lighting model, shared by the deferred direct and ambient passes, the transparent forward pass, the impostor
// capture, the environment faces and the lit puffs.
//
// The PBR BRDF (docs/plans/pbr-brdf.md, "The BRDF"): Lambert diffuse, plus GGX with the height-correlated Smith
// visibility term and Schlick's Fresnel term, with Filament's multiple-scattering energy compensation from the DFG table:
//   f = c_diff / π + D · Vis · F · c,   c_diff = b (1 − m),   F0 = (1 − m) · 0.16 ρ² + m · b
// The lights add f · E · (N·L), where E is the irradiance of the light. The ambient light comes from one ambient source,
// the scene environment map while it is on and the hemisphere while it is off, which gives a diffuse and a specular
// term (docs/plans/environment-map.md, "The one source of the ambient light"). The estimate of the light around each
// point, the occlusion V and the bounce G, applies to the two ambient terms in occludedAmbient alone
// (docs/plans/screen-space-illumination.md, "The ambient pass"). Every reader of the ambient light reads it through
// ambientTerms (docs/plans/screen-space-reflections.md, "The one place where SSR reads the ambient light").
// lobesRadiance pre-exposes the sum and caps it at MaxRadiance, the surfaces' share of the HDR image (exposure.glsl).
//
// The sun is also dimmed by the far shadows (a sphere per caster, tested analytically) and, within 2 km, by the cascaded
// shadow maps (shadows.glsl). The two multiply.
#ifndef TERTIUS_LIGHTING_GLSL
#define TERTIUS_LIGHTING_GLSL

#include "shadows.glsl"
#include "hemisphere.glsl"
#include "brdf.glsl"
#include "exposure.glsl"
#include "environment.glsl"

// How a view samples the sun's shadow maps (docs/plans/environment-map.md, "The sun's shadow maps in a face").
const uint SunShadowsNone = 0u;       // far shadows only: the impostor capture
const uint SunShadowsMainView = 1u;   // the cascades by view depth with their blend bands: the direct and transparent passes, the lit puffs
const uint SunShadowsHolding = 2u;    // the finest cascade whose box holds the point: the environment faces

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
    float reflectionFootprint; // f, in rad, at least p: how far d moves to the next pixel, set by the caller
    float occlusion;          // V, the unhidden share of the cosine-weighted hemisphere: 1 when nothing hides it
    vec3 bounce;              // G, plain radiance: 0 where no estimate exists
};

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

// f · max(N·L, 0): the radiance toward V per unit irradiance from direction L.
vec3 brdf(ShadingInput s, SurfaceLobes lobes, vec3 V, vec3 L)
{
    vec3 N = s.normal;
    float NdotL = dot(N, L);
    if (NdotL <= 0.0)
        return vec3(0.0);
    vec3 halfVector = L + V;
    float halfLengthSq = dot(halfVector, halfVector);
    vec3 H = halfLengthSq > 1e-12 ? halfVector * inversesqrt(halfLengthSq) : N;
    float distribution = ggxDistribution(dot(N, H), lobes.alpha);
    float visibility = smithVisibility(NdotL, lobes.NdotV, lobes.alpha);
    vec3 lobe = schlickFresnel(lobes.f0, dot(V, H)) * (distribution * visibility) * lobes.energyCompensation;
    return (lobes.diffuseColor / PI + lobe) * NdotL;
}

// The fraction of the sun's disc (angular radius sunRadius) hidden by an occluder's disc (angular radius
// occluderRadius) whose center is `separation` radians from the sun's center.
float discCoverage(float sunRadius, float occluderRadius, float separation)
{
    if (separation >= sunRadius + occluderRadius)
        return 0.0;
    if (occluderRadius > 4.0 * sunRadius)
    {
        // A planet seen from near its surface. Its limb is almost straight across the small sun disc, so the
        // hidden part is a circular segment. x is the sun center's distance outside the limb, in sun radii.
        // This form also avoids the float cancellation of the general formula when the radii differ a lot.
        float x = clamp((separation - occluderRadius) / sunRadius, -1.0, 1.0);
        return (acos(x) - x * sqrt(1.0 - x * x)) / PI;
    }
    if (separation <= abs(occluderRadius - sunRadius))
        return occluderRadius >= sunRadius ? 1.0 : (occluderRadius * occluderRadius) / (sunRadius * sunRadius);
    // Two discs of similar size, such as a moon eclipsing the sun: the exact lens area.
    float d = separation, r1 = sunRadius, r2 = occluderRadius;
    float d2 = d * d, r1s = r1 * r1, r2s = r2 * r2;
    float a1 = r1s * acos(clamp((d2 + r1s - r2s) / (2.0 * d * r1), -1.0, 1.0));
    float a2 = r2s * acos(clamp((d2 + r2s - r1s) / (2.0 * d * r2), -1.0, 1.0));
    float k = 0.5 * sqrt(max((-d + r1 + r2) * (d + r1 - r2) * (d - r1 + r2) * (d + r1 + r2), 0.0));
    return clamp((a1 + a2 - k) / (PI * r1s), 0.0, 1.0);
}

// The far shadows: an analytic eclipse test against the bounding sphere of every caster. The soft edge comes from the
// sun's angular radius. A sphere shadows only points outside it, so a caster never shadows its own surface; a planet's
// ground faces away from the sun at night anyway. The first frame.counts.w spheres belong to casters that the shadow maps
// also draw: they count by beyondMaps, the weight of the far shadow where the maps end (beyondShadowMaps). Impostor
// captures pass 1. Eclipse.SunVisibility is the same test on the CPU.
float sunVisibility(vec3 position, float beyondMaps)
{
    vec3 L = frame.sunDirection.xyz;
    float sunRadius = max(frame.sunDirection.w, 1e-5);
    float visibility = 1.0;
    for (uint i = 0u; i < frame.counts.y; i++)
    {
        float weight = i < frame.counts.w ? beyondMaps : 1.0;
        if (weight <= 0.0)
            continue;
        vec4 occluder = occluders[i];
        vec3 toCenter = occluder.xyz - position;
        float distanceToCenter = length(toCenter);
        if (distanceToCenter <= occluder.w)
            continue;
        // The test above makes the ratio below 1 only in exact arithmetic: a division may be off by 2.5 ULP, and for a
        // distance within 2 ULP of the radius, such as on a planet's own ground, the ratio can come out above 1.
        float occluderRadius = asin(min(occluder.w / distanceToCenter, 1.0));
        // atan2 of |cross| and dot keeps precision at small angles, where acos(dot) does not.
        float separation = atan(length(cross(toCenter, L)), dot(toCenter, L));
        visibility *= 1.0 - weight * discCoverage(sunRadius, occluderRadius, separation);
    }
    return visibility;
}

// Smooth range window from Karis 2013: (1 − (d/r)⁴)² / d². It reaches zero exactly at the light's range.
float distanceAttenuation(float distanceSq, float range)
{
    float ratio = distanceSq / (range * range);
    float window = clamp(1.0 - ratio * ratio, 0.0, 1.0);
    return window * window / max(distanceSq, 1e-4);
}

// The share of the sun that reaches a position: the far shadows, and the cascades by the rule that sunShadows names.
// geometricNormal is the surface that the shadow map sees, and geometricNdotL its cosine with the sun direction, which
// must be positive.
float sunReaches(vec3 position, vec3 geometricNormal, float geometricNdotL, uint sunShadows)
{
    if (sunShadows == SunShadowsMainView)
    {
        float visibility = sunVisibility(position, beyondShadowMaps(position));
        if (visibility > 0.0)
            visibility *= sunShadow(position, geometricNormal, geometricNdotL);
        return visibility;
    }
    if (sunShadows == SunShadowsHolding)
    {
        float visibility = sunVisibility(position, beyondShadowMapsHolding(position));
        if (visibility > 0.0)
            visibility *= sunShadowHolding(position, geometricNormal, geometricNdotL);
        return visibility;
    }
    return sunVisibility(position, 1.0);
}

// The share of a point or spot light's radiance that reaches a position: the distance attenuation times the cone of a
// spot light. 0 beyond its range. lightDirection is the unit direction from the position toward the light.
float lightReaches(GpuLight light, vec3 position, out vec3 lightDirection)
{
    vec3 toLight = light.positionRange.xyz - position;
    float distanceSq = dot(toLight, toLight);
    float range = light.positionRange.w;
    lightDirection = toLight * inversesqrt(max(distanceSq, 1e-8));
    if (distanceSq >= range * range)
        return 0.0;
    float attenuation = distanceAttenuation(distanceSq, range);
    if (light.radianceKind.w > 0.5)
    {
        float cosAngle = dot(-lightDirection, light.directionCosOuter.xyz);
        attenuation *= smoothstep(light.directionCosOuter.w, light.spot.x, cosAngle);
    }
    return attenuation;
}

// W = E · c, the compensated specular albedo: the weight of the light that arrives along the specular lobe in the light
// toward the eye, and the share of a uniform light that the specular part reflects. Every ambient source and the
// reflection trace weight specular light by it, and a pane of glass lets through what it leaves (transparent.frag).
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

// The one place where the estimate applies (docs/plans/screen-space-illumination.md): the open share V of the source's
// diffuse term, plus the bounce G under the diffuse color, and the specular term by V_s. It reads nothing about the
// source of the terms, and keeps them apart, because SSR replaces the occluded specular term where its ray hits.
AmbientTerms occludedAmbient(AmbientTerms terms, ShadingInput s, SurfaceLobes lobes)
{
    terms.diffuse = terms.diffuse * s.occlusion + lobes.diffuseColor * s.bounce;
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
    terms.diffuse = lobes.diffuseColor * environmentDiffuse(s.normal);
    vec3 incoming = environmentSpecular(
        ambientSpecularDirection(s, V), flooredRoughness(s.roughness), s.reflectionFootprint);
    terms.specular = specularWeight(lobes) * incoming;
    return terms;
}

// The ambient light of a surface point in its two terms, from the frame's ambient source, the map while it is on and
// the hemisphere while it is off, then occluded (docs/plans/screen-space-reflections.md, "The one place where SSR
// reads the ambient light", and docs/plans/environment-map.md, "The one source of the ambient light"). ambientLight
// and the reflection trace read the ambient light only through this function. V points from the surface to the eye.
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

// Sunlight reflected toward V, dimmed by the analytic eclipse term and by the cascades as sunShadows says.
vec3 sunLight(ShadingInput s, SurfaceLobes lobes, vec3 position, vec3 V, uint sunShadows)
{
    vec3 L = frame.sunDirection.xyz;
    // A normal map can tilt the shading normal toward the sun on a surface that itself faces away; that surface is
    // in its own shadow.
    float geometricNdotL = dot(s.geometricNormal, L);
    if (dot(s.normal, L) <= 0.0 || geometricNdotL <= 0.0)
        return vec3(0.0);
    float visibility = sunReaches(position, s.geometricNormal, geometricNdotL, sunShadows);
    return visibility > 0.0 ? brdf(s, lobes, V, L) * frame.sunRadiance.rgb * visibility : vec3(0.0);
}

// Light from the point and spot lights reflected toward V. They add in the order of the light list, so the sum rounds
// the same way on every run.
vec3 localLights(ShadingInput s, SurfaceLobes lobes, vec3 position, vec3 V)
{
    vec3 color = vec3(0.0);
    for (uint i = 0u; i < frame.counts.x; i++)
    {
        GpuLight light = lights[i];
        vec3 lightDirection;
        float attenuation = lightReaches(light, position, lightDirection);
        if (attenuation > 0.0)
            color += brdf(s, lobes, V, lightDirection) * light.radianceKind.rgb * attenuation;
    }
    return color;
}

// The light that the sun and the point and spot lights send from a surface point toward V, in plain radiance: no ambient
// light, no emission and no exposure. The direct pass writes it into the direct image (Shaders/direct.frag).
vec3 directLight(ShadingInput s, SurfaceLobes lobes, vec3 position, vec3 V, uint sunShadows)
{
    return sunLight(s, lobes, position, V, sunShadows) + localLights(s, lobes, position, V);
}

// All light that leaves a surface point of the given lobes toward the eye, pre-exposed and capped at MaxRadiance, the
// surfaces' share of the HDR image (exposure.glsl): the reflected light times `exposure`, plus `preExposedEmission`,
// which the caller has already exposed and capped. The cap applies to the whole sum, because the share bounds what a
// surface writes, whatever the parts.
// The reflected light is capped before it is multiplied, so the product stays a finite single float (exposure.glsl).
// V points from the surface to the eye. Impostor captures pass SunShadowsNone and an exposure of 1, because the atlas
// stores radiance. The main view's opaque surfaces split the same cap over the direct and the ambient pass
// (docs/plans/screen-space-illumination.md, "The cap at MaxRadiance").
vec3 lobesRadiance(
    ShadingInput s, SurfaceLobes lobes, vec3 preExposedEmission, vec3 position, vec3 V, uint sunShadows, float exposure)
{
    vec3 reflected = ambientLight(s, lobes, V) + directLight(s, lobes, position, V, sunShadows);
    return min(exposed(reflected, exposure) + preExposedEmission, vec3(MaxRadiance));
}

// lobesRadiance with the lobes of the material itself: every opaque surface.
vec3 surfaceRadiance(ShadingInput s, vec3 preExposedEmission, vec3 position, vec3 V, uint sunShadows, float exposure)
{
    return lobesRadiance(s, lobesOf(s, V), preExposedEmission, position, V, sunShadows, exposure);
}

// The light that the sun and the point and spot lights send from a surface point toward V, plus its emission, in plain
// radiance: no ambient light and no exposure, as the environment faces store it. Capped at MaxRadiance (exposure.glsl).
vec3 directRadiance(ShadingInput s, vec3 emission, vec3 position, vec3 V, uint sunShadows)
{
    SurfaceLobes lobes = lobesOf(s, V);
    return min(directLight(s, lobes, position, V, sunShadows) + emission, vec3(MaxRadiance));
}

// The light that a lit puff at `position` sends toward the eye (docs/plans/particles.md, "Lit puffs"). A puff has no
// surface and no normal: it scatters the light that reaches its center equally in every direction, which is the mean over
// every facing of a matte surface lit by the same lights. The mean of the cosine over every facing is 1/4, so the sun and
// each point and spot light give albedo / (4π) times their irradiance. The ambient light gives the albedo times the
// mean radiance of its source: the map's without the stars, or the mean of the sky and the ground color, whose shares
// average one half. The sun's visibility samples the cascades with the sun's direction as the normal, which needs no
// normal offset and no receiver-plane slope.
vec3 scatteredLight(vec3 albedo, vec3 position)
{
    vec3 meanAmbient = environmentMapOn
        ? environmentMeanRadiance()
        : 0.5 * (frame.ambientSky.rgb + frame.ambientGround.rgb);
    vec3 irradiance = frame.sunRadiance.rgb * sunReaches(position, frame.sunDirection.xyz, 1.0, SunShadowsMainView);
    for (uint i = 0u; i < frame.counts.x; i++)
    {
        GpuLight light = lights[i];
        vec3 lightDirection;
        irradiance += light.radianceKind.rgb * lightReaches(light, position, lightDirection);
    }
    return albedo * (meanAmbient + irradiance * (1.0 / (4.0 * PI)));
}

#endif
