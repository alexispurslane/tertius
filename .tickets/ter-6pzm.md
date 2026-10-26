---
id: ter-6pzm
status: open
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

