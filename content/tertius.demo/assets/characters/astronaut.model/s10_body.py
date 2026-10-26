# The human body, made by MPFB's own code (tertius_blender.mpfb): MPFB builds the body from the macro sliders of
# parameters.human.phenotype, fits the eyes, eyebrows, eyelashes, teeth and tongue of the MakeHuman system assets,
# gives the skin its MakeSkin material, and adds the "game_engine" rig. The rig poses the person (parameters.pose:
# bone -> XYZ Euler degrees from the rest pose): the arms lowered from MakeHuman's A-pose, the elbows a little bent,
# the fingers a little curled. freeze then applies the pose and removes the rig, because the importer ignores skins.
# No hair: the comm cap of s40_helmet.py covers the head.
#
# It makes, in the collection Body:
#   Body     the skin. Its vertex groups are the bones of the rig. Later scripts cut the head, the suit, the gloves
#            and the boots from it, and s80_assemble.py deletes it.
#   Eyes, Eyebrows, Eyelashes, Teeth, Tongue   the final parts of the face, inside the helmet.
#   the scene property astronaut_joints: a JSON object, joint name -> [x, y, z] in meters, from MPFB's joint helpers.
#
# The frame: the face looks toward -Y, MakeHuman's "l" side is the person's left, at +X. The body stands on the boot
# soles (parameters.suit.sole), with the pelvis joint over the origin.
import json

import bpy
from mathutils import Matrix

from tertius_blender import mpfb

body = mpfb.human(model.params["human"], "Astronaut")
mpfb.pose(body.parent, model.params["pose"])
joints = mpfb.joints(body)
parts = mpfb.freeze(body)

# The lowest skin point sits at the height of the boot sole.
lowest = min((parts["Body"].matrix_world @ v.co).z for v in parts["Body"].data.vertices)
shift = (0.0, -joints["pelvis"][1], model.params["suit"]["sole"] - lowest)
move = Matrix.Translation(shift)
target = bpy.data.collections["Body"]
for name, ob in sorted(parts.items()):
    ob.data.transform(move @ ob.matrix_world)
    ob.matrix_world = Matrix.Identity(4)
    for c in list(ob.users_collection):
        c.objects.unlink(ob)
    target.objects.link(ob)
# MPFB's collection for the human, now empty.
for c in [c for c in bpy.data.collections if c.name not in ("Body", "Suit", "Helmet", "Backpack") and not c.objects]:
    bpy.data.collections.remove(c)

joints = {k: [round(v[i] + shift[i], 6) for i in range(3)] for k, v in sorted(joints.items())}
bpy.context.scene["astronaut_joints"] = json.dumps(joints)

verts = [v.co for v in parts["Body"].data.vertices]
lo = [min(v[i] for v in verts) for i in range(3)]
hi = [max(v[i] for v in verts) for i in range(3)]
result = {"parts": {k: len(ob.data.polygons) for k, ob in sorted(parts.items())},
          "body_vertices": len(verts), "size_m": [round(hi[i] - lo[i], 3) for i in range(3)],
          "joints": len(joints), "vertex_groups": len(parts["Body"].vertex_groups)}
