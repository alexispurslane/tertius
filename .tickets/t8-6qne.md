---
id: t8-6qne
status: open
deps: []
links: [t8-pkag]
created: 2026-11-12T21:48:40Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: t8-pkag
tags: [rendering, linux]
---
# The prefilter of a uniform map gives 0.9995117 against 1 on NVIDIA

EnvironmentPrefilterTests.AUniformMapGivesACubeOfItsOwnRadiance fails on the RTX 2000 Ada for (16, 32): a texel holds 0.9995117, one half-float step below 1.


## Notes

**2026-11-12T21:52:41Z**

Kind (a), as t8-en95. environment_irradiance.comp divides the weighted sum by the sum of weights; for a uniform map of 1 the float quotient can lie 1 ulp below 1, and NVIDIA's store to the half-float cube rounds it to 0.9995117, the next half float below 1. Vulkan leaves that rounding undefined. Fix: the test allows one half-float step, 2^-10. A weight that the sum misses or counts twice moves a texel by far more than 0.1 %, so the bound still catches a normalization fault.
