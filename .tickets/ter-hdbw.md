---
id: ter-hdbw
status: open
deps: [ter-vges]
links: []
created: 2026-11-03T12:22:22Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal, fsr]
---
# FSR 3.1 frame generation: port AMD's presentation code to Linux and macOS

The user's choice of 2026-11-03: frame generation stays out of the temporal renderer's first build and gets this ticket. AMD's Vulkan presentation code for frame generation (FrameInterpolationSwapchainVK) uses Windows threads, events and timers. A port to Linux and to MoltenVK is needed, and its behavior on MoltenVK is unknown. See docs/research/temporal-renderer/fsr31-integration.md, section 5.

