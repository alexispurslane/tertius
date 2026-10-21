---
id: ter-asce
status: closed
deps: [ter-yhu8, ter-ftnj, ter-y0e5]
links: []
created: 2026-10-21T01:51:49Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
---
# Demo final assembly: the docking and the exit in the cut list, the frame outlines, and the final render

Step 8 of "The edit" in docs/plans/blue-danube-demo.md, split from ter-2zrs when the edit tools merged on 2026-10-20. After the Earth world (ter-yhu8), the docking (ter-ftnj) and the exit (ter-y0e5) merge: add their parts and the wheel's panel to BlueDanubeEdit's cut list, give the sections their Outlines, write scripts/blue-danube.json again, measure the dark frames again as in "The dark frames" (keep every frame where the shuttle or the station shows), render the master, the readout log and the edit with the music, look at the frames, and send the video to the user. One known change: the readout log reads the person's position and speed in earth-surface. After the Earth world, earth-surface turns, so the speed and the textbook check of the parking orbit must read in earth, the frame that does not turn.


## Notes

**2026-10-21T02:43:42Z**

The readout log moves to earth in the Earth world's review (ter-yhu8), if that review merges main with the edit in it. Check it before the final render.

**2026-10-21T14:38:18Z**

Merged into main on 2026-10-21 after one review-and-fix (9130ac3: one count of a card's frames, TitleCard.Frames, and a test that could not fail) and the plan's music lines (40ebd28). Full suites passed in Debug and Release. Coverage of the changed lines: 85 of 89 lines, 7 of 8 branches. The uncovered lines are the catch for a failed write of the cue sheet (EditCommand.cs 130-133) and one branch of CueSheet.cs line 42: the coda's ticket ter-h74c removes the cue sheet first. Under coverage, the known allocation tests failed (ContactAllocationTests twice, EventListTests.SteadyStateUseDoesNotAllocate once, see ter-odo6), and they pass without it.
