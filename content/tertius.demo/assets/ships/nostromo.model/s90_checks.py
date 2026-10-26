# Two checks. A failed check stops the build.
#
# 1. Attachment: every island of the structure objects (masts, dishes, sensors, docking clamps, landing struts,
#    the bridge visor) touches, overlaps or sits inside a hull part, directly or through other islands.
# 2. The close-up view: the camera of the shot "closeup" sees no pipe and no pipe bracket. The shot looks along
#    the starboard side of the nose and the bridge. A pipe in it means that a pipe run stands in front of the hull.
#
# The view check needs the camera of the shot, so it adds the stage of the views and removes it again before the
# export. A camera left in the scene fails the export.
from tertius_blender import checks, run, stage

STRUCTURES = ["Structures_Metal", "Structures_Light", "Structures_Gray", "Bridge_Visor"]
HULL = ["Hull_Fore", "Hull_Mid", "Hull_Aft"]
HIDDEN_IN_CLOSEUP = ("Pipes", "Pipe_Brackets")

attachment = checks.attachment(STRUCTURES, HULL)
if attachment["free"]:
    raise ValueError("{:d} islands hang free: {}".format(attachment["free"], attachment["freeIslands"]))

views = run.load_model(model.folder)["views"]
closeup = next(shot for shot in views["shots"] if shot["name"] == "closeup")
stage.build(views)
try:
    seen = checks.view_shares(stage.camera_name(closeup))
finally:
    stage.remove()
pipes = {name: share for name, share in seen["share"].items() if name in HIDDEN_IN_CLOSEUP}
if pipes:
    raise ValueError("the close-up sees pipes: {}".format(pipes))

result = {"attachment": attachment, "closeup": seen["share"]}
