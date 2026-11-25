---
id: ter-qbw8
status: open
deps: []
links: [ter-e76k]
created: 2026-11-25T23:31:29Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-buzm
tags: [rendering, blue-danube, memory]
---
# Measure and reduce the renderer’s peak GPU memory footprint

The film worker reported 16,380 MiB of physical GPU memory. The second-cut logs lack allocation and peak-usage measurements. Find the working set on Linux Runpod at 1920 × 1080 output and the film’s fixed internal scale. Use the measured footprint to choose a practical budget for smaller GPUs.

## Acceptance Criteria

Measure the process at idle, after loading assets, and during the largest peaks across the film, including scene transitions. Report device-local heap budget and usage in MiB, peak process usage, and allocations by owner, separating reserved backing memory from live resources. Identify which resources coexist at the peak. Quantify the saving from reducing the largest avoidable allocations; compare equivalent shots for quality and Runpod GPU time. Record a supported budget or explicitly mark it unverified until tested under that memory constraint.
