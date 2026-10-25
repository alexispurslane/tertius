---
id: ter-is7n
status: closed
deps: [ter-zzyr]
links: []
created: 2026-10-25T15:56:12Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, exposure]
---
# Meter the exposure with highlight priority

User, 2026-10-25: when a large bright area shares the view with a dark one, the exposure keeps the bright area's detail, and the dark part falls into deep shadow, as the eye sees it. Today the trimmed average of the surface pixels maps to middle gray, so over Earth's night side the sunlit hull clips to white (orbit-umbra, entry film frame 150).

## Design

- The target becomes the lower of two exposures: today's average target, and a highlight target. The highlight target maps the bright trim edge, the luminance at the (1 - BrightShare) quantile of the surface pixels, to 1.0 in the units before the tone curve. So a large bright area sits at the start of the curve's shoulder. Both come from the same histogram, in ExposureMeter (C#) and meter.glsl.
- The compensation and the clamp apply after the lower of the two is taken.
- Tests first: a histogram with a tenth or more of bright pixels gives the highlight target; one without gives today's target; a dim scene keeps today's target; the C# copy and the shader agree.
- Captures, before and after, at the same frames: orbit-umbra entry-000150, entry-000399 and settled, demo-surface in-cabin and apron, pbr-spheres front. The builder looks at each and reports the clipped share of pixels.
- Update docs/plans/auto-exposure.md and ARCHITECTURE.md.

## Acceptance Criteria

Over the night side the sunlit hull shows its detail. Scenes with no large bright area look as before. The full suites pass in Checked and Release.


## Notes

**2026-10-25T16:43:42Z**

Merged into main on 2026-10-25. Coverage of the changed lines (Debug and Release, Rendering): 26 of 29 lines. The three uncovered lines are the UnreachableException after the bin loop and two empty-histogram checks that Reading's first check makes unreachable. Captures at 1280x720: orbit-umbra entry frame 150 went from 3.85 % clipped to 0 %; every scene with no large bright area is bit for bit the same.
