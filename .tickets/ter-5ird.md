---
id: ter-5ird
status: in_progress
deps: []
links: []
created: 2026-10-31T21:17:06Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [tests, host, demo]
---
# Host tests run the checks of the checked-in scripts of the hair, skin and reflection demo scenes

Found by the coverage check of epic ter-n7lw (2026-10-31). The epic's changed engine lines are 96 to 100 % covered, and the rest are unreachable arms of closed unions and the cleanup after a failed pipeline creation. But its demo scene builders are almost untested: DemoScenes.HairCards.cs 5 of 148 changed lines, DemoScenes.HairRugs.cs 0 of 68, DemoScenes.Reflections.cs 1 of 18. Only headless captures exercise them, so a scene that breaks (a missing camera, a renamed view, a script that names a view that no longer exists) shows only when someone runs the script. ShadowScenesTests.TheCheckedInScriptOfEachShadowScenePassesTheChecksOfARun already builds each shadow scene from its checked-in script and runs ScriptRunner with StopsAtOnce. The work: the same test over the scripts of the hair, skin and reflection scenes (for example hair-cards, hair-rugs, skin-and-hair and the reflection scenes), plus a check that each scene's named views exist. No GPU is needed: ScriptRunner runs the world without a renderer, as in ShadowScenesTests.


## Notes

**2026-10-31T21:30:55Z**

Workflow run 10a316ab-f4fb-4f63-bef4-afa1df798711 (version 869b0b07), branch ter-5ird-hair-scene-script-checks from d4bd48de.
