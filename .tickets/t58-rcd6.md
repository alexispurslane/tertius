---
id: t58-rcd6
status: in_progress
deps: []
links: [ter-6fka]
created: 2026-11-22T22:51:40Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, history]
---
# After 100 draws on the bright exterior, a cut into the cabin stays saturated white for over 100 draws

Found by t0-gumm on 2026-11-22 (docs/research/t0-gumm-catch-up-spread, section "The card, job 1"), not hunted.

The film: docs/research/t0-gumm-catch-up-spread/cut-exterior.json. The orbit scene on the chase camera (the planet, far too bright at this exposure): 60 ticks, a still, film a of 100 frames (draws 33 to 132), then the camera changes to the person camera in the cabin and film b runs 200 frames (draws 133 to 332).

The run: the build xt of that record (X5, the tree e232080f of the line's merge 710834cc0, with the survey's probe patch and the record's scratch logs), headless at 1920 x 1080, the constant render scale 2/3 played back through TERTIUS_X_SIZE_REPLAY. The exposure is fixed: TERTIUS_PROBE_EXPOSURE=4.08, and exposure.json gives 4.08 stops for every frame. So the automatic exposure of t2-supl is not involved.

What the frames show (the whole image's mean of the probe's series, frames.series; the cabin's settled level is about 302):
- a-000099, the last chase frame: 29,614.
- b-000000 to b-000050: 65,536 or close to it, a white frame.
- b-000100: 40,421. b-000150: 5,166. b-000180: 571. b-000199: 329, still 9 % above the settled cabin.

The history log shows that the cut restarts the histories: at draw 133 the diffuse history's mean length falls from 10.5 to 0.98 draws. So some state that a cut does not empty carries the exterior's light into the cabin and decays over about 150 draws.

A cut inside the cabin (one frame on the chase camera between two person films, the record's cut.json) shows no such light: the first frame after the cut is at 293 against 302.

Picture: docs/research/t0-gumm-catch-up-spread/pics/cut-exterior-white.png: a-000099, b-000000 and b-000010 side by side.

To see it: run cut-exterior.json headless with TERTIUS_PROBE_EXPOSURE=4.08 and TERTIUS_PROBE_SERIES=<file>, and read the series of film b.


## Notes

**2026-11-23T14:17:05Z**

2026-11-23, line orchestrator of the back-face line: the fault repeats in the surface share's takes (t1-wl7c), on main 373da00eb and on the share's head e11b3548b alike, so the share neither causes nor changes it. Two takes of the orbit scene at one fixed exposure, in plain Release copies. 1. Person camera, 30 draws of the chase camera on the sunlit exterior, person camera again: the lamp hall stays saturated white for all 60 draws after the cut (videos/cut-via-exterior.mp4 and cut-via-exterior-c-000048-sbs.png, draw 48 after the cut). 2. The same with one draw of the chase camera: no saturation, and 3 capture specks in the first draw after the cut, then 0, on both builds (videos/cut.mp4). So the fault needs more than 1 draw on the exterior and has all it needs after 30. That bounds what holds the light: something that gathers over 2 to 30 draws, not the cut's first draw. Folder: /Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7-evidence/surface-share/videos/.

**2026-11-23T14:19:50Z**

2026-11-23, orchestrator: this is a visible fault by the user's bar (a white room for one to two seconds after a cut from outside), so it gets a diagnosis now, and I link it to the structural step ter-6fka. My hypothesis, to test and not to assume: the state that the cut does not empty is the eye's environment map. It refreshes one face per draw, which fits the back-face line's bound (nothing after 1 exterior draw, everything after 30). After the cut every history is empty, so leavingLightOf and the young histories read the closed form, and the closed form reads the map, which still holds the sunlit exterior. That light enters the diffuse histories. The bounce loop then feeds on itself (a ray's hit reads the hit pixel's history), and in a pale room its gain per round is high, which fits a decay over about 150 draws where the map itself is fresh after 6. The test, a log and no fix: per draw from 5 draws before the cut to 200 after it, the mean of the eye's map's irradiance, of the closed form's light, of the diffuse history and of the hit light, in the cut-via-exterior take. Then one scratch variant that refreshes all six faces of the eye's map in the reset draw, before any pass reads it: does the white frame go? If the hypothesis holds, the class is 'light that depends on where the eye was', the same class as t2b-xeo7, and the far-field store per semantic frame removes it by construction, because a frame's capture points do not follow the eye. A refresh of all six faces at a reset is then the direct fix that the batch of the face pass (t1-6tn2's probe) makes cheap.

**2026-11-23T14:21:49Z**

2026-11-23, line orchestrator of the back-face line: the diagnosis is launched, by the orchestrator's order of this day. It goes to the lead of the face cost split (t1-6tn2), because that lead holds the environment map's probe and because a log needs no quiet machine: it fills the time in which that lead's timing rows wait for a load below 8. Thread thread:delegated-task:command%3Amcp%3A49b05ffc-1378-4e84-92ef-b16c94dc7acb%3Adelegate-task%3Aslabs-4-ter-qt72-lead-1, worktree t3-1f553e45-00934a8d076a004a, clientRequestId back-face-6tn2-rcd6-diagnosis. The work: the orchestrator's log (per draw from 5 before the cut to 200 after it: each map's frame, refreshed face and mean irradiance, the means of the closed form's light, the diffuse history and the hit light, and the whole image's mean) on this ticket's own script cut-exterior.json, and one scratch variant that refreshes all six faces of every map that the eye's view reads in the cut's reset draw. One check that I added, which can speak against the hypothesis: if the map takes one exterior face per draw, then the one chase draw of cut.json puts the exterior into one face of six, and the map's irradiance must show it, yet that take shows no light (293 against 302). The lead logs that take too: which face of which map the chase draw refreshes, and what the irradiance does. If the variant does not remove the white room, the lead reports which logged mean stays high and builds nothing more. No fix is designed here. The record goes to docs/research/cut-white-room/README.md on the lead's branch.

**2026-11-23T14:55:06Z**

2026-11-23, line orchestrator of the back-face line: the diagnosis is done (the face cost split's lead; record docs/research/cut-white-room/README.md, commit ab5c2715b on t3/fix-projection-aspect-wobble, not merged yet; a plain copy of main e7d4970fe with a probe, no engine change). The orchestrator's hypothesis holds in its main part. The cause: every environment map is captured at the eye, and EnvironmentChain.Keeps keeps a map when the chain's frames are the same and looks at no eye position. Both cameras serve the same three frames, so the cut keeps every map. Map 2, the map of the camera's frame, still holds the faces that the chase eye drew outside the hull: its mean irradiance is 29.14 against a true 0.00603 in the cabin, about 4,800 times too much. Map 1 leaves out the ship, so it reads 28.07 from either eye. The scratch variant refreshes all six faces of every map in the reset draw, and the white room is gone from the first draw. Means as map 2's irradiance / diffuse history / whole image (8-bit mean x 257), main against the variant: draw 0 after the cut 29.14 / 309 / 65,535 against 0.00603 / 0.103 / 23,587; draw 10 8.10 / 218 / 65,535 against 0.00603 / 0.123 / 25,228; draw 50 0.00603 / 75.1 / 65,481 against 0.00603 / 0.131 / 26,042; draw 199 0.00603 / 0.405 / 43,597 against 0.00603 / 0.138 / 26,701. Why it lasts about 200 draws, in two parts. 1. The map is fresh only at draw 27: its faces refresh in turn, one face per draw over the 18 faces of the three maps, and it takes two rounds, because the prefilter writes the irradiance of only the drawn face from the six faces as they are stored then (EnvironmentPrefilterPass's own comment), so the first round convolves each face while later faces still hold the exterior. 2. After that the light stays in the loop between the diffuse history and the hit light (a ray reads the hit pixel's hit light, which reads that pixel's history). The loop loses about 3.4 percent per draw: the history's ratio is 0.963 to 0.968 per draw from draw 30 to 199. The one-draw take agrees: its one chase draw refreshed face -X of map 1 and no face of map 2, and map 2 stayed at 0.00603. One part of the hypothesis does not hold: the closed form reads 0 in the cabin at every draw after the cut, so it is not the path. The record does not name the term that reads the stale map at draw 0. The candidate is the diffuse ray that leaves the screen (environmentLobe in screen_trace.glsl, line 457, which reads the radiance cube). Also open: map 0's irradiance reads 0 at every draw. No fix is designed here. The fix that the variant shows: a reset draw refreshes all faces of every map before any pass reads them, as a run's first draw already does.
