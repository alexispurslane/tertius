---
id: ter-zj0b
status: closed
deps: [ter-7wbc]
links: []
created: 2026-10-12T10:54:27Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [core, rendering, design]
---
# Make GravityField and HemisphereUp unions instead of a kind with fields that only some kinds use

The user asked: GravityField (src/Tertius.Core/Frames/FrameTypes.cs) is a record struct with a GravityKind and fields that only some kinds use: Acceleration and Region for Uniform, Mu for Point. So a frame can hold states that mean nothing, such as a uniform field with a Mu. HemisphereUp (src/Tertius.Core/Seams/RenderSeam.cs), written for the ambient light, has the same flaw: one vector that means a direction for Along and a center for AwayFrom.

Shape: a custom C# 15 [Union] struct, as Target (src/Tertius.Core/Handles/Target.cs) is, so nothing boxes. GravityField's cases: UniformGravity(Vector3 Acceleration, BoundingBox? Region) and PointGravity(double Mu), with the empty union for no gravity. HemisphereUp's cases: a direction and a center relative to the instance, with the empty union for an even mix. Each At or reader becomes a switch over the cases. FrameTree keeps a per-frame array of values, and GpuInstance keeps its packed Vector4, so the hot paths pay nothing extra.

Do it after ter-7wbc merges, because that ticket rewrites the code that reads a parent's point gravity. The decisions are settled, so a Sonnet builder fits.

## Acceptance Criteria

GravityField and HemisphereUp cannot hold a field that their case does not use, every reader matches their cases with a switch, and every test passes.

