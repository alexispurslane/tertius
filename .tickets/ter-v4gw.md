---
id: ter-v4gw
status: open
deps: []
links: []
created: 2026-10-31T21:27:38Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [tools, performance]
---
# gpu-trace: show each pass's time outside the other passes' intervals

Found by ter-0y0z (2026-10-31). tools/gpu-trace.py reports a pass's busy time as the union of its own encoders' intervals. An encoder that runs beside another pass counts in both: the exposure pass's histogram clear, a separate compute encoder under MoltenVK, starts beside the haze pass and stays open until the haze pass's fragments end, so it counted 0.10 to 0.12 ms in the exposure pass that the frame does not pay. Add a column with each pass's exclusive time (its busy time outside every other pass's intervals) and the frame's wall time between named passes, so that an overlap shows directly. Document the columns in tools/README.md.

