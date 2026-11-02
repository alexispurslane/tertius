---
id: ter-eqtb
status: open
deps: [ter-rjts, ter-y8z0, ter-f3g2, ter-8bda]
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

**2026-11-02T02:28:35Z**

The user's decision of 2026-11-01, after ter-f3g2's probe: SSR's roughness limit goes to 0.37 now, inside epic ter-b2da; below it, today's method (one ray with a cone) stays; stochastic SSR (ter-rnjk) is not built; ter-gf0t moves into the epic to give back part of the lamp glow on rough surfaces that lose SSR. Reason: the probe showed that stochastic SSR helps only between roughness 0.37 and 0.6, which the 0.37 limit removes from SSR anyway, at +3 to +6 ms measured and with grain that never settles, while the limit saves 1.65 to 2.0 ms and matches the 64-ray reference on the deck paint. With the limit at 0.37, the demo's paint (roughness 0.38) has no SSR, so artifact 1 leaves the paint. After ter-8bda merges, the orchestrator captures the views of artifact 1 again (the floor film, chute-top, deck, unbelt) and keeps this ticket only for the views where the steps remain; if none remain, it closes.

**2026-11-02T09:31:46Z**

2026-11-02: closed without a change, as the note of 2026-11-02T02:28:35Z decided for this case. ter-8bda (merged as 6a96ae2db) set the SSR limit to 0.37. Its captures show each view of artifact 1 (the floor film, chute-top, the deck and unbelt) byte-identical to the same view with reflections off, and its probe marks no pixel that starts a ray in 363 frames of the films, deck-late and demo-surface (captures/8bda/ in the main checkout). So no reflection stair steps remain in the demo. Steps can still appear on surfaces below roughness 0.37, such as the mirrors of mirror-balls at grazing angles. No shipped scene shows them there, so no ticket opens for them now.

**2026-11-02T11:47:51Z**

2026-11-02: reopened at the user's word. The stair steps come from the level-0 hit test, which still runs on every surface below the SSR limit of 0.37. The demo has no such surface now, but any smooth surface (a mirror, polished metal, a smooth floor) can show them, so the fault stays. Scope from now: artifact 1 on surfaces below the limit. The builder first finds or builds a review view that shows the steps on such a surface (for example mirror-balls, the reflections scene, or a smooth floor in a test scene), then measures the candidates there (planeext, plane0, planeq, grazefade), and the user chooses with images. Its launch waits for the user's answer on epic ter-b2da's open question (the reflected brightness that the 0.37 limit loses), because a change of the limit changes which surfaces this ticket covers.
