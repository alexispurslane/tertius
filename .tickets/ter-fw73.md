---
id: ter-fw73
status: open
deps: []
links: []
created: 2026-11-12T02:50:00Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [linux]
---
# A headless run on a discrete GPU counts the capture's readback, 2.6 ms, in the frame span

On the RTX 2000 Ada pod of ter-libf (2026-11-12), the `capture` pass of a headless 1920 x 1080 run took 2.57 ms per frame, 20 % of the deck's 12.89 ms frame span. On the Mac it takes 0.03 ms, because the GPU shares memory with the CPU. A window does not capture, so this time is not part of a shown frame. tools/demo-timing.sh, the survey's timing part and gpu-trace all report the span with it. On a cloud GPU a comparison of two builds inside one job still holds, but a budget check does not. Options: report the span without the capture pass beside the span, or end the timed span before the capture's copy. The per-stage comparison is in the ter-libf note.
