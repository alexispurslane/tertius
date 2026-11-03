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


## Notes

**2026-11-03T03:45:25Z**

2026-11-02: the spike ter-afyv is closed. By the user's rule, the renderer goes all in on design A: stochastic screen-space reflections and screen-space bounce light, each with a temporal history, environment maps for what the screen does not show, and no world ray trace (report: docs/research/metal-trace-spike.md). Before the design doc, three researchers (Codex GPT-6.1 SOL) prepare the design forks: the FSR 3.1 integration path, temporal screen-space reflections and bounce light, and a map of today's passes for the temporal design.

**2026-11-03T12:22:22Z**

The user's choices of 2026-11-03, after three research reports (docs/research/temporal-renderer/: fsr31-integration.md, signals.md, pass-map.md):

- Bounce light: the shared screen tracer (design 2 in the chat). Diffuse rays and reflection rays go through one trace of the depth pyramid. The four occlusion passes (the visibility bitmask) are deleted. A miss reads the environment: its irradiance for a diffuse ray, its prefiltered map for a reflection ray. Above the reflection rays' roughness limit, rough reflections reuse the diffuse rays' hits. The orchestrator had recommended the temporal bitmask (design 1). The user chose design 2.
- Frame generation (FSR 3.1 interpolation): not in this epic. It gets a ticket of its own.

Settled by the orchestrator from the research, because it left one clear answer:
- FSR 3.1 path: the engine's own C# host code over AMD's GLSL shaders from FidelityFX SDK v1.1.4 (upscaler 3.1.4), compiled with glslang. The host repeats AMD's calculations exactly. Compare the 3.1.5 and 3.1.6 fixes before pinning the revision. Keep AMD's MIT notices.
- Portable baseline: 32-bit math and the variant without subgroup operations. Faster variants only after a comparison.
- Known defect: AMD's GLSL declares rw_luma_history as rgba8, while AMD's host allocates it as RGBA16F. The first FSR ticket resolves it and runs under validation on MoltenVK and lavapipe.
- Dynamic resolution: images at the largest render size, with the active part changing each frame. A camera cut resets the histories, and a size change does not.
- Motion vectors: no jitter in the vectors, a stable key per object, the previous scale added, last frame's deformed vertices kept, and the previous camera expressed relative to this frame's camera.
- Frame order: bloom and tone mapping move after FSR, at the output size.
- The accumulator: one module, slow and fast histories per signal, the surface test once per frame, and the reflection's own checks of the hit distance and the reflected point.
