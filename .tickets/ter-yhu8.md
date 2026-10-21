---
id: ter-yhu8
status: in_progress
deps: [ter-rlks, ter-4xkz]
links: []
created: 2026-10-20T22:46:02Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo]
---
# Demo world in the Earth system: the ground in the turning earth-surface, the station in earth

The user asked on 2026-10-20 to put the demo's world into the Earth system of the previous ticket at once. demo-surface builds the Sun, Earth's orbit frame earth, the turning earth-surface and the Moon. The ground, the pad, the ramp, the terminal and the shuttle start in earth-surface, which turns about the axis through the pad at the north pole. The station's orbit moves into earth. The guidance and the cues read in earth, the frame that does not turn. The ground's turn at the world's start puts the ramp in the station's plane at the departure. The shuttle leaves earth-surface through the top of its bubble during the climb: the first test of a dynamic hull with a cabin frame that leaves a turning frame. If it fails, the failure is an engine bug that goes to the user. The long script is written again.


## Notes

**2026-10-20T23:16:21Z**

Write the design first as a section of docs/plans/blue-danube-demo.md, then build it. The section settles: the ground and the shuttle in the turning earth-surface, the station's orbit in earth; every read of the guidance and the cues in earth, the frame that does not turn; the June solstice and the ground's turn at the world's start, so that the ramp lies in the station's plane at the cinematic's departure (the turntable of the live world stays at its home angle then); the station's orbit plane through the pole, chosen so that the sun lights the wheel's face at an angle while the orbit keeps nights for the night films (at the June solstice a plane with the sun in it lights the wheel edge-on, as the earth-system scene showed); the shuttle's hand-off out of earth-surface at 100 km during the climb, with its run test; and every line of the plan that says Earth does not turn. The docs of DemoStation.Spawn and DemoStation.Orbit still say earth-surface (from ter-rlks).

**2026-10-21T00:51:49Z**

The orchestrator settled the hand-off fork on 2026-10-20, with no engine change. Rule 2 of the hand-off sets the exit margin to 2 percent of the bubble's radius, 129.4 km for a bubble that ends 100 km up, so the shuttle would leave earth-surface 229.4 km up, above the 200 km parking orbit. The bubble of earth-surface now ends 50 km up (the stratopause), so its exit lies 178.4 km up and the shuttle hands off once, during the climb. AtmosphereTop stays at 100 km as the top of the air. A raised parking orbit retunes the window and many bounds, and a rendezvous autopilot that reads in the station's frame removes the guard that the docking builds on.
