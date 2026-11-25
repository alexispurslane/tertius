---
id: t1-vj0f
status: closed
deps: []
links: [t1-3er4]
created: 2026-11-24T19:19:26Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [rendering, temporal]
---
# Vertical streaks at 8-render-pixel period on early post-cut frames outside accumulate_spatial Step 3

**2026-11-25 · Retired.** Check the actual cabin camera cuts in ter-fl8r first. A camera-history or metering change needs a matched contribution at the affected frames.

During t1-3er4 Phase 1 verification on main e9e0dc043 (real-cut-nose.json at -1.7507 stops), disabling accumulate_spatial.comp Step 3 removes the lag-6 grid (0.637 -> 0.013 at d0), yet leaves a weaker lag-12 output-pixel (8-render-pixel) autocorrelation of 0.166 at d0, 0.142 at d1, 0.114 at d2, and 0.070 at d5. In docs/research/cut-grid-probe/pics/nose-zoom-2x.png (row 'off', branch t3/investigate-cut-grid-probe at 4a85a65fe), this appears as vertical streaks spaced 8 render pixels apart (12 output pixels at render scale 2/3). On the pre-ter-fe63 strip, lag-12 at d0 was 0.327 vs 0.396 after ter-fe63. Source is outside accumulate_spatial.comp Step 3 and remains undiagnosed.

## Acceptance Criteria

Identify which pass produces the 8-render-pixel vertical streaks on early post-cut frames under identical rays, quantify its visible impact at normal exposure, and decide whether a fix is warranted.

## Notes

**2026-11-24T19:25:00Z**

2026-11-24 14:25, replacement back-face line orchestrator (Gemini 4 Neon XHigh). Per parent instruction, defer further investigation of t1-vj0f until t1-3er4 lands on main, then confirm normal-exposure visibility on final main before escalating.
