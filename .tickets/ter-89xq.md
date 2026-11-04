---
id: ter-89xq
status: in_progress
deps: []
links: [ter-442t]
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

**2026-11-03T12:30:05Z**

The user's choices of 2026-11-03 on lightening dynamic light (beyond the render size and FSR). In the design now:
- Stochastic shadow taps: each frame takes a few of the shadow-map taps in a pattern that rotates per frame, and FSR averages them.
- Rays where history is short: the screen tracer spends rays on pixels whose history is new or changed, and fewer on converged pixels.
- Many-light sampling (ReSTIR DI): each pixel samples a few lights, weighted by their light, and reuses the picks of its neighbors and of last frame. Direct light becomes a temporal signal with a history of its own, through the same accumulator module.
Later, as tickets of their own: time-sliced environment maps and far shadow cascades, and a world-space radiance cache that keeps the light of surfaces that the camera saw.

The user asked why diffuse rays replace the occlusion passes. The answer recorded in chat: decision 41 of docs/plans/screen-space-illumination.md already says that a blocked direction brings the blocker's light and an open one the environment's light. The rays find the blocked directions at any distance on screen, and their average over frames gives the bounce and the crease shadowing at once. The far rays that the user removed on 2026-10-26 for cost and speckle come back: the speckle came from the old ban on accumulation, and the tracer's first ticket measures the cost.

**2026-11-03T12:35:35Z**

On 2026-11-03 the user cleared the open tickets of the pre-temporal renderer, to start fresh. Closed as superseded, with their work not merged: the epics ter-sglu, ter-pq5y and ter-b2da, and ter-0saa, ter-16vv, ter-3g1g, ter-5aak, ter-8hk9, ter-bsxh, ter-bytc, ter-ch7z, ter-cklj, ter-coy0, ter-efa0, ter-enb1, ter-g6af, ter-htg7, ter-ikm1, ter-ku9c, ter-nw4a, ter-olhk, ter-pn1w, ter-prio, ter-s6zr, ter-u102, ter-ww0h. Four closing notes name an idea that this design can reuse: the corrected environment read (ter-s6zr), the rule against counting a glowing lamp twice (ter-coy0), bloom at the output size (ter-ch7z) and the size split of experiment-fsr1 (ter-htg7). Kept open because they stay valid: the shadow, impostor, star, panel light, environment map bug, Linux, tool and snapshot test tickets. ter-g25a lost its closed parent, and ter-1r63 now reads "on top of FSR 3.1". The design ticket ter-vges started before this cleanup, so its list of open tickets (section 13) can name tickets that are now closed.

**2026-11-03T13:31:51Z**

2026-11-03: the user chose many bounces (option B of ter-vges's open question) for the diffuse rays' hits.

**2026-11-03T14:54:04Z**

2026-11-03: the design (ter-vges) merged as docs/plans/temporal-renderer.md. Build tickets: ter-k63u (1, FSR source and host), ter-ohqr (2, motion vectors and frame history), ter-nmbm (3, direct signal and ReSTIR DI), ter-gayt (4, screen tracer), ter-s7c6 (5, FSR in the frame with dynamic resolution), ter-odli (6, budget and defaults), ter-cgif (7, Linux run), ter-7tnv (8, corrected map read), ter-toaw (9, trace timing spike). Ticket 1's lavapipe run moved to ter-cgif, because the user decided on 2026-10-25 to skip Linux runs for now. ter-hdbw waits for ter-s7c6, ter-13rm for ter-gayt and ter-7khp for ter-odli.

**2026-11-03T15:24:49Z**

2026-11-03: the user chose that the exact lights of ReSTIR DI are the cascade-set holders (at most CascadedLights), not every far light.

**2026-11-03T16:31:49Z**

2026-11-03: the user asked for ter-7m38 at high priority: FSR 3.1's 16-bit build, kept only if it is faster on the M1 Max. It starts when ter-k63u merges, and ter-s7c6 waits for it.

**2026-11-04T12:20:46Z**

2026-11-04, the user: aim for 12 ms frames, not 15 (the budget decision of 2026-10-25 in docs/plans/rendering-performance.md, 12 ms with 4.7 ms of headroom for heavier scenes, still holds). The orchestrator's reading, which the user can correct: section 11's budget is 12.0 ms of GPU time per frame on demo-exit at the windowed clock (1296 MHz), 1920 x 1080 output from 1280 x 720 rendering. The controller's limit of 15.0 ms (60 Hz times 0.9) stays as the runtime limit for heavier scenes. By the trace budget facts (ter-gayt's note of the same day), the frame without the tracer is 9.34 to 10.02 ms, so at 12.0 ms the two trace dispatches get 1.36 to 2.05 ms, against V6's 2.92 to 3.16 ms at 10 % own rays. Against section 11's allowances, the two large overruns are the trace dispatches (3.16 against 1.1) and the direct signal (light candidates with direct light 3.06 against 1.3, accumulate direct 0.76 against 0.30, upper ends at the windowed clock). Section 11 changes with the budget decision after ter-sftw.

**2026-11-04T12:32:02Z**

2026-11-04, the user approved the rule 'exact lights when they are few, sampled lights above some count' for the direct light cost ticket (ter-9nrc, filed on the direct light branch), and left its design to the orchestrator. The orchestrator settled: one model, each light of the main view is exact or sampled, and the light table decides once per frame on the CPU. A light is exact when it holds a cascade set (as today) or when the main view keeps at most N lights. A frame with no sampled light runs no light candidates pass and no reservoir reuse, and the direct pass shades every light exactly, as EveryLight does today. N comes from measurement: the light count at which the two costs cross, from a sweep of n lamps plus the deck and lamp-hall-64. EveryLight becomes the same rule with no limit. The ticket also handles the switch between the two cases when the count crosses N: an exact frame's sample is already the converged light, so its history can count as full length, and the ticket measures whether the switch needs a margin against flicker. Left out until a scene needs it: a per-tile or per-pixel count, because the renderer has one light list per frame and no tile light lists, and building them adds a second culling structure. Expected gain on the deck, from the old measurements: the exact loop took 3.35 to 3.63 ms with the old ambient pass, against 6.34 to 6.49 ms for the four sampled passes (headless, 1280 x 720), so about 1.3 to 2.0 ms at the windowed clock. The sparkle bug (ter-x31x) stays, because sampled scenes keep it; its measurements force ResampledLights with no exact limit. When the direct light branch merges, ter-9nrc gets this note and priority 1.
