# The Nostromo, a tug in the style of the ship of the film. The scene name, the collections, and the materials as
# constant placeholders. s70_materials.py gives the materials their node trees. Later scripts take each material
# by its name.
#
# The build frame: s10 to s75 build the ship with its nose toward +Y and +Z up, in meters. s80_assemble.py turns
# every part 180 degrees about +Z, so the built model faces -Y, the front of the asset pipeline. The materials read
# object coordinates, which the turn does not change, so the materials stay in the build frame too.
import bpy

from tertius_blender.geometry import coll, constant_material

bpy.context.scene.name = "Nostromo"
for name in ("Hull", "Engines", "Greebles", "Structures", "Lights"):
    coll(name)

MATS = {
    "M_Hull_Fore": ((0.62, 0.60, 0.55), 0.0, 0.55),
    "M_Hull_Mid": ((0.62, 0.60, 0.55), 0.0, 0.55),
    "M_Hull_Aft": ((0.62, 0.60, 0.55), 0.0, 0.55),
    "M_Engine": ((0.06, 0.06, 0.065), 0.85, 0.45),
    "M_FrameMetal": ((0.055, 0.055, 0.06), 0.85, 0.5),
    "M_PipeMetal": ((0.16, 0.15, 0.14), 0.9, 0.38),
    "M_PaintLight": ((0.50, 0.49, 0.45), 0.0, 0.6),
    "M_PaintGray": ((0.22, 0.22, 0.23), 0.0, 0.55),
    "M_Window": ((0.02, 0.02, 0.02), 0.0, 0.2),
    "M_NavRed": ((0.1, 0.0, 0.0), 0.0, 0.3),
    "M_NavGreen": ((0.0, 0.1, 0.0), 0.0, 0.3),
    "M_NavWhite": ((0.1, 0.1, 0.1), 0.0, 0.3),
    "M_EngineGlow": ((0.0, 0.0, 0.0), 0.0, 0.5),
}
for name, (color, metallic, roughness) in MATS.items():
    constant_material(name, color, metallic, roughness)

result = {"collections": [c.name for c in bpy.data.collections], "materials": sorted(MATS)}
