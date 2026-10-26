# An astronaut in an EVA suit. The scene name, the collections, and the materials as constant placeholders.
# s70_materials.py gives the baked materials their node trees. Later scripts take each material by its name.
#
# The frame: meters, +Z up, the front of the person toward -Y, so the person's right hand is at -X. The origin is
# on the ground between the boots. s10_body.py makes the human body from the MakeHuman data of MPFB, and the suit,
# helmet, gloves, boots and backpack of the later scripts fit around that body.
import bpy

from tertius_blender.geometry import coll, constant_material

bpy.context.scene.name = "Astronaut"
for name in ("Body", "Suit", "Helmet", "Backpack"):
    coll(name)

MATS = {
    "M_Skin": ((0.62, 0.42, 0.33), 0.0, 0.5),
    "M_Eye": ((0.85, 0.85, 0.82), 0.0, 0.15),
    "M_Iris": ((0.10, 0.06, 0.035), 0.0, 0.15),
    "M_Cap": ((0.33, 0.24, 0.17), 0.0, 0.7),
    "M_Suit": ((0.85, 0.85, 0.82), 0.0, 0.8),
    "M_Glove": ((0.80, 0.80, 0.78), 0.0, 0.75),
    "M_Boot": ((0.80, 0.80, 0.78), 0.0, 0.75),
    "M_Pack": ((0.86, 0.86, 0.84), 0.0, 0.6),
    "M_PackGray": ((0.42, 0.43, 0.45), 0.0, 0.55),
    "M_Display": ((0.02, 0.025, 0.03), 0.0, 0.1),
    "M_HardWhite": ((0.88, 0.88, 0.86), 0.0, 0.35),
    "M_Metal": ((0.70, 0.71, 0.73), 0.6, 0.4),
    "M_DarkMetal": ((0.08, 0.08, 0.09), 0.8, 0.45),
    "M_Rubber": ((0.03, 0.03, 0.03), 0.0, 0.85),
    "M_RingRed": ((0.55, 0.05, 0.04), 0.3, 0.35),
    "M_RingBlue": ((0.05, 0.12, 0.45), 0.3, 0.35),
    "M_Gold": ((0.95, 0.66, 0.25), 0.7, 0.22),
    "M_Bubble": ((0.9, 0.92, 0.95), 0.0, 0.03),
    "M_Lamp": ((0.9, 0.9, 0.85), 0.0, 0.2),
    "M_Hose": ((0.75, 0.75, 0.72), 0.0, 0.6),
}
for name, (color, metallic, roughness) in MATS.items():
    constant_material(name, color, metallic, roughness)

result = {"collections": [c.name for c in bpy.data.collections], "materials": sorted(MATS)}
