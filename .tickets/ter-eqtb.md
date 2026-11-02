---
id: ter-eqtb
status: open
deps: [ter-rjts, ter-y8z0, ter-f3g2, ter-rnjk]
links: [ter-f3g2]
created: 2026-11-01T15:28:45Z
type: bug
parent: ter-b2da
priority: 2
assignee: Alexis Purslane
tags: [rendering, reflections]
---
# Reflections: stair steps and sawtooth ramps on surfaces that the eye sees at a grazing angle (ter-y8z0's artifact 1)

Found on 2026-11-01 in ter-pjza's captures of demo-exit's floor film (one frame from the middle of the film, 1920 x 1080, Release); the base ef3b6ae5 shows the same, so no share ticket caused it. Captures: captures/floor-reflection-steps-2026-11-01/floor-base-ef3b6ae5.png and floor-pjza.png in the main checkout (git ignores captures/). In the middle distance (about y 560 to 720), the floor's reflection of the far floor where it curves up shows horizontal stair steps, several pixels tall. The reflection also stops at hard horizontal cut lines (about y 565 to 590, and beside the boxes near y 855). A short green line floats at about (735 to 850, 497), above the reflection, which looks like a mirrored edge of the green box in the wrong place. The cause is not known: the half-resolution trace with its step limit (MaxSteps 256, ter-r5hb), the depth pyramid's levels at a grazing angle, or the screen edge fade. Find the cause with probes before any fix; ter-rjts changed the reflections pass, so start from main after ter-rjts merges.

## Acceptance Criteria

Scope narrowed on 2026-11-01 after ter-y8z0 (see its notes): this ticket fixes artifact 1, the stair steps and sawtooth ramps from the level-0 hit test, on every view that shows them (the floor film, chute-top, and inside the deck and unbelt reflections). The floating green line is scene geometry, not a reflection, so it leaves the scope. The hard cut lines are artifacts 3 and 5, which ter-f3g2 probes for the user's design choice. The fix keeps one model of a pixel's depth extent at every level of the depth pyramid (ter-y8z0's review finding F6), keeps the image of the other review scenes, and the user chooses among the measured candidates (planeext, plane0, planeq, grazefade) with images. It waits for ter-f3g2, because a resolve that reuses neighbors' rays can change how visible the steps are on rough surfaces.

## Notes

**2026-11-01T15:49:07Z**

The deck film shows the same kind of fault: the floor's reflection of the greenhouse breaks into blocky fragments (captures/mfyv-candidates/c4-gather/exit-films/deck.png in the main checkout, ter-mfyv's final build at 1920 x 1080; ter-mfyv did not touch the reflections).
