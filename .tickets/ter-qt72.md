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

**2026-11-22T19:01:23Z**

2026-11-22, the ticket lead. Step 1's film, the review and its fixes.

The film. The lamp hall (`scripts/rect-lamps.json`) at 1920 x 1080 with a fixed exposure of 4.08 stops, headless, under a played-back moving render size: the Design X lead's logged run d16M (`/private/tmp/t0-5tyj/dynlog/d16M/size-none.txt`, copied to the evidence folder), 31 render sizes from 992 x 560 to 1200 x 672 and 72 size changes over the 240 film frames. Both sides played back the same sizes (their size logs are equal). Main is ee9dec413, and the fix is 839bce1b5, each a build copy under /private/tmp/ter-qt72 with a scratch edit (`scratch-replay.py`) that never enters src. The scratch edit takes TERTIUS_X_FRAME_TIME_TARGET, TERTIUS_X_SIZE_REPLAY and TERTIUS_X_SIZE_LOG from the Design X lead's `scratch-dynamic-scale.patch`. The video: /Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a-evidence/video/lamp-hall-wobble-main-vs-fix.mp4, main on the left and the fix on the right, 240 frames at 60 a second, H.264 at CRF 12, 3840 x 1080.

The tracked edges (`edges.py`, adapted from the Design X lead's `wobble.py`): four vertical edges on rows 530 to 549, each to a sub-pixel by the peak of the brightness gradient. The outer pair are the inner edges of the nearest wall openings, about 850 px left and right of the centre. The inner pair are the two sides of the back wall's dark panel near the centre. All numbers are in output pixels:

| | Main | Fix |
|---|---:|---:|
| Outer distance (about 1,700 px): sd | 7.378 | 0.489 |
| Outer distance: range | 33.644 | 2.894 |
| Its midpoint: sd | 0.894 | 0.245 |
| Its midpoint: range | 4.771 | 1.931 |
| Inner distance (about 140 px): sd | 0.782 | 0.297 |
| Inner midpoint: sd | 0.300 | 0.136 |
| Correlation of the outer distance with (h / w) * 16/9 | +0.989 | +0.045 |
| Noise floor of the outer distance (same size, frame to frame, / sqrt 2) | 0.291 | 0.165 |
| Noise floor of its midpoint | 0.146 | 0.083 |

What is left on the fix side, and why. The outer distance still spreads by sd 0.49 px, against a noise floor of 0.17 px, and its midpoint by 0.25 px against 0.08 px. A framing change moves the two edges together (a shift) or against each other (a stretch). On the fix side the left edge's place and the right edge's place correlate at +0.003, with sd 0.32 and 0.37 px. So each edge has an offset of its own at each render size. Both edges stay sharp (1 to 2 px wide at half the gradient's peak). Their place correlates with the render width at -0.12 and -0.42. That is FSR's reconstruction of an edge from another grid of samples at each render size, the scatter that the GPU sweep showed in stills (±0.2 px with no trend in the render size). No common shift and no stretch is left. The midpoint's 1.0 to 1.7 px of the Design X lead's measurement was on main, with the stretch. On this film, main's midpoint spread (sd 0.89) also falls to 0.25 with the fix.

The review (one reviewer, Claude Opus 5.5 at high effort, on 4298ae7ec..839bce1b5): correctness pass, fulfillment changes-required, design changes-required. The resolutions:

- F1, a text that allowed non-square pixels as the design: fixed. ARCHITECTURE.md now says the active rectangle's ratio differs by about 1 % only until step 2. SceneBatcher's comment no longer says that pixels need not be square.
- F2, the fields of `CameraView` without docs: fixed. `Aspect` is the projection's, the output's. `Width` and `Height` are the render size, whose ratio can differ. `PixelAngle` is the vertical pixel angle.
- F3, the GPU test asserted a distance against a reason per axis: fixed. It asserts each axis below 0.6 px.
- F4, the sweep cannot see errors that arise only at a change of size: answered by the film above, which changes the size 72 times. No common shift is left.
- F5, no test pinned `UpscalePass`'s aspect: fixed. A test hook `VulkanRenderer.ReadFsrConstants` (which `ReadFsrExposureRatio` now uses) and the test `FsrRebuildsViewPositionsWithTheOutputsCameraAtARenderSizeOfAnotherShape`. With the pass's line set to the render size's ratio, the test failed: deviceToViewDepth2 1.0392 against 1.0264. How the CPU FSR test was red on 4298ae7ec: it compiled there without the `Aspect` line, because `Fsr3Frame` had no such member, and it asserted the output's tangent, 1.0264 at 60 degrees. It failed there. The green step added the required member and the one line that passes it.
- F6, the fact of AMD's aspect in two places: fixed, the line comment is gone, and the doc of `Fsr3Frame.Aspect` keeps it.
- F7, null for a default size: fixed, two overloads of `Batchers.FirstFrame`.
- F8, `FrameHistory.Jitter` and `ApplyRenderProjection` read the output size from another store of the same draw: rejected for this ticket. It is older than this change, both values are equal by construction, and the brief asks for a small diff in these files.
- Added for the mutants: `TheCamerasPixelCountsAreTheRenderSizes`, a CPU test that the camera's width and height are the render size.

The run on the code of this commit (the review fixes): `nix develop --command bash -c 'bash tools/gpu-lock.sh --shared <log> dotnet test --solution Tertius.slnx -c Checked'`: 11,057 tests, 0 failed, 28 skipped. No snapshot test failed.

**2026-11-22T19:38:21Z**

2026-11-22, the ticket lead. Step 1's remaining runs, all on the code of f54129fcc.

- Release: `nix develop --command bash -c 'bash tools/gpu-lock.sh --shared <log> dotnet test --solution Tertius.slnx -c Release'`: 11,044 tests, 1 failed, 29 skipped. The failure is `Fsr3UpscalerTests.ATimedBatchReportsTheTimeOfOneOfItsUpscalesNotOfTheWholeBatch` (a median of 0.411 ms against a limit of 0.039 ms), the timing test of lin-5bsm that fails under GPU load. Rerun alone under the exclusive GPU lock at a load of 6, with `-c Release --no-build -- --filter-method "*ATimedBatchReportsTheTimeOfOneOfItsUpscalesNotOfTheWholeBatch*"`: 1 passed.
- Linux: one cloud CPU job (0763a4d8-91ea-447d-be6b-bcc7b13df0b5-u2, 250 s, $0.0064 by the job log) ran `lavapipe-tests.sh` with CLASS_STEMS "OutputCameraTests OutputFramingRenderTests Fsr3HostTests UpscalingRenderTests" on Mesa 26.2.4 lavapipe (llvmpipe, LLVM 21.1.8): 89 tests, 0 failed, 0 skipped.
- Mutants: `tools/mutate.py` with 11 listed mutants of the changed lines (list and logs in the evidence folder, `mutants-step1.json`, `mutate-step1/`): 9 killed, 1 survived, 1 failed to build.

| id | place | change | result |
|---|---|---|---|
| C01 | SceneBatcher.cs:238 | the aspect from the render size | build-failed: the analyzer refuses the then-unused parameter `outputSize` (IDE0060). C03 makes the same change through the call and is killed. |
| C02 | SceneBatcher.cs:238 | drop `Math.Max(outputSize.Height, 1)` | survived. Judgment call: no draw has an output of zero height, because a headless renderer refuses a zero size and a window without an area acquires no image and draws nothing, so the guard is never reached. |
| C03 | SceneBatcher.cs:130 | Prepare passes the render size as the output size | killed by AWorldPointKeepsItsPlaceOnTheOutputAtARenderSizeOfAnotherShape |
| C04 | SceneBatcher.cs:129 | the pixel counts from the output size | killed by TheCamerasPixelCountsAreTheRenderSizes |
| C05 | SceneBatcher.cs:251 | the width from the output size | killed by TheCamerasPixelCountsAreTheRenderSizes |
| C06 | SceneBatcher.cs:252 | the height from the output size | killed by TheCamerasPixelCountsAreTheRenderSizes |
| C07 | Fsr3Constants.cs:98 | tanHalfFOV from the render size's ratio | killed by FsrsViewConstantsDescribeTheOutputsCameraAtARenderSizeOfAnotherShape |
| C08 | Fsr3Constants.cs:139 | deviceToViewDepth2 from the render size's ratio | killed by the same test |
| C09 | UpscalePass.cs:93 | the pass hands FSR the render size's ratio | killed by FsrRebuildsViewPositionsWithTheOutputsCameraAtARenderSizeOfAnotherShape |
| C10 | FrameHistory.cs:341 | the pending frame's output size is its render size | killed by AWorldPointKeepsItsPlaceOnTheOutputAtARenderSizeOfAnotherShape |
| C11 | VulkanRenderer.cs:1319 | the test hook reads another word | killed by FsrReadsThisFramesExposureRatioBeforeAdaptationWritesTheNextFramesExposure |

- Coverage: `dotnet test --project tests/Tertius.Rendering.Tests -c Checked --coverage --coverage-output-format cobertura` with the classes OutputCameraTests, OutputFramingRenderTests, Fsr3HostTests, UpscalingRenderTests and BatcherTests (190 passed), then `python3 tools/coverage/changed-lines.py --base 4298ae7ec`: 15 of 15 changed lines covered, no branches.

## Step 2: exact-ratio sizes, window snapping and realized-scale feedback (2026-11-22, the ticket lead)

Commits: 03762d578 (the build) and the fix commit after the review. Evidence folder: `/Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a-evidence/step2/`.

The model. `UpscalingSetting` is the one owner of the size and of the one predicate (decision 6).

- A step is the output divided by the greatest common divisor of its width and height: 16 × 9 px for 1920 × 1080.
- `RenderExtent` takes the largest whole number of steps at or below the requested scale, clamped to the hard limits (decision 4): (1, 1) without upscaling, (0.5, s) for `FixedRenderScale(s)`, and a `FrameTimeTarget`'s own `MinScale` to `MaxScale`. Both axes take the same step count, so the active size has exactly the output's aspect (decision 2).
- `Supports` (with its reason, `OutputProblem`) is the predicate. A fixed scale needs one size inside the limits. A frame-time target with a range needs at least as many as the eighths of the output inside its limits, and at least two: five for 0.5 to 1, four for 0.5 to 0.9.
- `SnapOutput` keeps a supported window as it is, and rounds each axis of another to the nearest multiple of a grid, ties up (decision 3). The grid is the smallest size from 8 px up that is supported as a square: 8 for every fixed scale and for a target with two eighths inside its limits, such as the default.
- The readers of the predicate: `SwapchainTarget` (no swapchain at an unsupported extent, as when minimized), `VulkanRenderer.ApplyPendingResize`, `CreateTargets` (the nearest supported size until the first image), the headless refusal at creation and at `Resize`, and `RenderExtent`, which throws. None of them repeats the rule.
- `FitWindow` asks the window for the snapped framebuffer through its logical size, at creation and at each framebuffer event, with a guard against the nested event that its own request raises.
- `RenderScaleController.Drew` records the realized scale, render width over output width, under the frame number (decision 5). Example: a request of 0.9 at 1920 × 1080 draws 1680 × 945, 0.875.

Red evidence (`step2/red.log`): with stubs that kept the old behavior, 43 of 118 tests failed for the expected reasons:
- the old sizes, such as 1272 × 712 for 1919 × 1079;
- no refusal and no snapping;
- the feedback of the request: the next request after a draw of 0.875 under 20 ms was off by the ratio of 0.9 to 0.875.

The fix stage's red (`step2/fix-red.log`): 1366 × 768 stayed unsnapped, and a window whose surface starts at 1919 × 1079, 7 × 5 or 0 × 0 threw `ArgumentException` in `CreateTargets`.

The audit of the alignment of 8 (decision 2):

| Part | What bounds its work | Needs a multiple of 8? |
|---|---|---|
| 32 compute shaders in `src/Tertius.Rendering/Shaders` | Each one compares its invocation with an image size, the active size or a count (`screen_trace_own_rays.comp` loops `entry < count`) | No |
| Graphics passes from the G-buffer to the exposure | Viewport and scissor of the active rectangle | No |
| Depth pyramid | Clears inactive pixels and bounds its reads by `frame.viewport` | No |
| FSR 3.1 | AMD's code takes any render size within its allocation. `Fsr3Upscaler.AllocationSize` pads only the allocation: a longer side of 64 and a shorter side of 2 | No: the padding belongs to the allocation |
| Native rendering | Already drew odd sizes, such as 159 × 91 | No |

The suites draw at odd exact-ratio sizes, such as 105 × 63, 3 × 2, 1 × 1, 84 × 70 and 1083 × 608, and they pass.

The real window (`step2/probe/`, a hidden Silk.NET window with the real swapchain on the Mac, at one framebuffer pixel per logical pixel, `FrameTimeTarget(16, 0.5, 0.9)`). The probe's source stays outside the branch. All 7 requests passed with 0 validation messages:

| Request | Framebuffer and acquired image | Active size |
|---|---|---|
| A window created at 1919 × 1080 | 1920 × 1080 | 1728 × 972 |
| 1920 × 1080 | 1920 × 1080 | 1520 × 855 |
| 1921 × 1080 | 1920 × 1080 | 1520 × 855 |
| 640 × 480 | 640 × 480 | 512 × 384 |
| 361 × 641 | 360 × 640 | 288 × 512 |
| 641 × 481 | 640 × 480 | 520 × 390 |
| 1366 × 768 | 1368 × 768 | 1083 × 608 |

Each active size has exactly the output's ratio. The scale fell below 0.9 because the window measured real GPU timings. The frames at 1920 × 1080 and 360 × 640 show the test box square.

The review (one reviewer, Claude Opus 5.5 at high effort, of the diff of 03762d578, read only): correctness, fulfillment and design changes-required. The resolutions:

- F1, important: a window whose surface starts at a size that the setting cannot draw, or at 0 × 0, threw in `CreateTargets`. Fixed: the targets take `SnapOutput` of the surface's size until the first image. The test `AWindowWhoseSurfaceStartsAtASizeItCannotDrawStartsAndDrawsOnceItHasOne` (1919 × 1079, 7 × 5, and 0 × 0 minimized through the new `TestOutput.Minimized`) was red, then green.
- F2, important: a window of 1366 × 768 counted as drawable under the default target. Its greatest common divisor 2 gives only 0.5 and 1, so a request of 0.99 drew a quarter of the area. Fixed by the eighths rule above, which the line orchestrator accepted: 1366 × 768 snaps to 1368 × 768 (13 sizes), and 1680 × 1050 (106 sizes) stays. A headless output keeps the rule of one size, because it cannot change: at 2/3, 1366 × 768 draws 683 × 384 (`AFixedScaleDrawsAnOutputWithOneSizeInsideItsLimits`). The report of a window that a platform holds at an undrawable size stays open in t10-yb2s.
- F3, important: the readers of the predicate. Resolved as decision 6 says: `UpscalingSetting` owns the predicate, and the readers listed above call it and repeat nothing.
- F4: the docs of `FixedRenderScale`, `CreateHeadless` and `Resize`. Fixed.
- F5: the controller's summary. Fixed: the largest exact-ratio size at or below the request, or the smallest inside the limits.
- F6: a false test name. Renamed to `ASmallHeadlessOutputDrawsTheLargestExactRatioSizeAtOrBelowTheDefaultScale`.
- F7: the repeated headless setting. Fixed: `VulkanRenderer.UpscalingOf`.
- F8: `AllocationSize` was hard to read. Fixed: named locals, and the reason for the minimum of 2 (AMD's images at half the largest render size need a pixel on each side).
- F9: the audit table and the full suites. The table is above, and the runs are below.
- F10, an unverified suspicion that asynchronous framebuffer events at a fractional HiDPI scale could cycle the fit: recorded in t10-yb2s. Linux and HiDPI runtime are deferred under ter-cgif.
- From the hook: `DrawFrame` and one test were over 60 lines. Fixed with `BeginDraw` and `GlowingPanelOverAMirror`. A trivial postcondition after the guard of `Drew` is gone.

The residual of step 1's film, for this audit: its cause is found, and the question of the previous frame's size and the jitter is closed. Another line's separating take ("Take B" of docs/research/t0-3vwx-dynamic-scale-films/README.md in the worktree t3-52bfcd29, commit ef379409e) played one size log with 97 size changes twice on step 1's code:

| Measure | Through the lamp switch | Lamps held on |
|---|---|---|
| Residual of the edge distance on draws whose size changed (px rms) | 0.465 | 0.096 |
| The same on draws of one size (px rms) | 0.213 | 0.139 |
| Correlation of the residual with the render scale | +0.610 | -0.025 |

- The residual on changed draws follows the change of the light, not the change of the size. It seemed to follow the size because the controller lowers the scale while the light changes. With a still light, a size change moves the tracked edges less than a draw of one size does.
- What is left is an offset per render size of 0.16 px rms at most (the other line's ramp of 181 sizes with a still light), from FSR's reconstruction of an edge from another grid of samples.
- So nothing of the residual is left on these edges for the exact-ratio sizes to remove, beyond that per-size offset. Step 2 rests on the user's decisions of 2026-11-05 and on correctness: square render pixels for every pass, exact-ratio sizes, and the realized feedback. Its film is a check that nothing got worse, not a visible gain, unless its numbers show one.

After step 2 every render size of a 16:9 window has the output's exact ratio, so the jamb leak of t1-gout cannot show through the size.

Step 2's runs:

- Checked, on e60d6c937's code: `nix develop --command bash -c 'bash tools/test-all.sh -c Checked'`, 0 failures in all 8 modules (Rendering 3,377 tests, 10 skipped). The five walk classes of ter-186q ran in it on the merged code with no failure. The hook of 924820e5f ran all of Rendering again: 3,383 tests, 0 failed.
- Release, on e60d6c937: `bash tools/test-all.sh -c Release`, 0 failures in all 8 modules (Rendering 3,376 tests, 10 skipped).
- Linux: one cloud CPU job (94ae5781-3f56-4b48-acc9-697e20793c31-u2, 700 s, $0.0154) ran `lavapipe-tests.sh` with CLASS_STEMS "RenderScaleTests UpscalingRenderTests OutputFramingRenderTests OutputCameraTests KeyAndFillRenderTests EnvironmentBoxRenderTests" on Mesa 26.2.4 lavapipe (llvmpipe, LLVM 21.1.8) at e60d6c937: 186 tests, 0 failed, 0 skipped.
- Mutants: `tools/mutate.py` with 34 listed mutants of the changed lines (`mutants-step2.json`, `mutate-step2/`), then 15 rewritten or still-alive ones after the hardening commit 924820e5f (`mutants-step2b.json`, `mutate-step2b/`). In round 1, 7 mutants failed to build: an analyzer refused an unused value, an unreachable loop or an `int` in a `uint`, and one build lost its Nix store path to the garbage collection. Round 2 rewrote them so that they compile.

| id | place | change | result |
|---|---|---|---|
| S01 | UpscalingSetting.RenderExtent | one step above the upper limit | killed by EveryActiveSizeHasExactlyTheOutputsAspectInsideTheScaleLimits |
| S02 | RenderExtent | always the largest step | round 2 killed by ARequestBelowTheLimitsDrawsTheSmallestSizeInsideThem |
| S03 | RenderExtent | the height rounds on its own | killed by AFixedScaleDrawsAnOutputWithOneSizeInsideItsLimits |
| S04 | OutputProblem | a zero height counts as drawable | killed by AMinimizedWindowKeepsItsEmptySize |
| S05 | SnapOutput | an empty window snaps to the grid | killed by AMinimizedWindowKeepsItsEmptySize |
| S06 | SnapOutput | the grid starts at 1 px | survived, then killed by the new AWindowOfAFixedScaleSnapsToTheGridOf8SoThatItsScaleIsAnEighth |
| S07 | SnapOutput | the grid never grows past 8 | survived round 2: its test's snap gave the same size on both grids. The test now snaps 1013 × 1012 to 1020 × 1020 on a grid of 20 px; round 3 below |
| S08 | ScaleLimits | a fixed scale must be met exactly | killed by AFixedScaleDrawsAnOutputWithOneSizeInsideItsLimits |
| S09 | ScaleLimits | a fixed scale may be exceeded | round 2 killed by AFixedOutputWithNoActiveSizeInsideTheScaleLimitsIsRefused |
| S10 | ScaleLimits | a target ignores its own limits | round 2 killed by EveryActiveSizeHasExactlyTheOutputsAspectInsideTheScaleLimits |
| S11 | SizesNeeded | a time target settles for two sizes | round 2 killed by AWindowOfATimeTargetHasAtLeastAsManySizesAsTheEighthsInsideItsLimits |
| S12 | SizesNeeded | no floor of two sizes | survived, then killed by the new ATargetWithNoEighthInsideItsLimitsStillNeedsTwoSizes |
| S13 | Steps | the first step from the floor | survived, then killed by the new ARequestBelowTheLimitsDrawsTheSmallestSizeInsideThem |
| S14 | Steps | no step down to a first step whose float is the limit | survived, then killed by the new ALowerLimitThatIsTheFloatOfAStepAdmitsThatStep |
| S15 | Steps | no step up of the first step | survived. Removed with its loop: the ceiling of the exact double product never needs a step up (the comment in Steps) |
| S16 | StepsAtMost | the ceiling of the request | survived (the step-down loop undid it), then killed once that loop was removed |
| S17 | StepsAtMost | no step up to a step whose float is the scale | survived, then killed by the new AScaleThatIsTheFloatOfAStepDrawsThatStep |
| S18 | StepsAtMost | no step down of the largest step | survived. Removed with its loop: the floor of the exact double product never needs a step down |
| S19 | Divisor | `/` for `%` | timeout: the loop never ends, which the test run's limit catches |
| S20 | Snapped | snapping rounds down | killed by AWindowOfATimeTargetSnapsRatherThanKeepNativeAsItsOnlyChoice |
| S21 | Snapped | a narrow axis snaps to 0 | survived, then killed by the new row 3 × 1080 → 8 × 1080 |
| S22 | RenderScaleController.Drew | NaN passes the guard | killed by ADrawAtAScaleOutsideTheLimitsIsRefused |
| S23 | Drew | records the request | killed by TheDelayedTimingsOfADrawCountTheScaleThatTheDrawRealized and DelayedGpuFeedbackCountsTheScaleThatWasDrawn |
| S24 | Fsr3Upscaler.AllocationSize | a shorter side of 1 | killed by FsrPadsOnlyItsAllocationToALongerSideOf64AndAShorterOf2 |
| S25 | AllocationSize | no minimum height | killed by the same test |
| S26 | VulkanRenderer.CreateOffscreen | checks the setting before the headless 2/3 | killed by ASmallHeadlessOutputDrawsTheLargestExactRatioSizeAtOrBelowTheDefaultScale |
| S27 | CreateOffscreen | no check at creation | killed by AHeadlessOutputWithNoActiveSizeInsideTheScaleLimitsIsRefused |
| S28 | Resize | a headless resize goes unchecked | killed by AHeadlessResizeToAnOutputWithNoActiveSizeInsideTheScaleLimitsIsRefused |
| S29 | CommitDrawnFrame | records the request | killed by DelayedGpuFeedbackCountsTheScaleThatWasDrawn |
| S30 | ApplyPendingResize | resizes the targets at any nonzero output | survived. Judgment call: only a window's swapchain reports an undrawable nonzero size after a resize, because every renderer without a window refuses one at `Resize`, and no suite test opens a window |
| S31 | SizesNeeded | a single-scale target needs the eighths too | round 2 killed by ANativeOnlySettingKeepsEveryWindowSizeAsItIs |
| S32 | SizesInside | no test of an empty range | survived twice. Equivalent: the first step exceeds the last by at most one, because the ceiling of the lower limit's product is at most one above the floor of the upper one. The code now returns `last + 1 - first`, and the mutant is gone |
| S33 | CreateTargets | the targets take the surface's size | killed by AWindowWhoseSurfaceStartsAtASizeItCannotDrawStartsAndDrawsOnceItHasOne |
| S34 | AllocationSize | always pads the width to 64 | round 2 killed by FsrPadsOnlyItsAllocationToALongerSideOf64AndAShorterOf2 |

- Coverage: `dotnet test --project tests/Tertius.Rendering.Tests -c Checked --coverage --coverage-output-format cobertura` with RenderScaleTests, UpscalingRenderTests, OutputFramingRenderTests, OutputCameraTests, KeyAndFillRenderTests and EnvironmentBoxRenderTests (192 passed), then `python3 tools/coverage/changed-lines.py --base ac5f23915`: 87 of 130 changed lines, 59 of 74 branches. What is not covered:
  - `CreateForWindow`, `FitWindow`, the framebuffer handler and the swapchain gate: 38 lines. Only a real window reaches them, and no suite test opens one. The real-window probe ran them (above).
  - The report of limits too close for any window: now covered by the new LimitsTooCloseForTwoSizesOfAnyWindowAreReported.
  - `ScaleLimits`' throw for an FSR setting without a scale source: unreachable, because `CheckOptions` refuses that setting first.
  - The failing branches of two contracts.
