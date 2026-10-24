---
id: ter-5k4t
status: open
deps: [ter-c3rf]
links: []
created: 2026-10-22T12:58:26Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [core, frames]
---
# Numeric frame data as a union per mode

FrameTree keeps numeric, stepAcceleration, stepPlan and anchor in parallel arrays, and the last three mean something only in some modes (docs/plans/union-tooling.md, section Closed sets that are not unions yet). Design a union of one case per numeric mode, with the hot path in mind.


## Design sketch (2026-10-24)

The arrays numeric, stepAcceleration, stepPlan and anchor become one array of a union, one case per numeric mode. The empty value is a semantic frame.

```csharp
// Sketch
internal readonly record struct Falling(Double3 StepAcceleration);
internal readonly record struct Resting;
internal readonly record struct Nesting;
internal readonly record struct Following(Entity Anchor, Double3 StepAcceleration, MotionSample? StepPlan);
[GeneratedUnion(typeof(Falling), typeof(Resting), typeof(Nesting), typeof(Following))]
internal readonly partial struct NumericState;

private NumericState[] numeric = new NumericState[64];
```

The invariants of AssertInvariants that tie these arrays together (a semantic frame has no step acceleration and no plan, only a following frame has an anchor) become the shape of the type.

Forks to design twice when the ticket starts:
- The hot path: the walk up from a frame to its semantic frame reads only whether each frame is numeric. A byte array of modes is denser than an array of unions that holds a MotionSample. Measure both before choosing (STYLE.md, "Priorities").
- apartTime and numericBodies hold for every numeric mode: in each case, or in a row struct around the union.
- A following frame has no plan between its creation and its first BeginStep: give it the plan of its first step at creation, or keep MotionSample? in the case.
