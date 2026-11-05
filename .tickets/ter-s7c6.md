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

## Preserved parent reconciliation notes

## Noise-research import coordination, 2026-11-05

The user explicitly requests importing the completed FSR/dynamic-resolution pipeline into the existing standalone `ter-icn3-unity-noise-research` branch, preserving and reapplying its uncommitted prototypes. This supersedes that branch's earlier no-import boundary, not its lack of production acceptance. At 13:40 EST this workflow is still in its existing fix stage; implementation checkpoint `9bbbea67cf35ae54f6dbb49f704ce6d704c45f8e` is not an approved completed import. Main and origin/main remain `7d9f6e6739b8fcd1b96fcd307c6434e3d4a8ebaf` without FSR. Do not import a dirty fixer checkout or relabel its earlier implementation as ready.

Parent notified the existing noise thread to prepare its full patch/archive and inspect compatibility only. Send that thread a verified immutable completed-fix or integrated-main SHA when available, with the exact validation/acceptance limits. One later bounded build/render allocation is reserved for the prototype comparison after that safe point; it is not permission to build an unfinished FSR state. No noise acceptance, new noise workflow, prototype commit or main write was authorized by this coordination. Box replacement proceeds independently in workflow `cff16030-2068-4f94-9af5-3c99a8d13ae6`.

## User-facing merged-pipeline video, 2026-11-05

The user explicitly wants a section of the real demo video with FSR and dynamic resolution enabled and working after this ticket merges. Follow-up `ter-570o`, parent `ter-89xq`, depends on this ticket only; do not delay it behind box projection, denoising or the final budget/defaults ticket. Show a bounded continuous moving clip, with evidence of actual window-controller-driven internal-resolution changes at fixed output dimensions. A headless fixed-scale render is not sufficient. Retain per-frame dimensions/timing and exact source/settings/history provenance, inspect motion and transitions, and distinguish playback frame rate from measured live performance. This is parent-owned post-merge visual delivery, not extra scope for the active fixer or a claim that the pipeline is already merged.

**2026-11-05T19:20:12Z**

2026-11-05 14:16 EST: existing review-fix workflow is terminal/succeeded; every member settled, cleanup complete. Independently verified clean exact HEAD 96db37df965c7b35dae499a2f34410d2c3b35011, code-input identity 0c0785ceb18618f1ba836b1f18d9d9956e3efb86, all eight Checked and eight Release records match. Approved this immutable COMPLETED checkpoint for the separately user-authorized standalone-noise import/comparison, explicitly not hardened or merged-main acceptance. Assigned that worker one fresh bounded build/render allocation after prior release. Parent is reconciling exact main 7d9f6e673 and commissioning one combined coverage/mutation/final-validation pass, without another routine implementation/review cycle. Parent owns integration and post-merge video ter-570o.

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

## Review-fix stage, 2026-11-05

This stage began at `9bbbea67cf35ae54f6dbb49f704ce6d704c45f8e`, with a clean checkout on the supplied ticket branch. The original review base remains `34c28466e641a864fd6a915c029e08f07b3b9ccc`. No other checkout, noise prototype, later main commit or automatic semantic-frame correction was imported. The earlier sentence, "Implementation and required validation are complete.", described the first build stage; this section supersedes it for the review obligations.

### Findings and assessment gaps

| Review item | Resolution and evidence |
|---|---|
| Small positive outputs fail FSR construction or resize | Reproduced construction failures at 64 × 64, 7 × 5 and 1 × 1, and the shrink failure. The 64 × 64 default still renders at 40 × 40. Only FSR's internal allocation is padded for its six-level pyramid; the active-size policy is unchanged. Replacement creation now precedes disposal. Shrink-and-restore regressions cover 40 × 32, 7 × 5 and 1 × 1. |
| Very narrow outputs can give zero jitter phases | Two CPU regressions observed zero phases for output widths one and two with active width eight. The phase count now has a minimum of one. The real default renderer also renders and captures the one-pixel output. |
| Controller parses diagnostic labels | The renamed-label regression failed before the change: the original scale fell to 0.9267767 while the renamed scale remained one. `FrameGraph.Stage` now travels with each recorded timestamp through conversion and frame-numbered timing storage. No controller decision reads a pass label. The regression and the stage-preserving timestamp-conversion test pass. The selected two-submission model is unchanged. |
| Solid and cutout variants repeat common rules | Confirmed the duplicated specialization assignments in geometry and impostor capture. Each pass now creates one non-null common configuration and derives its cutout configuration by changing only `CutoutAlpha`. The existing fully opaque cutout-versus-solid HDR regression is retained and passes. No shader or material policy changed. |
| Reflected motion while the active size changes was not tested | Two real-renderer regressions now move the camera and glowing panel over a mirror while the active size shrinks or grows. They check the new reflected location, rejection of the old location, retained history and no reset, with FSR enabled and Vulkan validation clean. Both pass. |
| FSR cost at the window's maximum allocation was missing | Two GPU-exclusive Release rounds now allocate 1920 × 1080 for both maximum render size and output. They measure the minimum, fixed-headless and full active sizes at a held GPU clock. The results and validation limits are below; no tracer parameter changed. |
| Live swapchain behavior was not exercised | A foreground Release probe exercised a real Vulkan window at 640 × 360, 7 × 5, 1 × 1 and 640 × 360. It completed 240 draws, retained frame-numbered stage timings and reported zero validation messages, including disposal. The evidence-only probe is retained in the ignored artifact folder, not added to the engine. This is a resize/synchronization smoke test, not a live-window frame-rate claim. |
| Complete cohesion and depth evidence was not established | The earlier hook's binding errors are not compiler failures, but they prevent a claim of complete engine-wide metric evidence. Review uses the actual owners and callers instead: one graph stage identity, one common configuration per variant pair, one FSR resource owner and one controller state owner. Thin pass adapters remain justified because they schedule existing owners rather than duplicate their algorithms. No incomplete metric ratio is presented as proof that the design goals are met. |
| Linux runtime was not exercised | Remains explicitly user-deferred under `ter-cgif`. The new allocation padding and CPU timing metadata add no Vulkan capability requirement. macOS validation does not establish Linux runtime behavior. |
| Coverage and mutation testing were not run | Remain assigned to the parent. This stage runs neither tool and claims neither result. |

The old architecture sentence, "The exposure buffer gets its second float, `previousStops`, in ticket 5.", was stale after the integration. The architecture now states the shared declaration, frame-zero initialization and adaptation ordering. Source provenance, the portable 32-bit FSR build and the applicability analysis remain unchanged. The FSR measurement still uses HDR input with render-pixel motion, reversed depth and AMD's reactive mask, at the renderer's actual 1080p allocation and active sizes. No denoiser, tracer parameter or light budget was changed.

The first broad red command selected no tests because its method filter combined patterns incorrectly. It is not counted as red evidence. Corrected runs then observed six small-output failures, two zero-phase failures and one renamed-label failure. A first green resize assertion exposed two test assumptions: the live controller can change scale, and the first capture allocates a readback buffer. The resize test now pins the controller's allowed range to one and creates its first capture before counting allocations. These changes preserve the behavior under test rather than loosen its expected result.

All review-fix artifacts are under `captures/ter-s7c6/review-fix/` in this checkout. The source/test/build-input identity after cleanup is `0c0785ceb18618f1ba836b1f18d9d9956e3efb86`. Maximum-allocation timing, final checks and inspected captures are recorded in the stage's final evidence note.

### Final checks and source identity

After `tools/cleanup.sh` processed the 14 touched C# files, the complete validation command was:

```bash
nix develop --command bash -c 'bash tools/gpu-lock.sh captures/ter-s7c6/review-fix/logs/final-validation.log bash tools/test-all.sh -c Checked,Release -j 1 -o captures/ter-s7c6/review-fix/final-tests'
```

It finished successfully on 2026-11-05, from 13:49:07 to 14:01:41 EST. Both solution builds had zero warnings and errors. The eight test modules reported:

| Module | Checked passed / skipped | Release passed / skipped |
|---|---:|---:|
| Core | 1207 / 0 | 1175 / 0 |
| Simulation | 2001 / 0 | 2020 / 0 |
| Physics | 1492 / 8 | 1493 / 8 |
| Rendering | 2315 / 10 | 2314 / 10 |
| Host | 2024 / 6 | 2024 / 6 |
| Unions | 59 / 0 | 59 / 0 |
| Style | 276 / 0 | 276 / 0 |
| Slop | 597 / 0 | 597 / 0 |
| Total | 9971 / 24 | 9958 / 24 |

The three new opt-in timing cases skip in normal suites and passed in both explicit measurement rounds. `nix develop --command dotnet build benchmarks/Tertius.Benchmarks -c Release` also passed with zero warnings and errors. The AMD source-pin check passed: the GLSL equals SDK v1.1.4 apart from the accepted declarations and `pin.diff` fix. No source, test or build input changed after these checks. Demo, Rendering tests, Host tests and benchmark output stamps all match `0c0785ceb18618f1ba836b1f18d9d9956e3efb86` and this worktree's git directory.

### Maximum-allocation FSR measurement

The foreground evidence command held `tools/gpu-lock.sh` from 14:02:38 to 14:05:58 EST. It ran source-pin verification, advancing real-demo captures, two isolated FSR timing rounds and a real-window resize probe, sequentially. The timing runs used `TERTIUS_FSR_TIMING=1`, Release, Apple M1 Max/MoltenVK and `tools/gpu-clock.sh`. Each active size used 60 warm-up batches and 150 timed batches, with eight FSR executions per batch and two batches queued. The 16 descriptor slots match those executions in flight. Timestamps enclose the batch; the reported per-frame cost divides the batch span by eight. There is no swapchain or presentation wait in this isolated measurement.

The maximum render allocation and output are both 1920 × 1080 in every row. The active rectangle is the pixels processed in that execution. At minimum controller scale 0.5, per-axis rounding to eight gives 960 × 536, not 960 × 540.

| Active pixels | Round | Median ms/frame | p10 / p90 ms/frame | Mean GPU MHz | Clock samples |
|---|---:|---:|---:|---:|---:|
| 960 × 536 | 1 | 1.710 | 1.697 / 1.723 | 1296.0 | 20 |
| 960 × 536 | 2 | 1.708 | 1.696 / 1.722 | 1296.0 | 20 |
| 1280 × 720 | 1 | 1.991 | 1.971 / 2.008 | 1295.9 | 23 |
| 1280 × 720 | 2 | 1.991 | 1.969 / 2.008 | 1296.0 | 22 |
| 1920 × 1080 | 1 | 2.905 | 2.893 / 2.917 | 1296.0 | 33 |
| 1920 × 1080 | 2 | 2.900 | 2.890 / 2.913 | 1296.0 | 33 |

Clock p10 and p90 are 1296.0 MHz for all six closed timed spans. These are controlled-clock, isolated FSR costs at the actual maximum allocation, not a window frame-rate claim or a tracer quality result. The patterned HDR input, reversed depth, zero motion and reactive-mask generation exercise the existing measurement procedure. Validation is disabled for timing, not silently claimed: `TheLargestWindowAllocationHandlesTheControllersActiveSizesUnderValidation` separately exercises full → 720p → minimum → full with finite output and zero validation messages, in both complete test configurations. Reflection motion, exposure transitions and real scene motion have separate behavioral checks.

The raw samples are in `logs/fsr-maximum-1.log` and `logs/fsr-maximum-2.log`. `analyze-evidence.py` extracts each timed span's clock, rather than treating all three spans as one. `maximum-allocation-timings.json` records all six results and the tested code identity. Its SHA-256 is `d27061af18ba31920bd4b7ebee58e4e8ff22070f48f491018f4980e2327ce174`. The run script's SHA-256 is `da7b4a1c2858868f78ee4f7128825382a85c2ff4aca703cf69f2520ad1e90695`.

### Advancing captures and live swapchain evidence

The final-code captures use `scripts/temporal-upscale-validation.json` (SHA-256 `d760a05ff997cc573f92a59083ccbaaacbe08e0422d288284961b00aa37c73fe`), the real demo, headless output 1920 × 1080, two frames in flight and default settings apart from `--upscaling off` or `--upscaling fsr3`. Native active size is 1920 × 1080; FSR active size is 1280 × 720. The actual CLI produced 67 PNGs per mode under `demo-off/` and `demo-fsr3/`. Their clocks varied during capture: native mean 1136.0 MHz, p10/p90 807.7/1296.0 MHz, 107 samples; FSR mean 1050.0 MHz, p10/p90 750.8/1296.0 MHz, 91 samples. These clocks are provenance only. The CLI frame-span summaries are not a controlled performance comparison.

The explicit `FsrScriptEvidence` test also passed at this identity, with validation on. Its `history-proof/off/history.json` and `history-proof/fsr3/history.json` each record 163 drawn frames, draw index 56 at the endpoint, reset false, 32 warm-up frames and zero validation messages. Both modes produced 67 images. Warm-up and films advance real draws; a frozen snapshot time is not counted as history warm-up.

Three final CLI images were inspected and shown:

- `demo-off/deck-still.png`: native deck with sharp structural edges and fine surface grain.
- `demo-fsr3/deck-still.png`: the same deck reconstructed from 1280 × 720, with softer edges and residual cloudy lighting variation.
- `demo-fsr3/glass-motion-000023.png`: moving glass with pronounced blue mottling; this one frame does not establish artifact-free motion.

The final real-window probe completed 60 frames at each framebuffer size 640 × 360 → 7 × 5 → 1 × 1 → 640 × 360. Active sizes were 632 × 352 → 8 × 8 → 8 × 8 → 632 × 352. Draw counts reached 60/120/180/240 and frame-numbered timing reads reached 58/118/178/238. Every endpoint had draw index 59 and reset false. Vulkan validation reported zero messages, including disposal. `logs/live-window-final.log` and `window-probe/` preserve this smoke test. The probe source SHA-256 is `519004fa6fa0a3c9c515816be14c0eb8139847cf32d2cb2254abf87f7625fef5`. An initial evidence-only probe used event-driven GLFW polling and waited for input; that process was stopped and its exit awaited before rerunning with continuous event polling. It was not an engine failure.

All required in-scope checks are complete. Native tracer noise, engine-wide metric completeness and Linux runtime are not claimed solved. Coverage, mutation testing, safe import into standalone noise research, integration and ticket closure remain the parent's work. No other checkout or dirty prototype was changed. The ticket remains in progress until integration; this stage commits its completed review fixes without closing it.

### Commit-hook assessment

The normal hook accepted the exact staged code identity and reused its successful complete Checked test record; no check was bypassed. It reported one advisory: the reflected-motion scenario is 67 measured lines, above the 60-line guide. The test retains its explicit mirror/panel setup, camera and object movement, scale transition and image/history assertions in one place, following `tests/STYLE.md`'s self-contained setup rule. Shared mirror construction and pixel lookup already use `ReflectionTesting`; extracting the remaining one-use scenario steps would only hide the behavior that this regression must explain. This is a scoped test-readability exception, not a production complexity finding.

The hook's v3 metrics reported erosion 0.1918, verbosity 0.0278, pooled TCC 7582/8553 = 0.8865 and depth 10733/4173 = 2.5720. Cohesion included 194 types, excluded 775 as not applicable and retained 42 as incomplete; source analysis had 3649 binding errors and 243 unresolved state-access locations. These values are partial design signals, not complete engine-wide evidence or proof of the TCC goal. The source/caller assessment remains the evidence for the two design fixes: graph stage metadata removes the controller's independent label parser, and one common configuration removes repeated variant rules. Diagnostic renaming and later common specialization changes no longer need those old coordinated edits. The hook log and metric note are retained under the stage's ignored artifact directory.

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

## Parent-commissioned test hardening, 2026-11-05

This is one hardening pass after the completed implementation/review/fix workflow, not another production cycle. The FSR scope is the immutable diff `34c28466e641a864fd6a915c029e08f07b3b9ccc..96db37df965c7b35dae499a2f34410d2c3b35011`, measured on the combined source with prerequisite main `7d9f6e6739b8fcd1b96fcd307c6434e3d4a8ebaf`. Imported Entity identity and portable-screen-walk changes are prerequisites, not new FSR mutation scope. No production source changed in this pass. The initially staged prerequisite diff was compared byte-for-byte with its saved patch before the final commit. The existing prerequisite ARCHITECTURE.md update stays intact; test-only hardening needs no invented engine design change.

Artifact root: `/private/tmp/tertius-hardening-20261105/ter-s7c6`. The final code/test/build-input identity is `ba5127acef21489cc12c01efc090351788f7c0ef`, replacing the initial combined identity `7734caec2c2bf91431eb30766ab17e94b8d22a2b`. The immutable final merge commit, both parents, clean state, sixteen matching test records and allocation release are recorded in `final-manifest.json` under that root. That manifest is the exact final-commit attestation: embedding a commit own hash in its committed ticket would change that hash. The completed normal commit retains the original FSR tip as first parent and the prepared prerequisite as second parent; it does not merge or write main.

### Final full-suite validation

The final code is frozen at the identity above. `nix develop --command bash tools/test-all.sh -c Checked,Release -j 1 -o <root>/final-tests` ran once after focused iteration and scoped cleanup, through `tools/gpu-lock.sh`. Both complete solution builds include the benchmark project, with zero warnings and zero errors. An explicit Release benchmark build also passed with zero warnings and zero errors. All sixteen full-run records and assembly stamps match that identity; filtered mutation runs are not substituted for these records.

| Module | Checked passed / skipped / failed | Release passed / skipped / failed |
|---|---:|---:|
| Core | 1207 / 0 / 0 | 1175 / 0 / 0 |
| Simulation | 2001 / 0 / 0 | 2020 / 0 / 0 |
| Physics | 1492 / 8 / 0 | 1493 / 8 / 0 |
| Rendering | 2390 / 10 / 0 | 2389 / 10 / 0 |
| Host | 2025 / 6 / 0 | 2025 / 6 / 0 |
| Unions | 59 / 0 / 0 | 59 / 0 / 0 |
| Style | 276 / 0 / 0 | 276 / 0 / 0 |
| Slop | 597 / 0 / 0 | 597 / 0 / 0 |

Checked: 10047 passed, 24 skipped, 0 failed (10071 total); Release: 10034 passed, 24 skipped, 0 failed (10058 total).

Logs: `final-tests/{Checked,Release}/{build,Core,Simulation,Physics,Rendering,Host,Unions,Style,Slop}.log`, `logs/final-validation.log`, and `logs/benchmark-release-build.log`. The declared skips remain skips, not passing evidence or performance results. Affected-project final coverage runs separately passed: Checked Rendering 2390/10, Release Rendering 2389/10, and Host 2025/6 in each configuration (passed/skipped, zero failures).

Scoped Roslyn/CSharpier cleanup and the final CSharpier check passed on the seven owned C# files. Code whitespace checks passed. The all-path staged whitespace check reports only the required unchanged ter-570o snapshot blank line at EOF; its mandated SHA256 is preserved rather than silently editing that parent-owned file. All 169 SPIR-V files found under the Checked/Release engine and rendering-test output trees passed `spirv-val --target-env vulkan1.3`; this is module validation, not a Linux runtime run. The unchanged AMD SDK pin check passed. Logs: `logs/cleanup.log` and `logs/shader-format-checks.log`.

### Changed-line and branch coverage

Coverage uses Microsoft CodeCoverage Cobertura from only Host and Rendering, in Checked and Release. `coverage/scope-report.py` maps the original FSR added lines onto the combined source, excluding prerequisite-only lines. The retained standard changed-lines report is a broader original-base comparison and is not used to relabel imported work as FSR. Before: 472/483 instrumented changed C# lines and 158/194 branch outcomes. After: 481/483 lines and 183/194 branch outcomes. The table shows per-file evidence, not just those pooled counts. A `0/0` entry means no instrumented changed lines or branches, not a measured zero-percent result.

| Affected C# file | Before changed lines | After changed lines | Before branches | After branches |
|---|---:|---:|---:|---:|
| `Tertius.Host/ScriptCommand.Options.cs` | 4/4 | 4/4 | 4/4 | 4/4 |
| `Tertius.Host/ScriptCommand.cs` | 19/19 | 19/19 | 14/14 | 14/14 |
| `Tertius.Rendering/Frame/DescriptorLayouts.cs` | 2/2 | 2/2 | 0/0 | 0/0 |
| `Tertius.Rendering/Frame/FrameGraph.cs` | 22/22 | 22/22 | 6/6 | 6/6 |
| `Tertius.Rendering/Frame/FrameHistory.cs` | 6/6 | 6/6 | 4/4 | 4/4 |
| `Tertius.Rendering/Frame/FrameResources.cs` | 7/7 | 7/7 | 0/0 | 0/0 |
| `Tertius.Rendering/Frame/GpuTimer.cs` | 3/3 | 3/3 | 0/0 | 0/0 |
| `Tertius.Rendering/Frame/RenderTargets.cs` | 20/20 | 20/20 | 13/16 | 15/16 |
| `Tertius.Rendering/Frame/TimestampClock.cs` | 1/1 | 1/1 | 0/0 | 0/0 |
| `Tertius.Rendering/Frame/UpscaleTargets.cs` | 39/39 | 39/39 | 16/16 | 16/16 |
| `Tertius.Rendering/GpuTimings.cs` | 0/0 | 0/0 | 0/0 | 0/0 |
| `Tertius.Rendering/Output/IOutputTarget.cs` | 15/15 | 15/15 | 0/0 | 0/0 |
| `Tertius.Rendering/Passes/BloomPass.cs` | 1/1 | 1/1 | 0/0 | 0/0 |
| `Tertius.Rendering/Passes/ExposurePass.cs` | 19/19 | 19/19 | 2/2 | 2/2 |
| `Tertius.Rendering/Passes/GeometryPass.cs` | 3/3 | 3/3 | 0/0 | 0/0 |
| `Tertius.Rendering/Passes/ImpostorPass.cs` | 3/3 | 3/3 | 0/0 | 0/0 |
| `Tertius.Rendering/Passes/OutlinePass.cs` | 1/1 | 1/1 | 0/0 | 0/0 |
| `Tertius.Rendering/Passes/ToneMapPass.cs` | 5/5 | 5/5 | 0/0 | 0/0 |
| `Tertius.Rendering/Passes/TransparentPass.cs` | 23/23 | 23/23 | 2/2 | 2/2 |
| `Tertius.Rendering/Passes/UpscalePass.cs` | 59/60 | 59/60 | 1/2 | 1/2 |
| `Tertius.Rendering/RendererOptions.cs` | 1/1 | 1/1 | 0/0 | 0/0 |
| `Tertius.Rendering/Scene/GpuTypes.cs` | 0/0 | 0/0 | 0/0 | 0/0 |
| `Tertius.Rendering/ShaderConstants.cs` | 2/2 | 2/2 | 0/0 | 0/0 |
| `Tertius.Rendering/Upscaling/Fsr3Bindings.cs` | 9/9 | 9/9 | 0/0 | 0/0 |
| `Tertius.Rendering/Upscaling/Fsr3Constants.cs` | 0/0 | 0/0 | 0/0 | 0/0 |
| `Tertius.Rendering/Upscaling/Fsr3Upscaler.cs` | 24/24 | 24/24 | 4/4 | 4/4 |
| `Tertius.Rendering/Upscaling/RenderScaleController.cs` | 58/62 | 62/62 | 43/58 | 49/58 |
| `Tertius.Rendering/UpscalingSetting.cs` | 43/49 | 48/49 | 11/22 | 22/22 |
| `Tertius.Rendering/VulkanRenderer.cs` | 83/83 | 83/83 | 38/44 | 44/44 |

Every remaining uncovered outcome was inspected:

- `UpscalingSetting.cs:27`: the absent scale source fallback in Largest cannot follow successful settings validation. The public missing-source refusal is now tested and kills C31. No private-state corruption was added to reach the fallback.
- `UpscalePass.cs:58-59`: an upscale pass without its owned upscale targets is unreachable through valid renderer construction. The guard remains a resource-ownership safety path; its throw and one branch are not claimed covered.
- `RenderTargets.cs:205`: the internal same-size return is precluded by the renderer resize checks. Both one-axis changes now run through requested-resize and acquired-image-resize paths; the remaining internal no-op branch is not forced through an artificial caller.
- `RenderScaleController.cs:32,61,89,93,94,99,100`: the nine remaining branch outcomes are failed postconditions/invariants. Valid workloads exercise the checks, but the tests do not corrupt internal state merely to fail a contract. All changed controller executable lines, including invalid/incomplete/zero-cost measurement rejection, are covered.
- `GpuTimings.cs`, `Scene/GpuTypes.cs` and `Fsr3Constants.cs`: changed declarations/layout data have no instrumented executable added lines. They are not silently counted as covered code. Existing host golden/layout tests, real renderer tests and module validation supply narrower evidence.

Reports and exact uncovered source/branch locations: `coverage/before.json`, `coverage/final.json`, `coverage/final-checked.json`, and `coverage-final/{Checked,Release}/{Rendering,Host}/*.cobertura.xml`. CPU coverage does not measure GLSL execution. Genuine GPU assertions killed the shader faults listed below; there is no invented shader line/branch coverage percentage.

### Deliberate mutation campaign

The required official classifier ran as `nix develop --command python3 -I tools/stryker/changed.py main <root>/official` and reported no changed C# lines in Core, Simulation or Physics. Its output is retained in `official/classifier.log`; no unrelated Stryker campaign was launched, and the imported prerequisite campaigns were not repeated.

One deliberate list, `mutations/list.json`, ran through `nix develop --command python3 -I tools/mutate.py <list.json> <out>` under the normal GPU lock. Initial faults established missing assertions before regressions were added. Only affected faults were rerun with `--only`. No mutant was edited/built/tested/restored by hand. Raw runs are preserved in `mutations/{initial,corrected-faults,regressions,residual}`; each contains results and bounded per-fault build/test logs. `mutations/final.json` combines the evidence with exactly one final outcome per valid fault and keeps every earlier attempt.

Final valid definitions: 45. Real assertion kills: 42. Raw survivors: 3, disposed as two equivalents and one explicit judgment below. Final timeouts, no-coverage reports, no-tests results and build failures: zero. The initial C06, C17 and C20 definitions failed to compile (unused local, unused private helper and nonexistent enum member); each was corrected and rerun, never scored as a kill. This is a deliberate sample of changed behavior families, not exhaustive mutation of every changed line or imported SDK instruction.

| ID | Represented fault | Final outcome | Assertion or disposition |
|---|---|---|---|
| C01 | accept a repeated finished-frame measurement | killed | `RenderScaleTests.ARepeatedMeasurementOfAFinishedFrameDoesNotMoveTheScaleAgain` |
| C02 | accept a stale frame after its slot was overwritten | killed | `RenderScaleTests.AStaleMeasurementCannotUseTheScaleOfANewerFrameInTheSameSlot` |
| C03 | attribute delayed cost to the current scale | killed | `RenderScaleTests.DelayingTheSamePerAreaLoadDoesNotChangeTheScaleResponse` |
| C04 | include presentation wait in controller cost | killed | `RenderScaleTests.ASteadyLoadSettlesAtTheGpuTimeTarget`; `RenderScaleTests.MeasurementsUseTheScaleOfTheirFrameRatherThanTheCurrentScale`; `RenderScaleTests.ALoadBeyondTheRangeStopsAtTheMinimumScale` |
| C05 | accept nonfinite or negative GPU timings | killed | `RenderScaleTests.AnInvalidGpuCostDoesNotInfluenceThisOrLaterMeasurements(milliseconds: NaN)`; `RenderScaleTests.AnInvalidGpuCostDoesNotInfluenceThisOrLaterMeasurements(milliseconds: Infinity)` |
| C06 | accept timings missing the output boundary | killed | `RenderScaleTests.AnIncompletePassSpanDoesNotInfluenceLaterMeasurements` |
| C07 | derive scaling cost from diagnostic names | killed | `RenderScaleTests.DiagnosticPassLabelsDoNotChangeTheMeasuredScale` |
| C08 | do not round the active axis to eight | killed | `RenderScaleTests.EachActiveAxisRoundsDownToEightPixels(width: 1919, height: 1079, scale: 0.666666687, expectedWidth: 1272, expectedHeight: 712)` |
| C09 | scale native output instead of preserving exact dimensions | killed | `RenderScaleTests.NoUpscalingPreservesTheExactOutputSizeWithoutRounding` |
| C10 | allow zero jitter phases for tiny outputs | killed | `Fsr3HostTests.AnOutputNarrowerThanTheMinimumActiveWidthStillHasAJitterPhase(outputWidth: 2)`; `Fsr3HostTests.AnOutputNarrowerThanTheMinimumActiveWidthStillHasAJitterPhase(outputWidth: 1)` |
| C11 | remove small-output internal FSR allocation padding | killed | `UpscalingRenderTests.AWindowCanShrinkBelowFsrsAllocationMinimumAndRestore(width: 7, height: 5)`; `UpscalingRenderTests.AWindowCanShrinkBelowFsrsAllocationMinimumAndRestore(width: 1, height: 1)`; `UpscalingRenderTests.AWindowCanShrinkBelowFsrsAllocationMinimumAndRestore(width: 40, height: 32)` |
| C12 | remove optional RCAS dispatch | killed | `Fsr3UpscalerTests.SharpeningChangesAnEdgeWithoutChangingTheInputOrTheRenderSize` |
| C13 | jitter native rendering | killed | `UpscalingRenderTests.NativeRenderingKeepsOddOutputDimensionsAndNoJitter` |
| C14 | remove main-view material mip bias | killed | `UpscalingRenderTests.MainViewMipBiasPreservesTextureDetailAtHalfResolution` |
| C15 | drop main-view sampling from cutout geometry | killed | `UpscalingRenderTests.FullyOpaqueCutoutPixelsUseTheSameMainViewMipBiasAsSolidPixels` |
| C16 | adapt next-frame exposure before FSR reads this frame | killed | `UpscalingRenderTests.FsrReadsThisFramesExposureRatioBeforeAdaptationWritesTheNextFramesExposure` |
| C17 | omit opaque-only color copy for the reactive mask | killed | `UpscalingRenderTests.TheReactiveMaskMarksGlassThatChangedTheOpaqueColorButNotTheSky` |
| C18 | discard FSR history on every frame | killed | `UpscalingRenderTests.ContinuingTheSameJitterPhaseUsesEarlierSubpixelSamples` |
| C19 | postprocess unscaled color rather than FSR output | killed | `UpscalingRenderTests.OutputToneMappingReadsTheFsrReconstructionAtEveryOutputPixel` |
| C20 | lose authoritative graph stages on real timestamp records | killed | `GpuTimingTests.TwoPassesWithTheSameLabelKeepTheirOwnMeasuredStages` |
| C21 | omit the output submission | killed | `UpscalingRenderTests.RepeatingAStillRestartsTheJitterWithoutStartingAController`; `UpscalingRenderTests.FullyOpaqueCutoutPixelsUseTheSameMainViewMipBiasAsSolidPixels`; `UpscalingRenderTests.JitterDoesNotAppearInTheMotionOfAStillBox` |
| C22 | ignore explicit CLI render scale | killed | `ScriptCommandTests.AHeadlessRenderScaleOverridesTheFixedFsrScale(text: "0.5", scale: 0.5)`; `ScriptCommandTests.AHeadlessRenderScaleOverridesTheFixedFsrScale(text: "1", scale: 1)` |
| C23 | accept a CLI scale below the supported range | killed | `ScriptCommandTests.AHeadlessRenderScaleOutsideTheFiniteRangeIsRefused(text: "0.49")` |
| C24 | enable FSR for explicit native CLI rendering | killed | `ScriptCommandOptionsTests.AHeadlessScriptRunCarriesItsSizeAndEveryOverrideOfTheRenderer`; `ScriptCommandTests.TheUpscalingOptionCarriesTheChoiceThatItNames(name: "off")` |
| C25 | restore acceptance of the removed SMAA option | killed | `ScriptCommandOptionsTests.TheRemovedSmaaOptionIsRefusedRatherThanSilentlyIgnored` |
| C26 | accept a controller target with zero frame budget or invalid bounds | killed | `RenderScaleTests.AnInvalidFrameBudgetOrScaleRangeIsRefused(milliseconds: 16, minimum: 0.5, maximum: NaN)`; `RenderScaleTests.AnInvalidFrameBudgetOrScaleRangeIsRefused(milliseconds: 16, minimum: 1.00999999, maximum: 1)`; `RenderScaleTests.AnInvalidFrameBudgetOrScaleRangeIsRefused(milliseconds: 16, minimum: 0.5, maximum: 1.00999999)` |
| C27 | accept an invalid negative frame identity | killed | `RenderScaleTests.ANegativeFrameNumberIsRejectedBeforeRecordingItsScale` |
| C28 | accept a render scale below one half | killed | `UpscalingRenderTests.AHeadlessRendererRefusesAnInvalidFixedScale(scale: 0.49000001)` |
| C29 | accept a zero frame-time budget | killed | `RenderScaleTests.AnInvalidFrameBudgetOrScaleRangeIsRefused(milliseconds: 0, minimum: 0.5, maximum: 1)` |
| C30 | accept sharpening above its supported range | killed | `UpscalingRenderTests.AHeadlessRendererRefusesUnsupportedSharpening(sharpness: 1.5)` |
| C31 | accept FSR without a render-scale source | killed | `UpscalingRenderTests.FsrWithoutAScaleSourceIsRefusedBeforeCreatingARenderer` |
| C32 | let a frame with no scaling work contaminate the cost model | killed | `RenderScaleTests.AFrameWithoutScalingWorkDoesNotContaminateTheCostModel` |
| C33 | ignore an output resize that changes only one axis | killed | `UpscalingRenderTests.AnAcquiredImageChangingOnlyOneAxisRecreatesTargetsAndResetsHistory(width: 80, height: 96, renderWidth: 48, renderHeight: 64)`; `UpscalingRenderTests.AnAcquiredImageChangingOnlyOneAxisRecreatesTargetsAndResetsHistory(width: 160, height: 48, renderWidth: 104, renderHeight: 32)` |
| C34 | keep render allocations on a one-axis output resize | killed | `UpscalingRenderTests.AWindowCanShrinkBelowFsrsAllocationMinimumAndRestore(width: 160, height: 48)`; `UpscalingRenderTests.AWindowCanShrinkBelowFsrsAllocationMinimumAndRestore(width: 80, height: 96)` |
| S01 | omit current jitter in surface reprojection | killed | `UpscalingRenderTests.SurfaceHistoryFindsTheSameWorldPointWhileTheActiveSizeChanges(grows: False)`; `UpscalingRenderTests.SurfaceHistoryFindsTheSameWorldPointWhileTheActiveSizeChanges(grows: True)` |
| S02 | normalize previous jitter with current dimensions | killed | `UpscalingRenderTests.SurfaceHistoryFindsTheSameWorldPointWhileTheActiveSizeChanges(grows: False)` |
| S03 | omit previous jitter in reflected-hit reprojection | judgment | The existing real reflected-hit shrinking/growing tests retain the moving reflection and history but do not distinguish omission of the previous subpixel jitter. No independent reflected subpixel reference or new noise/brightness tolerance is introduced in this test-only pass; that precision is an explicit remaining test limit, not equivalence or shader coverage proof. |
| S04 | use unjittered shading rays for jittered geometry | killed | `UpscalingRenderTests.FullscreenShadingUsesTheViewRayThroughTheJitteredSurfacePixel` |
| S05 | ignore material mip bias in the shader | killed | `UpscalingRenderTests.MainViewMipBiasPreservesTextureDetailAtHalfResolution` |
| S06 | freeze the temporal shadow tap group | killed | `UpscalingRenderTests.EveryPixelUsesEveryTemporalShadowGroupOverFourAdvancingDraws` |
| S07 | black out locally lit surfaces under temporal shadow filtering | killed | `LocalShadowRenderTests.TheShadowsEdgeIsSoftOverTheFilter(temporal: True)` |
| S08 | black out cascade-filtered light under FSR | killed | `UpscalingRenderTests.UnoccludedSunlightStaysLitThroughEveryTemporalShadowGroup` |
| S09 | meter allocated pixels outside the active rectangle | equivalent | GeometryPass.BeginRange clears G-buffer distance to zero over the entire largest allocation before active-rectangle drawing; the extra allocated-pixel meter reads therefore fail its positive-distance test and add no histogram samples. |
| S10 | keep inactive depth after active-size shrink | equivalent | The full-allocation G-buffer distance clear already makes every inactive distance zero, so expanding the finest-depth source guard still writes the same zero inverse depth outside the active rectangle. |
| S11 | replace sharpened output with black | killed | `Fsr3UpscalerTests.SharpeningWritesPositiveFiniteColorIncludingTheOutputBorders(width: 96, height: 72)`; `Fsr3UpscalerTests.SharpeningWritesPositiveFiniteColorIncludingTheOutputBorders(width: 95, height: 73)` |

### What the regressions establish and what they do not

The added tests exercise active versus allocated versus output dimensions; tiny output and jitter cases; one-axis output acquisition; physical surface reprojection while active dimensions shrink and grow; continued FSR history and output composition; native zero-jitter output and FSR-only readback refusal; exposure ordering; main-view mip sampling; all four shadow groups, cascade-lit surfaces and a partially lit tile-shadow edge; frame-numbered controller costs and invalid inputs; real stage metadata independent of duplicate diagnostic labels; CLI scale semantics and rejection of the removed SMAA flag. Existing real reflected-hit growing/shrinking tests remain intact. The subpixel reflected-hit precision limit is S03 above, not a hidden equivalence or new noise/brightness acceptance rule.

Several initial test expectations were too strong or tested the wrong setup: the unsharpened half-float constant tolerance did not describe RCAS approximation, four-texel checker contrast was not near unity at that projection, missing headless dimensions could satisfy unrelated argument exceptions, caster-free sunlight skipped cascade sampling, and requested resize did not exercise acquired-image resize. The retained baseline/fault runs exposed these gaps. Tests now check finite positive RCAS border writes, checker variation beyond 8-bit rounding, valid-size invalid-setting refusals, real casters, acquired-image dimension changes, and a partially lit temporal shadow edge. The native full-filter width assertion is preserved; no temporal-filter width or production quality tolerance was invented. No engine fix or tuning was needed.

Pinned SDK algorithms and ABI declarations were not rewritten or exhaustively mutated. Deleted SMAA code, shader files and table assets have no executable lines left to instrument; C25 tests refusal of its removed CLI entry, and native-output tests protect the surviving NoUpscaling path. Shader specialization, forward/impostor/hair/outline variants and resource lifetime paths retain their existing tests and Vulkan module validation; this campaign does not claim every specialization or exceptional resource failure was fault-injected.

The real two-submission path is retained and C21 proves the output submission is needed. C04 and C07 protect controller exclusion of output stages and independence from labels. Ordinary offscreen tests have no acquired-image wait semaphore, so they do not prove a deliberately delayed swapchain-acquire wait gates only the second submission. The original real-window review/fix evidence is preserved, not replaced by a false offscreen timing claim.

### Preserved notes and stage limits

Root-note additions were merged semantically from `/private/tmp/tertius-integration-20261105/root-notes/ter-s7c6.md` (SHA256 `e5257ee461a72a6be16d74a8854e652711d73d17584a04a1e460e5a6b13a33b2`) against its `.base.md` snapshot (`9b3c5f84a06bc6916d7e55cf8f2b3d7303527b9135a4a5ba8c8ae766f84768d6`), without dropping the branch evidence. The preserved original launch block remains verbatim. `.tickets/ter-570o.md` is the parent snapshot unchanged (SHA256 `1597561ad3a0394a3fd814c60dd45de8d590ca85641de68ba243d290242e0d8f`), still open and dependent on this ticket. `checkpoint/preservation.log` records these checks and the unchanged staged prerequisite diff.

No controlled FSR timing or capture campaign was repeated because production inputs did not change. Existing `captures/ter-s7c6/review-fix/` evidence remains in place. No standalone noise checkout or dirty prototype was touched. No new noise, brightness, ray, denoising or live-FPS acceptance is claimed. No soak/MTBF estimate was measured. Source type-metric evidence remains incomplete where reported by the normal hook; it is not proof of the design targets.

Linux runtime remains user-deferred in ter-cgif. The real-window dynamic-resolution controller video in ter-570o is parent-owned after integration; fixed headless scaling is not substituted for it. Neither is unfinished work for this hardening stage. Parent owns main integration, ticket closure, pushing and visual acceptance. This worker finishes only the already-prepared normal merge commit on the existing ticket branch, with normal hooks and no bypass, then releases its one heavy allocation after all owned commands have ended.
