"""Round-trip the authored ring galleries through the same interchange format as the demo.

Run in the asset shell with blender -b --factory-startup --python-exit-code 1 -P <this file>.
"""

import math
from pathlib import Path
import sys
import tempfile
from unittest import TestCase

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import habitat


def check_round_trip():
    check = TestCase()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    habitat.build()
    with tempfile.TemporaryDirectory() as folder:
        path = str(Path(folder) / "gallery.glb")
        bpy.ops.export_scene.gltf(filepath=path, export_tangents=True)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.gltf(filepath=path)

    glass = bpy.data.objects["Cabin glazing"].data
    # A real pane lives on each axial face, outside the skin and inside the
    # source model's flat radial band. Raised concourses project 16 m farther.
    heights = {round(v.co.z, 1) for v in glass.vertices}
    check.assertEqual(len(heights), 4)
    check.assertTrue(all(238.35 < z < 255 or -285 < z < -267.49 for z in heights))
    check.assertTrue(
        all(1885 < math.hypot(v.co.x, v.co.y) < 1975 for v in glass.vertices)
    )
    check.assertTrue(
        all(f.area > 0 and f.normal.z * f.center.z > 0 for f in glass.polygons)
    )
    check.assertGreater(len(glass.polygons), 1000)
    check.assertLess(len(glass.polygons), 30000)

    levels = set()
    for slot in {f.material_index for f in glass.polygons}:
        shader = glass.materials[slot].node_tree.nodes.get("Principled BSDF")
        strength = shader.inputs["Emission Strength"].default_value
        levels.add(
            tuple(
                value * strength
                for value in shader.inputs["Emission Color"].default_value[:3]
            )
        )
    check.assertIn((0.0, 0.0, 0.0), levels)
    check.assertGreaterEqual(len(levels), 3)
    panels = bpy.data.objects["Residential galleries"].data.materials[0]
    shader = panels.node_tree.nodes.get("Principled BSDF")
    normal = shader.inputs["Normal"].links[0].from_node
    check.assertEqual(normal.type, "NORMAL_MAP")
    check.assertIsNotNone(normal.inputs["Color"].links[0].from_node.image.packed_file)


check_round_trip()
