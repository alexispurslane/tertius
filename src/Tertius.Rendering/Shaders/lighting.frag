#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "gbuffer_inputs.glsl"
#include "octahedral.glsl"
#include "lighting.glsl"

layout(location = 0) in vec3 inRay;
layout(location = 0) out vec4 outColor;

// Hash without sine (Dave Hoskins), stable for the integer cell coordinates used below.
vec3 hash33(vec3 p)
{
    p = fract(p * vec3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return fract((p.xxy + p.yxx) * p.zyx);
}

// A cheap procedural star field fixed to the root frame's axes. Directions are split into cells of a 3D grid (about
// 0.4 degrees across); one cell in fifty holds a star at a hashed position, which gives roughly 450 stars per
// steradian, most of them dim. Each star is a Gaussian about one pixel wide, so its size does not depend on the
// resolution.
vec3 starField(vec3 direction)
{
    // Into the root frame's axes: the inverse of rootRotation, by the quaternion sandwich with the conjugate.
    vec3 u = -frame.rootRotation.xyz;
    direction += 2.0 * cross(u, cross(u, direction) + frame.rootRotation.w * direction);
    const float cells = 150.0;
    vec3 cell = floor(direction * cells);
    vec3 h = hash33(cell);
    if (h.x > 0.02)
        return vec3(0.0);
    vec3 jitter = hash33(cell + 17.0);
    vec3 star = normalize(cell + jitter);
    vec3 offset = direction - star;
    float pixel = max(frame.params.z, 1e-5);
    float falloff = exp(-dot(offset, offset) / (pixel * pixel));
    float brightness = 0.05 + 3.0 * pow(h.y, 8.0);
    vec3 tint = mix(vec3(1.0, 0.82, 0.62), vec3(0.72, 0.82, 1.0), h.z);
    return tint * brightness * falloff;
}

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    vec3 direction = normalize(inRay);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    if (distanceToEye <= 0.0)
    {
        outColor = vec4(starField(direction), 1.0);
        return;
    }

    vec4 emissiveSky = texelFetch(gbufferEmissive, pixel, 0);
    vec3 emissive = emissiveSky.rgb;
    vec4 albedo = texelFetch(gbufferAlbedo, pixel, 0);
    // Albedo alpha 0 marks an unlit pixel: an impostor, whose lighting is baked into its emission.
    if (albedo.a == 0.0)
    {
        outColor = vec4(emissive, 1.0);
        return;
    }

    // The position comes from the ray and the stored distance, so it does not depend on the projection of the
    // depth range that drew the pixel.
    vec3 position = direction * distanceToEye;
    ShadingInput s;
    s.albedo = albedo.rgb;
    vec4 normals = texelFetch(gbufferNormal, pixel, 0);
    s.normal = octDecode(normals.rg);
    s.geometricNormal = octDecode(normals.ba);
    vec2 specularGloss = texelFetch(gbufferSpecularGloss, pixel, 0).rg;
    s.specular = specularGloss.x;
    s.gloss = specularGloss.y;
    s.skyFraction = emissiveSky.a;

    outColor = vec4(shadeSurface(s, position, -direction) + emissive, 1.0);
}
