---
id: ter-qt72
status: in_progress
deps: []
links: [ter-89xq]
created: 2026-11-22T16:23:08Z
type: bug
priority: 0
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, dynamic-resolution]
---
# The whole picture wobbles horizontally when the dynamic render scale changes the render size: the projection takes its aspect from the rounded render size

Found by the user on 2026-11-22 in a side-by-side video of the lamp hall under the dynamic render scale: the whole picture wobbles in and out horizontally whenever the render size changes, on main and on Design X alike. The user's order: "we should fix the wobble immediately". The orchestrator settled a first design on 2026-11-22 and corrected it the same day: the bug already had the ticket ter-fkd3 with the user's binding decisions of 2026-11-05. The section "The user's decisions of 2026-11-05" below holds them, and they win wherever the first design differed. This ticket goes ahead of every ticket of the back-face line that has not started.

Video: /Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144/docs/research/design-x-acceptance-2026-11-22/pics/switch-cabin-dynamic-16ms.mp4 (each side shows its render size per draw).

## The cause, as read from main's code. Nobody measured it yet: the red tests do.

The render size rounds each axis on its own (src/Tertius.Rendering/UpscalingSetting.cs):

```csharp
    internal Extent2D RenderExtent(Extent2D output, float scale) =>
        this switch
        {
            NoUpscaling => output,
            Fsr3Upscaling => new(Scaled(output.Width, scale), Scaled(output.Height, scale)),
        };

    private static uint Scaled(uint pixels, float scale) => Math.Max(8, (uint)(pixels * scale) / 8 * 8);
```

where

- `output` = the window's size in output pixels, which the upscaler fills.
- `scale` = the render scale of the draw, the render size over the output size on each axis, which the dynamic render scale changes from draw to draw.
- `Scaled(pixels, scale)` = that axis of the render size: `pixels * scale`, rounded down to a multiple of 8, and at least 8.

So the render width over the render height is not the window's width over its height. For a 16:9 window it moves between about 1.775 and 1.786 (1904 x 1072, 1912 x 1072, 1872 x 1048), and it is exact at the scale 2/3 (1280 x 720).

The renderer passes that render size to the batcher (src/Tertius.Rendering/VulkanRenderer.cs, about line 771):

```csharp
        Batcher.Prepare(
            snapshot: snapshot,
            alpha: alpha,
            ...
            width: (int)pending.RenderSize.Width,
            height: (int)pending.RenderSize.Height,
```

and the batcher takes the camera's aspect from it (src/Tertius.Rendering/Scene/SceneBatcher.cs, `InterpolateCamera`):

```csharp
        float aspect = (float)Math.Max(width, 1) / Math.Max(height, 1);
        float tanY = MathF.Tan(fov * 0.5f);
```

where

- `width`, `height` = the render size of the draw, in render pixels.
- `aspect` = the projection's width over its height. With `tanY` it gives the horizontal field of view.
- `fov` = the camera's vertical field of view, in radians.

So the camera's horizontal field of view follows the rounded render size, and the upscaler stretches the picture back to the window's fixed shape. Prediction: horizontal distances on screen change by up to 0.57 % between those sizes, and nothing moves vertically or at the scale 2/3.

## The class of the bug

A quantity of the picture taken from the resolution that samples it. The picture's shape is a fact of the window. The render size says only how many samples the picture gets. Any code that reads a shape, a field of view or a pixel's footprint from the render size, or assumes square render pixels, is a member of the class.

## The model (settled)

The camera's shape is the window's shape. The picture's aspect ratio is one fact with one authority, the output extent. The render size is a resolution only: how many samples the picture gets, never what the camera sees. So the projection takes its aspect from the output extent, and every count of pixels keeps coming from the render size.

- What changes: the one place that turns a size into the projection's aspect. The lead chooses the signature. One fitting form: `Batcher.Prepare` takes the picture's aspect (output width over output height) beside the render size, so that no caller can build a projection from the render size again.
- Struck on 2026-11-22: the first design said here that render pixels need not be square. The user decided on 2026-11-05 that the active render size has exactly the output's aspect ratio, so render pixels are square after step 2. Between step 1 and step 2 they are off square by up to about 1 %.
- The audit stays. The lead audits every place in C# and GLSL that derives an aspect, a horizontal field of view or a pixel's footprint from the render size, or assumes square render pixels: the jitter's offsets, the motion vectors, FSR 3.1's inputs (its vertical field of view and whatever it derives from the render size), the screen tracer and its plan, the depth and light pyramids, the cascade fitting, the environment maps, and every upscaling setting beside `Fsr3Upscaling`. Each place gets a fix or a one-sentence reason why it is right as it is. The audit's table goes into this ticket's note.

## The user's decisions of 2026-11-05 (binding)

They come from the ticket ter-fkd3, "Keep camera framing fixed across FSR internal-resolution changes" (parent ter-89xq), whose file is not committed: it lies untracked in the main checkout as .tickets/ter-fkd3.md. Its text is copied here in this ticket's words. The first design's section "Rejected, with the reason" is struck: it rejected render sizes of the window's exact shape, because it assumed that the active render size keeps the alignment of 8.

1. The fixed output dimensions define the camera's framing: the field of view and the aspect. The active internal dimensions (the render size that a draw really fills) define only the sample locations and the texture bounds. All data derived from the projection describe the same camera: culling, ray reconstruction, motion and FSR's depth-to-view constants. The real current and previous render sizes and the jitter's normalization stay separate from the camera.
2. The active internal width and height have exactly the final output's aspect ratio at every size. One coupled integer pair chosen from the output's ratio, never independent rounding of the two axes. A padded backing allocation is allowed only if the active dimensions keep the exact ratio. The lead audits whether the tile alignment of 8 belongs to the padded allocation and not to the active dimensions.
3. The window stays reshapeable. Its final output dimensions snap to compatible integer sizes, at the real window and framebuffer boundary, and not at an intermediate image that is then stretched ("Snapping seems natural."). A compatible output is one that has exact-ratio internal pairs within the configured scale limits. An output that is already compatible stays unchanged. No fixed window shape, no letterbox, no crop, no approximate internal ratio. The predicate is about the common factors of the two dimensions.
4. The configured scale limits stay hard limits. For an unusual output, report the reduced set of exact-ratio scales. If a platform's output cannot change, report that limit, and do not claim that a changed request changed the acquired output.
5. If the quantization changes the realized scale, the frame-numbered timing feedback describes the realized drawn dimensions, not the unrounded request.
6. One owner and one predicate decide both whether an output is compatible and which internal size a scale gets.

Out of these decisions' scope: the controller's rate limit and hysteresis (ticket ter-5kz6, which depends on ter-fkd3), a change of vendor source, and a change of the noise or lighting algorithms.

The stalled implementation of ter-fkd3 is a head start, not the truth. It lies uncommitted in the worktree /Users/alexispurslane/.t3/worktrees/tertius/ter-fkd3-fixed-output-camera (branch ter-fkd3-fixed-output-camera, last commit 694a8c2bd, review base a004bdb73): 13 changed files and 2 untracked test files. Its suites passed on 2026-11-05. Its shader validation, review and hardening never ran, and main moved since. Read it there, and do not edit or build there.

The shape that the stalled diff gives the sizing owner, `UpscalingSetting`, for the lead to keep or change:

```csharp
    internal Extent2D RenderExtent(Extent2D output, float scale)
    internal bool SupportsWindow(Extent2D output)
    internal Extent2D SnapOutput(Extent2D output)
```

where

- `output` = the window's size in output pixels.
- `scale` = the render scale that the controller asks for, the render size over the output size on each axis.
- `RenderExtent` = the active render size: `output` divided by the greatest common divisor of its two dimensions, times one whole number of steps, the largest whose scale is at most `scale` and inside the scale limits. For 1920 x 1080 the divisor is 120, so every size is 16 k x 9 k.
- `SupportsWindow` = whether `output` has such a size inside the scale limits, and more than one where the scale moves.
- `SnapOutput` = `output` itself if it is compatible, otherwise the nearest compatible size on each axis.

And the controller's feedback: the renderer reads the controller's requested scale, chooses the size, and tells the controller the realized scale of the frame (`void Drew(long frame, float realizedScale)` in place of `float Next(long frame)`). This changes no policy of the controller.

## The build order (settled by the orchestrator, 2026-11-22)

- Step 1 merges into main as soon as it is green and reviewed: the output-defined camera. The projection and everything derived from it take the output's aspect. This alone removes the stretch. The Design X line waits for exactly this.
- Step 2 follows at once in this ticket: the coupled exact-ratio sizing with the alignment moved to the padded allocation, the realized-scale feedback, and the output snapping with the check in a real window.
- Both land together only if that takes about the same time. Step 2 never delays step 1 by hours.

## Tests, red first

1. The projection's horizontal field of view is the same for every render size at a fixed output extent: a unit test of the batcher, with 1904 x 1072, 1912 x 1072, 1872 x 1048 and 1280 x 720 for one 16:9 output.
2. A world point lands at the same place of the output image, within a small share of an output pixel, when the same view is drawn at those render sizes: a renderer test through the real upscaling path. It fails today by up to about 0.5 % of the distance from the centre.
3. Lock-in: a size whose ratio is not the window's, such as 1872 x 1048, so that a later change cannot pass by luck at 1280 x 720.

Each red test's numbers go to the line orchestrator at once, because they are the first measurement of the cause.

## Acceptance

- No reference image changes, because every reference take runs at an exact ratio. A changed reference image is a finding: report it, and do not approve it silently.
- The acceptance of ter-fkd3, with red tests before the production edits: the snapping of an incompatible output, including a requested 1919 x 1080 with a maximum scale below native, where the real final output is compatible and the active internal pair has exactly its aspect. Exact aspect by integer cross-products for every chosen pair, across scales and real output resizes: 1920 x 1080 with pairs such as 960 x 540, 1280 x 720 and 1536 x 864, awkward and portrait ratios, and ratios with few steps. A fixed camera at a fixed output keeps the same unjittered landmarks of the output image across awkward growing and shrinking sizes, including 976 x 544 to 976 x 552 at 1920 x 1080. Outputs of other shapes, portrait and one-axis changes still change the framing. Already-compatible outputs, the scale limits, bounded handling of resize events, a minimized window and the consistency of the current and previous frame all stay. A real resizable window is checked beside the headless tests. The report names the limits of jitter and motion that remain, and claims no more than the measurements show.
- One side-by-side video of the lamp hall under a played-back moving render scale: main before the fix on the left, the fix on the right, real frames at 60 a second, one fixed exposure, H.264 at CRF 12 or lower. With it, the distance between two tracked vertical edges per frame: after the fix it stays constant within about 0.2 output pixels. The Design X line's acceptance lead has the method (a logged scale played back, tracked edges). Ask the line orchestrator to connect you if you need its scratch patch.
- The game runs under the dynamic render scale. Judge under it, and never hold the render size to help this fix.
- The full Checked suite, then the full Release suite, one configuration at a time. The Linux run of the changed test classes on lavapipe.
- ARCHITECTURE.md says where the projection's aspect comes from.
- One reviewer. Coverage and mutants of the changed lines as CLAUDE.md says: Rendering code goes to tools/mutate.py.

## Out of scope

A change of the render scale controller's policy: its rate limit and hysteresis are ticket ter-5kz6. Step 2 does change the rounding of the render size, by the user's decisions above. Other tickets of the dynamic resolution's epic ter-89xq: keep the diff small, because other sessions work in those files.


## Notes

**2026-11-22T16:25:02Z**

2026-11-22, the line orchestrator. The ticket lead is launched. taskId and child thread: node/thread:delegated-task:command%3Amcp%3A49b05ffc-1378-4e84-92ef-b16c94dc7acb%3Adelegate-task%3Aslabs-4-ter-qt72-lead-1. clientRequestId: slabs-4-ter-qt72-lead-1. Target: claudeAgent, claude-opus-5-5, effort high, role implementation, isolation worktree. Its reviewer: the same target, role review. Base and review base: main 4298ae7ec. Cloud money: $0.30. The research take t1-r9vj starts no build and no render until this fix is in its review. Before the launch I removed the build output (bin and obj folders, 3.2 GiB) of three worktrees of the line whose work is merged or mine: t3-58f87f59-3970e2671e4b1d5d, t3-7e999be0 and t3-1b834d57.

**2026-11-22T16:26:33Z**

2026-11-22, the line orchestrator, from the orchestrator. The Design X line's acceptance lead measured the wobble on existing frames (headless probe, the render size played back). The code reading holds, and a second, smaller effect exists.

The measurement:
- The distance between two window edges about 1,185 output pixels apart follows (render height / render width) x 16/9 draw by draw, with no lag: correlation +0.993 to +0.996, on main and on Design X, at frame time targets of 16.67 ms and 10 ms. Its range is 11.9 to 19.6 pixels. The vertical distance does not move.
- The rounded render aspect runs up to 1.0 % from the exact one, 1.65 % peak to peak at 10 ms. Smaller render sizes round harder. So the 0.57 % in this ticket's text is too small.
- Chart: /Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144/docs/research/design-x-acceptance-2026-11-22/pics/wobble-switch-dynamic.png

What the aspect does not explain:
- A fixed offset per render size of 0.21 to 0.31 output pixels, which correlates +0.41 to +0.50 with the render width.
- The midpoint of the two edges, near the screen's centre, moves sideways by 1.0 to 1.7 output pixels peak to peak. A pure stretch about the centre would not move it.
- A reading that nobody measured: a sideways shift of about half a render pixel per size, such as a pixel-centre convention between the render grid and the output image, or the jitter's offset in the wrong size's units. It is the same class of bug, so it is part of the audit. A small and clear fix goes into this ticket. A separate cause with its own risk gets its own bug ticket, and the aspect's fix does not wait for it.

The acceptance changes: two numbers per frame under a played-back moving render size, the spread of the edge distance and the spread of the edges' midpoint. The target after the fix: both at the edge finder's noise floor (0.04 to 0.07 output pixels), or a named reason for what is left. This replaces the "about 0.2 output pixels" above.

The method's files, uncommitted in another lead's worktree: scratch-dynamic-scale.patch (TERTIUS_X_SIZE_LOG, TERTIUS_X_SIZE_REPLAY, TERTIUS_X_FRAME_TIME_TARGET) and tools/wobble.py, both under /Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144/docs/research/design-x-acceptance-2026-11-22/. The patch never enters src.

Not checked by anyone: the wobble in the windowed game. The report says whether the renderer test through the real upscaling path runs the path that a window runs.

**2026-11-22T16:33:02Z**

2026-11-22, the line orchestrator. The orchestrator corrected the first design: the user's decisions of 2026-11-05 from ticket ter-fkd3 are now in this ticket's text, with the build order in two steps. The first design's section "Rejected, with the reason" and its sentence that render pixels need not be square are struck. The lead got the correction as a steer message and confirmed it. It had built nothing against the first design: no file changed and no commit, only reading (the batcher, FSR's constants, the jitter's projection, the shaders' footprints). Its worktree: /Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a, branch t3/fix-projection-aspect-wobble. The orchestrator asks the user what happens to ter-fkd3 itself. No agent of this line runs a tk command that writes into ter-fkd3, ter-5kz6 or ter-89xq, because their files hold another session's uncommitted work.

**2026-11-22T17:59:31Z**

2026-11-22, the ticket lead. Step 1, the output-defined camera.

The change. `SceneBatcher.Prepare` takes no width and no height any more. It reads both sizes from the draw's `FrameHistory.PendingFrame`, which already held them: the output size gives the projection's aspect, and the render size gives the camera's pixel counts. So no caller can build a projection from the render size again, and the pending frame stays the one store of the draw's two sizes. `PendingFrame.OutputSize` is new. `Fsr3Frame.Aspect` is new and required: `UpscalePass` passes `CameraView.Aspect`, and `Fsr3Constants` takes `tanHalfFOV` and `deviceToViewDepth2` from it in place of the render size's ratio. The golden test of AMD's host passes the render size's ratio, because AMD's host draws at that aspect. The brief's suggested signature (an aspect beside the render size) would have left a second store of the output size and a caller that could pass a mismatched pair.

Red tests on 4298ae7ec (before the fix), with a 1920 x 1080 output and a vertical field of view of 60 degrees (the output's horizontal field of view is 91.4928 degrees):

| Render size | Horizontal fov (deg) | World point (3, 1, -5) moves, output px |
|---|---:|---:|
| 1904 x 1072 | 91.4394 | +0.524, 0.000 |
| 1912 x 1072 | 91.6795 | not run |
| 1872 x 1048 | 91.7654 | -2.665, 0.000 |
| 976 x 544 | not run | -5.111, 0.000 |
| 976 x 552 | not run | +3.067, 0.000 |
| 1280 x 720 | exact (passed) | reference |

FSR's `tanHalfFOV` and `deviceToViewDepth2` also took the render size's ratio: red at 1872 x 1048.

The GPU test through the real FSR path (headless stills at a fixed render scale, 640 x 360 output, 32 warm-up draws), markers at 6 px and 218 px from the centre, against 512 x 288 (the exact ratio). Before the fix, with 0.3 m markers, the far marker moved +1.27 px at 424 x 240, -2.72 px at 576 x 320 and -3.77 px at 608 x 336, against +1.36, -2.7 and -3.8 px that the ratio predicts. After the fix, with 0.8 m markers, every marker moves 0.45 px or less on each axis. The test's limit is 0.6 px.

The sideways shift that the aspect does not explain. A sweep of 12 render scales from 0.5 to 1 (320 x 176 to 640 x 360) measured each marker's centroid against the analytic projection of the output. The near marker's offset scatters from -0.05 to +0.44 px in x and from -0.22 to +0.25 px in y, with no trend in the render size. A convention error of half a render pixel would grow steadily from 0.5 output px at the scale 1 to 1.0 output px at the scale 0.5. The same scale gave the same numbers every time. So these stills show no pixel-centre error. The scatter comes from FSR's reconstruction of a sharp edge from a different grid of samples at each size. The moving video measures the midpoint again.

Does the test run a window's path? It runs the same `RenderExtent`, batcher, projection, jitter, FSR constants and FSR passes as a window. A window differs in the clock (the window clock and the controller's scale against a still with warm-up draws) and in the output (a swapchain image against an offscreen image). The upscale path is the same code.

The audit. Each place that reads a shape, a field of view or a pixel's footprint from the render size, or assumes square render pixels:

| Place | What it reads | Verdict |
|---|---|---|
| `SceneBatcher.InterpolateCamera` | the aspect from the render size | Fixed: the output size's aspect. |
| `CameraView.ViewProjection`, `Culls`, `TanHalfX` | the camera's aspect | Right: they follow the fixed aspect. |
| `Fsr3Constants`: `TanHalfFov`, `DeviceToViewDepth2` | the render size's ratio | Fixed: `Fsr3Frame.Aspect`. |
| `Fsr3Constants`: `RenderSize`, `DownscaleFactor`, `MotionVectorScale`, `JitterPhaseCount` | counts per axis | Right: they count samples, each axis on its own. |
| `VulkanRenderer.ApplyRenderProjection`: jitter shift `-2 j / W`, `-2 j / H`, and the previous jitter's phases from `PreviousRenderSize` | the render size per axis | Right: the jitter is in render pixels of each axis, of the size that drew it. |
| The main view's mip bias, `log2(renderWidth / outputWidth) - 1` | the horizontal ratio only | Right: the vertical ratio differs by 1 % at most, 0.015 of a mip level. |
| `CameraView.PixelAngle` (`2 tanY / H`, `frame.params.z`) and its readers: `gbuffer_surface.glsl`, `reflection_footprint.glsl`, `screen_ray.glsl` (origin offset), `ScreenRay.OriginOffset`, `key_fill_light.glsl`, `key_fill_corners.frag`, `sky.glsl` (stars), `accumulate_diffuse_single_temporal.comp`, `diffuse_spatial_prototype.glsl`, `reflection_sharing_prototype.glsl`, the batcher's occluder `pixelShare` | the vertical pixel angle as one angle for both axes | Right: each is an estimate of a footprint or a threshold, and the horizontal pixel angle differs from it by 1 % at most. |
| `ImpostorManager` projected size, `Height * tan / TanHalfY` | the vertical axis | Right: an estimate of a capture's resolution, exact vertically. |
| `DepthPyramid.cs` slope, `depth_pyramid.glsl` line 75 | `TanHalfX * 2 / W` and `TanHalfY * 2 / H` | Right: each axis on its own. |
| `LightPyramid.cs` lines 184 and 197, `screen_trace.glsl` lines 632 to 653 | per-axis edges and the product of both footprints | Right. |
| `screen.glsl` (`viewRayThrough`, screen place, uv), `lighting.vert`, `outline.vert`, `outline.frag` | `cameraRight`, `cameraUp` and the viewport per axis | Right. |
| `accumulate_temporal.comp`, `screen_trace_plan.comp` reprojection | `previousViewport` per axis, the previous jitter | Right. |
| `ScreenRay.cs`, `screen_ray.glsl` limits and cells | pixel bounds per axis | Right. |
| `skin_scatter.glsl` `pixelsPerMeter = H / (2 z tanY)`, which the rows take too | assumes square pixels | Right enough for step 1: the rows' kernel is up to 1 % too wide or too narrow in meters, and each tap rounds to a whole pixel, so a tap moves only where its offset lies within 1 % of a half pixel. Step 2 makes the pixels square again, which makes it exact. |
| The diffuse filters' and the reflection filters' kernels in pixels | pixel offsets, the same on both axes | Right enough for step 1: a blur radius up to 1 % wider in angle on one axis. Exact after step 2. |
| `ShadowCascades` fit | `TanHalfX`, `TanHalfY` | Right: it follows the camera. |
| Environment faces and local shadow tiles | their own square views (aspect 1) | Right: their own cameras. |
| The depth and light pyramids' level sizes | the render size's counts | Right. |
| Bloom, tone map, outlines | the output size | Right. |
| `NoUpscaling` | render size = output size | Right: exact. |
| `FixedRenderScale` | rounds each axis like `FrameTimeTarget` | Fixed by the same change: its camera now takes the output's aspect too. |
| `puff.vert` billboard axes | the camera's axes, normalized | Right. |
| `DrawnCamera.TanHalfX` into `PreviousView` | the drawn camera | Right: the motion of the next frame uses the same camera. |
| Host and Demo | no aspect of their own | Right. |

The runs on the code of this commit (step 1):

- `nix develop --command bash -c 'bash tools/gpu-lock.sh --shared <log> dotnet test --solution Tertius.slnx -c Checked'`: 11,055 tests, 0 failed, 28 skipped (Rendering 3,310 passed and 12 skipped in 32 min 51 s under a load of 30 to 100). No snapshot test failed, so no reference image changed.
- The red runs: `dotnet test --project tests/Tertius.Rendering.Tests -c Checked -- --filter-class "*OutputCameraTests" --filter-class "*OutputFramingRenderTests"` on 4298ae7ec with the new tests: 11 of 12 failed (the 1280 x 720 case passed). After the fix: the changed CPU classes (OutputCameraTests, BatcherTests, BatcherLightTests, BatcherMotionTests, BatcherDeformTests, ImpostorManagerTests, Fsr3HostTests) 330 passed, 0 failed.
