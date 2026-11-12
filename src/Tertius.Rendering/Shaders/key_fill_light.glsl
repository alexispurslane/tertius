// The direct light of a pixel under key and fill (docs/plans/light-pairs.md, "Key and fill"): every light of the main
// view with its shadow, as directLight takes them, but each light's response is its full response only by its key
// share, and its fill response by the rest. The fill response is the light's diffuse light as a point light gives it,
// with no shine. Only direct_key_fill.frag and key_fill_corners.frag include it.
#ifndef TERTIUS_KEY_FILL_LIGHT_GLSL
#define TERTIUS_KEY_FILL_LIGHT_GLSL

#include "gbuffer_surface.glsl"
#include "key_fill.glsl"

// The share of a corner whose surface lies off the pixel's plane, beside a corner on it.
const float OffSurfaceShare = 0.01;

// The place in the light list that no light has: the next key of a tile whose keys of a shape are all read.
const uint NoLight = 0xFFFFFFFFu;

// The fill response of the light of path p, per unit of p.irradiance: the diffuse light that the surface takes from the
// light as a point at its center. A point light's is its own diffuse light. A tube's is its diffuse light too, from its
// cosine vector. A rectangle's is that of a point light of the face's projected solid angle (RectView.PointSolidAngle),
// which misses the integral over the face by under 1 % from 8 half diagonals away and by more nearer. `shape` as in
// lightIrradiance.
vec3 fillResponse(uint shape, ShadingInput s, SurfaceLobes lobes, LightPath p)
{
    float cosine = max(dot(s.normal, shape == TubeShape ? p.cosine : p.L), 0.0);
    if (shape == RectShape)
    {
        float scale = 1.0 / max(p.d, 1e-6);
        cosine *= max(-4.0 * dot(p.toCenter * scale, cross(p.halfWidth * scale, p.halfHeight * scale)), 0.0);
    }
    return lobes.diffuseColor * (cosine / PI);
}

// The share of each corner of tile `t`, at `tile` of the grid, in the pixel's key shares: the bilinear weight of the
// corner's place, times how near the corner's surface point lies to the plane of the pixel's surface. The shares add up
// to 1, or all are 0 where no corner of the tile holds a lit surface. A corner counts as on the pixel's plane within the
// world size of a tile at the pixel. A corner off the plane keeps OffSurfaceShare of its weight, so that a pixel with no
// corner on its plane takes the corners that it has, and so that the shares stay continuous where a corner leaves the
// plane.
vec4 cornerShares(ShadingInput s, ivec2 pixel, ivec2 tile, uint t)
{
    ivec2 size = ivec2(frame.viewport.xy);
    vec2 f = vec2(pixel - tile * TileSize) / float(TileSize);
    vec4 bilinear = vec4((1.0 - f.x) * (1.0 - f.y), f.x * (1.0 - f.y), (1.0 - f.x) * f.y, f.x * f.y);
    float tolerance = max(length(s.position) * frame.params.z * float(TileSize), 1e-6);
    vec4 distances = lightTiles[t].cornerDistance;
    vec4 valid;
    vec4 near;
    for (int c = 0; c < 4; c++)
    {
        ivec2 corner = tile + ivec2(c & 1, c >> 1);
        vec3 position = normalize(viewRayThrough(vec2(cornerPixel(corner, size)) + 0.5)) * distances[c];
        float off = dot(s.geometricNormal, position - s.position) / tolerance;
        valid[c] = distances[c] > 0.0 ? 1.0 : 0.0;
        near[c] = max(1.0 - off * off, 0.0);
    }
    vec4 shares = bilinear * valid * (near + OffSurfaceShare);
    float sum = shares.x + shares.y + shares.z + shares.w;
    if (sum <= 0.0)
    {
        // Every corner with a bilinear weight is empty: the other corners stand in, evenly.
        shares = valid;
        sum = valid.x + valid.y + valid.z + valid.w;
    }
    return sum > 0.0 ? shares / sum : vec4(0.0);
}

// shapeLight's loop over the lights of the list of one shape, `shape`, added to `sum`, with each light's key share from
// the `count` entries of tile `t` from `first`, which are the tile's key lights of that shape in the order of the light
// list. A light of the list that is the next entry takes its key share from it, by the pixel's `shares` of the four
// corners; every other light has the key share `unkeyed` (keyFillLight). `fiber`, `tinted` and `shape` as in
// shapeLight; keyModelLight is the one caller, and it passes constants. A fiber takes every light's full response: the
// fill response is a surface's.
void keyShapeLight(bool fiber, bool tinted, uint shape, ShadingInput s, SurfaceLobes lobes, vec3 V, uint t, uint first,
    uint count, vec4 shares, float unkeyed, inout DirectTerms sum)
{
    if ((frame.counts.w & shape) == 0u)
        return;
    uint cursor = first;
    uint end = first + count;
    uint nextKey = cursor < end ? lightTiles[t].entries[cursor].x : NoLight;
    for (uint i = 0u; i < frame.counts.x; i++)
    {
        if (!viewKeeps(MainView, lights[i]) || shapeOf(lights[i]) != shape)
            continue;
        float key = fiber ? 1.0 : unkeyed;
        if (i == nextKey)
        {
            // Before any test of the light, so that the entries stay in step with the list.
            if (!fiber)
                key = dot(shares, unpackUnorm4x8(lightTiles[t].entries[cursor].y));
            cursor++;
            nextKey = cursor < end ? lightTiles[t].entries[cursor].x : NoLight;
        }
        LightPath p;
        if (!lightPath(i, fiber, tinted, shape, s, p))
            continue;
        float visibility = pathVisibility(i, p, s.position, ShadowMapsMainView);
        if (visibility <= 0.0)
            continue;
        vec3 diffuse = fiber ? vec3(0.0) : fillResponse(shape, s, lobes, p) * p.irradiance * (1.0 - key);
        if (key > 0.0)
        {
            DirectTerms terms = pathLight(i, fiber, shape, s, lobes, V, p);
            diffuse += terms.diffuse * key;
            sum.specular += terms.specular * (key * visibility);
        }
        sum.diffuse += diffuse * visibility;
    }
}

// modelLight under key and fill: a loop per shape, as modelLight has, with the key shares of tile `t`.
DirectTerms keyModelLight(bool fiber, bool tinted, ShadingInput s, SurfaceLobes lobes, vec3 V, uint t, vec4 shares,
    float unkeyed)
{
    DirectTerms sum;
    sum.diffuse = vec3(0.0);
    sum.specular = vec3(0.0);
    uint counts = lightTiles[t].header.x;
    uint points = counts & 0xFFu;
    uint tubes = (counts >> 8) & 0xFFu;
    uint rects = (counts >> 16) & 0xFFu;
    keyShapeLight(fiber, tinted, PointShape, s, lobes, V, t, 0u, points, shares, unkeyed, sum);
    keyShapeLight(fiber, tinted, TubeShape, s, lobes, V, t, points, tubes, shares, unkeyed, sum);
    keyShapeLight(fiber, tinted, RectShape, s, lobes, V, t, points + tubes, rects, shares, unkeyed, sum);
    return sum;
}

// directLight of the main view under key and fill, for the lit pixel at `pixel`.
DirectTerms keyFillLight(ShadingInput s, SurfaceLobes lobes, vec3 V, ivec2 pixel)
{
    ivec2 tile = pixel / TileSize;
    uint t = uint(tile.y * tileCount(ivec2(frame.viewport.xy)).x + tile.x);
    vec4 shares = cornerShares(s, pixel, tile, t);
    // The key share of a light that is no key light of the tile. No corner of the tile lies on a lit surface, as around
    // a thin object between the corners against the sky: nothing chose key lights for the pixel, so it takes every
    // light's full response. Else such a light is all fill.
    float unkeyed = shares == vec4(0.0) ? 1.0 : 0.0;
    bool tinted = frame.counts.y != NoAirSun;
    if (!dropsFibers && s.model == ShadingHair)
        return tinted ? keyModelLight(true, true, s, lobes, V, t, shares, unkeyed)
                      : keyModelLight(true, false, s, lobes, V, t, shares, unkeyed);
    return tinted ? keyModelLight(false, true, s, lobes, V, t, shares, unkeyed)
                  : keyModelLight(false, false, s, lobes, V, t, shares, unkeyed);
}

#endif
