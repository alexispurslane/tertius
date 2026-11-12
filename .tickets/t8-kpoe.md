---
id: t8-kpoe
status: closed
deps: []
links: [t8-pkag]
created: 2026-11-12T21:48:40Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: t8-pkag
tags: [rendering, linux]
---
# The young diffuse history finds noise on a wall of one luminance on NVIDIA: 7.09e-6 against 0

DiffuseFilterRenderTests.AYoungHistoryTakesNoNoiseFromAnotherSurface fails on the RTX 2000 Ada: the young estimate of the deviation is 7.09e-6 on a wall without noise. The estimate subtracts the squared mean from the mean square, which cancels exactly only where the driver's rounding happens to make the two equal.


## Notes

**2026-11-12T21:52:41Z**

Kind (b), a small engine fault on a regular Vulkan driver. youngDeviation in diffuse_filter_variance.comp took the variance as the mean square minus the squared mean of the window's luminances. On a wall of one luminance the true variance is exactly 0, but the float result is 0 only where the driver's rounding (division may be 2.5 ulp off in Vulkan) makes the two terms equal. NVIDIA 595.91.07 gave 7.09e-6 for a wall of 1. On the Mac, a wall of 7.77 also gave 6.03e-5: the new test AYoungHistoryOfOneLuminanceFindsNoNoiseWhateverItsValue(7.77) was red on the Mac first. Fix: the sums run over each luminance minus the pixel's own, which leaves the variance unchanged and makes every difference exactly 0 on a uniform surface. All 20 DiffuseFilterRenderTests pass on the Mac.

**2026-11-12T22:55:21Z**

Card confirmation on a7924ebd7 (2026-11-12): the fixed Rendering methods 10 of 10, the fixed classes 80 of 80 and the Host edit classes 40 of 40 pass on the RTX 2000 Ada. The Mac's Checked and Release suites pass.
