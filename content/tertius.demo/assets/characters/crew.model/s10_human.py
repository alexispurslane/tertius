# The person, made by MPFB's own code (tertius_blender.mpfb): MPFB builds the body from the macro sliders of
# parameters.human.phenotype, fits the eyes, eyebrows, eyelashes, teeth, tongue and hair of the MakeHuman system
# assets, gives the skin its MakeSkin material, and adds the "game_engine" rig. The rig poses the person
# (parameters.pose: bone -> XYZ Euler degrees from the rest pose): arms relaxed at the sides, elbows a little bent,
# fingers a little curled. freeze then applies the pose and removes the rig, because the importer ignores skins.
#
# It makes, in the collection Body:
#   Body, Eyes, Eyebrows, Eyelashes, Teeth, Tongue, Hair   the frozen parts. Body keeps the rig's bone weights as
#                                                          vertex groups, for the jumpsuit of s20_jumpsuit.py.
#   the scene property crew_joints: a JSON object, joint name -> [x, y, z] in meters, from MPFB's joint helpers.
#
# The whole person stands on the boot soles: every part rises until the lowest skin point is at parameters.suit.sole.
import json

import bpy
from mathutils import Matrix

from tertius_blender import mpfb

body = mpfb.human(model.params["human"], "Crew")
mpfb.pose(body.parent, model.params["pose"])
joints = mpfb.joints(body)
parts = mpfb.freeze(body)

# The lowest skin point sits at the height of the boot sole.
lowest = min((parts["Body"].matrix_world @ v.co).z for v in parts["Body"].data.vertices)
rise = model.params["suit"]["sole"] - lowest
lift = Matrix.Translation((0.0, 0.0, rise))
target = bpy.data.collections["Body"]
for name, ob in sorted(parts.items()):
    ob.data.transform(lift @ ob.matrix_world)
    ob.matrix_world = Matrix.Identity(4)
    for c in list(ob.users_collection):
        c.objects.unlink(ob)
    target.objects.link(ob)
# MPFB's collection for the human, now empty.
for c in [c for c in bpy.data.collections if c.name not in ("Body", "Suit") and not c.objects]:
    bpy.data.collections.remove(c)

bpy.context.scene["crew_joints"] = json.dumps(
    {k: [round(v[0], 6), round(v[1], 6), round(v[2] + rise, 6)] for k, v in joints.items()})

verts = [v.co for v in parts["Body"].data.vertices]
result = {"parts": {k: len(ob.data.polygons) for k, ob in sorted(parts.items())},
          "height_m": round(max(v.z for v in verts) - min(v.z for v in verts), 3),
          "joints": len(joints), "vertex_groups": len(parts["Body"].vertex_groups)}
