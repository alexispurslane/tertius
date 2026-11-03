---
id: ter-ohqr
status: open
deps: []
links: []
created: 2026-11-03T14:53:48Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal]
---
# Motion vectors, the frame history and the surface test

Build ticket 2 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, section 3, section 8 ("The surface test") and decisions 14 and 19 to 24. Acceptance: section 14, item 2. Nothing on screen changes. Its result is the measured time of the G-buffer and the surface test.


## Notes

**2026-11-03T16:17:30Z**

Builder, 2026-11-03: built and measured.

What was built:
- The snapshot: `RenderInstance.EntityGeneration` and `PreviousScale`, and `RenderSnapshot.CameraCuts` (a count of builds that did not interpolate the camera). The builder keeps the last scale per entity in a `ScaleMemory` column. `RenderLight` gets no generation yet: ReSTIR's ticket adds it.
- `FrameHistory` (src/Tertius.Rendering/Frame/FrameHistory.cs): `Begin` is the one choke point of the resets (first frame, a new cut count, a new output size), the draw index and the last frame's camera relative to this eye. `Drew` commits after the submission, beside `EnvironmentMap.Drew`. Contracts on `Drew`, and `AssertInvariants`.
- The placement history: `SceneBatcher.Prepare` joins the snapshot's keys (slot, generation, part) with the last frame's in one sorted merge (`MotionJoin`) and writes one `GpuMotion` per placed instance (Moved, Still, New). The batcher now interpolates the scale.
- The kept deformed positions (`KeptPositions`): two device buffers by parity, written by `deform.comp` (deform set binding 5), read by `mesh.vert` (frame set binding 18).
- The motion target, RGBA16F, the G-buffer's seventh attachment, written by `mesh.vert` and `gbuffer.frag` under `ShaderConstants.WritesMotion`, and by the impostor billboards. A device below 8 color attachments gets a `NotSupportedException`.
- The surface test pass (`SurfaceTestPass`, `surface_test.comp`, `signals.glsl`'s `surfaceTaps`) and `SurfaceHistory`: two surface histories, the R8_UINT surface test image, the R32F device depth, and the sky's motion, in one dispatch after the G-buffer.
- Read hooks for the tests: `ReadMotion`, `ReadSurfaceTest`, `ReadDeviceDepth`, `ReadSurfaceHistory`.

Choices inside the design:
- The motion rows are indexed by placed index, not like the instance array, because the billboards, faces and shadow views append instances after the main view's. A billboard reads its object's row.
- `GpuMotion` adds `FirstVertex` in its padding, so a vertex finds its place in its kept copy.
- The frame set's vertex stage now sees 10 storage buffers (was 8): the motion rows and the kept positions.

Measured (Release, M1 Max, demo-exit's deck window, `--start 399`, 120 frames; Metal traces without counters, 1-minute loads 13.5 to 31 at the starts, because other agents built at the same time):

| Pass | 1280 × 720 base | 1280 × 720 branch | 1920 × 1080 base | 1920 × 1080 branch | Budget at 1280 × 720 |
|---|---:|---:|---:|---:|---:|
| G-buffer, busy mean, ms | 0.284, 0.333 | 0.341 | 0.398 | 0.425 | 0.22 |
| Surface test, busy mean, ms | | 0.187 | | 0.325 | 0.10 |

The renderer's timer, 7 interleaved rounds of base, a byte copy of base as the control, and the branch, on the deck and floor windows (loads 4.2 to 20.6 at 1280 × 720, 6.5 to 14.0 at 1920 × 1080): the surface test's median 0.173 and 0.198 ms at 1280 × 720, 0.304 and 0.362 ms at 1920 × 1080. The G-buffer's median paired change from base: +0.014 and +0.009 ms at 1280 × 720, +0.018 and −0.008 ms at 1920 × 1080, against the control's +0.036, +0.005, +0.008 and −0.003 ms. So the motion target's cost is within the noise, and the surface test takes about twice its budget of 0.10 ms. The base G-buffer already takes more than the budget's 0.22 ms.
