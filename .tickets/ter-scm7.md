---
id: ter-scm7
status: open
deps: []
links: [ter-gayt]
created: 2026-11-04T01:46:17Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal, performance]
---
# Unroll the lean walk's step loop two to four times, and measure it at a recorded clock

Found in ter-3ug8 (docs/research/temporal-renderer/gpu-calibration.md). On the development Mac's GPU, a rolled loop of dependent FMAs reaches only 0.41 of the FP32 peak, natively in Metal and through MoltenVK alike. In one experiment, two, four and eight steps per pass of the loop reached 0.58, about 0.80 and 0.84. The calibration probe, eight steps per pass with 16 independent chains, reaches 0.97. Apple's compiler causes this, not MoltenVK. The lean walk of the screen tracer takes 2.3 to 2.7 times what a rolled loop of about 40 operations per step predicts. Unroll its step loop two to four times, keep its result unchanged, and time it beside tools/gpu-calibration/gpu-clock at the same clock as the rolled form (ter-qk78). The unrolled loop must not cost time on other Vulkan drivers. The profiling spike (ter-sftw) owns the walk's instruction count.

