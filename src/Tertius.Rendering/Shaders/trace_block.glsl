// The screen tracer's block (docs/plans/temporal-renderer.md, section 6, "The ray budget"): the 2 × 2 pixels that share
// one ray of each kind, while each pixel of a young block traces a ray of its own. Block (i, j) holds the pixels of columns
// 2i and 2i + 1 and rows 2j and 2j + 1, so the blocks start at even pixels, and a block at an odd border of the image holds
// pixels outside it. The tracer (screen_trace.glsl) and the fix of short histories (accumulate_spatial.comp, step 3) read
// the block from here. The block records on the C# side, ScreenTraceImages, hold one texel per block.
#ifndef TERTIUS_TRACE_BLOCK_GLSL
#define TERTIUS_TRACE_BLOCK_GLSL

// The pixels of a block, which traceBlockPixel numbers from 0.
const uint TraceBlockPixels = 4u;

// The block that holds a pixel. The shift rounds down, so a pixel left of or above the image lies in a block outside it.
ivec2 traceBlockOf(ivec2 pixel)
{
    return pixel >> 1;
}

// The pixel of a block at its corner: 0 the even column and row, 1 the odd column, 2 the odd row, 3 both odd.
ivec2 traceBlockPixel(ivec2 block, uint corner)
{
    return 2 * block + ivec2(corner & 1u, corner >> 1);
}

#endif
