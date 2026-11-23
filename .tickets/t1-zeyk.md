---
id: t1-zeyk
status: open
deps: []
links: [ter-5kz6]
created: 2026-11-23T01:47:50Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, render-scale]
---
# The render scale's request never arrives at a target above it, so the size rests one grid step below the native size

No build now. The orchestrator ordered this ticket on 2026-11-22 as a record of the fault and of the two designs to compare.

## What was seen

On the card, at rest under a budget that allows the native size, the request stays at 0.9993 to 1.0000 and the size stays one step below native: 1904 x 1071 on main 3549d3283 (119 of 120 steps of 16 x 9 px, a realized scale of 0.9917), and 1912 x 1072 on the code before step 2 of ter-qt72 (239 of 240 steps of the old 8 px grid, 0.9958). Job 60e80117, the cabin's lamp switch log, draws 133 to 192. The table is in ter-qt72.

## The class of the fault

`RenderScaleController.Measure` moves the request toward its target by a share of the remaining distance:

```csharp
double wanted = Math.Sqrt(Math.Max(0, (budget - fixedTime) / renderTime));
wanted = Math.Clamp(wanted, target.MinScale, target.MaxScale);
double weight = wanted < RequestedScale ? 0.25 : 0.05;
RequestedScale = (float)
    Math.Clamp(RequestedScale + weight * (wanted - RequestedScale), target.MinScale, target.MaxScale);
```

where

- `budget` = 0.9 times the target's GPU frame time, in ms.
- `fixedTime` and `renderTime` = the cost model: the time that does not depend on the render size, and the time at the render size per unit of scale squared, in ms.
- `wanted` = the scale at which the model's predicted time equals the budget, inside the target's limits.
- `weight` = the share of the remaining distance that one measured draw covers: 0.25 downward and 0.05 upward.
- `RequestedScale` = the scale that the next draw asks for. The renderer draws the largest exact-ratio size at or below it (`UpscalingSetting.RenderExtent`).

A request that covers 5 % of the remaining distance per measured draw reaches no target above it in finite time. The size is the floor of the request to the grid. So the size rests one step below every target that lies on a grid step, not only below 1. The top of the range is where it shows, because the clamp puts the target exactly on the top step. A downward approach has no such fault: the floor of a request just above a step is that step.

## Two designs to compare (design it twice; no third)

**Design 1, the orchestrator's: the controller raises the size by one grid step when the next size fits.** The predicted GPU span of the next larger size is the measured span times the ratio of the pixel counts, or the cost model at that size's scale. If it still fits under the budget less the band, the request becomes that size's scale. This needs no rule for the top. The realized-scale feedback (`Drew`) already gives the controller the size that it drew. The controller then needs the scale of the next larger size. `UpscalingSetting` owns the grid, so that scale comes from it as a query, and the renderer hands it to the controller with the realized scale:

```csharp
// UpscalingSetting: the realized scale of the next larger active size of output above scale, or null at the largest.
internal float? NextScaleAbove(Extent2D output, float scale);

// RenderScaleController
public void Drew(long frame, float realizedScale, float? nextLargerScale);
```

**Design 2, the line orchestrator's: the request takes its target when it is within half a grid step of it.** The approach by shares stays, and its last half step is one jump. It also needs the grid's step at the request, or a fixed small distance in its place.

Whoever builds this compares the two designs on the scale logs of the lamp switch and of the walk: the rest size, the number of size changes, and the draws until the rest.

## Acceptance

- At rest under a budget that allows the native size, the render size is the native size.
- The number of size changes in the two logs does not rise.
- A test pins the class, not only the top: a request below a target that lies on a grid step arrives at that step's size within a stated number of measured draws.

## Out of scope

The 25 % down and 5 % up rates themselves, and the band of 3 %. They belong to ter-5kz6. Step 2 of ter-qt72 does not change.


## Notes

**2026-11-23T01:48:01Z**

2026-11-22, the line orchestrator. The link to ter-5kz6 is written into this ticket's front matter only. tk link is symmetric and would write into .tickets/ter-5kz6.md, which another session holds uncommitted in the main checkout, so I did not run it. When ter-5kz6 is committed, tk link t1-zeyk ter-5kz6 completes the pair.
