---
id: ter-rgg9
status: in_progress
deps: [ter-gayt]
links: []
created: 2026-11-04T20:36:02Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance, trace]
---
# The screen tracer's walk takes the hill-climb's portable changes

From ter-dzxf (docs/research/temporal-renderer/walk-hill-climb.md, 'Reproduction and handoff'): after ter-gayt merges, change walkScreen in screen_walk.glsl to the two-image combination bounds-predicate+loop-predicate+facing: the vector Boolean bounds test, the vector Boolean loop condition without short-circuit control flow, and the sign-only facing test (no normalize of the decoded normal, local to the facing test). Keep the original arithmetic order, the two-image depth layout (plan decision 46), reflection cap 256 and diffuse cap 62, and the words form of the per-ray inputs (tools/sol-walk/loop-reads.py counts 0). The harness cut the extracted walk by 21.8 to 23.6 % at cap 62 with every hit unchanged on 3,038,431 saved rays; the projected whole trace on the deck is about 3.14 ms against 3.90. Acceptance: the C# copy (ScreenRay) and the GPU tests that pin the shader to it pass unchanged; the real busy frame on demo-exit's deck at the top clock is measured and reported against the 12 ms budget, before and after. The user chose portable only on 2026-11-04: no Mac-only Metal path.


## Notes

**2026-11-05T15:42:54Z**

Preserved launch-ter-rgg9.md from /private/tmp/tertius-integration-20261105/ before the final commit. SHA-256: 6ce6024479ea5e9cc33a4033b1273f2663433e5578285383f3e060b5c58b0d1b

```markdown
---
id: ter-rgg9
status: in_progress
deps: [ter-gayt]
links: []
created: 2026-11-04T20:36:02Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance, trace]
---
# The screen tracer's walk takes the hill-climb's portable changes

From ter-dzxf (docs/research/temporal-renderer/walk-hill-climb.md, 'Reproduction and handoff'): after ter-gayt merges, change walkScreen in screen_walk.glsl to the two-image combination bounds-predicate+loop-predicate+facing: the vector Boolean bounds test, the vector Boolean loop condition without short-circuit control flow, and the sign-only facing test (no normalize of the decoded normal, local to the facing test). Keep the original arithmetic order, the two-image depth layout (plan decision 46), reflection cap 256 and diffuse cap 62, and the words form of the per-ray inputs (tools/sol-walk/loop-reads.py counts 0). The harness cut the extracted walk by 21.8 to 23.6 % at cap 62 with every hit unchanged on 3,038,431 saved rays; the projected whole trace on the deck is about 3.14 ms against 3.90. Acceptance: the C# copy (ScreenRay) and the GPU tests that pin the shader to it pass unchanged; the real busy frame on demo-exit's deck at the top clock is measured and reported against the 12 ms budget, before and after. The user chose portable only on 2026-11-04: no Mac-only Metal path.


## Notes

**2026-11-05T15:17:51Z**

2026-11-05: tracer/Rule A merged, closed and pushed at 34c28466e641a864fd6a915c029e08f07b3b9ccc. Started workflow 680e3ea3-0eba-481b-88ba-41f1e578fa95, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-rgg9-portable-screen-walk, exact local base 34c28466e with startFromOrigin=false. Codex gpt-6.1-sol reasoningEffort=xhigh for build/review/fix. One of four heavy allocations. Only previously selected two-image portable bounds/loop/facing changes; retain childAt/solved correctness and ray caps 256/62. No approximation or Mac-only path. Parent owns hardening, integration and closure.

```

**2026-11-05T16:26:21Z**

Implementation stage complete on the supplied integrated tracer/History Rule A base 34c28466e641a864fd6a915c029e08f07b3b9ccc, in /Users/alexispurslane/.t3/worktrees/tertius/ter-rgg9-portable-screen-walk on branch ter-rgg9-portable-screen-walk. This is not ticket closure or parent hardening completion.

Ported only the settled vector Boolean bounds and loop predicates and local sign-only facing normal. Bounds remain in their existing screen_ray.glsl owner. The loop keeps the original negated ray-end comparison. Arithmetic order, exact childAt/solved tracking, slab checks, two depth images, integer-words inputs, reflection cap 256 and diffuse cap 62 are unchanged. No nudge, approximation, one-image pyramid, scheduler, private Metal API, or history/denoising change.

Three real compiled-probe tests failed for the intended missing execution-cost behavior before implementation and passed afterward. GPU-locked replays preserve all 3,038,431 active saved-ray hit identities against the current integrated baseline. The historical cap-62 V6 baseline differs on 195,044 identities, so its 21.8–23.6% saving and 3.14-ms trace projection are not transferred. Captured input file hashes match retained provenance. Both extracted current walks have five record accesses outside the loop and zero inside. Both extracted modules and the real block/own-ray shaders pass Vulkan 1.3 SPIR-V validation.

Three 120-frame real demo-exit deck runs per build, after tick 399, 1280x720, two captures in flight, all under the project GPU lock, all at mean/p10/p90 1296 MHz: mean GPU frame span 11.316 -> 11.182 ms, saving 0.134 ms (1.18%); mean margin below the existing 12-ms budget is 0.818 ms. Each run's frame p95 is below 12 ms. The neighboring reflection-rays-through-screen-trace-resolve interval is 3.602 -> 3.474 ms (3.56%); it is not isolated pass-own GPU cost on MoltenVK.

The real deck observation film records 152 draws: 32 initial warm-up draws and 120 shown frames with distinct snapshot times. Shown draw index advances 32 -> 151 without another reset, and history-image parity alternates. Three inspected captures are /private/tmp/ter-rgg9-20261105/screenshots/deck-000000.png (lit cabin framing and floor reflections), deck-000059.png (person in doorway) and deck-000119.png (person left on deck). Fine floor speckle remains. The PNG observation run averages 812.6 MHz and is excluded from the top-clock performance comparison.

Required checks completed in the Nix dev shell, one build/test command at a time: tools/cleanup.sh on the changed C# file; full Checked solution 9,948 passed/20 skipped/0 failed; full Release solution 9,935 passed/20 skipped/0 failed; Release benchmark build 0 warnings/0 errors. The unchanged skips include ter-186q and the late-latching driver's isolated-pass assertion. Temporary measurement drivers are absent from both complete suite builds. Code/test/build-input tree d22c4fcb51fae5ecf942d661aecdee2341d7b3b8 is unchanged after those checks. Full report, source/input hashes, timings, saved-ray gate and history/validation records: docs/research/temporal-renderer/portable-screen-walk.md and its sibling records directory. All raw logs and replay scripts remain in /private/tmp/ter-rgg9-20261105.

No unsettled fork. Linux runtime/performance is unavailable here and remains user-deferred under ter-cgif. Coverage, mutation testing, integration, push and ticket closure belong to the parent and were not run here. The launch brief is preserved in the earlier note before this commit. No other checkout, preserved tracer, root source, or worker artifact was changed.
