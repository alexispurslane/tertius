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

