---
id: ter-hxhl
status: open
deps: []
links: []
created: 2026-11-11T18:45:51Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# Thin the diffuse filter: six steps of a 3 x 3 grid in place of five steps of a 5 x 5 grid

Step 7 of the renderer-finish proposal (/private/tmp/tertius-report-20261109/proposal.md, section 2.3, design A). The diffuse filter takes 4.18 ms on the deck, the second-largest cost after the light loop. Five steps of 24 taps give 120 taps per pixel (diffuse_filter_wavelet.comp). Six steps of a 3 x 3 grid with weights 1, 2, 1 along each axis give 48 taps. A sixth step, 32 pixels apart, restores the reach of 63 pixels.

## Design

Keep the edge tests, the rule that light moves and is not created, and the feedback of the first step into the slow history. Merge the variance dispatch into the accumulation's second dispatch, which reads the same images. Keep the old kernel as a configuration value (decision 2 of docs/plans/rendering-performance.md). Risk: more grain or a faint grid at the wide steps. The guided rays (ter-05wb) merged first, as the proposal required, because the remaining noise decides how much filter the image needs. Coordinate with the frame-time cuts (t6-h5e3), whose change 6 records the specular accumulation between the filter's steps.

## Acceptance Criteria

The filter takes about 2.2 ms on the deck in a Metal System Trace (estimate: about 2 ms saved). No survey still or film moves from pass to fail on blotch, specks or flicker, and no grid shows in the stills.

