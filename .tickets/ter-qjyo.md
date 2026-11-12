---
id: ter-qjyo
status: open
deps: []
links: [t8-pkag]
created: 2026-11-12T02:50:00Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [linux]
---
# An FSR 3.1 host constant differs by 1 ulp on x86-64

`Fsr3HostTests.TheConstantsOfEveryFrameAreAmdsApartFromThePreExposureRatioThatTheGpuWrites` fails on the RTX 2000 Ada pod of ter-libf (x86-64, 2026-11-12): AMD's tanHalfFOV of frame 8 of the budget case is 0.7461058, ours 0.7461057, a difference of 1.19e-7 against the test's tolerance of 5.96e-8. The C# `tan` of x86-64 likely rounds differently from arm64's. Either compute the value as AMD's code does, or widen the tolerance to 1 ulp with the reason in the test.
