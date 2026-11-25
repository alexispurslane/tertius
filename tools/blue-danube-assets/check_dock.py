"""Check the cooked station in Blender's Z-up metres, before station placement."""

import sys
from pathlib import Path

import bpy
from mathutils import Vector


def check(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    graph = bpy.context.evaluated_depsgraph_get()

    def first_surface(station_y, station_z):
        start = Vector((-100, -station_z, station_y - 14.6))
        hit, point, *_ = bpy.context.scene.ray_cast(graph, start, Vector((-1, 0, 0)))
        assert hit, "the station must keep its outer hull"
        return point.x

    for y in (-1.99, 0, 1.99):
        for z in (1.81, 3.8, 5.79):
            x = first_surface(y, z)
            assert x < -2000, f"lift path blocked at station ({x}, {y}, {z}) m"
    for y, z in ((2.1, 3.8), (-2.1, 3.8), (0, 1.7), (0, 5.9), (11.25, 0)):
        x = first_surface(y, z)
        assert abs(x + 104.611) < 0.002, f"missing berth deck at ({x}, {y}, {z}) m"


check(Path(sys.argv[-1]).resolve())
