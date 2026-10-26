# A procedural paint for the crate body, and the UV map that its bake needs.
import math

import bpy

from tertius_blender.nodes import NB

crate = bpy.data.objects["Crate"]
mat = bpy.data.materials.new("M_Crate")
nt = mat.node_tree
nt.nodes.clear()
nb = NB(nt)
coords = nb.out(nb.n("ShaderNodeTexCoord"), "Object")
wear = nb.noise(coords, 6.0, detail=6.0, rough=0.6)
base = nb.mixc(nb.maprange(wear, 0.45, 0.7), (0.32, 0.22, 0.08), (0.55, 0.52, 0.48))
rough = nb.maprange(wear, 0.3, 0.8, 0.35, 0.8)
bump = nb.n("ShaderNodeBump", {"Strength": 0.4, "Height": wear})
principled = nb.n("ShaderNodeBsdfPrincipled", {"Base Color": base, "Roughness": rough, "Normal": bump.outputs["Normal"],
                                               "Metallic": 0.0})
output = nb.n("ShaderNodeOutputMaterial")
nb.set(output.inputs["Surface"], principled)
crate.data.materials.append(mat)

view = bpy.context.view_layer
for ob in view.objects:
    ob.select_set(False)
crate.select_set(True)
view.objects.active = crate
crate.data.uv_layers.new(name="UVMap")
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.02)
bpy.ops.object.mode_set(mode="OBJECT")
result = {"material": mat.name}
