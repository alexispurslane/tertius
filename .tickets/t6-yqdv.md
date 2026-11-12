---
id: t6-yqdv
status: open
deps: []
links: []
created: 2026-11-11T20:48:26Z
type: chore
priority: 3
assignee: Alexis Purslane
parent: ter-b4ux
---
# One owner decides where the GPU timer stamps: the cost groups beside FrameGraph.Stage, not in the render scale controller

The review of t6-h5e3 found the decision of where to stamp spread over four places: RendererOptions.PassTimestamps, GpuTimer.EachPass (stored by the timer and read only by FrameGraph), FrameGraph.Record with NextRecordedGroup, and RenderScaleController.CostGroup, CostGroupOf and NameOf. FrameGraph now depends on the controller, and the controller depends on FrameGraph.Stage, so the dependency runs both ways. Move the cost groups next to FrameGraph.Stage and let one owner, the timer or the graph, decide each stamp from the stage of the pass and of the next recorded pass. The behavior stays the same: GpuTimingTests and RenderScaleTests hold it.

