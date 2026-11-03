---
id: ter-89xq
status: in_progress
deps: []
links: []
created: 2026-11-03T01:57:54Z
type: epic
priority: 1
assignee: Alexis Purslane
tags: [rendering, temporal]
---
# Temporal renderer: temporal lighting, FSR 3.1 and dynamic resolution

The user's decision of 2026-11-02: the renderer moves from spatial reuse to temporal reuse. Decision 8 of docs/plans/rendering-performance.md (no temporal accumulation, no TAA, no temporal upscaling) no longer holds. FSR 3.1 is the one TAA and upscaler, with dynamic resolution from the start. No sunk cost: rewrite what a rewrite improves, but a rewrite is not a goal in itself.

Settled in chat on 2026-11-02:
- FSR 3.1 does not replace the histories of the noisy signals. Bounce light and reflections at 1 sample per pixel each keep a history, through one shared accumulator module with a parameter set per signal (where the history comes from: the surface's motion or the reflected point's motion; the history length). One disocclusion test per frame serves both. The combination stays in the lighting composite. Direct light, shadow maps and panel lights (LTC) need no history.
- Reflections: design A is stochastic screen-space reflections with the temporal denoiser, and environment maps for misses. Design B adds a world ray trace after the screen trace (Vulkan ray query on Linux, Metal through VK_EXT_metal_objects on the Mac), and the same trace later serves bounce light. The spike decides: if the trace is fast enough on the M1 Max, the renderer goes all in on B, and if not, all in on A (user, 2026-11-02).

The design doc, docs/plans/temporal-renderer.md, follows the spike.

