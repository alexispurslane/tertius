# The lid must sit on the body. A lid that floats free is a modeling mistake, so the build stops.
from tertius_blender import checks

report = checks.attachment(["Lid"], ["Crate"])
if report["free"]:
    raise ValueError("islands hang free: {}".format(report["freeIslands"]))
result = report
