---
id: ter-gnwg
status: open
deps: []
links: []
created: 2026-10-30T18:09:41Z
type: task
priority: 3
assignee: Alexis Purslane
---
# Represent each mesh and its deformation in one upload entry

Found during ter-dyss calibration, baseline 0eb95afa. UploadQueue.Pending exports Meshes and Deforms as parallel lists; GeometryStore.Stage asserts equal lengths and joins by index; VulkanRenderer forwards both lists. One per-mesh upload entry containing MeshId, MeshData, and optional PackedDeform removes a shared alignment invariant. Keep queue order, dense ids, allocation/staging ownership, and deterministic upload behavior. Scope is association representation, not splitting the resource managers.

## Acceptance Criteria

Use one authoritative mesh/deformation association from enqueue through draining/staging. Behavior tests cover interleaved rigid/deformed meshes and empty batches, proving correct mesh ids and deformation attachment. Preserve allocation/lifetime behavior and update ARCHITECTURE.md.

