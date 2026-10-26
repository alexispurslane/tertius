# A crew member in a ship's jumpsuit. The scene name, the collections, and the constant materials of the jumpsuit's
# small parts. s70_materials.py gives the baked materials their node trees. Later scripts take each material by name.
#
# The frame: meters, +Z up, the front of the person toward -Y, so the person's right hand is at -X. The origin is
# on the ground between the boots. s10_human.py makes the person with MPFB, and the jumpsuit and boots of the later
# scripts fit around that body.
import bpy

from tertius_blender.geometry import coll, constant_material

bpy.context.scene.name = "Crew"
for name in ("Body", "Suit"):
    coll(name)

MATS = {
    "M_Suit": ((0.12, 0.2, 0.3), 0.0, 0.8),
    "M_Trims": ((0.12, 0.2, 0.3), 0.0, 0.8),
    "M_Boot": ((0.03, 0.026, 0.022), 0.0, 0.55),
    "M_Sole": ((0.025, 0.025, 0.025), 0.0, 0.9),
    "M_Zip": ((0.55, 0.56, 0.58), 0.8, 0.35),
    "M_Belt": ((0.08, 0.08, 0.085), 0.0, 0.55),
    "M_Buckle": ((0.62, 0.63, 0.66), 0.85, 0.3),
}
for name, (color, metallic, roughness) in MATS.items():
    constant_material(name, color, metallic, roughness)

result = {"collections": [c.name for c in bpy.data.collections], "materials": sorted(MATS)}
