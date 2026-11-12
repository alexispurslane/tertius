"""Scratch edit of ticket t2-mlvy, variant "spread corners": the corner pass of key and fill draws at the full render
size and discards every fragment that is not a corner's pixel, so that the corners lie in every tile of a tiled GPU's
frame buffer, not in the few tiles of a small target. The tile pass reads each corner at its pixel.
Usage: kfs.py <root of a copy of the tree>"""
import sys

root = sys.argv[1]

def edit(path, pairs):
    p = f"{root}/{path}"
    s = open(p).read()
    for a, b in pairs:
        assert s.count(a) == 1, (path, a[:60], s.count(a))
        s = s.replace(a, b)
    open(p, "w").write(s)

edit("src/Tertius.Rendering/Scene/GpuTypes.cs", [(
    "    public uint TilesHigh;\n}",
    "    public uint TilesHigh;\n\n    public uint Wide;\n\n    public uint High;\n}",
)])
edit("src/Tertius.Rendering/Passes/KeyFillPass.cs", [
    ("context.SetViewport(KeyFillImages.CornersOf(context.Extent));", "context.SetViewport(context.Extent);"),
    ("new KeyFillTilesPushConstants { TilesWide = count.Width, TilesHigh = count.Height };",
     "new KeyFillTilesPushConstants { TilesWide = count.Width, TilesHigh = count.Height, Wide = context.Extent.Width, High = context.Extent.Height };"),
])
edit("src/Tertius.Rendering/Frame/KeyFillImages.cs", [
    ("Extent2D corners = CornersOf(targets.Extent);", "Extent2D corners = targets.Extent;"),
])
edit("src/Tertius.Rendering/Shaders/key_fill_tiles.comp", [
    ("    uvec2 tiles;  // the tiles of the frame's render size along each axis\n",
     "    uvec2 tiles;  // the tiles of the frame's render size along each axis\n    uvec2 size;   // the frame's render size\n"),
    ("        ivec2 corner = ivec2(tile) + ivec2(c & 1u, c >> 1);",
     "        ivec2 corner = cornerPixel(ivec2(tile) + ivec2(c & 1u, c >> 1), ivec2(pass.size));"),
])
edit("src/Tertius.Rendering/Shaders/key_fill_corners.frag", [
    ("    ivec2 pixel = nearestLitPixel(cornerPixel(ivec2(gl_FragCoord.xy), size), size);",
     "    ivec2 own = ivec2(gl_FragCoord.xy);\n"
     "    if ((own.x % TileSize != 0 && own.x != size.x - 1) || (own.y % TileSize != 0 && own.y != size.y - 1))\n"
     "        discard;\n"
     "    ivec2 pixel = nearestLitPixel(own, size);"),
])
