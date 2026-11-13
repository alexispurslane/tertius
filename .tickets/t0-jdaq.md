---
id: t0-jdaq
status: in_progress
deps: []
links: []
created: 2026-11-13T01:01:07Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Name the sample age's layout in one include

Design: docs/plans/bounce-light-confidence.md (Design A with Design X's evidence), section 23. Branch t7-3e22-retained-deviation. Step 11. New Shaders/sample_age.glsl with SampleMotionBit (bit 15), LightCodeMask (bits 0 to 14), RecentLightAge (7), LeastMotion (1/32 pixel per draw), sampleAgeOf, lightCodeOf and sawMotion (section 8.5). A few lines, so that steps 12 and 13 build in parallel against one layout.


## Notes

**2026-11-13T01:30:45Z**

Built in 1190adce1: Shaders/sample_age.glsl (SampleMotionBit bit 15, LightCodeMask bits 0-14, RecentLightAge 7, LeastMotion 1/32 px, sampleAgeOf, lightCodeOf, sawMotion, recentLightCode) and TemporalSignal.SampleMotionBit/LightCodeMask for the C# readers. No test of its own: the include has no reader until steps 12 and 13, whose GPU tests write ages through the C# layout and read the shaders' cut and box, so a drift fails them. Compiled with -Werror against a scratch shader; full Checked suite green (10,865 passed).
