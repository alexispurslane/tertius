---
id: ter-jr0f
status: open
deps: []
links: []
created: 2026-11-12T07:03:49Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# Local shadow tiles of a frame in one render pass: up to 0.25 ms (spike first)

Sweep addendum (/private/tmp/tertius-sweep-20261111/addendum.md) A6 proposal 3. The pass draws each tile of the shadow atlas in its own render pass (13 on the deck), so 13 encoders. One render pass with a viewport and scissor per tile makes one. Open question: a tile-based GPU loads and stores the whole render area, and undrawn tiles must keep their contents. A spike measures both forms on the deck first. Bound about 0.25 ms (0.30 ms idle on stock).

