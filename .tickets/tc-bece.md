---
id: tc-bece
status: closed
deps: []
links: []
created: 2026-11-22T16:31:25Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# After a lamp switch darkens part of the cabin, the floor, walls and ceiling show grain and boiling light for about 60 draws, on main and on Design X

**2026-11-25 · Superseded by ter-xzj7.** Use the old observations as controls after isolating which pass produces the filmed berth grain. A result on the old test scene alone would not settle the film defect.

The survey's switch "cabin" (scene orbit, camera person, `TERTIUS_PROBE_LAMP_SEL=z:-12:4`, lamps off at draw 193) turns
off 2 of the scene's 6 lights: the near overhead lamp and, probably, the lamp about 2 m behind the eye. The middle and
far overhead lamps keep their light. Before 2026-11-22 the probe left the switched panels glowing
(`TERTIUS_PROBE_LAMP_GLOW=stays` now). That glow reached the image only through the main view's emission and its
bloom: `glowSent` (Shaders/frame.glsl) already keeps a light-holding instance's glow out of screen-ray hits and
environment faces. So the darkened half of the room is lit by the remaining lamps and by bounce light, not by ray hits
on the switched panels.

## Evidence (t0-5tyj)

- Videos under the dynamic render scale on the RTX 2000 Ada, main c4c1f1f2f left, Design X 57b4045ae right:
  docs/research/design-x-acceptance-2026-11-22/pics/switch-cabin-dynamic-16ms.mp4 and switch-cabin-dynamic-10ms.mp4.
  The user saw grain on the floor and boiling light after the lamps go off.
- docs/research/design-x-acceptance-2026-11-22/tools/boilnoise.py measures the boil without the fade: per 16 x 16 px
  display-linear block, the residual b[k] - (b[k - 1] + b[k + 1]) / 2 against the straight line through the two
  neighbouring draws, as a mean absolute value in % of the crop's light. A fade that is linear over three draws gives
  zero. (Its first version took |b[k] - b[k - 1]|, which counts the room's smooth fade after off as boil. The numbers
  of that version, 0.45 to 1.25 % on the card, were the fade plus the boil.)
- The fair switch (glow off) at the fixed 2/3 on the Mac, no size change (pics/switch-cabin-fair-fixed-23.mp4): before
  off 0.02 to 0.04 %; 0 to 60 draws after off M 0.23 to 0.29 %, X 0.26 to 0.32 % (floor, left wall, ceiling); 60 to 200
  draws after off M 0.07 to 0.09 %, X 0.04 to 0.07 %. So the boil is there at a fixed size, about 8 times its level
  before off in the first 60 draws, and X shows slightly more there and less afterwards.
- The old switch on the card at 16.67 ms, on draws of one size: 0 to 60 draws after off M 0.27 to 0.43 %, X 0.34 to
  0.47 %. On draws whose size changed it is 0.37 to 0.86 %.
- The crops' mean light falls smoothly after off at the fixed 2/3 (pics/boil-levels-fair-23.png): the largest rise
  from one draw to the next in the 200 draws after off is 0.18 %. On the card's moving scale, single draws rise by up to
  0.7 to 1.1 %, which follows the size changes.
- The 3 x 3 region means fall smoothly after off on all runs (pics/boil-switch.png). The boil is smaller than a region.

## Open

- What lights the grain: the young histories of the darkened surfaces after the light change, or rare bright ray hits on
  the lit far panels. ter-fe63 (the ceiling's speck cloud in a sunlit room) is a candidate of the second class.

No fix in t0-5tyj.
