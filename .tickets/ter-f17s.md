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

**2026-11-04T22:26:52Z**

2026-11-04: built. Commit 123a61b3b changes the types alone: PreparedLight.Entity and PreparedInstance.Entity, one Entity value each, with every comparison still by slot. On that commit the five new tests fail for the defect (a lamp in a destroyed holder's slot ranks as a holder, keeps its layer, and keeps the cached tile; a caster in a reused slot keeps the tile; a ball in a reused slot inherits the impostor hysteresis). The next commit compares whole handles in ShadowTiles (Keep, Held, RanksAsHolder, IsStale, the caster records, the invariants) and keys ImpostorKey by Entity; the tests pass. Two guard tests, a lamp of generation 7 that keeps its layers and its margin, pass on both commits. No GPU layout copies the slot. Full Checked and Release suites pass.

**2026-11-04T22:37:55Z**

2026-11-04: review fixes. RenderInstance, PointRenderLight and SpotRenderLight gain a computed Entity member (slot with generation, no new field, RenderLight stays 88 bytes), and the six batcher sites read it. New tests pass a live generation through the batcher: a kept point and spot light carry their whole entity, a placed instance carries its whole entity, and the light-holder mark test now uses generation 3 on both sides. Core tests pin the three Entity members. These tests are green on arrival, because the batcher already passed the generation; they lock it in. Mutants for the orchestrator's list: drop the generation at SceneBatcher's light preparation (point, spot), at PreparedInstance.Entity, and on one side of the light-holder mark. ARCHITECTURE 'The render snapshot' and 'Lights' now say that the batcher marks and names by the whole Entity, and limit the claim to stores that read the prepared types. ter-6gf8 follows up PlacementKey.

**2026-11-04T23:36:53Z**

2026-11-04 Sol handoff: workflow d8dc1d3b-6a17-4f9c-85a0-92ec9720c346 already completed build, review and fix successfully. Preserve the clean final branch ter-f17s-entity-handle at aff67ddcf2d04b0744c0744647cf6a3dfdd4c281 and its reported Checked/Release evidence. Do not restart or redo those stages. Orchestrator hardening and integration remain separate. The branch also retains follow-up ticket ter-6gf8.

**2026-11-05T12:13:08Z**

2026-11-05: parent-owned hardening continuation started as workflow 7a3aa2cf-f86c-43ed-8fd1-d766e4ec63e6, version 077df081-6e60-4160-b9a4-b89ea970421a, preserving aff67ddcf. Exact original base verified as 808e8966873f659cdf81ad4ca85b1e9b87c5543a; an erroneous expanded base in launch arguments was corrected immediately by steering the existing worker, without restart or history changes. Coverage, official Core Stryker, listed Rendering mutants, and final suites remain the scope. Parent retains merge/closure ownership.
