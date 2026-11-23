---
id: t58-rcd6
status: open
deps: []
links: []
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

