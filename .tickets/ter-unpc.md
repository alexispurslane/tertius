---
id: ter-unpc
status: closed
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


## Notes

**2026-10-27T08:33:13Z**

2026-10-27: confirmed by a red test (8.33 mm off at 0.5 m/s; the drain hides it above 1 m/s). The first builder stopped on a fork: hold still in the home (ARCHITECTURE line 373) or in the semantic frame. The orchestrator settled it by numeric-frames.md line 7 (gameplay never sees these frames) and the user's decision of 2026-10-19 (line 615): every kinematic body without a motion intent holds still in its semantic frame. The stop rule gives the chord of rest in the semantic frame and runs before the step's intents. Today's rule also lets a stopped kinematic ball ride its falling frame at its last speed for good.

**2026-10-27T09:41:52Z**

Review fix (2026-10-27): the related limit above is gone. A kinematic hull that teleports without a motion intent in a numeric tree parent is the anchor of its own following frame, which AimStillAnchors aims at the teleport's place, so the read and the step agree. World.PoseNowAtRunningStepEnd no longer moves a hull's pose with its tree parent.

**2026-10-27T10:13:22Z**

2026-10-27: merged. One stop rule (StopBodies, before the step's intents) gives every body that stops the rest chord of its semantic frame (RestChord: the home's velocity reversed, at the end of the step for a kinematic body and at its start for a dynamic body, because LinearGravity already takes out the home's acceleration). Coverage of the changed lines 34/34. The damped case drifts 63 µm (ter-u75o), and a kinematic body that a hand-off gives a velocity now stops in a numeric home but keeps it in a semantic home (ter-64tv, waits for the user).
