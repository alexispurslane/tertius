---
id: ter-cgif
status: open
deps: [ter-s7c6, ter-kzj3]
links: [t8-xp96]
created: 2026-11-03T14:53:48Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, linux]
---
# The temporal renderer on Linux: every GPU test on lavapipe and the Linux drivers

Build ticket 7 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, "Portable Vulkan". Acceptance: section 14, item 7, plus ticket 1's FSR test on lavapipe, which waits here because the user decided on 2026-10-25 to skip Linux runs for now. It waits for ter-kzj3.


## Notes

**2026-11-03T19:46:22Z**

2026-11-03, from ter-7m38 (FSR's 16-bit and subgroup builds). Two more items wait for the Linux runs here. First, FSR's reference image ReferenceImages/fsr3-pattern.png was approved on MoltenVK only: run Fsr3UpscalerTests.TheSettledPatternAndTheFramesAfterAChangeOfTheSceneMatchTheirReferenceImage on lavapipe, RADV, ANV and NVIDIA's driver, and record its largest FLIP value against the tolerance of 0.1. Second, time FSR's builds again on each Linux driver with tools/fsr3-timing (tools/README.md, 'FSR 3.1 timing'): the engine's build, its byte copy as the control, the 16-bit build, the subgroup build and the build with both, in at least 5 alternating rounds under the GPU lock. On the M1 Max, the build with both changes was 0.085 ms faster by the paired run median and 0.073 ms faster by the paired run 10th percentile, but not measurably faster by the pooled frame median (src/Tertius.Rendering/ThirdParty/Fsr3/README.md, 'The timing'). Report both readings. If the engine changed to a variant in the meantime, time the variant against the 32-bit build instead.

**2026-11-12T03:15:00Z**

From ter-libf, the first Linux run on NVIDIA (L4, driver 595.91.07, 2026-11-12): `Fsr3UpscalerTests.TheSettledPatternAndTheFramesAfterAChangeOfTheSceneMatchTheirReferenceImage` fails. 142 pixels have a FLIP value above 0.1, the largest 0.315, the mean 0.00393. The reference, the actual image and the heat map show the same four frames; the heat map has about a dozen isolated single-pixel spots spread over the checkerboard. So the largest FLIP value on NVIDIA's driver is 0.315 against the tolerance of 0.1. The images: /private/tmp/ter-libf/l4-out/ref/fsr3-pattern*.png. The same run also failed the radius-zero sphere light's reference image by 16 pixels (largest FLIP 0.119); that has its own ticket. tools/README.md, "Linux NVIDIA machines", says how to bring up a pod.
