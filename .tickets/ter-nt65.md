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

