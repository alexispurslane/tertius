---
id: t58-aqrg
status: open
deps: []
links: [ter-bcwr]
created: 2026-11-23T13:16:20Z
type: task
priority: 4
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering]
---
# The catch-up length is stored twice: FrameHistory.CatchUpDraws in C# and FullRateFrames in sample_age.glsl

Found in the review of ter-bcwr. FrameHistory.CatchUpDraws (8) copies the shader constant FullRateFrames (sample_age.glsl), below which the ray plan traces an own ray. A test (FrameHistoryTests.TheCatchUpLengthIsTheHistoryLengthBelowWhichThePlanTracesAnOwnRay) reads the shader's value and compares, which catches a drift but keeps two stores. One store: make FullRateFrames a specialization constant that C# sets from CatchUpDraws (ShaderConstants), as the other pipeline constants are. It touches the plan, the spatial accumulation and sample_age.glsl.

