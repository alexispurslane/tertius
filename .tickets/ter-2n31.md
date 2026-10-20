---
id: ter-2n31
status: closed
deps: [ter-ozm8, ter-bgk5, ter-mln3]
links: []
created: 2026-10-18T14:08:42Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo, video]
---
# Demo video pipeline: the long-script generator, a chase camera, and the demo scene's frame time

Step 1 of docs/plans/blue-danube-demo.md. ffmpeg in the dev shell and the video sink came with E4 (ter-mln3), and the film step with E2 (ter-bgk5). Write a generator for the long script that writes film steps (a film step's every gives the time-lapse). A chase camera component on an entity attached to the character. Measure the headless frame time of the demo scene itself.


## Notes

**2026-10-20T00:45:42Z**

2026-10-19, merged. Coverage of the changed lines in src/ (Host tests, Debug): 254 of 270 lines (94.1%), 96 of 106 branches. A test of the generator's file error was added (its hand mutation, dropping IOException from the catch, fails it). The lines left uncovered need no test: Program.Main is a one-line call of Program.Run, which the tests call; the generator's catches of ScriptException and InvalidOperationException fire only when a change of the demo scene breaks a section, which the drift test of the checked-in script reports first; ChaseCamera's return when it has no subject or no pose, and its skip of a ray answer with another tag, are guards that the demo never reaches; BlueDanube.HandToPen's return when the pen is gone is a guard, because the pen lives through the whole demo. The final run was judged sane by a reader: 4,210 frames, no hitch inside a film, the follow camera clear of walls, and the person no longer fills the pen camera in fetch.
