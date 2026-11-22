---
id: tc-bece
status: open
deps: []
links: []
created: 2026-11-22T16:31:25Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# After a lamp switch darkens part of the cabin, the floor, walls and ceiling show grain and boiling light for about 60 draws, on main and on Design X

The survey's switch "cabin" (scene orbit, camera person, `TERTIUS_PROBE_LAMP_SEL=z:-12:4`, lamps off at draw 193) turns
off 2 of the scene's 6 lights: the near overhead lamp and, probably, the lamp about 2 m behind the eye. The middle and
far overhead lamps keep their light. Before 2026-11-22 the probe left the switched panels glowing
(`TERTIUS_PROBE_LAMP_GLOW=stays` now). That glow reached the image only through the main view's emission and its
bloom: `glowSent` (Shaders/frame.glsl) already keeps a light-holding instance's glow out of screen-ray hits and
environment faces. So the darkened half of the room is lit by the remaining lamps and by bounce light, not by ray hits
on the switched panels.

## Evidence (t0-5tyj, the old switch, glow stays)

- Videos under the dynamic render scale on the RTX 2000 Ada, main c4c1f1f2f left, Design X 57b4045ae right:
  docs/research/design-x-acceptance-2026-11-22/pics/switch-cabin-dynamic-16ms.mp4 and switch-cabin-dynamic-10ms.mp4.
  The user saw grain on the floor and boiling light after the lamps go off.
- docs/research/design-x-acceptance-2026-11-22/tools/boilnoise.py measures the boil: the mean absolute change of
  16 x 16 px display-linear blocks from one draw to the next, in % of the crop's light. In the 20 draws before off it is
  0.03 to 0.08 %. In the first 60 draws after off, on draws whose render size did not change, it is 0.45 to 1.25 %
  (floor M 0.81, X 0.45, left wall M 1.18, X 0.93, ceiling M 0.96, X 1.25 at 16.67 ms). A size change about doubles it
  on that draw.
- The 3 x 3 region means fall smoothly after off on all runs (pics/boil-switch.png). The boil is smaller than a region.

## Open

- Whether the boil and the grain stay with the fair switch (`TERTIUS_PROBE_LAMP_GLOW=off`, the panel dark with its
  light) and at a fixed render scale. t0-5tyj takes the fair switch at the fixed 2/3 on M and X.
- What lights the grain: the young histories of the darkened surfaces after the light change, or rare bright ray hits on
  the lit far panels. ter-fe63 (the ceiling's speck cloud in a sunlit room) is a candidate of the second class.

No fix in t0-5tyj.
