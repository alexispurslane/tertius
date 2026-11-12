"""Scratch edit of ticket t2-mlvy, cost only: the corner pass of key and fill, version 2, without the walk of the rings
and with another density of its work, to find how its time follows the count of its fragments. The image of these
variants is wrong by design. Usage: density.py <root of a copy of the tree>, with one of two settings in the environment.
TILE=<n> sets the tile's side to n render pixels. COPIES=<c> draws c x c copies of the corner grid side by side, and
each copy ranks only the lights whose index is its own modulo c x c, so each fragment does 1 / (c x c) of a corner's
work and the tile pass reads the first copy."""
import os
import sys

root = sys.argv[1]

def edit(path, pairs):
    p = f"{root}/{path}"
    s = open(p).read()
    for a, b in pairs:
        assert s.count(a) == 1, (path, a[:60], s.count(a))
        s = s.replace(a, b)
    open(p, "w").write(s)

corners = "src/Tertius.Rendering/Shaders/key_fill_corners.frag"
edit(corners, [("    for (int r = 1; r < TileSize; r++)\n", "    for (int r = 1; r < min(TileSize, 1); r++)\n")])
tile = os.environ.get("TILE")
copies = os.environ.get("COPIES")
if tile:
    edit("src/Tertius.Rendering/Shaders/key_fill.glsl", [("const int TileSize = 8;", f"const int TileSize = {tile};")])
    edit("src/Tertius.Rendering/LightPairSetting.cs", [("public const int TileSize = 8;", f"public const int TileSize = {tile};")])
if copies:
    c = int(copies)
    edit("src/Tertius.Rendering/Frame/KeyFillImages.cs", [(
        "        Extent2D corners = CornersOf(targets.Extent);",
        f"        Extent2D corners = new(CornersOf(targets.Extent).Width * {c}, CornersOf(targets.Extent).Height * {c});",
    )])
    edit("src/Tertius.Rendering/Passes/KeyFillPass.cs", [(
        "context.SetViewport(KeyFillImages.CornersOf(context.Extent));",
        f"context.SetViewport(new Extent2D(KeyFillImages.CornersOf(context.Extent).Width * {c}, KeyFillImages.CornersOf(context.Extent).Height * {c}));",
    )])
    edit(corners, [
        ("    ivec2 pixel = nearestLitPixel(cornerPixel(ivec2(gl_FragCoord.xy), size), size);",
         "    ivec2 grid = tileCount(size) + 1;\n"
         "    ivec2 copy = ivec2(gl_FragCoord.xy) / grid;\n"
         f"    uint ownGroup = uint(copy.y * {c} + copy.x);\n"
         "    ivec2 pixel = nearestLitPixel(cornerPixel(ivec2(gl_FragCoord.xy) % grid, size), size);"),
        ("        if (!viewKeeps(MainView, lights[i]))\n            continue;\n        uint shape = shapeOf(lights[i]);\n        LightPath p;\n        if (!lightPath(i, false, true, shape, s, p))",
         f"        if (i % {c * c}u != ownGroup || !viewKeeps(MainView, lights[i]))\n            continue;\n        uint shape = shapeOf(lights[i]);\n        LightPath p;\n        if (!lightPath(i, false, true, shape, s, p))"),
    ])
