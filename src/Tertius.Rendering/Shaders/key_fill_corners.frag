#version 460
// The corner pass of key and fill (docs/plans/light-pairs.md, "Key and fill"): one fragment per corner of the tile grid
// and per group of lights. The target holds GroupSide × GroupSide copies of the corner grid side by side, and the copy
// of a fragment is its group (cornerGroup in key_fill.glsl). A fragment ranks only the lights of its group, every
// Groups-th light of the light list from the group's own place. It writes the group's candidates: the lights of the
// greatest loss at the corner, greatest first, one more than the most key lights. The tile pass merges the candidates of
// a corner's groups into the corner's keys (key_fill_tiles.comp).
//
// A fragment per group, and not one per corner that ranks every light: on Apple's GPU the pass's time follows the
// length of its fragments, and the same lights in sixteen short fragments take about half the time of one long
// fragment (ARCHITECTURE.md, "Key and fill").
//
// The fragment samples the surface at the corner's own pixel, and writes that surface's distance and normal beside the
// candidates. A corner whose pixel holds no lit surface stays empty. A pixel counts only the corners on its own surface,
// and with no such corner it takes every light's full response (key_fill_light.glsl, cornerShares).
//
// The loss of a light at the corner is what its fill response loses of its full response there, behind its shadow: the
// luminance of its shine, plus that of the difference of the two diffuse lights. The surface is the corner's, with a
// white base color, its geometric normal and a lobe at least as wide as a tile. The white base color and the geometric
// normal keep a texture's or a normal map's change inside a tile out of the choice. The wide lobe gives a highlight that
// lies between two corners some loss at both.
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "gbuffer_surface.glsl"
#include "key_fill_light.glsl"

// The candidates of the group, greatest loss first. A slot without a light holds the loss 0 and NoKey.
layout(location = 0) out uvec4 outLosses;  // the bits of the losses of the first four candidates
layout(location = 1) out uvec4 outKeys;    // their keys, each without a share (NoKey in key_fill.glsl)
layout(location = 2) out uvec4 outRest;    // x, y: the bits of the fifth candidate's loss, and its key. z: the bits of
                                           // the group's part of the corner's whole direct light. w unused
// The corner's surface, the same in every group. x: the bits of its distance to the eye, in m, 0 for an empty corner.
// y: its geometric normal, in the G-buffer's axes (packSnorm2x16 of octEncode). zw unused.
layout(location = 3) out uvec4 outSurface;

// Whether the pixel holds a lit surface: the sky and the impostors take no direct light (direct.frag).
bool holdsLitSurface(ivec2 pixel)
{
    return texelFetch(gbufferDistance, pixel, 0).r > 0.0 && texelFetch(gbufferBaseColor, pixel, 0).a != 0.0;
}

// Keeps the greater of the slot's loss and `loss` in the slot, with its key, and carries the lesser on in `loss` and
// `key`. A chain of these over the slots, greatest first, keeps the slots in the order of loss. Of two lights of one
// loss, the one that came first stays ahead.
void sift(inout float slotLoss, inout uint slotKey, inout float loss, inout uint key)
{
    bool greater = loss > slotLoss;
    float lesserLoss = greater ? slotLoss : loss;
    uint lesserKey = greater ? slotKey : key;
    slotLoss = greater ? loss : slotLoss;
    slotKey = greater ? key : slotKey;
    loss = lesserLoss;
    key = lesserKey;
}

void main()
{
    outLosses = uvec4(0u);
    outKeys = uvec4(NoKey);
    outRest = uvec4(0u, NoKey, 0u, 0u);
    outSurface = uvec4(0u);
    ivec2 size = ivec2(frame.viewport.xy);
    ivec2 corners = cornerCount(size);
    ivec2 at = ivec2(gl_FragCoord.xy);
    ivec2 copy = at / corners;
    ivec2 pixel = cornerPixel(at - copy * corners, size);
    if (!holdsLitSurface(pixel))
        return;

    ShadingInput s = materialAt(pixel);
    outSurface.xy = uvec2(floatBitsToUint(length(s.position)), packSnorm2x16(octEncode(s.geometricNormal)));
    // A fiber takes every light's full response (keyShapeLight), so its corner needs no key.
    if (s.model == ShadingHair)
        return;
    s.normal = s.geometricNormal;
    s.baseColor = vec3(1.0);
    vec3 V = -normalize(s.position);
    SurfaceLobes lobes = lobesOf(s, V);
    // The mirror direction turns by about the tile's angle from one corner to the next.
    lobes.alpha = max(lobes.alpha, 2.0 * float(TileSize) * frame.params.z);

    // They are named values and not an array, because an array that a run-time index reads lives in memory and not in
    // registers on a tiled GPU.
    float loss0 = 0.0, loss1 = 0.0, loss2 = 0.0, loss3 = 0.0, loss4 = 0.0;
    uint key0 = NoKey, key1 = NoKey, key2 = NoKey, key3 = NoKey, key4 = NoKey;
    float whole = 0.0;
    for (uint i = uint(copy.y * GroupSide + copy.x); i < frame.counts.x; i += Groups)
    {
        if (!viewKeeps(MainView, lights[i]))
            continue;
        uint shape = shapeOf(lights[i]);
        LightPath p;
        if (!lightPath(i, false, true, shape, s, p))
            continue;
        float visibility = pathVisibility(i, p, s.position, ShadowMapsMainView);
        if (visibility <= 0.0)
            continue;
        DirectTerms full = pathLight(i, false, shape, s, lobes, V, p);
        vec3 fill = fillResponse(shape, s, lobes, lobes.diffuseColor, p) * p.irradiance;
        whole += visibility * dot(full.diffuse + full.specular, KeyLuminanceWeights);
        float loss = visibility * dot(full.specular + abs(full.diffuse - fill), KeyLuminanceWeights);
        uint key = i | (shape << 24);
        sift(loss0, key0, loss, key);
        sift(loss1, key1, loss, key);
        sift(loss2, key2, loss, key);
        sift(loss3, key3, loss, key);
        sift(loss4, key4, loss, key);
    }
    outLosses = floatBitsToUint(vec4(loss0, loss1, loss2, loss3));
    outKeys = uvec4(key0, key1, key2, key3);
    outRest.xyz = uvec3(floatBitsToUint(loss4), key4, floatBitsToUint(whole));
}
