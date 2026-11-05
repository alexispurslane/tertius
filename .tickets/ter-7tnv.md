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
