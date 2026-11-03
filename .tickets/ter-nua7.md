---
id: ter-nua7
status: open
deps: []
links: []
created: 2026-11-03T17:34:46Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [rendering]
---
# A frame that throws after its uploads or a growth of the vertex buffer loses them

Found in the review of ter-ohqr (2026-11-03). The renderer records the uploads (GeometryStore.Upload, DeformStore.Upload, MaterialStore.Upload, the textures) and the growth of the vertex buffer (GeometryStore.Grow and ReserveDeformed through GeometryStore.Replace) into the frame's command buffer, and changes the stores' CPU state at once: the queue is drained, and the store points at the new buffer. If the frame then throws before SubmitAndPresent, the command buffer never runs. The next frame finds the queue empty and the new buffer in place, with undefined contents, so the static vertices or the uploaded meshes are lost. The same class covers the tracked state of every resource in BarrierBatch: the frame that threw advanced each resource's ResourceState as if its barriers ran, so the retry can skip a barrier that it needs (a read after the discarded frame's read needs none by the tracked state, but the GPU last wrote the resource in an earlier submitted frame). ter-ohqr fixed the class for KeptPositions alone: a growth keeps its old buffers as the copy's source until KeptPositions.Drew after the submission, and every frame before it records the copy again. A fix of the class commits each such change after the submission, as the frame's other stocks do (docs/plans/temporal-renderer.md, decision 20).

