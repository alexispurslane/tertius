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
