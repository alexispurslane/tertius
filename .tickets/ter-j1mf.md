---
id: ter-j1mf
status: closed
deps: []
links: []
created: 2026-10-26T01:57:15Z
type: task
priority: 2
assignee: Alexis Purslane
---
# Timing runs write no frames to disk

The cost recipe (docs/plans/rendering-upgrade.md, "Measuring the cost of a feature") runs the demo with --video, only so that the run skips the slow PNG encode of each frame. Each run leaves a 550 to 690 MB run.mkv that nobody reads. On 2026-10-25, 33.5 GB of these videos in the session scratchpad, with 93 GB of Nix copies of another project, filled the disk and stopped every agent. Give headless runs a way to draw each frame and discard it, such as --video none, so that a timing run writes only gpu-timings.json and its log. Update the recipe, tools/gpu-trace.sh and tools/README.md to use it.

