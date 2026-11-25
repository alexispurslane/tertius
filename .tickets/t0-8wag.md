---
id: t0-8wag
status: closed
deps: []
links: []
created: 2026-11-14T14:53:59Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# A block's chosen ray pixel starts from only half of the block's width: its column follows the draw's parity, and so does the jitter's x half

**2026-11-25 · Retired.** Preserve the temporal lighting measurements. Choose any change to sampling, weighting or history only after isolating the source of the pattern on the new deck (ter-xzj7).

Found by the t0-qwx8 design, 2026-11-14: the block's chosen pixel turns with the draw index modulo 4; its column follows the draw's parity, and so does the jitter's x half, so each block starts its rays from only half of its width. The same kind of fault as the shadow comb of ter-yfb5. See docs/plans/sub-pixel-strips.md, 'Found on the way'.

