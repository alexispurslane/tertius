// Key and fill (docs/plans/light-pairs.md, "Key and fill"): what the corner pass, the tile pass and the direct pass
// share. A corner is a point of a grid of TileSize render pixels, and a tile is the square between four corners. The
// corner pass (key_fill_corners.frag) ranks the lights at each corner in Groups groups, one fragment per corner and
// group, and leaves each group's candidates. The tile pass (key_fill_tiles.comp) merges them into each corner's key
// lights with their key shares, and gathers a tile's four corners into one record. The direct pass
// (direct_key_fill.frag) reads a pixel's key shares from its tile's record alone. This file reads no descriptor, so that
// the tile pass can include it without the frame set.
#ifndef TERTIUS_KEY_FILL_GLSL
#define TERTIUS_KEY_FILL_GLSL

// KeyAndFill.TileSize: the side of a tile, in render pixels.
const int TileSize = 16;
// KeyAndFill.MaxKeyLights: the most key lights of a corner.
const uint KeySlots = 4u;
// The candidates that a group of lights leaves at a corner: one more than the most key lights, because the corner's
// threshold is the loss of the light that follows its key lights.
const uint CandidateSlots = KeySlots + 1u;
// KeyAndFill.GroupSide: the copies of the corner grid along each axis of the corner pass's target. Each copy is one
// group of lights, so the groups are GroupSide × GroupSide.
const int GroupSide = 4;
const uint Groups = uint(GroupSide * GroupSide);
// KeyAndFill.TileEntries: the most lights of a tile's list, the key lights of its four corners.
const uint TileEntries = 16u;
// The key of an empty slot. A key holds its light's place in the light list in bits 0 to 15, its key
// share as 8 bits in bits 16 to 23, and its shape's bit (PointShape, TubeShape or RectShape) from bit 24 on.
const uint NoKey = 0xFFFFFFFFu;
// KeyAndFill.NegligibleLoss: the share of a corner's light below which a light's loss earns no key share.
const float NegligibleLoss = 1.0 / 256.0;

// The weights of a linear RGB color's luminance, as meter.glsl holds them: the tile pass includes this file alone.
const vec3 KeyLuminanceWeights = vec3(0.2126, 0.7152, 0.0722);

// One tile's record is TileWords words of the tile records (KeyFillImages.TileBytes in C#). The corners are in the order
// (0, 0), (1, 0), (0, 1), (1, 1) from the tile's own corner.
// - Words 0 to 3: per corner, the bits of the distance to the eye of the surface that the corner sampled, in m, 0 for a
//   corner without a lit surface.
// - Words 4 to 7: per corner, that surface's geometric normal, in the G-buffer's axes (packSnorm2x16 of octEncode).
// - Words 8 and 9: ListEnd and 0, an entry of no light before the list, so that the entry before the list's first one is
//   no light's own (keyShapeLight).
// - From word TileEntriesAt: the tile's list, two words per entry. The first is the light's place in the light list. The
//   second is its key share at each of the four corners, 8 bits each. The entries are in the order of the shapes and
//   then of the light list. The word after the last entry is ListEnd, so the list needs no count.
const uint TileWords = 44u;
const uint TileEntriesAt = 10u;
// The place in the light list that no light has: the end of a tile's list.
const uint ListEnd = 0xFFFFFFFFu;

// The pixel that the corner at `corner` of the grid samples, in a render target of `size` pixels: the corner's own
// pixel, or the last pixel of the row or column for the corners of the last tiles' far sides.
ivec2 cornerPixel(ivec2 corner, ivec2 size)
{
    return min(corner * TileSize, size - 1);
}

// The tiles of a render target of `size` pixels along each axis.
ivec2 tileCount(ivec2 size)
{
    return (size + TileSize - 1) / TileSize;
}

// The corners of a render target of `size` pixels along each axis: one more than the tiles. It is also the size of one
// copy of the corner grid in the corner pass's target. The copy of group g lies at (g % GroupSide, g / GroupSide)
// copies from the target's first texel, and group g holds the lights g, g + Groups, g + 2 Groups and so on of the
// light list.
ivec2 cornerCount(ivec2 size)
{
    return tileCount(size) + 1;
}

// KeyAndFill.KeyShare: the key share of a light whose fill response loses `loss` at a corner whose threshold is
// `threshold`: 0 at the threshold or below, 1 at twice the threshold or above, and even in between. Without a threshold,
// every light that loses something is all key.
float keyShare(float loss, float threshold)
{
    if (loss <= 0.0)
        return 0.0;
    return threshold <= 0.0 ? 1.0 : clamp(loss / threshold - 1.0, 0.0, 1.0);
}

#endif
