---
id: t0-d3vd
status: in_progress
deps: [t0-0fhn, t0-aiim]
links: []
created: 2026-11-13T01:01:07Z
type: task
priority: 2
assignee: Alexis Purslane
parent: t7-3e22
---
# Lock in Design X with real rays: a lamp that goes out, a still scene and a box that slides

Design: docs/plans/bounce-light-confidence.md (Design A with Design X's evidence), section 23. Branch t7-3e22-retained-deviation. Step 14, section 20 (real rays end to end), ScreenTraceRenderTests: ALampThatGoesOutCutsTheBounceHistoriesOfTheBlocksThatSeeItsLight, AStillSceneGivesNoEvidenceToEitherSignal, ABoxThatSlidesPastAWallNarrowsTheWallsBoxWhileItMovesAndNotAfter.


## Notes

**2026-11-14T16:07:29Z**

Built: the lamp test and the still scene had landed in steps 12 and 13; this step adds OnceTheCodeOfALampThatWentOutIsNoLongerRecentTheCutBounceHistoriesGrowAgain (LightAgeHistoryTests; median length 22 after 24 draws, red at 4 on a hand mutant RecentLightAge 64) and ScreenTraceRenderTests.BoxSlidingPastAWall.ABoxThatSlidesPastAWallNarrowsTheWallsBoxWhileItMovesAndNotAfter (wall along the view, glowing box beside it slides away from the eye; 24 to 39 of 40 wall blocks carry a motion bit per draw on the workbench, none has evidence from 16 draws after the stop; red on hand mutants that drop the diffuse moved mark and set LeastMotion 0). The box needs its own entity: an instance without a rising entity and part has no identity and shows no motion. Workbench jobs for these tests and mutants: about $0.18. Full Checked on the Mac: 10,922, 0 failed.
