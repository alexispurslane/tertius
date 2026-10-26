# The piece. Every part goes under one root empty, Astronaut, which is the only piece of the model (asset pipeline,
# "Models: nodes and pieces"). So tertius.demo:characters/astronaut.model#Astronaut names the whole person, and its
# origin, on the ground between the boots, is the origin of the entity. The model is built facing -Y, the front of
# the pipeline, so nothing turns.
#
# Body, the skin that the shells were cut from, lies inside the suit and goes away here, with the joints that the
# scripts shared through the scene.
import bpy

from tertius_blender.geometry import remove_object

remove_object("Body")
del bpy.context.scene["astronaut_joints"]

root = bpy.data.objects.new("Astronaut", None)
root.empty_display_type = "PLAIN_AXES"
root.empty_display_size = 0.3
root["tertius_piece"] = 1
bpy.context.scene.collection.objects.link(root)
bpy.context.view_layer.update()
parts = sorted(ob.name for ob in bpy.context.scene.objects if ob.parent is None and ob is not root)
for name in parts:
    ob = bpy.data.objects[name]
    world = ob.matrix_world.copy()
    ob.parent = root
    ob.matrix_world = world

result = {"piece": root.name, "parts": parts}
