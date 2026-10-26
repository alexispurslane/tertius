# The piece and the front. Every part goes under one root empty, Nostromo, which is the only piece of the model
# (asset pipeline, "Models: nodes and pieces"). So tertius.demo:ships/nostromo.model#Nostromo names the whole ship,
# and its origin, the origin of the build frame in the middle of the spine, is the origin of the ship entity.
#
# The scripts before this one build the ship with its nose toward +Y. The asset pipeline puts the front of a model
# at -Y in Blender (decision 10). So each part turns 180 degrees about +Z through its object transform. The mesh
# data stays in the build frame, so the materials, which read object coordinates, bake the same maps. The importer
# bakes each part's transform into the axes of the piece.
import math

import bpy

root = bpy.data.objects.new("Nostromo", None)
root.empty_display_type = "PLAIN_AXES"
root.empty_display_size = 10.0
# A root node is a piece in any case. The mark keeps it a piece if a later script parents it under something.
root["tertius_piece"] = 1
bpy.context.scene.collection.objects.link(root)

parts = sorted(ob.name for ob in bpy.context.scene.objects if ob.type == "MESH")
for name in parts:
    ob = bpy.data.objects[name]
    ob.parent = root
    ob.rotation_euler = (0.0, 0.0, math.pi)

result = {"piece": root.name, "parts": parts}
