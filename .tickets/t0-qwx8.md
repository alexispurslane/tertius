---
id: t0-qwx8
status: open
deps: []
links: []
created: 2026-11-12T13:56:24Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# The light at a hit must not depend on whether a jittered sample lands on a sub-pixel strip

From ter-g3tk cause 1 and docs/plans/bounce-light-confidence.md, step 8: at world tick 300-306 the cabin's single-bounce region r11 falls 43 % while direct sunlight on sub-pixel sill strips rises 2 %. Design A follows such steps by evidence but does not remove the fault. Needs its own design.


## Notes

**2026-11-13T03:46:10Z**

2026-11-13 (t0-3kyb): the sun strips' jitter fault also keeps their light-age records young in a still cabin, at about 0.13 % of pixels. Under Design X those young codes become light evidence for whole 8x8 blocks in a still: extra cuts, own rays and noise there. Take T12's evidence census must count them; if they matter, this ticket blocks the Design X merge.
