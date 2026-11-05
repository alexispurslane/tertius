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
