---
id: ter-6gf8
status: in_progress
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

## Workflow launch, November 5, 2026, 09:06 EST

Prerequisite ter-f17s is merged, closed and pushed. This existing follow-up started from main 64ab730a0e0300942212d9b2d96e74f0bfcd3ec5 on its own T3-created branch ter-6gf8-placement-entity. Workflow run e9068609-558c-48ee-9769-c6c272602fff uses saved version 869b0b07-3413-4b06-8707-a88a91bf6e91, source/schema SHA-256 ef99649844214c80f35d90851840fa568fce615016714356c3fd31b470f6fade. Its stages are build, read-only review, and one fix stage if needed. Builder and reviewer use Codex gpt-6.1-sol with reasoningEffort xhigh. This is workflow launch, not implementation acceptance or ticket closure.

Settled scope and checks are in /private/tmp/tertius-integration-20261105/ter-6gf8-brief.md. Preserve generation-sensitive equality, generation-independent slot-and-part order, and existing motion/history behavior. Do not import unmerged tracer, light-budget or denoising work. The parent owns coverage, mutations, integration and closure. This workflow takes the freed fourth heavy-agent allocation; each worker runs only one heavy command at a time and checks load, memory and disk before starting it. Automatic workflow delivery is unavailable to this parent; stages may send one ordinary queued completion message as specified in the brief.
