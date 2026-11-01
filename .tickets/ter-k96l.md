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

## The fix: settled by the user on 2026-11-01

This ticket computes the open share's light as D times the open pieces' share of the map's light. It replaces decision 42's subtraction, D(n) − M. The user called it "the obviously correct optimal approach". No other form of the fix is in scope, and the two-designs step does not apply to this choice.

- The sectors pass reads the map along the open pieces as well as along the blocked pieces, with the same rule for a read's direction and lobe.
- The open light is D(n) · S, where S = (the map's light along the open pieces) / (the map's light along all pieces). D(n) stays the irradiance at the pixel's full-resolution normal.
- The diffuse term is c_diff · (D(n) · S + G). It needs no clamp at 0, because S lies between 0 and 1.
- What follows from the form: an open point keeps exactly D(n), because S = 1 there. The result never goes below 0, so no pixel turns black or brown from an overshoot. A read's error appears in both sums of S, so much of it cancels. D(n) and S come from different stores of the map, but S is a ratio of reads from one store, so the two stores no longer have to agree.
- Settled by the orchestrator inside this design: S is taken per color channel, so that the open light takes the color of the open directions. Where the map's light along all pieces is 0 in a channel, S in that channel is the open share of the cosine weight.
- Left to this ticket's design, with the reason recorded: how the estimate stores S beside G and V (the targets carry G − M and V today), and the measured cost of the open pieces' reads.
- The fix updates decision 42 and "Known limits" in docs/plans/screen-space-illumination.md, and the section on the screen-space light in ARCHITECTURE.md.

Found by the orchestrator on 2026-10-31, in the screenshots of ter-qsye (main cae67c5e, headless Release, 1280 x 720). Script captures/deck-fringe-2026-10-31/late.json: demo-exit, cabin camera, post LeaveShip, run 399 ticks, deck camera, run 520 ticks, capture deck-late. At that moment a bright blue light floods the deck through the opening at the upper left.

What shows: along the crease where the deck floor meets the cabin's base, and along the base of the far wall, the floor carries a band about 10 to 20 px wide of dark brown blotches, and a smaller blotch lies at the foot of the blue pillar. 3x crops: captures/deck-fringe-2026-10-31/fringe-3x.png (cabin base) and captures/deck-fringe-2026-10-31/fringe-left-3x.png (far wall).

Which pass: one headless run per switch (--screen-space-light off, --reflections off, --environment-map off, same script). With the screen-space light off the band is gone (captures/deck-fringe-2026-10-31/ab-ssl-3x.png). With the reflections off it stays (captures/deck-fringe-2026-10-31/ab-refl-3x.png). With the environment map off the crease shows a smooth gray darkening with no blotches (captures/deck-fringe-2026-10-31/ab-env-3x.png). So the blotches come from the screen-space light (VisibilityBitmaskLight, 1 m, 3 slices, 3 steps per side) where the environment map's ambient is bright. A guess, not checked: the crease takes away the bright blue ambient and the bounce adds the cabin's dark brown, and with 3 slices the estimate's noise shows as blotches at that contrast. The earlier deck frame (deck-early, tick 519, before the light floods the deck) shows no band.

Not caused by ter-qsye: its builder found all 740 deck frames identical to its base.

## Acceptance Criteria

The open share's light is D(n) · S, as the section "The fix" says, and the subtraction D(n) − M and its clamp are gone. The deck-late crease, the brake-run crease, unbelt, chute-top and spinward (ter-xgra's artifact 1) and the deck-late band (artifact 2) show no blotches, dots or brown hue at the default ScreenSpaceLight. Each is compared with the light-off view and with a reference at 16 slices and 3 steps (ter-xgra found that more steps make M's reads sharper, so 16 x 16 is not a reference for the map's reads). A render test pins two behaviors: an open point keeps exactly D(n), and a crease beside a small bright light in the map gets the map's light over its open directions, within a stated tolerance of a reference at many slices. The cost is measured against ter-pjza's numbers.

## Notes

**2026-11-01T01:19:31Z**

The evidence (the script, the full deck-late.png and the 3x crops) is in captures/deck-fringe-2026-10-31/, which git ignores, so it lives only on this machine. The script re-creates every image: dotnet run -c Release --project src/Tertius.Demo -- --script captures/deck-fringe-2026-10-31/late.json --headless --out <folder>, plus one switch for the A/B runs.

**2026-11-01T17:13:29Z**

2026-11-01, the user's question for the fix design: why M at all, instead of reading the map's light along the open directions, as the bounce reads the blockers' light along the blocked ones? D(n) − M is the map's light over the open directions, so the value is decision 41's rule (the user's, 2026-10-27). Only the way of computing it differs. Decision 42 chose whole minus blocked: exact at open points, cost per blocked piece, and D keeps the full-resolution normal. Its weakness is the crease: there the open light is a small difference of two large numbers, so all of M's error lands on it, and the clamp at 0 turns an overshoot black. D comes from the spherical harmonics and M from the specular map, so the two stores can also disagree under a small bright light without any noise. Forms to probe, each with captures of deck-late, brake-run and chute-top and its GPU cost, before the fix comes to the user as two designs: (1) subtraction as now, with M read through wider lobes; (2) the direct sum: the open share as the sum of the map's light read along the open pieces; (3) the ratio: D times the share that the open pieces hold of the map's light over all pieces, from the same reads, so that the read error cancels between the two sums, the result stays between 0 and D, and an open point stays exactly D.
