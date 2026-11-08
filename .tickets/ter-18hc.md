---
id: ter-18hc
status: open
deps: []
links: [tiunr-wk29, tiunr-nxtr]
created: 2026-11-04T20:48:47Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
---
# The slow history's clamp cuts rare bright samples: a small glowing share reads low

Found in ter-gayt, part 5. A floor row in front of a glowing panel (glow 2, sky 0.2, 160 frames) reads these panel shares in the diffuse slow history, against the form factor:

| Distance to the panel | Form factor | Slow history | Mean of the two fast histories |
|---|---|---|---|
| 4.95 m | 0.0433 | 0.0236 | 0.0368 |
| 3.95 m | 0.0689 | 0.0437 | 0.0593 |
| 2.95 m | 0.1144 | 0.0879 | 0.1019 |
| 1.95 m | 0.1955 | 0.1741 | 0.1726 |
| 0.95 m | 0.3292 | 0.3439 | 0.3566 |

The first frame's rays are unbiased (the C# walk and the GPU's own rays hit the panel at its form factor). The bias comes from step 4 of accumulate_spatial.comp: the slow history is clamped into mean ± clampSigmas · sigma of the fast history over 3 × 3 pixels. A bright sample that arrives in a few percent of the frames is mostly outside that box. The screen tracer's block rays make it worse: the four pixels of a 2 × 2 block share one ray, so the 3 × 3 neighborhood holds about four independent samples, and its sigma is small.

What the user gets: a glow that covers a small share of a pixel's hemisphere, such as a lamp far across a room, lights the surfaces about a third to a half too dimly in a still scene.

To do: measure the bias against the clamp's sigmas and against a neighborhood that spans more blocks, and choose a fix (for example a wider box, or a box from the fast history's own variance over time). The test AGlowingPanelBesideAFloorPixelTakesItsShareOfThePixelsDiffuseLight measures at 0.95 m, where the bias is small. Move it to 4.95 m when the fix lands.

