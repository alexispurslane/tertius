---
id: t58-rcd6
status: open
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
