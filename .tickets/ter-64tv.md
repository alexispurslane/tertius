---
id: ter-64tv
status: open
deps: [ter-unpc]
links: []
created: 2026-10-27T09:49:13Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [physics, frames]
---
# A kinematic body that a hand-off or a re-creation gives a velocity: keep it or stop it

Found by the review of ter-unpc (2026-10-27). A still kinematic prop, such as a shut door, rests in a station. A passing ship's bubble sweeps over it, and the hand-off gives it the ship's relative velocity in the ship frame. Rehome does the same when it re-creates a body because its frame died. Since ter-unpc, the stop rule visits every kinematic member of a numeric home in every step, so such a body holds still in the ship frame there, while in a semantic home it keeps the velocity (the stop visits only last step's movers). So whether a numeric frame holds it decides what gameplay sees. ARCHITECTURE.md line 169 (user, 2026-10-18): a kinematic body moves only by its motion intents and its teleports, and holds still in a step without one.

Options, waiting for the user:
(A) Stop every such body in the next step (about five lines: add each kinematic body that CreatePrimary creates with a velocity to the next step's stop list). The prop then holds still in the ship frame, so a passing ship carries a shut door away with it.
(B) The body keeps the velocity: in space it holds still, passes through the ship and hands back. The numeric-home loop must then carry that velocity in the rest chord, Self.PoseAtNextStepStartIn must predict the move, and the docs must drop 'every step'.
(C) A bubble never adopts a kinematic body without a motion intent, so the case cannot arise. This changes the hand-off rule.

## Acceptance Criteria

The user picks an option. A kinematic body that a hand-off gives a velocity moves the same with and without numeric frames.


## Notes

**2026-10-27T09:49:45Z**

2026-10-27: before ter-unpc, both homes kept the hand-off's velocity (option B's behavior), but a stopped kinematic member of a falling frame rode that frame at its last speed for good. ter-unpc fixed the riding and, as a side effect, gives the numeric-home case option A's behavior. The orchestrator merged ter-unpc because the riding happened in more scenes; this ticket restores one behavior in both homes once the user picks.
