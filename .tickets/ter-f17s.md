---
id: ter-f17s
status: in_progress
deps: []
links: []
created: 2026-11-04T21:59:59Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
---
# The renderer follows an entity from frame to frame by its whole handle: the shadow tiles' holders and layers, and the impostor key

Found in the review of ter-nt65 on 2026-11-04. ShadowTiles follows a light from frame to frame by its entity slot alone: Keep and Held give a light the layers that its slot held, RanksAsHolder gives it the holder margin (it ranks at twice its rank irradiance), and IsStale matches a tile record by the slot. So a shadow-casting lamp that takes the slot of a destroyed holder before the next submitted frame inherits the old lamp's layers and margin, a share that it did not earn. The cached tile content stays correct, because a record also holds the light's place, aim, projection and origin frame. ImpostorKey (EntityIndex, Part, Mesh, Material) also keys state that lasts from frame to frame by the slot alone. STYLE.md: no member between modules takes or returns a bare slot. RenderInstance and the render lights already carry EntityGeneration beside EntityIndex, and Core has the handle Entity(uint Index, uint Generation). The user's decision of 2026-11-04 (design A): keep the light's generation on the snapshot and make the renderer's frame-to-frame state key by the whole entity, so that the class of bug is impossible; ter-nt65 (the removal of the light's generation) closes without a merge.


## Notes

**2026-11-04T22:01:02Z**

2026-11-04: started as a workflow, run d8dc1d3b-6a17-4f9c-85a0-92ec9720c346 (version 869b0b07), branch ter-f17s-entity-handle from 808e89668, Opus builder at high and Opus reviewer at xhigh. Settled by the orchestrator: PreparedLight and PreparedInstance carry one Core Entity value in place of the bare slot, so every renderer store that lasts from frame to frame (ShadowTiles' records, holders and layers; ImpostorKey) keys by slot and generation and no comparison can use the slot alone; other slot-only state outside the prepared types is reported, not fixed.
