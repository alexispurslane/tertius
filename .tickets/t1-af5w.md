---
id: t1-af5w
status: in_progress
deps: [t8-rjno]
links: []
created: 2026-11-15T16:12:21Z
type: task
priority: 4
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


## Notes

**2026-11-21T22:41:30Z**

## New design note (the line orchestrator, 2026-11-21). No build: the user decides first.

The ticket's text above is out of date in two points. Step 1 (t8-rjno) did not become one tap. And its filter is no longer symmetric. This note replaces 'The model' and 'Signatures' above. The rest stays as a record.

### What step 1 became
The cascades keep the 16 taps of main, which reach 2.5 texels. The sun's share is `lightOutsideShadow(litFraction) = clamp(2 * litFraction - 1, 0, 1)`. So the whole ramp of an edge lies on the lit side and is 2.5 texels wide: 0.15 m in cascade 0, 0.32 m in cascade 1, 0.8 m in cascade 2 and 5.8 m in cascade 3, at the demo's view. No sunlight lies inside a shadow. The user chose this form on 2026-11-21 ("yeah, new filter as built") and accepted that every edge is about twice as sharp as before.

### The purpose, in the user's words through the orchestrator
Softness that varies: sharp near the caster and soft far from it.

### What the measurements of step 1 rule out
- A ramp wider than 2.5 texels from a wider kernel. A reach of 5 texels on 16 taps gave acne over all lit ground. A reach of 3.75 on 16 taps gave a comb. 36 taps at a reach of 3.5 gave acne on diagonal slopes, a step at the seam of cascades 2 and 3, +0.25 to +0.47 ms, and narrow far strips that fade at a cascade crossing. The slope bias, the margins and the tap count are sized for 2.5 texels.
- A symmetric blur. It puts half of its width inside the shadow, which was the leak.

### The numbers that decide what 'soft far from it' can mean
The sun's angular radius is 0.00467 rad. The true penumbra's half-width is the gap between caster and receiver times 0.00467: 0.027 m at 5.86 m, 0.15 m at 31 m, 0.47 m at 100 m. So the true penumbra is narrower than today's ramp of 0.15 m for every gap under 31 m. In a room, a physically right sun gives sharper edges than step 1, not softer ones. A soft edge far from a caster shows only outdoors, at gaps of tens of meters.

So one question comes before the design, and it is the user's: is the sun's size the physical one, or a larger, chosen one? A chosen size is a look, not physics. It is one number on the light.

### Two designs, for the user to combine
| Point | Design A: one kernel, a ramp that follows the gap | Design B: the penumbra in screen space |
|---|---|---|
| Where the softness is made | In the cascade lookup. The 16 taps stay. A blocker search in the same 5 x 5 texels gives the gap. The ramp on the lit side gets a width from the gap, between a least width that still hides the texel grid and 2.5 texels. | After the lookup. A pass writes the sun's share and the gap of each pixel into an image. A blur over screen neighbors on the same surface widens each edge by the penumbra's width at its gap, on the lit side only. The direct pass reads that image. |
| The softest edge | Today's 2.5 texels. It can only make near edges sharper than step 1. | Any width: 0.29 m and more, with no change of the bias or the margins |
| The sharpest edge | Open: how narrow the ramp can get before the texel grid shows. The contour is the kernel's smooth half-lit line, so a steeper ramp can stay clean. A film of the 5 m edge decides. | The lookup's own edge |
| New state | None. One sampler with no comparison beside the comparison sampler. | One image at the render size, written by one new pass, read by the direct pass and by the passes that shade with the sun |
| Cost, estimated and not measured | The search: about 16 depth reads or 4 gathers per lit pixel near an edge | The search, plus two blur passes at the render size |
| Risks | A blocker search at a slope reads the receiver as its own blocker. | Edges at depth steps, the jitter of FSR, and one more reader of the frame's order |

Usage, design A:
```glsl
float gap = blockerGap(coordinate, layer, reference);          // m, or negative with no blocker
float width = rampWidthTexels(gap, light.angularRadius, texel);  // between LeastRampTexels and 2.5
float share = lightOutsideShadow(filteredCascade(coordinate, layer, reference), width);
```
Usage, design B:
```glsl
// The pass 'sun share': the hard share and the gap per pixel. Then 'sun penumbra': the blur. The direct pass:
float share = texelFetch(sunShare, pixel, 0).r;
```
What both share: the rule that no blur puts light inside the shadow, the blocker search, the sun's angular radius read from the light (the probe hard-codes 0.00467), the tiles of the local lights unchanged, and step 1's filter as the base.

### The points for the user's choice
1. The sun's size: physical (sharp edges in rooms, soft ones only outdoors at large gaps), or a chosen larger size (soft edges in rooms too, as a look).
2. The softest edge: today's 2.5 texels at most (design A, small), or wider (design B, a new pass and a new image).
3. Whether near edges may become sharper than step 1 at all. The user's first reaction to step 1 was that shadows must not all become hard-edged.

### The first measurement, before any design is final
- The gaps of the demo's views: the share of shadow-edge pixels by gap, in the cabin, the deck, rect-lamps and one outdoor view.
- One film of the 5 m edge with the ramp at half and at a quarter of 2.5 texels, to find the least width that hides the texel grid.
- Stills of both designs' softest and sharpest edge in the cabin and outdoors, from scratch builds.

**2026-11-22T20:52:59Z**

2026-11-22, the line orchestrator, from the orchestrator. This step no longer waits for the user: the user handed the design choices to the orchestrator on 2026-11-22. The line orchestrator sends the two options with a recommendation when a slot is free, and the orchestrator decides.

**2026-11-23T09:56:54Z**

2026-11-23, the line orchestrator, with the orchestrator's agreement of the same day. Neither design of the note of 2026-11-21 is built. Three reasons. 1. The sun's true half-penumbra is the gap times 0.00467, which is narrower than today's ramp for every gap under about 31 m. So in a room design A can only sharpen edges, and the orchestrator ruled on 2026-11-23 that sharper lamp shadows are not acceptable (t1-gout, no commit B); the user's first reaction to step 1 was that shadows must not all become hard-edged. 2. Design B gives soft edges in rooms only with a sun larger than the real one, which is a look, and it costs one image at the render size with a reader in every pass that shades with the sun. The key and fill shader has no register left on the M1 (t2-x53j). 3. Design L of t1-gout changed the ramp on 2026-11-23: it is 1.5 texels wide and starts half a texel past the map's edge. Any measure of the least ramp that hides the texel grid must start again from there. The ticket's first step, when a slot has nothing else ready: one count over the demo's views of the share of shadow-edge pixels whose caster is more than 31 m from its receiver. If no view has such edges, the ticket closes as not needed. If one does, that view becomes the ticket's test. Priority lowered from 2 to 4.

**2026-11-23T14:11:37Z**

2026-11-23, orchestrator: the user reopens this ticket and corrects the rule that parked it this morning. The user's words: 'can we bring back the shadow softness varying by distance? if increased shadow sharpness is justified by ~physical processes it's fine. I just didn't want them *always* sharp when they shouldn't be'. So the first of the three reasons against a build falls: a sharper edge near a caster is fine where the light's size and the gap give it. The ticket is active again at priority 1, on the back-face line, in a third slot that the Design X line lends. The model, one for the sun and for the lamps: an edge's true width at a receiver is the gap between the caster and the receiver times the light's angular size as the caster's edge sees it. For the sun that size is 0.0093 rad. For a lamp it is the emitter's size over its distance from the caster (the lights carry a radius and an emitter half extent: LightUnits.AngularRadius, SceneBatcher.EmitterHalfExtent). The rendered ramp is the larger of that width and the least ramp that hides the shadow map's texel grid. What I expect, to be measured and not assumed: (1) The sun in a room: cascade 0's texels are 0.058 m, so the true width is under one texel for gaps under 12.5 m. The ramp can only shrink toward the grid's least ramp there, and it grows for casters tens of meters away. (2) The lamps: an emitter is large and its casters are near, so the true width is many texels a short way behind a caster. Today every lamp shadow has one fixed blur. I expect the visible gain there. The tiles' lookup already holds the least and the greatest depth of its window (TileLookup.bounds), which may give the gap without a new search. Constraints: direct_key_fill.frag has 0 registers of room, so each candidate gets a register count before anything else. Design L's rule stays: no sunlight inside a shadow at any place of the texel grid. No noise in the direct light. The first step is a measure with no engine change: per demo view, for the shadow edges of the sun and of each lamp, the gap, the true width and today's rendered width in meters, as a chart of today's width over the true width. Then two designs in this ticket, a probe with stills, and my choice. The ticket's old line 'this step does not merge without the user's choice' is replaced by the user's standing instruction of 2026-11-22: I choose, and the user sees stills of a near and a far caster under the sun and under a lamp, main against the build.

**2026-11-23T14:12:44Z**

2026-11-23, orchestrator: one correction of my note above. The ticket goes to the Design X line, not to the back-face line. The Design X line has a free lead with a built worktree, and the back-face line's two leads are bound for hours by the surface share's gates, the batch probe and the reflection census. The back-face line's analysis of the two designs stays the starting point (its note above), and its record of design L is docs/research/shadow-grid-phase/README.md.

**2026-11-23T14:13:53Z**

2026-11-23, 09:13 New York time, line orchestrator of the Design X line. The ticket is on this line by the orchestrator's note above. Its first step starts now: the measure with no engine change, by the lead of the render scale's dip, in its worktree t3-52bfcd29-81249085ce80dd9f, branch t1-af5w-shadow-softness from main 8c1343118. Terms that the step uses, each with one meaning: the gap = the distance in m along the light's direction between the caster's edge and the receiver; the light's angular size = the full angle in rad under which the caster's edge sees the emitter (the sun: 0.0093; a lamp: its emitter's width over its distance from the caster); the true width = the gap times the angular size, in m on a receiver that faces the light; the rendered width = the width in m on the receiver over which today's filter takes the light's share from 0 to 1. The step brings a table and a chart of the rendered width over the true width per view and per light. After it I write the two designs in this ticket, and the orchestrator chooses.
