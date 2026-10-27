---
id: ter-6pzm
status: closed
deps: []
links: []
created: 2026-10-26T20:22:28Z
type: chore
priority: 2
assignee: Alexis Purslane
tags: [refactor]
---
# Refactor the long methods that the hook listed in the lights merge

The pre-commit hook of the merge of main into the lights epic (14aff809, 205494f0) listed methods over its 60-line limit: the Setup methods of LitPuffRenderBenchmarks, OutlineRenderBenchmarks and PuffRenderBenchmarks, RenderSnapshotBuilder.Build, RenderLightUnionTests:237, three in ShadowTests, and TestScene.cs:123 (112 lines). They were long before the merge. Split each by what it does.


## Notes

**2026-10-27T08:23:52Z**

2026-10-27: merged at 8c91dfc7. Coverage of the changed lines: 1332/1417 lines (94.0%), 271/308 branches (88.0%). The uncovered lines are moved code that no test ran before either: the WeightlessPush and Puffs demo scenes, SwapchainTarget (window only), and VulkanDevice's error paths. The snapshot benchmark's 43% read was load: in turns at loads 10 to 16, frames1000 read 1.58 and 1.69 ms on main against 1.49 and 1.62 ms on the branch; flat spread from 1.08 to 1.91 ms on both sides. The only coverage failures are the known allocation tests under instrumentation.
