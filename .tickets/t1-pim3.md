---
id: t1-pim3
status: closed
deps: []
links: []
created: 2026-11-15T01:24:22Z
type: chore
priority: 4
assignee: Alexis Purslane
tags: [rendering, docs]
---
# The comment of thickness.glsl names the environment map's near entry as a reader of ThicknessShare, and no code reads it for that

**2026-11-25 · Retired.** The back-face depth and screen-ray study retains the probes. A future geometry or ray-hit change needs a production shot whose incorrect hit can be traced.

Found in t1-n8x9 on 2026-11-14 (docs/research/back-face-depth-layer/stage-1b/probe.md, 'The readers of ThicknessShare that the probe leaves'). A search of main ab54c785e finds four readers of ThicknessShare: the walk, TargetDepthShare in screen_trace.glsl, the plane tolerance in screen_trace_plan.comp, and the C# copies. The comment at the top of src/Tertius.Rendering/Shaders/thickness.glsl still says that the environment map's near entry reads it. Correct the comment, or remove it with the constant when the back-face depth layer lands.

## Acceptance Criteria

The comment names only the readers that exist.

