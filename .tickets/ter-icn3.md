---
id: ter-icn3
status: open
deps: [ter-gayt]
links: []
created: 2026-11-05T12:22:53Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
---
# Temporal renderer: finish rough-paint image quality and long-motion acceptance

Track the unfinished image-quality acceptance separately from the approved receiving-surface history correction A. The actual roughness-0.38 demo paint retains strong grain, and an identifiable reflected lamp or corridor is not established. Existing fixed-exposure before/after evidence and manifest: /private/tmp/gayt-history-a/manifest.md. Existing noise diagnosis: /private/tmp/gayt-history-diagnosis/report.md. Continue the already-running diagnosis without choosing or implementing another algorithm before the user settles its design. Do not treat merging A as proof that this quality criterion passed.

## Acceptance Criteria

Measure and explain the remaining diffuse/reflected-light noise on actual demo paint. Obtain identifiable rough-paint reflections in the real demo at matched exposure and settings, or present the measured limitation for an explicit user decision. Inspect real-renderer captures for glass, silhouettes, changed lighting, and longer camera/light motion than the existing 0.67-second clip. Preserve response to moving or extinguished lights. Record matched stills, sequence duration, remaining limits, and the final visual acceptance decision.


## Notes

**2026-11-05T12:47:24Z**

2026-11-05: User selected integrated denoising Design B and separately authorized a new worktree thread. Verified active thread mcp:launch:292aeffbc9de3813773ac7bca768398550ea9ddead60df9f2bfac6bfdf7ce14e on branch ter-gayt-integrated-spatial-denoising, checkout /Users/alexispurslane/.t3/worktrees/tertius/ter-gayt-integrated-spatial-denoising, based on immutable tracer/A commit aaa5e776c4b33b985ca1a7cf0b9caea008b30a60, without copying validation mutations. It works directly with TDD, no subagents/workflows. Parent's tracer/Rule A validation and main integration remain independent. At 07:46 EST all four heavy slots were assigned to three validation workflows plus the light-budget builder, and load was 22.98. The new denoising thread was asked to do reading/design/test planning only until parent releases a heavy slot. Exact brightness/noise-estimation policies and GPU cost are not yet settled or validated. The original preserved tracer checkout's write restriction remains in force.

## Implementation handoff inspected, November 5, 2026, approximately 09:18 EST

The direct Design B worker delivered its implementation child tgisd-pyop, which remains in progress in that checkout. Raw-temporal noise estimation was settled under user delegation. Its checkout remains at aaa5e776c4b33b985ca1a7cf0b9caea008b30a60 with uncommitted engine, test, design and ticket changes; no main import, commit, merge, push or closure occurred. The parent verified all 11 implementation/test SHA-256 entries against current files, confirmed the external patch includes the new shaders and design document, and inspected all four matched renderer stills. Source and tickets remain unchanged in the worker checkout. The three worker ticket records are separately backed up with handoff- prefixes under /private/tmp/gayt-history-diagnosis/integrated-spatial-20261105/.

Evidence is report.md, manifest.md, measurements.json, evidence-checks.json, implementation.patch and implementation-files.sha256 in that directory. The late static window is renderer draws 161–192 at 1280x720 and fixed exposure 1.57 stops. Reported displayed fluctuation reductions are 42.16%, 36.86% and 41.81% for floor-left, floor-center and wall; corresponding displayed means rise 0.239%, 0.302% and 0.300%. This measures temporal variation at fixed pixels, not complete noise removal. Parent visual inspection confirms reduced fine grain and recognizable cabin/doorway boundaries, but broad mottling remains. Neither these stills nor the three-second sequences establish identifiable rough-paint reflections or final trail acceptance. All original acceptance criteria remain open.

Focused accumulator 43, screen-trace 21 and corrected bloom 21 cases pass in both configurations. The final full Release Rendering log records 2,280 passed, eight skipped and zero failures. Full Checked was not rerun after bloom fixture isolation: the earlier full run had 2,278 passes, two bloom failures, eight skips, followed by the separate recorder exception. Do not describe full Checked as green. The recorder defect is the worker's separate open tgisd-s1gs; its ticket is preserved, and no recorder fix is included.

All worker heavy commands have ended and its allocation is released. Parent reserves the freed allocation for critical-path integration/validation rather than starting unrelated heavy work at load 16.14. No new denoising workflow, restart, build or hardening campaign was launched for this coordination handoff. Independent tracer/History Rule A validation and integration remain unblocked by denoising. Later denoising acceptance must reconcile validated tracer changes and the integrated Entity API, obtain review and final combined Checked/Release evidence, complete required changed-line coverage/mutations, and establish controlled-clock GPU cost and visual acceptance without claiming deferred Linux-driver validation. Avoid repeating a full suite on the old base merely to repeat it again after reconciliation.

## User rejects current image quality; AMD comparison clarified

After the implementation handoff the user explicitly rejected the spatially denoised image for remaining mottling and speckles. The measured fluctuation reduction is not visual acceptance. Do not start post-acceptance hardening, close this ticket, or describe Design B as visually successful on that basis.

The user asked whether this is what AMD implements. Parent checked the pinned FidelityFX SDK v1.1.4 denoiser/SSSR documentation and reflection prefilter, reprojection and temporal-resolve sources against this checkout. This is an adaptation, not the complete AMD reflection denoiser: the pass order and center-plus-15 footprint are shared, while the temporal raw-light statistics, spatial weights, fast/slow histories and existing repair/clamp are Tertius mechanisms. AMD's reference combines reflection-specific reprojection/variance, tile-average-guided filtering and bright-outlier suppression with its own neighborhood-guided temporal resolve. The adaptation intentionally did not adopt that brightness-suppression policy. Tertius also applies its model to diffuse light and retains filtered light for later bounces. These differences are not proof of a particular bug, but make implementation equivalence an invalid explanation for expected quality.

The next diagnosis must distinguish artifacts already present in raw light, artifacts introduced or preserved by the spatial weights, and artifacts retained or amplified by temporal/bounce feedback, using the existing matched captured stages. It must report fine speckles and broad spatial mottling separately, not only pixel-wise temporal fluctuation. An exact reference comparison on compatible reflection inputs can test the adaptation separately; the source inspection alone has not established the remaining artifact's root cause. No replacement algorithm, brightness clipping, history/ray-budget tuning, workflow, or further heavy command is authorized or launched by this clarification. Tracer/History Rule A remains independent.

Separate budget-capture correction: ter-69s8's original visual-isolation-final images reset history on every draw because its fixture reused snapshot time. Parent verified its failing [0, 0, 0] history-index regression and subsequent green result, and retracts the earlier inference from already-corrected source that the old captures advanced time. Those budget captures are invalid as warmed-tracer evidence. The independent gayt-history-a and integrated-spatial-20261105 datasets already use advancing clocks and are not invalidated by this other fixture defect. The user's rejection of Design B's mottling/speckles and all remaining quality criteria remain in force.

**2026-11-05T15:10:14Z**

2026-11-05 tracer/A integration checks passed independently of Design B. Fresh root-engine deck and reflection-front captures reproduce the narrow A references byte-for-byte under matched native 1280x720 fixed exposures, default tracer/SMAA/bloom and advancing film clocks. Existing mottling and speckles remain visible; neither these images nor passing tests establish this ticket acceptance. Integrated Design B remains unaccepted, dirty and unmerged in its separate checkout. Preserve all original quality, identifiable rough-paint reflection and longer-motion criteria. Root capture provenance and logs: /private/tmp/tertius-integration-20261105/merge-gayt/.

**2026-11-05T16:22:29Z**

2026-11-05 user requested a new direct discussion thread and isolated worktree for a Unity-guided noise research program, independent of workflows and FSR integration. Research child tiunr-9b7v is in progress. See docs/plans/unity-guided-indirect-light-research.md. Thread mcp:launch:ee1c1f6e2a6bd945ec813ad2be3e94c1640798e8c20818e376d5f281cfba4558, branch ter-icn3-unity-noise-research, starting main 34c28466e641a864fd6a915c029e08f07b3b9ccc. Existing Design B remains visually rejected and preserved. Three primary hypotheses are bright-ray suppression, hit-radiance source/feedback, and post-temporal diffuse filtering. A previous scene image still contains indirect-light feedback; direct/material double counting, exposure, component contents, and history write points must be resolved. No replacement policy, production edit, heavy command, workflow, merge, or quality acceptance is authorized by this handoff.
November 5: child tiunr-9b7v adds the user-selected standalone controlled
reflection reference tool, without changing live noise policy. Bounded
checks/build/reference outputs complete; allocation RELEASED. See child
and research document for convergence and the provisional narrow-lobe
cross-check. No noise-quality or production integration acceptance.
