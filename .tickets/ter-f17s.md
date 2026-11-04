---
id: ter-f17s
status: open
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

**2026-11-04T22:26:52Z**

2026-11-04: built. Commit 123a61b3b changes the types alone: PreparedLight.Entity and PreparedInstance.Entity, one Entity value each, with every comparison still by slot. On that commit the five new tests fail for the defect (a lamp in a destroyed holder's slot ranks as a holder, keeps its layer, and keeps the cached tile; a caster in a reused slot keeps the tile; a ball in a reused slot inherits the impostor hysteresis). The next commit compares whole handles in ShadowTiles (Keep, Held, RanksAsHolder, IsStale, the caster records, the invariants) and keys ImpostorKey by Entity; the tests pass. Two guard tests, a lamp of generation 7 that keeps its layers and its margin, pass on both commits. No GPU layout copies the slot. Full Checked and Release suites pass.
