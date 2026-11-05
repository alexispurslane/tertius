---
id: ter-s7c6
status: in_progress
deps: [ter-k63u, ter-gayt, ter-7m38]
links: []
created: 2026-11-03T14:53:48Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, fsr]
---
# FSR 3.1 in the frame, with dynamic resolution

Build ticket 5 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, sections 4, 5 and 9, and decisions 2, 13, 15, 30, 31, 33, 34 and 37. Acceptance: section 14, item 5. It starts from branch experiment-fsr1 at 3be9aef99 (the closed ter-htg7), and the glow's width follows the output size (the closed ter-ch7z). SMAA goes.

## Implementation and source applicability — 2026-11-05

The actual base is the integrated tracer/Rule A commit `34c28466e641a864fd6a915c029e08f07b3b9ccc`, not the old experiment. The checkout was clean at launch. The prior accepted experiment at `3be9aef99` supplied the settled render/output split and jitter convention; its old branch was not imported wholesale. The existing FSR host and pinned GLSL remain the algorithm source.

AMD FSR 3.1.4 reconstructs a display-resolution image from a lower-resolution, jittered shaded image, depth, motion, exposure and temporal history on a compute-capable GPU. It is not an indirect-light denoiser. Its source situation includes temporally usable shaded inputs; Tertius currently has unresolved native tracer noise and no dedicated denoiser. FSR applies here as the chosen temporal anti-aliasing/upscale, not as evidence that native tracer noise is solved. At the selected 1920 × 1080 output, a fixed 2/3 scale gives 1280 × 720 render pixels, 4/9 of native pixel area. Its fixed output-resolution reconstruction cost must be weighed against those saved render pixels, not justified by an asymptotic claim. Pinned source, formats, optional RCAS and capabilities are documented in `src/Tertius.Rendering/ThirdParty/Fsr3/README.md`.

The implementation follows the settled decisions:

- Largest render-size images keep their allocation while an 8-pixel-rounded active rectangle changes. Output resizing recreates targets and resets history; active-size changes preserve history. Native `NoUpscaling` keeps exact output dimensions, zero jitter and the existing pixel-test behavior.
- FSR receives active render size, unjittered normalized motion, reversed device depth, draw-index jitter, current/previous exposure and AMD's generated reactive mask. The opaque image is copied after haze and before transparency. No transparency/composition mask or experimental denoising is added.
- Exposure metering reads active pixels before FSR. Under FSR only, adaptation runs after FSR, so it cannot replace this frame's exposure ratio with the next frame's ratio. The integrated base already initialized both `stops` and `previousStops` and declared both coherently in all three shaders; regression tests verify frame-zero initialization rather than inventing another initialization path.
- Bloom, tone mapping and outlines use output resolution. Only main-view material sampling gets AMD's mip bias. The main view rotates a quarter of the shadow taps under FSR; native rendering, environment faces and impostor captures retain their full filters. Superseded SMAA code, tables, descriptors, shaders and notice are removed.
- The window-only controller consumes delayed frame-numbered GPU timings. It filters fixed/scaling costs with weight 0.2, uses 90% of the frame target, has a 3% dead band and falls/rises by 1/4 and 1/20 of the gap. Headless defaults to fixed 2/3 and never starts a controller.
- Two command buffers are submitted as two `SubmitInfo2` entries. The first ends after bloom and has no acquire wait. Only the output submission waits at color-attachment output; its fence covers both. The controller stops its summed timestamp span before tone mapping, excluding that presentation wait.

## Behavior-test evidence

The initial active-extent/controller tests failed for the missing behavior, and the first real-renderer test observed native width 160 where active width 104 was required. Both subsequently passed. The optional sharpening test first observed identical output with sharpness 0 and 1, then passed with the pinned RCAS algorithm behind a guarded wrapper. A later cutout regression first observed different HDR pixels for fully opaque cutout and solid materials; the cutout pipeline was missing the main-view specialization and now uses it.

The repeat-still test was corrected to match the binding motion contract: a reset preserves the prior camera/object placement. A first-ever still and a repeated still therefore need not have identical motion inputs. The replacement checks jitter restart and draw-index advancement; independent fresh headless runs with the same draw history must be bit-identical. No motion-history or tracer policy was changed to satisfy a misleading equality assertion.

Coverage, mutation testing, integration, push and closure belong to the parent. Linux runtime remains user-deferred under `ter-cgif`; macOS synchronization validation does not establish Linux runtime behavior.

## Final validation and build-input identity

Final source/test/build-input tree: `16c00a286050822cf1ace22e06c78ac4bd0d395b`, from `tools/code-tree.sh`, with the same hash for the staged code. Original HEAD/base: `34c28466e641a864fd6a915c029e08f07b3b9ccc`. Worktree: `/Users/alexispurslane/.t3/worktrees/tertius/ter-s7c6-fsr31-dynamic-resolution`; branch: `ter-s7c6-fsr31-dynamic-resolution`. `flake.lock` SHA-256: `3b46e6ef791267a2abe5401fd494a65588f62c82fb96e8e4e8496e293deb12c3`; `global.json` SHA-256: `9384d5f057ed52752aa3dc5407e75b2265495154fb3a6597afe70b79f8776f93`.

`tools/cleanup.sh` completed on the 51 initially touched C# files, then on five native-fixture migrations before this final code freeze. All builds and tests ran in the Nix dev shell, one heavy command at a time. GPU tests held `tools/gpu-lock.sh`; legitimate lock holders were neither bypassed nor interrupted. The source-pin check passed: the vendor files still equal AMD's SDK v1.1.4 apart from the accepted `pin.diff` declarations and reprojection fix. The clock sampler was built locally through the dev shell.

The first complete Checked/Release run found seven outdated test assumptions per configuration: two no-history still rules, the shader-buffer layout, two physical-star pixel tests, skipped-output exposure readback and a 16 × 16 ffmpeg-failure fixture. Native tests now explicitly select `NoUpscaling`; their image and lighting tolerances are unchanged. Shader-layout tests account for the three added vec4s and verify their offsets. Focused native tests then passed: Rendering 128/128, Host video 13/13. Earlier integration/controller/constant tests passed 37/37 and headless command tests 242/242.

The final full command, `nix develop --command bash -c 'bash tools/gpu-lock.sh captures/ter-s7c6/logs/final-validation.log bash tools/test-all.sh -c Checked,Release -j 1 -o captures/ter-s7c6/final-tests'`, completed on 2026-11-05 at 12:31:31 EST with no failures. Both solution builds had zero warnings and errors.

| Module | Checked passed / skipped | Release passed / skipped |
|---|---:|---:|
| Core | 1207 / 0 | 1175 / 0 |
| Simulation | 2001 / 0 | 2020 / 0 |
| Physics | 1492 / 8 | 1493 / 8 |
| Rendering | 2302 / 7 | 2301 / 7 |
| Host | 2024 / 6 | 2024 / 6 |
| Unions | 59 / 0 | 59 / 0 |
| Style | 276 / 0 | 276 / 0 |
| Slop | 597 / 0 | 597 / 0 |

The 21 skips per configuration are recorded in the module logs; they include existing platform/known-defect checks and gated profiling/evidence tests. They are not claimed as passes. Raw red/green logs, cleanup logs, build logs, exact staged filenames and source identity are preserved under the worktree's ignored `captures/ter-s7c6/` folder. Thin frame-graph adapters (`UpscalePass`, `ExposureAdaptPass`) deliberately retain algorithm and pipeline ownership in `Fsr3Upscaler` and `ExposurePass`; they do not duplicate those owners.

### Commit-hook inspection

The normal commit hook passed. It skipped all five selected builds and both selected test projects because their records matched the staged code tree. No extra test run was needed. Its advisory report contains two method-length findings, three complexity findings and six verbosity findings. The following inspection keeps those findings separate from build or test failures:

| Flagged code | Inspection and retained design |
|---|---|
| `VulkanRenderer` constructor and `DrawFrame` | Resource construction and frame submission remain explicit in their owning methods. Their order controls failure cleanup, image acquisition, history updates, fence reset and capture completion. Extracting wrappers solely to meet the line count hides that order without reducing caller knowledge. |
| `ShaderConstants.Specialization` and `RendererOverrides.ApplyTo` | The branches express independent specialization values and independent user overrides. The specialization table already shares its storage-writing loop. Splitting each option into another method adds navigation without removing a duplicated decision. |
| `RenderScaleController.Costs` | One bounded scan owns the timestamp range and its fixed/scaling split. Moving its range flags into separate helpers distributes the same boundary decision. The existing tests exercise missing, invalid, delayed and presentation-excluded timings. |
| `DescriptorLayouts` tuple assignment | The tuple transfers one graphics-layout group into the owner's readonly fields. A one-use record adds a second representation without reducing the constructor's required layout knowledge. |
| `RenderTargets.Create` and `ExposurePass.Adapt` | The flagged statements describe one target-allocation group and one ordered Vulkan adaptation dispatch. No duplicated operation or separate responsibility was found in those groups. |
| `ExposureAdaptPass` | This frame-graph adapter schedules the existing exposure owner after FSR. It does not copy exposure state or algorithm behavior. |
| `RenderScaleController.Next` | The guard rejects a negative public frame argument. The postcondition checks the recorded frame/scale pair after the ring write. They check different facts, so removing either to satisfy the heuristic loses a check. |
| `VulkanRenderer.CreateTargets` local | `extent` names the output dimensions with a nonzero fallback before target construction. Keeping the named quantity makes the allocation call easier to read. |

The report's 3647 binding errors and 243 unresolved state-access locations describe incomplete source-metric evidence, not compiler errors. Both real solution builds completed with zero warnings and errors. The retained findings are nonblocking observations. No source change followed the final code freeze.

## Real-renderer evidence and controlled-clock timings

The standalone Release benchmark project compiled separately with zero warnings and errors (`logs/benchmark-build.log`). The final Demo, Rendering tests, Host tests and benchmark output stamps all contain source/test/build-input tree `16c00a286050822cf1ace22e06c78ac4bd0d395b`. The Nix SDK is `11.0.100-rc.1.26425.128`.

`captures/ter-s7c6/run-evidence.sh` ran sequentially in the Nix dev shell under one shared GPU lock. Its commands completed at 12:40:08 EST. It first ran the explicit real-demo history test, then actual `dotnet run --project src/Tertius.Demo -c Release --no-build -- --script scripts/temporal-upscale-validation.json --headless --out <folder> --upscaling off|fsr3 --in-flight 2` captures, then timing-only demo runs and two isolated FSR measurements. No mock, synthetic replacement image or other checkout's executable supplied these images.

The explicit history test passed 1/1 in Release with synchronization validation enabled. In each mode it recorded 163 real draws, 32 warm-up draws per still, final draw index 56, final reset false and zero validation messages across the camera cuts and moving frames. Native render/output sizes were 1920 × 1080 / 1920 × 1080; FSR sizes were 1280 × 720 / 1920 × 1080. The records are `history-proof/off/history.json` and `history-proof/fsr3/history.json`. They prove that draw history advances even while a still's simulation snapshot stays fixed. PNGs from the separate Demo invocation are in `demo-off/` and `demo-fsr3/`.

Capture settings: fixed headless 2/3 FSR or explicit `NoUpscaling`, sharpness 0, two frames in flight, 32 still warm-up draws, ACES fitted tone curve, automatic exposure, default bloom, default screen tracer, default environment map, default local shadows, one cascaded light and default subsurface scattering. The rendering source is the integrated tracer/Rule A, with no denoising or tracer-policy changes. The capture-run timed GPU clocks averaged 1126.5 MHz native and 1015.6 MHz FSR, with substantial clock variation due to capture/readback/PNG work. Those capture clocks are provenance, not a controlled cost comparison.

Inspected the actual native/FSR deck stills and the first/last FSR glass-motion frames, including the matching native last glass frame. FSR softens the native grain and smooths edges, but leaves visible grain and broad lighting variation. No obvious new duplicate glass silhouette was visible in those inspected frames; this is a short-sequence observation, not a universal ghosting claim. Keep sharpness 0: boosting local contrast would also boost this unresolved noise, and the extra RCAS dispatch is unnecessary by default. The reactive generator retains AMD sample values: scale 1, threshold 0.2, binary value 0.9, tone-map/threshold/component-max flags. The GPU test separately verifies that changed glass marks the reactive mask and untouched sky does not. No transparency/composition mask was added.

The validation script's files named `exterior-*` actually use `spoke-camera` and show the station's interior chute. They are not exhaust evidence. The `demo-exit` scene starts after docking, so supplemental real exhaust footage uses the existing `demo-surface` launch and its moving shuttle-attached chase camera, not a production scene change. That supplemental script is recorded below for reproducibility.

For cost, `scripts/temporal-upscale-timing.json` (SHA-256 `f881515e8d04e118ef2820e6c6068946bb70cc6601b59f3111895b34dd0633e4`) ran the deck for 120 moving frames with `--video none`, two frames in flight and contiguous GPU ranges. Both timed-span clocks were 1296.0 MHz, with p10/p90 also 1296.0 (26 native samples, 15 FSR samples). `tools/gpu_clock.py compare` accepted the 0.0% clock difference against its 3% limit. These are actual measured times, not clock-normalized estimates:

| Timestamp span | Native mean / median / p95, ms | FSR fixed 2/3 mean / median / p95, ms |
|---|---:|---:|
| Frame | 23.209 / 23.205 / 23.465 | 13.418 / 13.409 / 13.699 |
| Deformation through bloom | 22.966 / 22.955 / 23.218 | 13.167 / 13.159 / 13.449 |
| G-buffer through exposure | 21.398 / 21.386 / 21.561 | 9.483 / 9.473 / 9.597 |

This is a whole-configuration comparison, including FSR's quarter shadow filter, not a pure pixel-count speedup. No individual MoltenVK pass timestamp is used alone, and no live windowed-controller or frame-rate result is claimed. Machine load was 4.48 at entry to this locked evidence group and 4.04 at exit; it was GPU-exclusive, not an otherwise idle machine.

The existing isolated FSR batched measurement passed twice on Apple M1 Max: largest/active render 1280 × 720, output 1920 × 1080, 150 timed batches of eight frames, two batches queued. Per-frame medians were 1.966 and 1.964 ms; p10/p90 were 1.949/1.988 and 1.950/1.981 ms. Timed clocks were 1295.9 MHz for both rounds (p10/p90 1296.0, 22 samples each), with continuous GPU activity during the timed samples. Logs are `logs/fsr-timing-1.log` and `logs/fsr-timing-2.log`. This measures the fixed headless allocation, not the window's maximum allocation. Do not treat the difference from the ticket's old 2.79 ms figure as an optimization: the old clock and submission conditions were not controlled by this comparison.


### Supplemental real exhaust capture

The supplemental capture completed on 2026-11-05 at 12:55:35 EST, after a 597-second wait for the shared GPU lock. Both native and FSR commands finished successfully. No lock holder was bypassed or interrupted. The commands used the same Release executable, code tree and rendering configuration recorded above. They ran through the Nix dev shell, `tools/gpu-lock.sh` and `tools/gpu-clock.sh`:

```sh
dotnet run --project src/Tertius.Demo -c Release --no-build -- --script captures/ter-s7c6/plume-validation.json --headless --out captures/ter-s7c6/plume-off --upscaling off --in-flight 2
dotnet run --project src/Tertius.Demo -c Release --no-build -- --script captures/ter-s7c6/plume-validation.json --headless --out captures/ter-s7c6/plume-fsr3 --upscaling fsr3 --in-flight 2
```

The script uses the existing launch simulation and its shuttle-attached camera. The first 1204 ticks match the elapsed simulation prefix before ignition in the checked-in Blue Danube master script. The camera and outline commands omitted from that prefix do not change the simulation. The script SHA-256 is `1d5707088bec0aa55728c2b28db9a97a5b6995b1827a860534c9eff08a49ad4f`. Its complete contents are:

```json
{
  "scene": "demo-surface",
  "window": { "width": 1920, "height": 1080 },
  "steps": [
    { "do": "run", "ticks": 1204 },
    { "do": "post", "to": { "kind": "entity", "id": "shuttle" }, "event": "Ignition", "payload": null },
    { "do": "run", "ticks": 240 },
    { "do": "camera", "entity": "chase-camera" },
    { "do": "capture", "name": "plume-still" },
    { "do": "film", "name": "plume-motion", "frames": 48, "every": 1 }
  ]
}
```

Each mode produced a still and 48 moving frames at 1920 × 1080 output. FSR rendered at 1280 × 720. The first and last inspected FSR frames show the shuttle leaving the ramp with a bright exhaust plume. The matching last native frame has the same broad plume shape and shuttle placement. No obvious duplicate plume or detached trail appears in these inspected endpoints. These endpoints do not establish that every intermediate frame is free of temporal artifacts. FSR softens some shuttle edges and surface grain. It does not fix native tracer noise.

The capture clocks varied during PNG output: native mean 783.5 MHz, p10/p90 645.4/918.0 MHz, 52 samples. FSR mean 776.2 MHz, p10/p90 644.1/932.8 MHz, 54 samples. These are capture provenance only, not controlled performance results. Each log reports 49 captured frames. The complete script, images, clocks and logs remain under `captures/ter-s7c6/` in this worktree.

The four inspected images selected for the user are:

- `demo-off/deck-still.png`: Native deck at 1920 × 1080, with visible surface grain.
- `demo-fsr3/deck-still.png`: The same deck reconstructed from 1280 × 720, with smoother edges and residual lighting variation.
- `demo-fsr3/glass-motion-000023.png`: The moving glass scene, with visible grain and no obvious duplicate glass silhouette in this frame.
- `plume-fsr3/plume-motion-000047.png`: The moving shuttle and exhaust after leaving the ramp, with no obvious detached plume in this frame.

Implementation and required validation are complete. There are no unresolved design forks. The parent owns the later review, coverage, mutation testing, integration and ticket closure. No Linux runtime or live-window frame-rate result is claimed.

## Notes

**2026-11-03T17:00:00Z**

From the review of ter-k63u:

- The exposure buffer gains previousStops here. Write it on the first frame too, as this frame's stops. AMD forces deltaPreExposure to 1 on its first execution, and the GPU test gets 1 only because it passes the same stops twice. FSR reads no history on frame 0, so the value has no effect there, but an uninitialized float can be NaN.
- The layout of the exposure buffer is declared in three shaders: frame.glsl, exposure_adapt.comp and fsr_exposure.comp. Only fsr_exposure.comp has previousStops. Change them together, or move the declaration into one shared GLSL file.
- No GPU test of ter-k63u has motion. The sign and the units of the motion vectors (MotionVectorScale = 1, in units of the render size) are first exercised here.
- FSR's GPU time of 2.79 ms was measured at a load of 5.5 with a largest render size of 1280 x 720. Measure it again on a quiet machine with the largest render size that this ticket picks.

## Preserved parent launch note

The following is the complete launch note supplied by the parent, preserved verbatim before the final commit.

````text
---
id: ter-s7c6
status: in_progress
deps: [ter-k63u, ter-gayt, ter-7m38]
links: []
created: 2026-11-03T14:53:48Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, fsr]
---
# FSR 3.1 in the frame, with dynamic resolution

Build ticket 5 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, sections 4, 5 and 9, and decisions 2, 13, 15, 30, 31, 33, 34 and 37. Acceptance: section 14, item 5. It starts from branch experiment-fsr1 at 3be9aef99 (the closed ter-htg7), and the glow's width follows the output size (the closed ter-ch7z). SMAA goes.


## Notes

**2026-11-03T17:00:00Z**

From the review of ter-k63u:

- The exposure buffer gains previousStops here. Write it on the first frame too, as this frame's stops. AMD forces deltaPreExposure to 1 on its first execution, and the GPU test gets 1 only because it passes the same stops twice. FSR reads no history on frame 0, so the value has no effect there, but an uninitialized float can be NaN.
- The layout of the exposure buffer is declared in three shaders: frame.glsl, exposure_adapt.comp and fsr_exposure.comp. Only fsr_exposure.comp has previousStops. Change them together, or move the declaration into one shared GLSL file.
- No GPU test of ter-k63u has motion. The sign and the units of the motion vectors (MotionVectorScale = 1, in units of the render size) are first exercised here.
- FSR's GPU time of 2.79 ms was measured at a load of 5.5 with a largest render size of 1280 x 720. Measure it again on a quiet machine with the largest render size that this ticket picks.

**2026-11-05T15:17:51Z**

2026-11-05: tracer/Rule A merged, closed and pushed at 34c28466e641a864fd6a915c029e08f07b3b9ccc. Started workflow 1d4812ac-3824-42b6-81e8-505ac1cb62a3, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-s7c6-fsr31-dynamic-resolution, exact local base 34c28466e with startFromOrigin=false. Codex gpt-6.1-sol reasoningEffort=xhigh for build/review/fix. One of four heavy allocations. Settled FSR/dynamic-resolution scope only; no denoising or tracer retuning. Parent owns hardening, integration and closure. Shared launch rules: /private/tmp/tertius-integration-20261105/ready-ticket-rules.md.
````
