---
id: t1-af5w
status: open
deps: [t8-rjno]
links: []
created: 2026-11-15T16:12:21Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [rendering, shadows]
---
# The reach of the sun's shadow filter follows the penumbra at the receiver's gap: a search for the blockers, on top of the one-tap fix

Step 2 of the fix of the sun's shadow leak. Step 1 is ticket `t8-rjno`: read its design first, and `docs/research/back-face-depth-layer/stage-2/shadow-leak.md`, parts 2 and 3. The parent agent settled the order on 2026-11-15. This step does not merge without the user's choice.

## The model

The edge of a shadow is a penumbra. Its width is the gap between the caster and the receiver, times twice the tangent of the sun's angular radius: 0.055 m at 5.86 m, and 0.93 m at 100 m. After step 1, the filter's reach is one bilinear comparison at every gap. So a shadow is right near its caster and too sharp far from it.

This step makes the reach follow the penumbra. A search around the lookup gives the mean depth of the casters nearer the light, and so the gap. The gap and the light's angular radius give the reach, between step 1's least reach and the 2.5 texels of today.

## Signatures

GLSL, in `shadows.glsl`:

```glsl
// The mean depth of the casters nearer the light than `reference`, among the texels within the filter's largest
// reach of `coordinate` in cascade `layer`, or a negative value where no texel holds one.
float blockerDepth(vec2 coordinate, float layer, float reference);

// The reach of the cascade filter's taps in texels: the half-width of the light's penumbra over a gap of `gap` m, for a
// light of angular radius `angularRadius` and texels of `texel` m. At least MinFilterReachTexels, which hides the
// texel grid, and at most FilterReachTexels, which the biases and the margins cover.
float filterReach(float gap, float angularRadius, float texel);
```

The search reads raw depths, so the cascades need a sampler with no comparison beside the comparison sampler. Both are core Vulkan. The fix reads the sun's angular radius from the light.

## The source's situation, and ours

The technique is percentage-closer soft shadows (Fernando, 2005). It was made for area lights whose penumbra spans many texels. Our sun has an angular radius of 0.00467 rad, and cascade 0 has texels of 0.058 m. So the true half-penumbra stays under one texel for gaps under 12.5 m. In rooms the search changes nothing. It pays off only where a caster is 30 m or more from its receiver.

So the measurement decides: the search's cost in every frame, against its gain at the gaps that our scenes have.

## The bar (the user, 2026-11-15)

The renderer must look good and show no bug. Path-traced accuracy is not the goal. A bug is light through a shadow, a blotch, a daub or a firefly. Lost light is acceptable, and it is no bug. The user's own words are in the notes of the epic `ter-kh4g`.

So the engine's stills and films decide: no light through a shadow, no specks, no sparkle, no blotches, no flicker, and a good look. A darker room after a fix is acceptable. A path trace is a tool to find and explain a bug. It is not the bar, so order no new path trace for this ticket.

Two things judge this step: how the shadows look, and what the search costs. The lit share's error against a line cast is not the measure.

## What to bring

- The search's time on the M1 Max and on the RTX 2000 Ada, in the deck, the cabin and `rect-lamps`, against step 1.
- Stills of the same views with main, step 1 and step 2. One view has a caster 30 m or more from its receiver, where the two steps differ. Take no new path trace for them.
- The gaps of the scenes: for the demo's views, the share of shadowed pixels whose blocker is farther than 12.5 m.
- A plain recommendation with its reason: keep step 1 alone, or add the search.

## Acceptance

The record gives the costs, the stills and the share of pixels that the search changes. The parent agent brings them to the user.

