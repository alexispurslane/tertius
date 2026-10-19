---
id: ter-unpc
status: open
deps: [ter-xdt8]
links: []
created: 2026-10-19T17:19:52Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [physics, frames]
---
# A plain kinematic body may keep a velocity after a teleport in a numeric home

Found by the fix stage of ter-xdt8 (2026-10-19), not confirmed by a test. In a numeric home, ApplyBodyIntent gives a plain kinematic body that teleports a velocity that keeps it still in its semantic frame, not zero. The body is not among the bodies that the stop rule visits in the next step, so it may keep that velocity. The design says that in a step without a motion intent a kinematic body holds still: physics sets its velocity to zero. First write the test (a plain kinematic body in a falling numeric frame teleports, then gets no motion intent; it must hold still in its home's... check the design for the exact frame), then fix. Related limit, named in the design: a kinematic hull in a numeric frame that teleports without a motion intent holds still in the moving frame, so it ends the step one step of that frame's motion past where the read says.

