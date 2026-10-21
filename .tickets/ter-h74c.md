---
id: ter-h74c
status: closed
deps: [ter-asce]
links: []
created: 2026-10-21T14:13:35Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo, video]
---
# Demo coda: loose boxes on the wheel's floor, a knocked-over stack and two throws that prove spin gravity

The user asked on 2026-10-21 for a coda with loose boxes in the station, where the person interacts with them to prove that the spin gravity is real, and chose these answers: the proof is the throws, and the person first walks into a stack of boxes and knocks it over. After the floor film, the person walks into a stack of loose boxes on the wheel's floor and knocks it over, so the boxes tumble and settle under the spin gravity. Then the person throws one box spinward and one antispinward in the same way, 8 m/s at 30 degrees up from the eye: they land 5.8 m and 11.3 m away, where a flat 1 g field gives 7.7 m both ways (survey numbers, from the inertial straight line). Floor marks at the 7.7 m points show where real gravity puts them. A textbook panel shows the measured landing points beside the predicted ones. The boxes are ordinary dynamic bodies in the station's frame (CrateSpec, with NoImpostor). The person runs a new flat state machine started by a posted order, and takes boxes by the messages with which it takes the pen (Use, CrateOffer, CrateTaken), after it puts the pen away. Cameras outside the person film it. The first step is a test that drops a box in the demo's own station, which orbits on Kepler rails with a spin attitude: no test pins a free box there yet. The boxes cast no shadows under the lamps (the sun is the only light with shadows), and the user accepts that for now. No engine change.


## Notes

**2026-10-21T14:18:38Z**

First step, before the coda: remove the cue sheet (CueSheet.cs, CueSheetTests, the writer in EditCommand and its two tests, the plan's section 'The cue sheet'). It was made for the score of ter-zd4t, which the user left out on 2026-10-21, so nothing reads it, and the user prefers to leave out what nothing needs. Keep TitleCard.Frames, the one count of a card's frames, with a test that pins it against the video.

**2026-10-21T17:33:43Z**

Merged into main on 2026-10-21. Coverage of the changed lines: 447 of 473 lines and 182 of 199 branches. The uncovered lines are guards for a person or box that is gone, the union members HasValue and GetHashCode, the log line for an event that BoxPlay ignores, the guards of FlightRecorder for a history shorter than two steps, and the exception of ThrowRelease for a line that never comes down. The only failures under coverage are allocation tests that the coverage instrumentation breaks (ter-odo6).
