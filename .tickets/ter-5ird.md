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

**2026-10-31T23:20:34Z**

Scenes covered: hair-cards, hair-cards-backlit, hair-bent-cards and hair-rugs (DemoScenes.HairCards.cs, with the hair-rugs builder in DemoScenes.HairRugs.cs), skin-and-hair (DemoScenes.SkinAndHair.cs) and reflections (DemoScenes.Reflections.cs). The checked-in scripts of these scenes are hair-bent-cards.json, hair-rugs.json, skin-and-hair.json and reflections.json. The hair-cards and hair-cards-backlit scenes have no script. lamp-hall-16-hair, also built in DemoScenes.SkinAndHair.cs, stays in ShadowScenesTests, which already runs its script.

**2026-10-31T23:20:34Z**

Settled: the tests are a new class, tests/Tertius.Host.Tests/HairSkinAndReflectionScenesTests.cs, beside ShadowScenesTests, because no existing class covers a sibling scene. It keeps its own private StopsAtOnce playback, as each Host test class that runs a script does. A view is in the picture when the center of its subject lies in front of it, inside the world's vertical field of view and inside the 16:9 width of the scripts' 1280 by 720 window. The starting-view test covers only hair-rugs and reflections, because only their scripts capture before their first camera step. No test found a defect in a demo scene.

**2026-10-31T23:44:53Z**

Review fix, settled: one theory, CheckedInScriptsTests.EveryCheckedInScriptPassesTheChecksOfARun, runs the checks of a run (ScriptRunner.Run with a playback that stops before the first tick) over every file in scripts/. Its rows come from HostTesting.CheckedInScripts, the folder listing that ScriptJsonFormatTests already used and now shares. So a new script needs no row anywhere. The per-family script theories of ShadowScenesTests, RigsSceneTests and HairSkinAndReflectionScenesTests and the run checks of BlueDanubeScriptTests and DemoExitScriptTests are gone. Their other facts stay: each shadow script films its own scene, each rigs script names its own scene, blue-danube.json films, and demo-exit.json films the exit's sections and then the coda's. The one StopsAtOnce is private to CheckedInScriptsTests, its only user. All 54 scripts pass, so the 8 scripts that no test named before (bounce-raised-box, bounce-red-wall, cabin-light, mars-air, mirror-balls, orbit-umbra, pbr-spheres, weightless-push) have no defect. No ticket is filed for this cleanup, because this commit does it.

**2026-10-31T23:44:53Z**

Review fix, settled (supersedes the 16:9 and starting-view parts of the note above): InPicture takes the width of the picture from the window of the scene's own checked-in script, scripts/<scene>.json, or from ScriptWindow.Default (1280 by 720) for hair-cards and hair-cards-backlit, which have no script. The starting-view test is now TheFirstPictureOfEachScriptShowsANamedViewOfItsOwnScene over the four scripts: it reads from each script the last camera step before its first capture, or the scene's starting camera when none comes first, and checks that this camera is one of the scene's named views. It also checks that each script names the scene of its own file name. A new fact test covers the reflections low-camera: it looks along the strips, and all six strips are in its picture. The class doc no longer says that each view is checked.
