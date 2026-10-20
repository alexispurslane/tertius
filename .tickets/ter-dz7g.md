---
id: ter-dz7g
status: open
deps: []
links: []
created: 2026-10-19T23:36:33Z
type: feature
priority: 3
assignee: Alexis Purslane
tags: [engine, camera]
---
# An attachment that follows only its parent's position, in the axes of the parent's frame

Found by ter-2n31 (2026-10-19). AttachTo applies the offset in the subject's axes after the step that runs beside the camera's tick, so a sudden move of the person (about 5 cm at a walking turn, 0.3 m at a stair) and the engine's instant turn of a character's up (up to 90 degrees, at the ignition and at the ends of the ramp) each reach the demo's chase camera for one tick. An attachment mode that follows only the parent's position, in the axes of the parent's frame, would remove only the swing from a turn of the character's up. It still adds the parent's position at the end of the step in flight, so a sudden move of the person still reaches the camera in the tick of the move. Not needed for the demo, whose sections film those moments with fixed cameras. Engine change: goes to the user first.


## Notes

**2026-10-20T00:41:25Z**

2026-10-19, from the review of ter-2n31: the first text said that this mode would also smooth a sudden move of the person. It would not, because the attachment still adds the parent's position at the end of the step in flight. The text above is corrected. The chase camera of ter-2n31 now follows the eye's position exactly (it has no position lag) and smooths only its heading, its up and its arm, so this mode would only remove the swing from an up-turn.
