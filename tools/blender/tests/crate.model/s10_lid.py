# The lid: a piece of its own under the crate, with its origin on the hinge at the back top edge.
import bpy

from tertius_blender.geometry import MB, constant_material

width, depth, height = model.params["size"]
mb = MB()
# Built around the hinge, which is the origin of the lid.
mb.box((0.0, -depth / 2, 0.025), (width + 0.04, depth + 0.04, 0.05), bevel=0.01)
lid = mb.finish("Lid", constant_material("M_LidPaint", (0.12, 0.2, 0.32), 0.0, 0.4), "Crate")
lid.location = (0.0, depth / 2, height)
lid.parent = bpy.data.objects["Crate"]
lid["tertius_piece"] = 1
result = {"lid": lid.name}
