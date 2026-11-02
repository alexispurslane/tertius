// The surface of a half-resolution texel, which the bounce light's near steps read (docs/plans/screen-space-illumination.md,
// "A near step"): its plane in the texel grid, over the part of its square that reaches toward each edge neighbor that
// continues it, by the pixels' rule of depth_pyramid.glsl (planesContinue, sampleSurface). The occlusion depth pass
// writes each texel's copy of its depth and normal and its continuation flags once per frame, and the steps read them.
// SectorSearch.TexelSurface is the C# copy. Change both together.
#ifndef TERTIUS_TEXEL_SURFACE_GLSL
#define TERTIUS_TEXEL_SURFACE_GLSL

#include "gtao.glsl"
#include "depth_pyramid.glsl"

// The edge neighbors of a texel in the order of its continuation flags: left, up, right, down.
const ivec2 EdgeNeighbors[4] = ivec2[4](ivec2(-1, 0), ivec2(0, -1), ivec2(1, 0), ivec2(0, 1));

// The depth pass's copy of a half-resolution texel inside the image, from the G-buffer at its representative pixel 2t:
// x the bits of its view depth, SkyDepth for sky, and y the normals target's ba, the octahedral geometric normal, as
// packSnorm2x16 packs it. The copy is exact: the normal's components are SNORM16 values k / 32767, and their product
// with 32767 lies within 10^-2 of k, so packSnorm2x16's round to nearest gives back k.
uvec2 texelDepthAndNormal(ivec2 texel)
{
    ivec2 pixel = 2 * texel;
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    vec3 ray = gtaoViewPosition(pixelScreenPosition(pixel), 1.0);
    // The distance target holds 0 for sky. SkyDepth puts sky beyond every radius, where an infinite depth gives NaN.
    float z = distanceToEye > 0.0 ? distanceToEye / length(ray) : SkyDepth;
    return uvec2(floatBitsToUint(z), packSnorm2x16(texelFetch(gbufferNormal, pixel, 0).ba));
}

// The plane of a half-resolution texel t that sees a surface, in the texel grid, from its copy: through P(t) along its
// geometric normal, centered at t (SectorSearch.TexelPlane).
PixelPlane texelPlane(ivec2 t, uvec2 depthAndNormal)
{
    float z = depthOf(depthAndNormal);
    vec3 n = geometricNormalOf(depthAndNormal);
    vec3 position = gtaoViewPosition(pixelScreenPosition(2 * t), z);
    // A texel spans two pixels, 2 / W of the screen: the view ray's x changes by 4 t_x / W per texel, and its y by
    // −4 t_y / H.
    vec2 tangents = cameraTangents();
    return samplePlane(vec2(t), 1.0 / z, dot(n, position), length(position),
        vec2(n.x * 4.0 * tangents.x / float(fullSize().x), -n.y * 4.0 * tangents.y / float(fullSize().y)));
}

// The continuation flags of a texel t that sees a surface, from its copy: bit k is set where edge neighbor k of
// EdgeNeighbors continues t's plane. A neighbor beyond the image continues it, as no depth edge lies there, and one that
// sees a surface continues it where planesContinue. Sky does not. Each neighbor's copy comes from the G-buffer, the very
// bits that the depth pass stores for it, so the flags are those of the copies that the steps read. Every texelFetch
// stays inside its image: a neighbor inside the half image has its representative pixel inside the full one.
uint continuationFlags(ivec2 t, uvec2 depthAndNormal)
{
    PixelPlane plane = texelPlane(t, depthAndNormal);
    uint flags = 0u;
    for (int side = 0; side < 4; side++)
    {
        ivec2 neighbor = t + EdgeNeighbors[side];
        bool continues = true;
        if (insideHalf(neighbor))
        {
            uvec2 other = texelDepthAndNormal(neighbor);
            continues = depthOf(other) < SkyDepth && planesContinue(plane, texelPlane(neighbor, other));
        }
        if (continues)
            flags |= 1u << side;
    }
    return flags;
}

// The surface of a texel t that sees a surface, from its copy and its continuation flags (sampleSurface).
PixelSurface texelSurface(ivec2 t, uvec2 depthAndNormal, uint flags)
{
    return sampleSurface(texelPlane(t, depthAndNormal), notEqual(uvec4(flags) & uvec4(1u, 2u, 4u, 8u), uvec4(0u)));
}

#endif
