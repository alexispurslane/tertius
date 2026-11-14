// The direct light of a pixel under key and fill (docs/plans/light-pairs.md, "Key and fill"): every light of the main
// view with its shadow, as directLight takes them, but each light's response is its full response only by its key
// share, and its fill response by the rest. The fill response is the light's diffuse light as a point light gives it,
// with no shine. Only direct_key_fill.frag and key_fill_corners.frag include it.
#ifndef TERTIUS_KEY_FILL_LIGHT_GLSL
#define TERTIUS_KEY_FILL_LIGHT_GLSL

#include "gbuffer_surface.glsl"
#include "key_fill.glsl"
#include "octahedral.glsl"

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

// The cosine of the angle between a corner's normal and the pixel's at which the corner stops to count as on the
// pixel's surface: about 26 degrees. The corner's weight falls evenly from the same normal to there.
const float FacingFloor = 0.9;

// The summed weight of the corners on the pixel's surface below which the pixel takes a part of every light's full
// response: a sixteenth, the bilinear weight of a corner three quarters of a tile away along both axes.
const float TrustedWeight = 1.0 / 16.0;

// The pixel's shares of tile `t`, at `tile` of the grid, as four counts of 255 in one word. Bits 0 to 23 hold the shares
// of the tile's first three corners in the pixel's key shares. Bits 24 to 31 hold the open share: the part of every
// light's full response that the pixel takes whatever the corners chose. The fourth corner's share is the rest of 255.
//
// A corner's weight is the bilinear weight of its place, times how well its surface is the pixel's own. That is how
// near the corner's surface point lies to the plane of the pixel's surface, within the world size of a tile at the
// pixel, times how alike the two geometric normals are (FacingFloor). The corners chose their key lights for their own
// surfaces, so a corner on another surface has no say: a thin edge between the corners, or a wall behind them, has
// its highlights from other lights.
//
// The shares are the weights over their sum. Where the sum is under TrustedWeight, the shares are the weights over
// TrustedWeight, and the open share is the rest. So a pixel with no corner on its surface takes every light's full
// response, and the shares stay continuous where the corners leave the pixel's surface.
uint cornerShares(ShadingInput s, ivec2 pixel, ivec2 tile, uint t)
{
    ivec2 size = ivec2(frame.viewport.xy);
    vec2 f = vec2(pixel - tile * TileSize) / float(TileSize);
    vec4 weights = vec4((1.0 - f.x) * (1.0 - f.y), f.x * (1.0 - f.y), (1.0 - f.x) * f.y, f.x * f.y);
    float tolerance = max(length(s.position) * frame.params.z * float(TileSize), 1e-6);
    uint record = t * TileWords;
    for (int c = 0; c < 4; c++)
    {
        float distance = uintBitsToFloat(tileWords[record + uint(c)]);
        vec3 normal = octDecode(unpackSnorm2x16(tileWords[record + 4u + uint(c)]));
        ivec2 corner = tile + ivec2(c & 1, c >> 1);
        vec3 position = normalize(viewRayThrough(vec2(cornerPixel(corner, size)) + 0.5)) * distance;
        float off = dot(s.geometricNormal, position - s.position) / tolerance;
        float facing = (dot(s.geometricNormal, normal) - FacingFloor) / (1.0 - FacingFloor);
        weights[c] *= distance > 0.0 ? max(1.0 - off * off, 0.0) * clamp(facing, 0.0, 1.0) : 0.0;
    }
    float sum = weights.x + weights.y + weights.z + weights.w;
    weights /= max(sum, TrustedWeight);
    uvec3 shares = uvec3(weights.xyz * 255.0 + 0.5);
    uint open = uint((1.0 - min(sum / TrustedWeight, 1.0)) * 255.0 + 0.5);
    return shares.x | (shares.y << 8) | (shares.z << 16) | (open << 24);
}

// The key share of a light at a pixel of `shares` (cornerShares) whose entry in the tile's list holds `entry`: the
// light's key share at each of the four corners, 8 bits each. It is the open share, plus each corner's share of the
// pixel times the light's key share there. The counts are whole numbers, so four corners that agree on a share give
// exactly that share.
float keyShareOf(uint shares, uint entry)
{
    vec4 corner = unpackUnorm4x8(shares);
    vec4 key = unpackUnorm4x8(entry);
    // corner.w is the open share, and the fourth corner's share is the rest.
    float fourth = max(1.0 - corner.x - corner.y - corner.z - corner.w, 0.0);
    return min(corner.w + dot(corner.xyz, key.xyz) + fourth * key.w, 1.0);
}

// shapeLight's loop over the lights of the list of one shape, `shape`, added to `sum`, with each light's key share from
// the tile's list. `at` is the word of the list's next entry: the list is in the order of the shapes and then of the
// light list, so the loops of the three shapes walk it once, in step with the light list. A light that is the next
// entry takes its key share from it, by the pixel's `shares` (cornerShares, keyShareOf). Every other light has the open
// share of `shares` alone: it is all fill where the corners lie on the pixel's surface.
//
// The loop reads the next entry's light from the records at every light, and holds no copy of it. A held copy is one
// more value across the shadow lookup, and with it the shader no longer fits Apple's GPU at its full count of threads
// (ARCHITECTURE.md, "Key and fill"). For the same reason the loop holds no sum of the light's own across pathLight.
// `fiber`, `tinted` and `shape` as in shapeLight; keyModelLight is the one caller, and it passes constants. A fiber
// takes every light's full response: the fill response is a surface's.
void keyShapeLight(bool fiber, bool tinted, uint shape, ShadingInput s, SurfaceLobes lobes, vec3 V, uint shares,
    inout uint at, inout DirectTerms sum)
{
    if ((frame.counts.w & shape) == 0u)
        return;
    for (uint i = 0u; i < frame.counts.x; i++)
    {
        if (!viewKeeps(MainView, lights[i]) || shapeOf(lights[i]) != shape)
            continue;
        if (i == tileWords[at])
        {
            // Before any test of the light, so that the list stays in step with the light list.
            at += 2u;
        }
        LightPath p;
        if (!lightPath(i, fiber, tinted, shape, s, p))
            continue;
        float visibility = pathVisibility(i, p, s.position, ShadowMapsMainView);
        if (visibility <= 0.0)
            continue;
        // Only now, so that no key share is held across the shadow lookup: the light is a key light when the entry
        // before the next one is its own.
        float key = fiber ? 1.0 : (shares >> 24) == 0u ? 0.0 : unpackUnorm4x8(shares).w;
        if (!fiber && tileWords[at - 2u] == i)
            key = keyShareOf(shares, tileWords[at - 1u]);
        // The fill goes into the sum before the full response, and the full response has one weight, so that only that
        // weight is held across pathLight.
        if (!fiber)
            sum.diffuse += fillResponse(shape, s, lobes, p) * p.irradiance * ((1.0 - key) * visibility);
        if (key > 0.0)
        {
            float weight = key * visibility;
            DirectTerms terms = pathLight(i, fiber, shape, s, lobes, V, p);
            sum.diffuse += terms.diffuse * weight;
            sum.specular += terms.specular * weight;
        }
    }
}

// modelLight under key and fill: a loop per shape, as modelLight has, with the key shares of tile `t`.
DirectTerms keyModelLight(bool fiber, bool tinted, ShadingInput s, SurfaceLobes lobes, vec3 V, uint t, uint shares)
{
    DirectTerms sum;
    sum.diffuse = vec3(0.0);
    sum.specular = vec3(0.0);
    uint at = t * TileWords + TileEntriesAt;
    keyShapeLight(fiber, tinted, PointShape, s, lobes, V, shares, at, sum);
    keyShapeLight(fiber, tinted, TubeShape, s, lobes, V, shares, at, sum);
    keyShapeLight(fiber, tinted, RectShape, s, lobes, V, shares, at, sum);
    return sum;
}

// directLight of the main view under key and fill, for the lit pixel at `pixel`.
DirectTerms keyFillLight(ShadingInput s, SurfaceLobes lobes, vec3 V, ivec2 pixel)
{
    ivec2 tile = pixel / TileSize;
    uint t = uint(tile.y * tileCount(ivec2(frame.viewport.xy)).x + tile.x);
    uint shares = cornerShares(s, pixel, tile, t);
    bool tinted = frame.counts.y != NoAirSun;
    if (!dropsFibers && s.model == ShadingHair)
        return tinted ? keyModelLight(true, true, s, lobes, V, t, shares)
                      : keyModelLight(true, false, s, lobes, V, t, shares);
    return tinted ? keyModelLight(false, true, s, lobes, V, t, shares)
                  : keyModelLight(false, false, s, lobes, V, t, shares);
}

#endif
