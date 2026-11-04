---
id: ter-nt65
status: closed
deps: []
links: []
created: 2026-11-04T14:47:45Z
type: chore
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, cleanup]
---
# Remove the light's entity generation from the render snapshot, which only ReSTIR read

ter-9nrc removed ReSTIR DI and its light keys (FrameHistory's light remap, PreparedLight.EntityGeneration). The snapshot still carries EntityGeneration on PointRenderLight and SpotRenderLight (src/Tertius.Core/Seams/RenderLight.cs), and RenderSnapshotBuilder writes it (lines 628 and 653), but no reader is left. Remove the field from both cases and from the builder, update RenderLightUnionTests, RenderSnapshotKeyTests (its summary says ReSTIR keys its lights), SphereLightRenderTests and the 88-byte size of RenderLight in ARCHITECTURE.md, 'Lights'. Left out of ter-9nrc because it changes Core's seam and the Simulation module, outside the renderer's scope. If a later pass needs to follow a light from frame to frame, it brings the key back with its reader.


## Notes

**2026-11-04T20:36:03Z**

2026-11-04: on hold behind ter-bqun. The user chose a per-light rate limit on the main view's share, kept from frame to frame per light. That state needs a light's identity, and the snapshot's EntityGeneration with the slot is that key (a reused slot must not inherit another light's share). If ter-bqun keys its state by slot and generation, this ticket is void and closes as such; if it keys by slot alone, this ticket proceeds.

**2026-11-04T21:34:44Z**

2026-11-04: no longer on hold. The view cap with its per-light memory is not merged, so the light's generation has no reader again; this ticket proceeds as written.

**2026-11-04T21:36:12Z**

2026-11-04: started as a workflow, run 397dccea-d408-4189-a208-4f3e3a2c7103 (version 869b0b07), branch ter-nt65-light-generation from cad8a6787.

**2026-11-04T21:54:47Z**

2026-11-04, for the hardening brief: merge main into the branch before the Stryker run, so that the run uses the Stryker lock of ter-s4fg (its record). A run.py from before ter-s4fg stops after 60 s on a lock with a record and prints rm -r; nobody follows that advice while another run holds the lock.

**2026-11-04T21:59:59Z**

2026-11-04: closed without a merge, by the user's decision (design A). The review of run 397dccea found that ShadowTiles follows a light from frame to frame by its slot alone, so a new lamp in a reused slot inherits a holder's layers and margin; the fix needs the generation that this ticket removed. The light keeps its generation, and ter-f17s makes the renderer's frame-to-frame state key by the whole entity. The run was stopped in its fix stage. Branch ter-nt65-light-generation stays as a record at b1793cb26.
