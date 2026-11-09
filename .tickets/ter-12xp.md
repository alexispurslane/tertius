---
id: ter-12xp
status: open
deps: []
links: [tiunr-nxtr, tiunr-5qf3, tiunr-5jqs, ter-9371]
created: 2026-11-04T22:46:40Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
---
# Single bright pixels on the deck's walls in a still under the screen tracer

A headless still of demo-exit's deck at 1280 x 720 under ScreenTrace.Default, after 32 warm-up draws, shows isolated white pixels over the walls, beside the expected fine grain. The same still under --screen-trace off is smooth. Found during ter-gayt's screenshots (2026-11-04, /private/tmp/gayt-part8/shots, not kept). Find which rays bring them, such as a block ray at a weight near 4 that hits the bright lamp, and whether the slow history's clamp should hold them, and fix the cause.


## Notes

**2026-11-08T03:55:43Z**

2026-11-08: the same kind of isolated bright pixels shows across the rough floor of scripts/reflections-long under FSR, now that the pixel history rule keeps rare bright hits (/private/tmp/tertius-pixel-accept-20261108/shots/reflections-long/front.png). The mechanism is in /private/tmp/tertius-pan-cuts-20261107/report.md: one rare ray that hits the bright emitter stays in one pixel's long slow history. The box fix tiunr-nxtr keeps that light, so the specks need a spatial answer as well.
