---
id: ter-qjyo
status: closed
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

## Notes

**2026-11-12T21:52:41Z**

Kind (a), a value that one machine recorded. The golden values carry the float functions of the arm64 Mac, which are not correctly rounded; x86-64 rounds otherwise. Frame 8 of the budget case gives tanHalfFOV 0.7461058 on the Mac against the correctly rounded 0.7461057, 2 ulps apart. Fix: Fsr3Constants computes the chain of AMD's three float calls in double and rounds once, and Fsr3HostTests allows 3 ulps for the transcendental values (one per float call in AMD's chain). A formula fault moves a value by thousands of ulps. All 31 Fsr3HostTests pass on the Mac.

**2026-11-12T22:55:21Z**

Card confirmation on a7924ebd7 (2026-11-12): the fixed Rendering methods 10 of 10, the fixed classes 80 of 80 and the Host edit classes 40 of 40 pass on the RTX 2000 Ada. The Mac's Checked and Release suites pass.
