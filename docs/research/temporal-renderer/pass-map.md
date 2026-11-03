# Research: a map of today's frame for the temporal renderer (ter-89xq)

A code reader (Codex GPT-6.1 SOL, reasoning effort high) wrote this report on 2026-11-02 against main at `707a15eab`. The orchestrator kept its content and changed only its punctuation to the project's writing rules. Each disposition is the reader's assessment, not a settled pass design. No file changed during the reading.

## 1 and 2. Pass order, resources, timings and disposition

`src/Tertius.Rendering/Frame/FrameGraph.cs:272` defines the stage order, and `FrameGraph.cs:366` keeps the insertion order within each stage. `ARCHITECTURE.md:861` describes the resource flow.

"Full" means the current window-sized render image. "Half" means half its width and height. "Fixed" images do not follow the window size. HDR means scene light before the conversion to display colors.

The timings all come from 2026-11-02, on the Apple M1 Max at 1920 x 1080:

- D: demo-exit, deck camera, 120 frames. `captures/nw4a/timing-cost/deck-r7-cmain2/time-deck/gpu-timings.json:1`. `captures/nw4a/README.txt:55` names the control build.
- S: the main baseline of the Metal trace spike, demo-exit antispinward camera, 120 frames. `docs/research/metal-trace-spike.md:234`. Its raw captures are gone.
- T: the newest kept main trace, pbr-spheres low camera. `captures/nw4a/profile/pbrlow-cmain/passes.md:1`. It records actual GPU work, but it started under heavy machine load.

The table gives the median in milliseconds from S where S has the pass, otherwise from D. These are GPU timestamps, not isolated costs. On MoltenVK, a timestamp can charge the next pass's work to the pass before it. Use traces to attribute a pass and timestamps for the whole frame (`tools/README.md:427`). D's frame span is 15.173 ms and S's is 15.37 ms.

| Pass, in execution order | Reads, then writes | Image size | ms | Disposition for the temporal design |
|---|---|---|---:|---|
| Deformation | Source vertices, morph deltas, posed joints and weights, then deformed vertices | Buffers | 0.023 D | Keep its purpose, but extend it for the previous deformed positions. |
| Shadow maps | Geometry, instances, cutout materials, then cascade depth | Fixed | 0.269 D | Keep. Direct shadows need no history. |
| Local shadows | Geometry and lights, then cached depth tiles | Fixed | 0.640 D | Keep. Its tile cache is not pixel accumulation. |
| Environment faces | Geometry, lights and shadows, then radiance and distance cube faces | Fixed | 0.35 S | Keep as the fallback for what is off screen. |
| Environment prefilter | Cube faces, then radiance and distance levels, the specular map and the irradiance cube | Fixed | 0.62 S | Keep its purpose. Both indirect signals still need light from off screen. |
| Impostor captures | Geometry, materials and light, then color and depth-offset tiles | Fixed, 512² tiles | none found | Unclear. Billboard motion and recapture jumps need a rule. |
| G-buffer | Geometry, instances and materials, then base color, normals, specular, emission, distance, shading and depth | Full | 0.417 D | Keep, follow the render size, and add a motion vector output. |
| Depth pyramid | Distance and geometric normal, then hierarchical distance bounds | Derived from render size | 1.189 D | Unclear. Stochastic rays still need a depth traversal, but not necessarily this builder. |
| Direct light | G-buffer, lights and shadows, then the direct image and an optional diffuse image | Full | 4.209 D | Keep, follow the render size. Direct light stays current-frame work. |
| Occlusion depth | G-buffer and the light source image, then view depth, copies and continuation flags | Half | 0.30 S | Replaced by the preparation of temporal bounce light. |
| Occlusion sectors | Copies, normals, shading and environment, then a packed light estimate and edges | Half | 1.60 S | Replaced by the temporal bounce estimate. |
| Occlusion denoise | Estimate and edges, then alternating estimate images | Half | 0.85 S | Replaced. Its six spatial blurs are today's answer to noise. |
| Occlusion upsample | Half estimate and depth, full distance and normals, then the full estimate | Half to full | 0.49 S | Replaced, or folded into the temporal bounce resolve. |
| Ambient light | Direct image, G-buffer, environment and estimate, then HDR and skin diffuse | Full | 0.429 D | Unclear. A composite stays, but its inputs and duties change. |
| Scatter rows | Skin diffuse, distance and kernel, then a row image | Full | 0.110 D | Keep, follow the render size. Spatial skin scattering does not conflict with temporal light. |
| Scatter columns | Row image, diffuse, distance and kernel, then the HDR correction | Full | 0.048 D | Keep, follow the render size. |
| Radiance pyramid | Lit HDR and G-buffer, then covered radiance levels | Full plus levels | 0.57 S | Replaced. Its filtered cone reads belong to the old reflection method. |
| Reflection trace | Depth and radiance levels, G-buffer, estimate and environment, then the half trace and hit depths | Half | 0.14 S | Replaced by stochastic reflection rays. |
| Reflections | Trace, hit depths, G-buffer, pyramids and environment, then HDR with extra edge rays | Full | 0.45 S | Replaced by temporal reflection filtering and composition. |
| Haze | Distance and atmosphere, then blended HDR | Full | 0.390 D | Keep, follow the render size. |
| Transparent | Transparent geometry, distance, lights and shadows, then blended HDR | Full | 1.256 D | Keep, follow the render size. Its motion and its FSR mask inputs are undecided. |
| Exposure | HDR, distance and the previous exposure, then a histogram and the next exposure | Screen reads, fixed buffers | 0.190 D | Keep, follow the render size for metering. Exposure already has temporal state. |
| Bloom | HDR, then downsampled and rebuilt glow levels | Derived from render size | 0.310 D | Unclear. Its place around FSR decides its size and its glow width. |
| Tone map | HDR and bloom, then the display-color image | Full today | 0.162 D | Unclear. It stays, but its place changes with FSR. |
| SMAA edges | Display colors, then edges | Full | 0.026 D | Replaced by FSR 3.1. |
| SMAA weights | Edges and fixed tables, then blend weights | Full | 0.060 D | Replaced by FSR 3.1. |
| SMAA blending | Display colors and weights, then the output | Full | 0.069 D | Replaced by FSR 3.1. |
| Outlines | Frame outlines, distance and the output, then the output | Output | 0.016 D | Unclear. It stays after the upscale, but it maps render-depth coordinates. |
| Capture | Output, then a buffer that the CPU reads | Output | 0.019 D | Keep. It captures the final output. |

A pass that is off does not appear. With full-resolution reflections, the separate half-resolution trace disappears. Bloom's draws and occlusion's six blurs each count as one graph pass.

Allocation evidence: `src/Tertius.Rendering/Frame/RenderTargets.cs:248`, `src/Tertius.Rendering/Frame/OcclusionImages.cs:123`, `src/Tertius.Rendering/Frame/ReflectionTargets.cs:160` and `src/Tertius.Rendering/Frame/ScatterImages.cs:131`.

## 3. What motion vectors need

A motion vector gives the place where the same visible surface appeared in the previous rendered image.

`GpuInstance` holds only this frame's model rows, relative to the camera (`src/Tertius.Rendering/Scene/GpuTypes.cs:43`). `SceneBatcher.AppendInstanceCommand` builds them at `src/Tertius.Rendering/Scene/SceneBatcher.cs:853`, and `FrameResources.Write` uploads them at `src/Tertius.Rendering/Frame/FrameResources.cs:132`.

The placement today, at `src/Tertius.Rendering/Scene/SceneBatcher.cs:445`:

```csharp
        Vector3 position = Vector3.Lerp(instance.PreviousPosition, instance.Position, alpha) - eye;
```

where

- `position` = the object's position in meters, relative to the interpolated camera.
- `instance` = the snapshot's object, with this snapshot's and the previous snapshot's positions.
- `alpha` = the interpolation fraction, from 0 to 1.
- `eye` = the interpolated camera's displacement from this snapshot's render origin.

So previous transforms exist, but they describe snapshots, not necessarily the previous rendered frame. `RenderInstance` has a previous position and rotation, but no previous scale (`src/Tertius.Core/Seams/RenderSeam.cs:99`). The entity index and the part identify an object across ticks. The order of GPU instances is not a lasting identity.

Previous joint poses and morph weights also exist (`src/Tertius.Core/Seams/RenderSeam.cs:302`). The batcher interpolates them into one current pose at `SceneBatcher.cs:343`. The deform shader applies morphs, then skinning, and writes each vertex into this frame's copy (`src/Tertius.Rendering/Shaders/deform.comp:119`). The copies live in a shared region that every frame rewrites, and no previous vertices are kept (`src/Tertius.Rendering/Scene/PreparedScene.cs:605`, `src/Tertius.Rendering/Resources/GeometryStore.cs:241`).

The frame tree composes double-precision poses relative to the camera's nearby physics home. Each snapshot then subtracts the current camera position before it converts to floats (`src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:212`). `OriginInFrame` keeps the camera's position in doubles, in its reference frame (`src/Tertius.Core/Seams/RenderSeam.cs:219`).

Needed additions:

- the previous rendered frame's camera, object and deformation state
- a stable correspondence between frames
- the previous projection with its jitter
- history invalidation for camera cuts, changes of reference frame, creation and incompatible geometry changes. A difference of two frames' camera-relative rows alone cannot tell object motion from a moving origin.

## 4. The branch experiment-fsr1 and ticket ter-htg7

Against main `707a15eab`, the experiment is 5 commits ahead and 252 behind. Its head is `3be9aef99` of 2026-11-01, and its merge base is `06050163e`. The three-dot diff covers 52 files, with 5,611 insertions and 163 deletions, including AMD's headers.

The branch already separates:

- the render-sized geometry, light, effects, tone mapping and SMAA
- the output-sized FSR upscale and sharpen images, outlines and capture
- the output's aspect ratio from the render size, the material mip bias, and the conversion of outline depth coordinates

References on the branch: `src/Tertius.Rendering/UpscalingSetting.cs:65`, `src/Tertius.Rendering/Frame/RenderTargets.cs:224`, `src/Tertius.Rendering/Frame/FrameGraph.cs:42` and `src/Tertius.Rendering/Passes/OutlinePass.cs:196`.

It is a static spatial upscaling experiment. It has no temporal histories, no motion vectors, no jitter and no controller of dynamic resolution. A resize destroys and rebuilds the images and needs an idle device.

`.tickets/ter-htg7.md:3` is open and blocked by ter-b2da. Its merge procedure has these steps (`.tickets/ter-htg7.md:21`): bring in current main, reconcile the renderer, resource and layout changes, and update ARCHITECTURE.md. Then rerun the Checked and Release tests, review captures, and do the skipped coverage and mutation work. A later note approves the demo's scale 0.77 and a sharpness of one stop (`.tickets/ter-htg7.md:30`).

The reusable part is the size split. Merging its FSR 1 and SMAA pipeline as it is does not carry out the FSR 3.1 decision.

## 5. Snapshots and headless warm-up

`Gpu.RenderAndCapture` asks for a capture and renders once (`tests/Tertius.Rendering.Tests/TestSupport.cs:94`). The default test options turn off bloom, SMAA, screen-space light, reflections and environment maps unless a test asks for them (line 54).

Reference images are compared with FLIP, a perceptual difference measure, with a default tolerance of 0.1. Approval uses `TERTIUS_APPROVE_SNAPSHOTS=1`. A failure writes the actual image and a heat map (`tests/Tertius.Rendering.Tests/ReferenceImages.cs:22` and line 67). Seven approved reference PNGs exist.

The planned switch for deterministic rendering is not in `RendererOptions`. The plan still describes it at `docs/plans/renderer-snapshot-tests.md:16`.

Headless runs use:

```bash
nix develop --command dotnet run --project src/Tertius.Demo -- --script scripts/reflections.json --headless --out <folder>
```

A script's `run` steps advance the simulation without rendering. Films alternate simulation ticks and captures (`src/Tertius.Host/Scripting/ScriptRunner.cs:45`). Each headless capture publishes a snapshot and renders at interpolation fraction 1 (`src/Tertius.Host/Scripting/Playback.cs:85`).

Automatic exposure can add an extra full draw that is not captured, before a capture, also at an unchanged simulation time (`src/Tertius.Rendering/VulkanRenderer.cs:458`).

A temporal warm-up must define these:

- the count of rendered frames and the simulation's progress
- the jitter and noise sequence, and the resets
- the exposure's behavior
- which frames enter the timing summaries.
 Simulation-only `run` steps cannot warm renderer histories. Trace trimming also replaces earlier captures with simulation-only steps (`tools/README.md:412`).

## 6. What states the old decision

These places state that nothing accumulates over frames, or build on that rule. Keep their measured history, but mark the decision as superseded.

| Document | Lines |
|---|---|
| `docs/plans/rendering-performance.md` | 34, 50, 99, and the exclusion of dynamic resolution at 100 |
| `docs/plans/rendering-upgrade.md` | 47 |
| `docs/plans/renderer-snapshot-tests.md` | 16, 19 |
| `docs/plans/ambient-occlusion.md` | 53, 72, 211, 350, 568, 578 |
| `docs/plans/screen-space-illumination.md` | 26, 76, 87, 137, 145, 1004, 1052, 1060, 1061, 1374, 1632 |
| `docs/plans/screen-space-reflections.md` | 54, 62, 95, 465, 1445, 1447 |
| `docs/plans/screen-space-artifacts.md` | 16 |
| `docs/plans/environment-map-distances.md` | 22, 83, 734 |
| `docs/plans/environment-maps-per-frame.md` | 507, 513 |
| `docs/plans/skinning-and-morphs.md` | 784, 791 |
| `docs/plans/skin-and-hair.md` | 75, 101, 111, 721, 792, 1307 |
| `ARCHITECTURE.md` | 2117, 2305, and the fixed-noise description at 2116 |

Tickets:

- The original effects: `.tickets/ter-b3h5.md` lines 21 and 36, `.tickets/ter-jxv9.md:21`, `.tickets/ter-a24f.md:32`, `.tickets/ter-zi4i.md:15`.
- Performance limits: `.tickets/ter-izsu.md:30`, `.tickets/ter-ff6y.md:26`, `.tickets/ter-of1p.md:30`, `.tickets/ter-3g1g.md:26`.
- Reflection alternatives: `.tickets/ter-y8z0.md` lines 121 and 127, `.tickets/ter-f3g2.md` lines 15, 30, 37, 42 and 125, `.tickets/ter-rnjk.md:13`, `.tickets/ter-nw4a.md:15`, `.tickets/ter-jw7g.md:15`.
- Other limits and history: `.tickets/ter-k96l.md:38`, `.tickets/ter-rjts.md:136`, `.tickets/ter-1r63.md:13`, `.tickets/ter-7gtc.md:14`, `.tickets/ter-p349.md:15`.
- The superseding decision: `.tickets/ter-89xq.md` lines 14 and 27.

Code assumptions: `src/Tertius.Rendering/Shaders/skin_scatter_rows.comp:4`, `src/Tertius.Rendering/Shaders/gtao.glsl:141` and `src/Tertius.Rendering/Shading/OcclusionNoise.cs:5`. If that pass stays spatial, the skin scatter comment stays right.

Tests that assert no history: `tests/Tertius.Rendering.Tests/AmbientOcclusionRenderTests.cs:374`, `tests/Tertius.Rendering.Tests/ReflectionRenderTests.cs:694` and `tests/Tertius.Rendering.Tests/ReflectionShaderTests.cs:416`.

Branch-only wording: `src/Tertius.Rendering/UpscalingSetting.cs:10` on experiment-fsr1. Other hits of "decision 8" concern unrelated decisions, so no mechanical replacement is safe.
