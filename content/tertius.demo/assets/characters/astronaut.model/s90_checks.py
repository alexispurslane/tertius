# The attachment check: every island of every part touches, overlaps or sits inside the suit, directly or through
# other islands. A part that floats free is a modeling mistake, so the build stops.
import bpy

from tertius_blender import checks

parts = sorted(ob.name for ob in bpy.data.objects if ob.type == "MESH" and ob.name != "Suit")
attachment = checks.attachment(parts, ["Suit"])
if attachment["free"]:
    raise ValueError("{:d} islands hang free: {}".format(attachment["free"], attachment["freeIslands"]))
result = {"attachment": attachment}
