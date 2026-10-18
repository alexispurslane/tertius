---
id: ter-gwbe
status: in_progress
deps: []
links: []
created: 2026-10-18T17:50:48Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [simulation, physics, refactor]
---
# Make PhysicsRequest a custom union of ray, shape cast and overlap

Found by the survey of kind enums (2026-10-18), following the rule for closed sets (custom [Union] like Tertius.Target). PhysicsRequest (src/Tertius.Simulation/Entities/PhysicsQueries.cs:98) carries PhysicsRequestKind with fields per kind: rays use Pose.Position, Direction and Length and ignore Shape; ShapeCast uses all; Overlap uses Shape and Pose and ignores Direction and Length. 11 branch sites (9 in PhysicsQueryRunner, World.Tick.cs:229, PhysicsQueries.cs:102). Ray and RayAll differ only by the data-free RayCastKind flag, so three cases suffice: Ray (with RayCastKind), ShapeCast, Overlap. This also removes the shape argument that a ray ignores in RequestCast. Internal, no serialization boundary. No change of behavior.

