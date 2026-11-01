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
# Screen-space light: stamp out the splashing: the open share S, a lower bound on the read lobes, and the cheapest clean set of slices and denoise passes

## The fix: settled by the user on 2026-11-01

This ticket computes the open share's light as D times the open pieces' share of the map's light. It replaces decision 42's subtraction, D(n) − M. The user called it "the obviously correct optimal approach". No other form of the fix is in scope, and the two-designs step does not apply to this choice.

- The sectors pass reads the map along the open pieces as well as along the blocked pieces, each along the middle of its piece, as decision 42 reads a blocked piece.
- The open light is D(n) · S, where S = (the map's light along the open pieces) / (the map's light along all pieces). D(n) stays the irradiance at the pixel's full-resolution normal.
- The diffuse term is c_diff · (D(n) · S + G). It needs no clamp at 0, because S lies between 0 and 1.
- What follows from the form: an open point keeps exactly D(n), because S = 1 there. The result never goes below 0, so no pixel turns black or brown from an overshoot. A read's error appears in both sums of S, so much of it cancels. D(n) and S come from different stores of the map, but S is a ratio of reads from one store, so the two stores no longer have to agree.
- Settled by the orchestrator inside this design: S is taken per color channel, so that the open light takes the color of the open directions. Where the map's light along all pieces is 0 in a channel, S in that channel is the open share of the cosine weight.
- Left to this ticket's design, with the reason recorded: how the estimate stores S beside G and V (the targets carry G − M and V today), and the measured cost of the open pieces' reads. The lobe width is settled below.
- The fix updates decision 42 and "Known limits" in docs/plans/screen-space-illumination.md, and the section on the screen-space light in ARCHITECTURE.md.

## Also in scope: every cheap splash fix (user, 2026-11-01)

The user: "that splashing is really truly horrible and I want to stamp it out with flat prejudice." So every candidate of ter-xgra that costs nothing measurable or about 0.05 ms goes in, beside the share form, and not only if specks remain:

1. A lower bound on the lobe width of every map read, open and blocked: roughness 0.5 in ter-xgra's probe, about 0.01 ms. It replaces the open item above on the lobe width. The ticket measures the bound and records its value.
2. The cheapest clean set of slices and denoise passes (user, 2026-11-01, after ter-xgra's fix stage). Part of the splashing is the angular noise of 3 slices, in S and in G: spinward's ceiling, chute-top, the demo-surface cabin's ceiling panel, part of brake-run, and the bounce's blotches (artifact 3) on brake-run, the terminal, the mirror-balls wall and the cabin floor. No lobe width removes it. On top of the share form and the lobe bound, the builder tries one and two more denoise dispatches and 4, 5 and 6 slices, alone and together, and keeps the cheapest set that is clean on every one of those views. Clean: no blotch, speck or grain shows at 3x beside the 16-slice, 3-step reference, and the review's noise measure lies within 10 % of the reference's. The set may cost up to about 1.1 ms. If no set within that is clean, the builder reports the best set and its cost as a fork. ter-xgra measured on the old code: two more denoise dispatches +0.11 ms (softer, not gone), 6 slices +0.99 ms (some mottling left).

Dropped by the user after ter-xgra's fix stage: the 3 x 3 outlier clamp. It changed nothing visible and cost +0.10 ms.

Two candidates are gone with the subtraction: the one clamp factor for all channels and the cap of M at (1 − V) · D. The share form has no clamp and no M. Out of scope: 16 slices (about 5 ms), one map read per slice (it does not fit the share form, which reads per piece), and a thinner slab (artifact 4 is a limit, and κ is SSR's shared constant).

Each part is measured on its own, in this order: the share form, then 1 and 2. Each reports its cost and its captures. Nothing accumulates over frames (decision 8). If a part makes a view worse, for example the lobe bound turns the cabin's base face black, the builder reports it as a fork and does not drop it alone.

Found by the orchestrator on 2026-10-31, in the screenshots of ter-qsye (main cae67c5e, headless Release, 1280 x 720). Script captures/deck-fringe-2026-10-31/late.json: demo-exit, cabin camera, post LeaveShip, run 399 ticks, deck camera, run 520 ticks, capture deck-late. At that moment a bright blue light floods the deck through the opening at the upper left.

What shows: along the crease where the deck floor meets the cabin's base, and along the base of the far wall, the floor carries a band about 10 to 20 px wide of dark brown blotches, and a smaller blotch lies at the foot of the blue pillar. 3x crops: captures/deck-fringe-2026-10-31/fringe-3x.png (cabin base) and captures/deck-fringe-2026-10-31/fringe-left-3x.png (far wall).

Which pass: one headless run per switch (--screen-space-light off, --reflections off, --environment-map off, same script). With the screen-space light off the band is gone (captures/deck-fringe-2026-10-31/ab-ssl-3x.png). With the reflections off it stays (captures/deck-fringe-2026-10-31/ab-refl-3x.png). With the environment map off the crease shows a smooth gray darkening with no blotches (captures/deck-fringe-2026-10-31/ab-env-3x.png). So the blotches come from the screen-space light (VisibilityBitmaskLight, 1 m, 3 slices, 3 steps per side) where the environment map's ambient is bright. A guess, not checked: the crease takes away the bright blue ambient and the bounce adds the cabin's dark brown, and with 3 slices the estimate's noise shows as blotches at that contrast. The earlier deck frame (deck-early, tick 519, before the light floods the deck) shows no band.

Not caused by ter-qsye: its builder found all 740 deck frames identical to its base.

## Acceptance Criteria

The open share's light is D(n) · S, as the section "The fix" says, and the subtraction D(n) − M and its clamp are gone. The lobe bound and the cheapest clean set of slices and denoise passes are in, each with its measured cost. The deck-late crease, the brake-run crease, unbelt, chute-top and spinward (ter-xgra's artifact 1), the deck-late band (artifact 2) and the bounce's blotches beside lit surfaces in brake-run, terminal, mirror-balls and cabin (artifact 3) show no blotches, dots, splashes or brown hue at the default ScreenSpaceLight. Each is compared with the light-off view and with a reference at 16 slices and 3 steps (ter-xgra found that more steps make M's reads sharper, so 16 x 16 is not a reference for the map's reads). A render test pins two behaviors: an open point keeps exactly D(n), and a crease beside a small bright light in the map gets the map's light over its open directions, within a stated tolerance of a reference at many slices. The cost is measured against ter-pjza's numbers.

## Notes

**2026-11-01T01:19:31Z**

The evidence (the script, the full deck-late.png and the 3x crops) is in captures/deck-fringe-2026-10-31/, which git ignores, so it lives only on this machine. The script re-creates every image: dotnet run -c Release --project src/Tertius.Demo -- --script captures/deck-fringe-2026-10-31/late.json --headless --out <folder>, plus one switch for the A/B runs.

**2026-11-01T17:13:29Z**

2026-11-01, the user's question for the fix design: why M at all, instead of reading the map's light along the open directions, as the bounce reads the blockers' light along the blocked ones? D(n) − M is the map's light over the open directions, so the value is decision 41's rule (the user's, 2026-10-27). Only the way of computing it differs. Decision 42 chose whole minus blocked: exact at open points, cost per blocked piece, and D keeps the full-resolution normal. Its weakness is the crease: there the open light is a small difference of two large numbers, so all of M's error lands on it, and the clamp at 0 turns an overshoot black. D comes from the spherical harmonics and M from the specular map, so the two stores can also disagree under a small bright light without any noise. Forms to probe, each with captures of deck-late, brake-run and chute-top and its GPU cost, before the fix comes to the user as two designs: (1) subtraction as now, with M read through wider lobes; (2) the direct sum: the open share as the sum of the map's light read along the open pieces; (3) the ratio: D times the share that the open pieces hold of the map's light over all pieces, from the same reads, so that the read error cancels between the two sums, the result stays between 0 and D, and an open point stays exactly D.

**2026-11-01T17:30:40Z**

2026-11-01, from ter-xgra's review (the fix stage is still running, so these may change): (1) The brown band's bias comes from the two stores of the map: D is band-2 spherical harmonics, M comes from the specular map, so a converged M can exceed D under a small bright light (Known limit 2). The share form removes that class: D(n) · S never goes below 0, so no clamp and no hue change. D keeps its own band-2 error, now only as a bias of D. The fixer probes a brute-force D to measure how large that bias is. (2) The cabin base face above the deck-late crease (x 1200 to 1900, y 560 to 700) responds the opposite way to the floor: a wider lobe makes it black (rough1), because the lowest blocked piece's lobe reaches past the receiver's own plane (Known limit 1). So the lower bound on the lobe width can darken a vertical face beside a crease. This ticket checks that face with every lobe width, and clips a read's lobe at the receiver's plane if the bound darkens it. A face that turns black is a fork for the user, not a reason to drop the bound alone. (3) Part of artifact 1 is the angular noise of 3 slices in M (spinward, chute-top, part of brake-run), which no lobe width removes. In the share form that noise moves into S. The outlier clamp and the second denoise are its fixes, and the acceptance views include spinward and chute-top. (4) Two more views of artifact 1: the floor at the foot of the stacked boxes in the floor film (about 1400,860 to 1520,960) and the base of the gray ceiling panel in the demo-surface cabin (1800 to 1920, 470 to 490). The reviewer's noise measure (the mean absolute difference between luma and its 9 x 9 box blur, times 100, over a fixed box) scores each view against the light-off twin and the 16 x 3 reference.
