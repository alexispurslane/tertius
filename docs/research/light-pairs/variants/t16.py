"""Scratch edit of ticket t2-mlvy, variant "16-pixel tiles": key and fill, version 2, with tiles of 16 render pixels.
Usage: t16.py <root of a copy of the tree>"""
import sys

for path, a, b in (
    ("src/Tertius.Rendering/Shaders/key_fill.glsl", "const int TileSize = 8;", "const int TileSize = 16;"),
    ("src/Tertius.Rendering/LightPairSetting.cs", "public const int TileSize = 8;", "public const int TileSize = 16;"),
):
    p = f"{sys.argv[1]}/{path}"
    s = open(p).read()
    assert s.count(a) == 1, path
    open(p, "w").write(s.replace(a, b))
