// The tail of a pyramid's build (docs/plans/screen-space-reflections.md, "The depth pyramid", and PyramidTail): each cell
// merges the up to four cells under it, as the pyramid's down shader does, for every mip from the tail's first to 1 × 1,
// in one workgroup, with a barrier of the workgroup between two mips instead of a dispatch per mip. The merges are the
// down shader's, in its order, so the tail gives the bits of a dispatch per mip. PyramidTail chooses the first mip; its
// Texels and Mips are the limits. The including shader defines TailCell, TAIL_FORMAT, emptyCell, mergedCells,
// cellOfTexel and texelOfCell.
#ifndef TERTIUS_PYRAMID_TAIL_GLSL
#define TERTIUS_PYRAMID_TAIL_GLSL

const uint TailThreads = 256;   // the local size, 16 × 16
const uint TailTexels = 512;    // PyramidTail.Texels
const uint TailMips = 10;       // PyramidTail.Mips
// The most texels of a tail mip after its first, which is at most half of the first: a quarter, or a half in a row.
const uint TexelsPerThread = TailTexels / 2 / TailThreads;

layout(local_size_x = 16, local_size_y = 16) in;

layout(set = 3, binding = 0) uniform texture2D source;                             // the mip before the tail
layout(set = 3, binding = 1, TAIL_FORMAT) uniform writeonly image2D mips[TailMips];  // the tail, from its first mip

layout(push_constant, std430) uniform TailPush
{
    uint mipCount;   // the tail's mips, 1 to TailMips
} tail;

// The cells of the mip that the workgroup built last, row by row.
shared TailCell cells[TailTexels];

// A store into tail mip k. The switch indexes the array by constants only, so no device needs the dynamic indexing of
// storage image arrays.
void storeMip(uint k, ivec2 cell, TailCell merged)
{
    vec4 value = texelOfCell(merged);
    switch (k)
    {
    case 0u: imageStore(mips[0], cell, value); break;
    case 1u: imageStore(mips[1], cell, value); break;
    case 2u: imageStore(mips[2], cell, value); break;
    case 3u: imageStore(mips[3], cell, value); break;
    case 4u: imageStore(mips[4], cell, value); break;
    case 5u: imageStore(mips[5], cell, value); break;
    case 6u: imageStore(mips[6], cell, value); break;
    case 7u: imageStore(mips[7], cell, value); break;
    case 8u: imageStore(mips[8], cell, value); break;
    default: imageStore(mips[9], cell, value); break;
    }
}

// A child outside the source's mip lies beyond the screen, and the fetch must stay inside the image.
TailCell sourceCell(ivec2 child)
{
    if (any(greaterThanEqual(child, textureSize(source, 0))))
        return emptyCell();
    return cellOfTexel(texelFetch(source, child, 0));
}

TailCell sharedCell(ivec2 child, ivec2 size)
{
    if (any(greaterThanEqual(child, size)))
        return emptyCell();
    return cells[child.y * size.x + child.x];
}

void main()
{
    uint thread = gl_LocalInvocationIndex;
    ivec2 size = imageSize(mips[0]);
    for (uint i = thread; i < uint(size.x * size.y); i += TailThreads)
    {
        ivec2 cell = ivec2(int(i) % size.x, int(i) / size.x);
        TailCell merged = emptyCell();
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
                merged = mergedCells(merged, sourceCell(2 * cell + ivec2(x, y)));
        cells[i] = merged;
        storeMip(0u, cell, merged);
    }
    for (uint k = 1u; k < tail.mipCount; k++)
    {
        ivec2 below = size;
        // Vulkan halves a mip and rounds down, never below 1.
        size = max(size >> 1, ivec2(1));
        // Every thread has written the mip below before any thread reads it.
        barrier();
        TailCell merged[TexelsPerThread];
        for (uint j = 0u; j < TexelsPerThread; j++)
        {
            uint i = thread + j * TailThreads;
            ivec2 cell = ivec2(int(i) % size.x, int(i) / size.x);
            merged[j] = emptyCell();
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    merged[j] = mergedCells(merged[j], sharedCell(2 * cell + ivec2(x, y), below));
        }
        // Every thread has read the mip below before any thread overwrites it with this mip.
        barrier();
        for (uint j = 0u; j < TexelsPerThread; j++)
        {
            uint i = thread + j * TailThreads;
            if (i >= uint(size.x * size.y))
                continue;
            cells[i] = merged[j];
            storeMip(k, ivec2(int(i) % size.x, int(i) / size.x), merged[j]);
        }
    }
}

#endif
