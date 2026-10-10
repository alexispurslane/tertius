// The lighting model, shared by the deferred lighting pass and the transparent forward pass.
//
// Energy-normalized Blinn-Phong (ARCHITECTURE.md, Shading):
//   f = albedo / π · (1 − F) + F · (n + 8) / (8π) · (N·H)^n
//   n  = 2^(1 + 11 · gloss), so gloss 0 gives n = 2 and gloss 1 gives n = 4096
//   F  = Schlick: F0 + (1 − F0)(1 − L·H)^5, with F0 = 0.16 · specular² (specular 0.5 gives F0 = 0.04)
// Lights add f · E · (N·L), where E is the irradiance of the light. Ambient light is a hemisphere: ground color
// below, sky color above, blended by the world-space normal's y.
//
// The sun is also dimmed by the analytic eclipse term (occluder spheres: planets and moons) and, within 2 km, by the
// cascaded shadow maps (shadows.glsl). The two multiply.
#ifndef TERTIUS_LIGHTING_GLSL
#define TERTIUS_LIGHTING_GLSL

#include "shadows.glsl"

struct ShadingInput
{
    vec3 albedo;
    vec3 normal;            // shading normal, after normal mapping
    vec3 geometricNormal;   // interpolated vertex normal: the surface the shadow map sees
    float specular;
    float gloss;
};

float specularExponent(float gloss)
{
    return exp2(1.0 + 11.0 * clamp(gloss, 0.0, 1.0));
}

float fresnelF0(float specularIntensity)
{
    return 0.16 * specularIntensity * specularIntensity;
}

// Reflected radiance for unit irradiance from direction L, including the cosine term.
vec3 blinnPhong(ShadingInput s, vec3 V, vec3 L)
{
    vec3 N = s.normal;
    float NdotL = dot(N, L);
    if (NdotL <= 0.0)
        return vec3(0.0);
    vec3 halfVector = L + V;
    float halfLengthSq = dot(halfVector, halfVector);
    vec3 H = halfLengthSq > 1e-12 ? halfVector * inversesqrt(halfLengthSq) : N;
    float NdotH = max(dot(N, H), 0.0);
    float LdotH = max(dot(L, H), 0.0);
    float n = specularExponent(s.gloss);
    float f0 = fresnelF0(s.specular);
    float F = f0 + (1.0 - f0) * pow(1.0 - LdotH, 5.0);
    float specular = (n + 8.0) / (8.0 * PI) * pow(NdotH, n);
    return (s.albedo * ((1.0 - F) / PI) + vec3(F * specular)) * NdotL;
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

// Analytic eclipse and planet-shadow test against every occluder sphere. The soft edge comes from the sun's
// angular radius.
float sunVisibility(vec3 position)
{
    vec3 L = frame.sunDirection.xyz;
    float sunRadius = max(frame.sunDirection.w, 1e-5);
    float visibility = 1.0;
    for (uint i = 0u; i < frame.counts.y; i++)
    {
        vec4 occluder = occluders[i];
        vec3 toCenter = occluder.xyz - position;
        float distanceToCenter = length(toCenter);
        float occluderRadius = distanceToCenter > occluder.w ? asin(occluder.w / distanceToCenter) : 0.5 * PI;
        // atan2 of |cross| and dot keeps precision at small angles, where acos(dot) does not.
        float separation = atan(length(cross(toCenter, L)), dot(toCenter, L));
        visibility *= 1.0 - discCoverage(sunRadius, occluderRadius, separation);
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

// Hemisphere ambient light reflected by a surface.
vec3 ambientLight(ShadingInput s)
{
    return mix(frame.ambientGround.rgb, frame.ambientSky.rgb, s.normal.y * 0.5 + 0.5) * s.albedo;
}

// Sunlight reflected toward V, dimmed by the analytic eclipse term and, when shadowMaps is set, by the cascades.
// Impostor captures pass false: the cascades cover the main view, not the capture.
vec3 sunLight(ShadingInput s, vec3 position, vec3 V, bool shadowMaps)
{
    vec3 L = frame.sunDirection.xyz;
    // A normal map can tilt the shading normal toward the sun on a surface that itself faces away; that surface is
    // in its own shadow.
    float geometricNdotL = dot(s.geometricNormal, L);
    if (dot(s.normal, L) <= 0.0 || geometricNdotL <= 0.0)
        return vec3(0.0);
    float visibility = sunVisibility(position);
    if (shadowMaps && visibility > 0.0)
        visibility *= sunShadow(position, s.geometricNormal, geometricNdotL);
    return visibility > 0.0 ? blinnPhong(s, V, L) * frame.sunRadiance.rgb * visibility : vec3(0.0);
}

// Light from the point and spot lights reflected toward V.
vec3 localLights(ShadingInput s, vec3 position, vec3 V)
{
    vec3 color = vec3(0.0);
    for (uint i = 0u; i < frame.counts.x; i++)
    {
        GpuLight light = lights[i];
        vec3 toLight = light.positionRange.xyz - position;
        float distanceSq = dot(toLight, toLight);
        float range = light.positionRange.w;
        if (distanceSq >= range * range)
            continue;
        vec3 lightDirection = toLight * inversesqrt(max(distanceSq, 1e-8));
        float attenuation = distanceAttenuation(distanceSq, range);
        if (light.radianceKind.w > 0.5)
        {
            float cosAngle = dot(-lightDirection, light.directionCosOuter.xyz);
            attenuation *= smoothstep(light.directionCosOuter.w, light.spot.x, cosAngle);
        }
        if (attenuation > 0.0)
            color += blinnPhong(s, V, lightDirection) * light.radianceKind.rgb * attenuation;
    }
    return color;
}

// All light that leaves a surface point toward the viewer, except emission. V points from the surface to the eye.
vec3 shadeSurface(ShadingInput s, vec3 position, vec3 V)
{
    return ambientLight(s) + sunLight(s, position, V, true) + localLights(s, position, V);
}

#endif
