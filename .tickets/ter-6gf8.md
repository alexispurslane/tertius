---
id: ter-6gf8
status: open
deps: [ter-f17s]
links: []
created: 2026-11-04T22:37:00Z
type: chore
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
---
# The placement key holds the instance's entity as one Entity

Found in the review of ter-f17s on 2026-11-04. PlacementKey(uint Slot, uint Generation, ushort Part) in src/Tertius.Rendering/Frame/FrameHistory.cs is a second encoding of the identity that PreparedInstance.Entity with Part holds, and that ImpostorKey and the shadow tiles' caster records now use. MotionJoin.Identify builds it from RenderInstance's two fields. It is correct, because it compares the generation too, but the renderer has two models of instance identity. Make it PlacementKey(Entity Entity, ushort Part), built from RenderInstance.Entity, with Order => ((ulong)Entity.Index << 16) | Part, so that Order still ignores the generation (one slot holds one entity at a time).

