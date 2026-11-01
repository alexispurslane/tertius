---
id: ter-k96l
status: open
deps: [ter-xgra]
links: []
created: 2026-11-01T01:19:16Z
type: bug
parent: ter-b2da
priority: 2
assignee: Alexis Purslane
tags: [rendering, screen-space-light]
---
# Screen-space light: a blotchy dark-brown band along the floor's creases on demo-exit's deck under a bright environment map

Found by the orchestrator on 2026-10-31, in the screenshots of ter-qsye (main cae67c5e, headless Release, 1280 x 720). Script captures/deck-fringe-2026-10-31/late.json: demo-exit, cabin camera, post LeaveShip, run 399 ticks, deck camera, run 520 ticks, capture deck-late. At that moment a bright blue light floods the deck through the opening at the upper left.

What shows: along the crease where the deck floor meets the cabin's base, and along the base of the far wall, the floor carries a band about 10 to 20 px wide of dark brown blotches, and a smaller blotch lies at the foot of the blue pillar. 3x crops: captures/deck-fringe-2026-10-31/fringe-3x.png (cabin base) and captures/deck-fringe-2026-10-31/fringe-left-3x.png (far wall).

Which pass: one headless run per switch (--screen-space-light off, --reflections off, --environment-map off, same script). With the screen-space light off the band is gone (captures/deck-fringe-2026-10-31/ab-ssl-3x.png). With the reflections off it stays (captures/deck-fringe-2026-10-31/ab-refl-3x.png). With the environment map off the crease shows a smooth gray darkening with no blotches (captures/deck-fringe-2026-10-31/ab-env-3x.png). So the blotches come from the screen-space light (VisibilityBitmaskLight, 1 m, 3 slices, 3 steps per side) where the environment map's ambient is bright. A guess, not checked: the crease takes away the bright blue ambient and the bounce adds the cabin's dark brown, and with 3 slices the estimate's noise shows as blotches at that contrast. The earlier deck frame (deck-early, tick 519, before the light floods the deck) shows no band.

Not caused by ter-qsye: its builder found all 740 deck frames identical to its base.

## Acceptance Criteria

The deck-late view shows no blotches along the floor's creases, at the default ScreenSpaceLight, or the ticket records why the blotches are the right answer of the estimate and what a later change would cost. A reference image or a render test pins the result.

## Notes

**2026-11-01T01:19:31Z**

The evidence (the script, the full deck-late.png and the 3x crops) is in captures/deck-fringe-2026-10-31/, which git ignores, so it lives only on this machine. The script re-creates every image: dotnet run -c Release --project src/Tertius.Demo -- --script captures/deck-fringe-2026-10-31/late.json --headless --out <folder>, plus one switch for the A/B runs.

**2026-11-01T17:13:29Z**

2026-11-01, the user's question for the fix design: why M at all, instead of reading the map's light along the open directions, as the bounce reads the blockers' light along the blocked ones? D(n) − M is the map's light over the open directions, so the value is decision 41's rule (the user's, 2026-10-27). Only the way of computing it differs. Decision 42 chose whole minus blocked: exact at open points, cost per blocked piece, and D keeps the full-resolution normal. Its weakness is the crease: there the open light is a small difference of two large numbers, so all of M's error lands on it, and the clamp at 0 turns an overshoot black. D comes from the spherical harmonics and M from the specular map, so the two stores can also disagree under a small bright light without any noise. Forms to probe, each with captures of deck-late, brake-run and chute-top and its GPU cost, before the fix comes to the user as two designs: (1) subtraction as now, with M read through wider lobes; (2) the direct sum: the open share as the sum of the map's light read along the open pieces; (3) the ratio: D times the share that the open pieces hold of the map's light over all pieces, from the same reads, so that the read error cancels between the two sums, the result stays between 0 and D, and an open point stays exactly D.
