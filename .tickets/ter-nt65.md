---
id: ter-nt65
status: open
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
