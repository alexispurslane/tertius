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

**2026-11-14T14:55:57Z**

2026-11-14: design written, docs/plans/sub-pixel-strips.md (two designs, R: receivers average over a longer history; H: hits read a footprint mean). Mechanism: the hit light, the guide light and the light-age record all read this draw's direct light at the jittered sample point; AMD's 18 fixed Halton phases repeat forever, so a sub-pixel sunlit strip arrives as k/N of its light, a fixed bias; at ticks 300 to 306 k moves from 3 to 1 of 18 on the far sill, which steps r11 by 33 to 41 %. Young light-age records in a still: 0.136 % of pixels with jitter, 0.000 % without. The shared part (a long Halton sequence; the record counts a change only over an area) can merge first and unblocks Design X. Questions to the user pending.
