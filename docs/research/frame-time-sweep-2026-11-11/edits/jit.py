# Prototype: the grid of the 2 x 2 blocks shifts by one pixel on each axis in a cycle of 16 draws. A pixel then shares its
# block's ray with other pixels in other draws, so the accumulated noise has no fixed 2 x 2 pattern. Same ray count.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; common.envexp(root)
p = root + common.SH + 'screen_trace.glsl'
common.sub(p, "bool chosenPixelOf(ivec2 block, uint kind, out ivec2 chosen, out TracingPixel p)\n{",
    "ivec2 gridShift()\n{\n    return ivec2((trace.drawIndex >> 2) & 1u, (trace.drawIndex >> 3) & 1u);\n}\n\n"
    "// The block of a pixel under the shifted grid. The last block takes the pixels past it.\n"
    "ivec2 blockOf(ivec2 pixel)\n{\n    return min((pixel + gridShift()) / 2, ivec2(ceil(frame.viewport.xy * 0.5)) - 1);\n}\n\n"
    "bool chosenPixelOf(ivec2 block, uint kind, out ivec2 chosen, out TracingPixel p)\n{")
common.sub(p, "        chosen = 2 * block + ivec2(corner & 1u, corner >> 1);\n        if (all(lessThan(vec2(chosen), frame.viewport.xy)) && tracedSurface(chosen))",
    "        chosen = 2 * block - gridShift() + ivec2(corner & 1u, corner >> 1);\n        if (all(greaterThanEqual(chosen, ivec2(0))) && all(lessThan(vec2(chosen), frame.viewport.xy)) && tracedSurface(chosen))")
common.sub(root + common.SH + 'screen_trace_plan.comp', "!chosenPixelOf(pixel / 2, kind, chosen, c)", "!chosenPixelOf(blockOf(pixel), kind, chosen, c)")
common.sub(root + common.SH + 'screen_trace_resolve.comp', "    ivec3 at = ivec3(own ? pixel : pixel / 2, kind);", "    ivec3 at = ivec3(own ? pixel : blockOf(pixel), kind);")
