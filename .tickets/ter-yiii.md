---
id: ter-yiii
status: open
deps: []
links: []
created: 2026-10-11T22:48:45Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [rendering, performance]
---
# Add GPU timestamp queries to the render statistics, so each pass's cost can be measured

The project rules ask to measure the cost of every rendering feature, but the renderer has no GPU timer: RenderStats counts instances, commands and impostors only, and the renderer tests time whole frames with the Vulkan validation layers on, which mostly measures the layers. So the GPU cost of a change cannot be measured today. An example is ter-1zfs, which grew each GPU instance from 64 to 80 bytes for the hemisphere's up, 320 KB more per frame at 20,000 instances.

Add Vulkan timestamp queries around each pass (shadow cascades, G-buffer ranges, lighting, transparent, impostor captures, tone mapping), read them back a few frames later without a stall, and report them in RenderStats. Check that MoltenVK supports timestamps on the device, and fall back to no timings where it does not. A measurement run turns the validation layers off.

## Acceptance Criteria

RenderStats reports the GPU time of each pass, a few frames late, on the development Mac. A Release run without validation can compare two builds of the renderer, and the plan docs record how to do it.

