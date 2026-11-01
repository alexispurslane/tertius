---
id: ter-ch7z
status: open
deps: [ter-htg7]
links: []
created: 2026-11-01T22:08:21Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, bloom, fsr]
---
# Bloom: make the glow's width follow the output size, not the render size

Found by the FSR experiment (2026-11-01). The bloom builds a fixed number of levels, each half the render size, so its glow's width is a fixed count of render pixels. Under FSR at 0.77 the glow round very bright lights is 1/0.77 = 1.3 times wider on the screen than native (1.15 at 0.87). Native rendering does the same between window sizes. If the level count (or the blur radius) followed the output size, the glow would match native at every render scale and window size. Evidence: the terminal still in /tmp/fsr1/final77 (danube-terminal), exposure 0.034 stops lower at 0.77, so exposure does not explain it.

