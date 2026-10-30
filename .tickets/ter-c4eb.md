---
id: ter-c4eb
status: open
deps: []
links: []
created: 2026-10-30T18:09:41Z
type: task
priority: 3
assignee: Alexis Purslane
---
# Derive descriptor pool requirements from authoritative binding definitions

Found during ter-dyss calibration, baseline 0eb95afa. DescriptorLayouts defines descriptor slots/types, CreatePools separately computes aggregate requirements, and FrameResources/ScatterImages/SmaaTargets repeat binding schema knowledge while populating sets. Changing a binding can require edits in several independent C# representations plus shader declarations. Centralize the C# schema, derive pool contributions from it and set multiplicities, and let existing owner provide semantic binding operations where that removes repeated slot/type knowledge. Preserve shared layout compatibility and pool lifetimes; no class-per-pass framework. Shader/Vulkan ABI agreement still needs explicit validation.

## Acceptance Criteria

A binding change cannot silently leave C# pool counts/types or set writers inconsistent. Meaningful tests cover descriptor types and per-frame/per-feature multiplicities. Validate shader agreement and regular Vulkan behavior; update ARCHITECTURE.md.

