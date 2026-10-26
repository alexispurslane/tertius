# The attachment check: every island of every part touches, overlaps or sits inside the body or the jumpsuit,
# directly or through other islands. A part that floats free is a modeling mistake, so the build stops.
import bpy

from tertius_blender import checks

anchors = ["Body", "Jumpsuit"]
parts = sorted(ob.name for ob in bpy.data.objects if ob.type == "MESH" and ob.name not in anchors)
attachment = checks.attachment(parts, anchors)
if attachment["free"]:
    raise ValueError("{:d} islands hang free: {}".format(attachment["free"], attachment["freeIslands"]))
result = {"attachment": attachment}
