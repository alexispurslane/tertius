---
id: tiunr-gdxl
status: open
deps: []
links: []
created: 2026-11-07T20:19:37Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
---
# The signal scratch allocates a second part that no renderer signal uses


## Notes

**2026-11-07T20:19:37Z**

Found on 2026-11-07 during a VRAM review in the research worktree. SignalScratch makes TemporalSignal.MostParts (2) scratch images per format, but VulkanRenderer creates both screen-tracer signals with parts: 1, so only part 0 is used. A second part serves only the CreateSignal test hook.

On main this wastes one RGBA16F image: 8 bytes per render pixel, 7.0 MiB at 1280x720. In the research worktree's precise specular modes it wastes one RGBA16F and one RGBA32F image: 24 bytes per render pixel, 21.1 MiB at 1280x720 and 47.5 MiB at 1920x1080.

Allocate the scratch parts that the renderer's signals need, and let a test that needs a second part create it.
