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

    # The docked hull enters nose first through the +Y port. Sample the swept
    # cross-section back out to space, then the parked footprint across to the deck.
    for x in (-4.901, 0, 4.37):
        for z in (-6.92, 0, 6.92):
            start = Vector((x, -z, 21.4344 - 14.6))
            hit, *_ = bpy.context.scene.ray_cast(graph, start, Vector((0, 0, 1)))
            assert not hit, f"obstruction in axial approach at ({x}, {z})"
    for y in (6.414, 21.434, 35.5):
        for z in (-10.92, -4.0, 2.92):
            start = Vector((0, -z, y - 14.6))
            hit, point, *_ = bpy.context.scene.ray_cast(graph, start, Vector((-1, 0, 0)))
            assert hit and abs(point.x + 104.611) < 0.002, f"obstruction before berth at ({y}, {z}): {point}"

    start = Vector((-100, 0, 11.249 - 14.6))
    hit, _, _, face, obj, _ = bpy.context.scene.ray_cast(graph, start, Vector((-1, 0, 0)))
    assert hit
    mat = obj.data.materials[obj.data.polygons[face].material_index]
    shader = mat.node_tree.nodes.get("Principled BSDF")
    assert shader.inputs["Normal"].is_linked, "the physical berth deck needs its anti-slip relief"


check(Path(sys.argv[-1]).resolve())
