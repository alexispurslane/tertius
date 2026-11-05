---
id: ter-7tnv
status: in_progress
deps: [ter-gayt]
links: []
created: 2026-11-03T14:53:48Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, environment-map]
---
# The corrected map read in the tracer's reflection misses

Build ticket 8 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, section 6 ("Misses") and decision 35. Acceptance: section 14, item 8. It ports the corrected read from branch ter-s6zr-corrected-read at 570fdbbf8 (the closed ter-s6zr), with the misses that the user accepted on 2026-11-02 as the expected result.


## Bound-checkout implementation, 2026-11-05

The worker verified this checkout at `/Users/alexispurslane/.t3/worktrees/tertius/ter-7tnv-corrected-environment-misses`, branch `ter-7tnv-corrected-environment-misses`, clean original HEAD `34c28466e641a864fd6a915c029e08f07b3b9ccc`, then ran `tk start ter-7tnv`. No agents, workflows, checkout changes, merges, publication or ticket closure were performed. The parent retains coverage, mutation testing, integration and closure.

The existing Design A and the accepted S + Q2 implementation settle the scope. Reflection misses and all closed-form and forward specular readers use the receiving point, geometric normal, map identity, roughness and footprint. Diffuse misses and the hemisphere path remain unchanged. The accepted search was designed for the same local environment cubes and M1 Max renderer, with no denoiser dependency. Its actual previous full-screen ambient cost was about +2.7 ms, not the old 0.2–0.6 ms estimate. The current tracer has fewer searches; this port adds no further algorithm or tuning.

Red evidence: on the supplied integrated base, the mirror and glass stripe regressions each missed by 0.4596871 rad against a 0.0625-rad bound. The rough-opening regression met the wall at y = -0.51041454 m instead of the opening at y = 2 m. Both exact open-sky controls passed. The accepted search and CPU twin then passed all 44 search/render cases. Additional checks cover off-screen reflection misses, unchanged diffuse misses and real advancing warm-up/history.

The first full Checked run exposed the same two fixture expectations already changed on accepted branch `570fdbbf8`: the UNORM/SRGB SMAA comparison must turn the environment map off on both renderers, and the airlock floor's corrected reflection is about (1, 2, 5), not (2, 4, 8), with the accepted summed-channel bound of 6. These are ports of settled fixture changes, not new lighting policy. The original rendering binary passed the unchanged SMAA case in an isolated copy of the worker's own test output. The off-screen stripe regression was also given the complete dark wall behind its bright stripe, rather than asking a sky/stripe silhouette to represent a wall.

The scoped report is `docs/research/temporal-renderer/corrected-environment-misses.md`. Seven rotated rounds over deck and floor completed, with base, byte-copy control and branch in each round. All 42 timed spans ran at 1296 MHz, and all 28 within-window clock comparisons remained within 3%. The median within-round frame increase is 2.762 ms on deck and 2.175 ms on floor. Byte-copy control differences are +0.005 ms and −0.004 ms. Three interleaved base/branch/base no-counter Metal traces confirm the reflection and transparent pass increases. This exceeds the old corrected-read allowance, without changing the accepted search or ray/history policy; budget integration remains with the parent.

The terminal, floor and pbr-spheres scripts capture matching simulation states and unchanged settings on both binaries. Draw indices advance through 0–33 with exactly one reset, not a frozen warm-up. All twelve observer PNGs and all twelve clock-recorded rerun PNGs match the corresponding CLI PNGs byte for byte. Three inspected vertical before/after pairs were sent to the user. Severe tracer grain remains, as do the accepted read's known limits. Screenshots are not performance evidence. Linux runtime stays user-deferred under `ter-cgif`.

The final code/test/build-input identity is `f7733d3c09d2b3e9768cd51f41b9001a7a9b26e4`. Capture and measurement identity `dc82b223727ec1599c05826c32cc95acbb41864b` differs only by the removal of an obsolete test comment during self-review. The production Rendering DLL is unchanged. `tools/cleanup.sh` ran on the touched C# files and again on the reviewed test file. The full Checked run on the preceding tree passed all 9,971 executed cases with 20 explicit skips.

Final validation on `f7733d3c09d2b3e9768cd51f41b9001a7a9b26e4` completed through Nix, one heavy command at a time, with GPU validation under the project GPU lock. Complete Checked: 9,971 passed, 20 skipped, zero failed, 9,991 total. Complete Release: 9,958 passed, 20 skipped, zero failed, 9,978 total. Both solution builds and the explicit Release benchmark compilation reported zero warnings and zero errors. All 83 built Release engine/test/reference SPIR-V modules passed Vulkan 1.3 validation. Logs are `captures/ter-7tnv/logs/readiness-all.log`, `captures/ter-7tnv/readiness-tests/` and `captures/ter-7tnv/logs/final-benchmark-and-spirv.log`. Every worker-owned command finished. No coverage or mutation run, merge, push, PR publication or ticket closure was performed. There is no unresolved design fork. This is implementation readiness; the parent retains hardening and integration.

### Original launch brief, preserved verbatim

```text
---
id: ter-7tnv
status: in_progress
deps: [ter-gayt]
links: []
created: 2026-11-03T14:53:48Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, environment-map]
---
# The corrected map read in the tracer's reflection misses

Build ticket 8 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, section 6 ("Misses") and decision 35. Acceptance: section 14, item 8. It ports the corrected read from branch ter-s6zr-corrected-read at 570fdbbf8 (the closed ter-s6zr), with the misses that the user accepted on 2026-11-02 as the expected result.


## Notes

**2026-11-05T15:17:51Z**

2026-11-05: tracer/Rule A merged, closed and pushed at 34c28466e641a864fd6a915c029e08f07b3b9ccc. Started workflow f097d0eb-7ed1-40bb-be47-f77f8bb645e7, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-7tnv-corrected-environment-misses, exact local base 34c28466e with startFromOrigin=false. Codex gpt-6.1-sol reasoningEffort=xhigh for build/review/fix. One of four heavy allocations. Settled corrected specular-read scope and accepted prior source only; diffuse misses unchanged. No new denoising or ray/history tuning. Parent owns hardening, integration and closure.
```

**2026-11-05T17:57:24Z**

Single review-fix stage completed in the bound checkout, starting from clean d42045d1ee44f4cc64a6e7f99736f523f8f2bb75. No checkout, branch, merge or publication operation was used. The original launch brief remains preserved verbatim and was compared byte for byte with /private/tmp/tertius-integration-20261105/launch-ter-7tnv.md.

Design finding and linked-design fulfillment gap: removed the parallel receiving-position arguments from lightPath, modelLight, directLight, lobesRadiance, surfaceRadiance and directRadiance, and updated every deferred and forward caller. The direct pass now uses materialAt and derives its direction toward the eye from ShadingInput.position. Position-only helpers remain focused, and offset tracer origins remain separate. Decision 22 settles this ownership change; no fork was reopened. No visible pre-fix lighting failure is claimed. The stale report sentence "One receiving-position field serves all readers; there is no parallel position store or new mode." is quoted and corrected in docs/research/temporal-renderer/corrected-environment-misses.md. ARCHITECTURE.md now documents the sole receiving-position input.

Fulfillment red-evidence gap: ran the unchanged final complete-wall off-screen tracer fixture with only reflection misses restored to the original four-argument directional environmentSpecular body from 34c28466e641a864fd6a915c029e08f07b3b9ccc. It failed for the intended image-position error, 0.3666097 rad versus the 0.0625-rad limit; seven other cases passed, including both exact open-sky controls and the diffuse-miss control. The temporary baseline was removed, screen_trace.glsl was verified identical to d42045d1, and all 47 search tests passed after the position fix. The baseline patch and red/green logs remain under captures/ter-7tnv/review-fix/.

Correctness assessment gap: this worker executed complete Checked and Release solution runs through Nix and the GPU lock, one test module at a time. Checked: 9,971 passed, 20 skipped, zero failed, 9,991 total. Release: 9,958 passed, 20 skipped, zero failed, 9,978 total. Release benchmark compilation passed with zero warnings and errors. All 83 Release shader modules passed Vulkan 1.3 validation. The final code/test/build-input identity and both Rendering output stamps are d7daa2215acd2055df7d75b581258be4678e5212; final Release Rendering DLL SHA-256 is bf54986295051c005ed52560a58464e6e8dc02f337a9b26b13da40338fa84065. The fix touches no C# file, and needs no new C# cleanup. A mistaken combined-class filter selected no tests (exit 5) before the corrected 47-case run; it is explicitly excluded from passing evidence. An initial missing patch executable was resolved through the bundled patch tool before the valid red run.

Matched visual evidence: preserved the reviewed Release DLL and reran the terminal, floor and PBR scripts with pre-fix and fixed binaries. Settings and simulation dumps match exactly. Actual draw indices match at 0–33 with one initial reset in all six scene runs; every observer PNG matches its ordinary CLI PNG byte for byte. Source identities, clocks, settings, history and exposure records are retained with the captures. All six primary before/after images were inspected and three fixed images were shown inline. All six matched PNG byte streams differ; no bit-exact before/after claim is made. The normal image-reference checks passed with no changed references or loosened assertions. Tracer grain and the bright terminal doorway remain; parallax correction is not claimed to fix them.

Remaining assessment limits are parent-owned, not hidden or rejected: Linux runtime remains deferred under ter-cgif; module validation is not Linux execution. No coverage or mutation testing was run. The previous +2.762 ms deck and +2.175 ms floor paired costs at 1296 MHz remain over the old allowance, and parent integration owns budget acceptance. They are measurements of the original implementation, not a new performance claim for this fix. No search algorithm, diffuse miss, hemisphere, exposure policy, denoiser, clipping, ray budget or history tuning changed. environment.glsl remains byte-identical to accepted 570fdbbf8. Parent retains hardening, merge and ticket closure. Every worker-owned command finished. No unresolved in-scope fork remains.

## User redirect: replace the distance search with box projection, 2026-11-05

The user explicitly selected box-projected corrected environment reads as soon as possible, accepting that the depth-search implementation will be superseded while remaining available in git history. The parent will not harden or merge the costly distance-search port as the chosen production solution. Completed workflow `f097d0eb-7ed1-40bb-be47-f77f8bb645e7` is terminal, its members are settled and cleanup is complete; do not restart its completed stages.

Preserve original build `d42045d1ee44f4cc64a6e7f99736f523f8f2bb75` and reviewed/fixed checkpoint `a541601534246e87dad7e4c74b51da3f5c693eee`, together with all source, tests and measurement history. Continue forward from the existing branch; no reset, deletion of history, checkout replacement or adoption of the rejected shorter search. The receiving-position ownership fix remains useful independently of the search algorithm. The old +2.762/+2.175 ms figures describe the old search, not a measured cost for box projection.

The selected replacement intersects the specular ray with a physical proxy box and samples the existing prefiltered environment map in the direction from its capture position to that intersection. This is a geometry approximation, not reconstruction of the individual objects captured in the map. No per-pixel distance-cube search belongs in the replacement. Keep stars in their original world direction, existing sky handling, diffuse environment reads, stochastic ray settings, hit-light composition and history/exposure rules unchanged. Coordinate with FSR through its existing render-size, motion and image interfaces; do not import either noise prototype or change those interfaces unnecessarily.

One real input choice remains before implementation: explicit stable boxes supplied by scene generation versus automatic boxes enclosing a map's objects. The renderer has map axes and instance bounding spheres, but no existing physical proxy-box payload. The parent recommends explicit boxes because distant or moving objects can make automatic boxes oversized or unstable; the user has not selected the source of the box dimensions yet. Both proposals keep directional fallback when no usable box exists. The parent presented both designs and attempted the question tool, which reported unavailable in Default mode. Do not silently choose a source of box dimensions or launch dependent implementation before that choice is settled.

The independent portable walk and Entity changes are integrated and pushed at main `7d9f6e6739b8fcd1b96fcd307c6434e3d4a8ebaf`, tested code identity `a60dc7a1e0dc37d804f799cce0e89a4d53ce5281`. Reconcile that prerequisite once in this preserved worktree when the replacement continuation starts, preserving both ticket histories. No new workflow or heavy command was started for the box replacement by this redirect note. The separate simplified single-diffuse-history prototype has its own active bounded capture allocation and must remain independent.

## Settled automatic-box source and replacement continuation, 2026-11-05

The user rejected explicit/authored boxes. The preceding paragraph that leaves the input choice open is superseded. Each existing environment map on the camera's semantic-frame chain gets one automatically fitted box from geometry already assigned to that map group, in that semantic frame's axes. Reuse the existing exclusive assignment, including off-chain descendants mapped to their nearest served ancestor. Do not add manually supplied room volumes, another hierarchy or another classifier. Captured ancestor geometry does not enlarge a child's box.

The replacement plan is `docs/plans/semantic-frame-box-projection.md`. It defines bounds, analytic lookup, fallback, approximation limits, preserved interfaces and required evidence. The selected model is settled, not a new design fork. Preserve both old implementation commits and the receiving-position ownership fix. Do not harden the discarded distance search or claim its old costs as box-projection measurements.

Continue in the preserved checkout from `a541601534246e87dad7e4c74b51da3f5c693eee`. The parent explicitly authorizes prerequisite main `7d9f6e6739b8fcd1b96fcd307c6434e3d4a8ebaf` to be merged into this ticket branch, preserving both histories and all parent-owned dirty notes. No main writes or unfinished FSR import are authorized. One heavy allocation is assigned; Nix, normal GPU lock, one foreground command, maximum four heavy agents globally, and a 5 GB free-disk guard apply. The standalone noise worker has released its previous allocation and remains independent.

Launch request: `ter-7tnv-automatic-semantic-boxes-20261105-v2`; reusable workflow version `869b0b07-3413-4b06-8707-a88a91bf6e91`, source/schema SHA-256 `ef99649844214c80f35d90851840fa568fce615016714356c3fd31b470f6fade`. The first request, suffix `v1`, was rejected before run admission because automatic delivery to the current parent conversation is not authorized by the workflow service. No worker started from that request. The corrected request omits optional automatic delivery; each member must notify the parent through T3 before its final result. No grant or service state was changed. The parent records the returned run ID in its root ticket. The workflow implements the replacement, performs one read-only review and one fix stage if needed; parent-owned hardening/integration follow readiness. The ticket remains in progress until merged.

## Automatic semantic-frame box implementation, 2026-11-05

Implemented the settled replacement directly in the preserved ticket checkout, without children or workflows. The one authorized prerequisite merge of `7d9f6e6739b8fcd1b96fcd307c6434e3d4a8ebaf` is included in the final merge commit. Original search implementation `d42045d1ee44f4cc64a6e7f99736f523f8f2bb75` and ownership fix `a541601534246e87dad7e4c74b51da3f5c693eee` stay in ancestry. Both parent-owned launch-note files were backed up before the prerequisite merge, and their full original prefixes remain unchanged. No main checkout change, unfinished FSR/noise import, ticket closure, push or PR publication is part of this stage.

Preparation fits one conservative sphere-derived enclosure per existing map group, in that semantic frame's axes, before main-view culling. It uses the complete prepared mesh extent, including skinning/morph deformation and interpolated scale, rather than culling-radius overrides or ownership `FrameBounds`. Existing off-chain/owner assignment and the 42-map served limit stay unchanged. Ancestors captured by a child never enlarge the child's enclosure. Bounds rebuild each preparation, without retained proxy state.

All corrected specular readers now share analytic box projection. The discarded per-pixel distance search and its cone/CPU twins are removed. `ShadingInput.position` remains authoritative. Empty/invalid/degenerate bounds, parallel axes, non-intersections and unusable lookup directions have explicit finite fallback. Fully open original sky directions remain directional through the existing level-zero map alpha; two exact sky controls showed why pure box projection needs that exception. Material roughness, diffuse reads, stars' original world direction, ray settings, hit-light composition, brightness/exposure/history policies and render-size/motion/image interfaces are unchanged. A proxy intersection never becomes a real reflected hit distance.

Red evidence and revised expectations are in `docs/research/temporal-renderer/automatic-box-projection.md`. Eight core behavior cases failed for the intended reason before implementation; a separate actual reader read about 0.0000219 radiance instead of 8. Search-specific physical-wall expectations were replaced with an independent fitted-box lookup prediction, not weakened thresholds: the stripe bound remains 0.0625 rad and the rough-opening bound remains 0.4 m in captured-wall coordinates.

| Required check | Final result |
|---|---|
| Focused enclosure cases, Checked | 37 passed, zero failed |
| Complete deformation class, Checked | 30 passed, including the final skinning/morph enclosure theory |
| Full Checked solution | 9,990 passed, 20 explicit skips, zero failed; 10,010 total |
| Full Release solution | 9,977 passed, 20 explicit skips, zero failed; 9,997 total |
| Checked/Release solution and explicit Release benchmark builds | All passed, zero warnings and errors |
| Scoped cleanup | Roslyn and CSharpier on the eight owned C# files; final pass changed no source |
| Vulkan 1.3 module validation | All 89 retained compiled Release engine/test/reference modules passed; not Linux execution |

Final source/test/build-input identity is `cda9d325a704aa321e2f921a7d3b60144373705d`, also used by the replacement captures and timings. The final Release Rendering DLL equals the preserved capture DLL, SHA-256 `694e7a5f9591ac811d0679690fbaf982e0381441f68b36d427335469c667e091`. Full logs are in `captures/ter-7tnv-box/logs/`; structured settings, hashes, scripts, history rows, checks and measured pairs are retained in `docs/research/temporal-renderer/automatic-box-projection/provenance.json`.

Three inspected warmed real-renderer screenshots were shown: terminal interior/reflection, floor/objects and nested cabin. They use unchanged 1280 × 720/SMAA/tracer/auto-exposure settings, 32 warm-up draws and one next-tick capture. Both variants' twelve CLI PNGs equal their observer counterparts byte for byte. Draw indices are 0–33 with exactly one reset; snapshot clocks advance by 1/60 s, and a separate renderer test confirms accumulated history length above one. The cabin has four maps and nonidentity ancestor axes. Original images remain untouched.

Three matched 120-frame pairs per deck/floor window completed, in alternating control/box order, against an integrated discarded-search control. All twelve timed spans ran at mean/p10/p90 1296 MHz, with every pair within the 3% clock limit. Median within-pair whole-frame GPU differences are −2.355 ms deck and −1.986 ms floor; median box run means are 11.721 and 10.854 ms. These are bounded GPU spans, not isolated lookup costs or CPU-fitting measurements, and not final budget acceptance. The earlier +2.762/+2.175 ms figures remain superseded-search history only.

Explicit limits: one enclosure cannot recover separate captured depths; ancestor radiance remains mixed into a child's locally corrected capture; the terminal group's enclosure is planet-scale (about 12,742 km across), while the cabin's is about 17.46 m per axis. Large/sparse groups can therefore yield weak room-scale correction. Severe existing tracer grain remains. No ownership texture, new classifier, hierarchy walk or authored room setting was added to hide these limits. Distance-cube producers still cost allocation/writes/downsampling; separate dependent ticket `t7cem-d5f9` tracks their removal.

The first timing batch stopped at the 5 GiB free-disk guard, and resumed only after space recovered. Parent and worker removed no other worker artifacts; this stage removed no output copies. Initial observer-only JSON/analyzer errors and the follow-up ticket's trailing blank line were corrected; no product test was waived. There is no unresolved design fork. Parent-owned coverage/mutations, Linux runtime (`ter-cgif`), final budget/quality acceptance, FSR reconciliation, integration, closure and push remain outside this stage. The stage result releases its one heavy allocation only after the normal commit hook and all owned commands finish.

The normal hook's only touched-code finding requested logical grouping of the preparation reset statements. Two blank lines now group environment faces and projection bounds together. The stage's own unpublished merge commit is amended after that fix; both original ticket commits and the authorized prerequisite remain its ancestors. Complete captures, focused/full Checked/Release checks, both solution builds, benchmark compilation, Vulkan module validation and all matched timing pairs were rerun on final identity `cda9d325a704aa321e2f921a7d3b60144373705d`. The twelve fresh PNGs equal the inspected images byte for byte. Earlier evidence is preserved under `captures/ter-7tnv-box-before-reset-grouping/`; nothing was deleted or cancelled.
