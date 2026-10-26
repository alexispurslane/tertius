# The piece and the front. Every part goes under one root empty, Nostromo, which is the only piece of the model
# (asset pipeline, "Models: nodes and pieces"). So tertius.demo:ships/nostromo.model#Nostromo names the whole ship,
# and its origin, the origin of the build frame in the middle of the spine, is the origin of the ship entity.
#
# The scripts before this one build the ship with its nose toward +Y. The asset pipeline puts the front of a model
# at -Y in Blender (decision 10). So every top object, a mesh, an empty or a light, turns 180 degrees about the +Z
# axis of the root, and its children turn with it. The mesh data stays in the build frame, so the materials, which
# read object coordinates, bake the same maps. The importer bakes each part's transform into the axes of the piece.
import math

import bpy
from mathutils import Matrix

root = bpy.data.objects.new("Nostromo", None)
root.empty_display_type = "PLAIN_AXES"
root.empty_display_size = 10.0
# A root node is a piece in any case. The mark keeps it a piece if a later script parents it under something.
root["tertius_piece"] = 1
bpy.context.scene.collection.objects.link(root)

turn = Matrix.Rotation(math.pi, 4, "Z")
# matrix_world holds the transform of the last update, so update before reading it.
bpy.context.view_layer.update()
parts = sorted(ob.name for ob in bpy.context.scene.objects if ob.parent is None and ob is not root)
for name in parts:
    ob = bpy.data.objects[name]
    world = ob.matrix_world.copy()
    ob.parent = root
    # The root sits at the origin with no rotation, so the turn about the root is a turn about the world origin.
    ob.matrix_world = turn @ world

result = {"piece": root.name, "parts": parts}
