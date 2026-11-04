---
id: ter-l113
status: open
deps: []
links: []
created: 2026-11-04T15:51:35Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
---
# Headless timing summary: on stamps that latch late, print neighbouring sums, not single-pass times

On MoltenVK (VulkanRenderer.StampsLatchLate), a pass's GPU time can hold part of the next pass's work. Today WriteGpuTimings in src/Tertius.Host/ScriptCommand.cs prints every pass's own mean, median and p95, then LateStampsNote. A reader takes the per-pass lines as true and has to sum neighbours by hand: ter-9nrc's measurements needed a script (fix/neighbours.py) for this. Suggested by ter-9nrc's review on 2026-11-04. Make the summary print only what holds on such a renderer: the span, and the sum of each run of neighbouring passes that the user names or that the tool picks, or mark each single-pass line as not valid on this driver. Portable: on a driver whose stamps do not latch late, the per-pass lines stay as they are. Design the rule for which sums to print before the build (two designs, with the user).

