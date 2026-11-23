---
id: t1-af5w
status: in_progress
deps: [t8-rjno]
links: [t58-7645]
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

**2026-11-23T14:15:03Z**

2026-11-23, line orchestrator of the back-face line, on the orchestrator's request: four facts about shadows.glsl and the tiles that this ticket and design L's record do not state in one place. Terms: u = the distance in map texels from the map's edge of a straight caster to the lookup, positive on the lit side; the lit fraction = the share of the 4 x 4 filter's comparisons that are lit. 1. The cascades' ramp has a hard upper width with today's taps. The lit fraction is 1/2 + u / 4 and reaches 1 at u = 2. Design L's rule gives a share of 0 up to u = 1/2. So the widest ramp that keeps design L's rule is 1.5 texels, and today's ramp already is that one. A narrower ramp is a remap of the lit fraction with no new read. A wider ramp needs more taps or a pass. A change of FilterTapsPerAxis changes three more places (the comment above the constant names them): ShadowBias.FilterReachTexels, the reads of tileLitFraction, which are written for 4 taps, and CascadeDoubtfulLitFraction in ShadowTests. 2. The tiles' ramp is the lit fraction itself: 4 texels wide and centered on the map's edge, so 2 texels of it lie inside the shadow. That part is the leak of t1-hdtm (about 6 px down the exit cabin's beam face). A ramp on the tiles that narrows at a small gap also shrinks that leak, so t1-hdtm's crop is a test of this ticket's model, and the two tickets have one root on the tiles. 3. A steeper ramp amplifies the tiles' acne by its slope. Measured in t1-gout with the cascades' rule on the tiles (slope 1 / (1 - 5/8) = 2.67): in ALitFloorUnderASpotLightHasNoAcne at 60 degrees with a 45 degree turn, 634 of 25,784 pixels differ at 4 m (main 4) and 5,505 of 135,815 at 12 m (main 2), about 2.3 percent darker, largest 8 of 255 (t1-hdtm holds the table). The hazard for this ticket's model: on lit ground that shadows itself, the gap is near 0, so the true width is near 0 and the model asks for its steepest ramp exactly where the acne is. The gap rule needs an answer for a window whose only caster is the receiver's own surface. My interpretation, not tested: the amplification comes from a ramp whose upper end is the lit fraction 1, and an upper end below 1 would leave a dead band for the acne. 4. Three texts carry the reading that the user has now corrected, and a build under this ticket changes them: the comment above tileShadow in shadows.glsl ('which the user does not accept'), the paragraph 'The lit side of a cascade's edge' in ARCHITECTURE.md, and t1-hdtm's text. The acne of fact 3 is a second, measured reason in each of them, and it stays true. The readers of both functions are in lighting.glsl (tileShadow at line 503, cascadeShadow and cascadeShadowHolding at 513 and 518), so every change here is in the include closure of both guarded shaders.

**2026-11-23T14:53:22Z**

2026-11-23, 09:53 New York time, line orchestrator of the Design X line. The measure's first result, and the orchestrator's three points for the designs.

Measured by the lead (a scratch probe, 12 views at 1280 x 720; the record goes to docs/research/t1-af5w-shadow-softness): the sun's rendered width is 0.088 m in cascade 0, and its rendered width over its true width is 1.1 / 3.9 / 60 at the 10th, 50th and 90th percentile in the cabin; only bay-sunbeam's gaps of 12 m and more are drawn too sharp (0.6 / 0.8 / 1.8). The lamps go both ways: emitters 1.2 m wide with gaps of 5 to 15 m have true widths of 1 to 2 m and are drawn 0.12 to 0.27 m wide, and edges with gaps under 0.5 m are drawn 10 to 200 times too wide. 26 to 59 % of some lamps' edge pixels have a gap under 5 cm: receivers that shadow themselves.

The orchestrator's points (2026-11-23): 1. The sun gets no design of its own unless the stills ask for one: the record states the least ramp that hides the grid after design L and the cm that the sun's edge could gain at contact; mars-day's 3.5 m is a cascade's texel size and belongs to a ticket of its own. 2. For the wide half at the lamps, three candidates, each to be checked against the situation that it was made for: several shadow views across an emitter whose shares the lookup averages (the orchestrator's lean, because the geometry makes the width and no gap estimate can be wrong), a prefiltered tile (its known fault is light that bleeds where casters overlap in depth), and a screen-space blur of a key light's share (an image, a rule that mixes only pixels with the same key light, widths of 100 pixels and more). 3. The sharpening half at contact is a remap of the ramp by the gap that both designs can share; the gap must be the distance to a caster that is not the receiver's own surface, and a design that cannot know that does not sharpen.

One fact for candidate three, from key_fill.glsl: a tile of 16 pixels lists up to 16 lights (TileEntries), and each pixel looks up a shadow for each listed light. So a blur of the lights' shares in screen space needs a share per listed light per pixel, not one image.

The lead now adds four counts to the measure: the sun's possible gain at contact, the lamps whose emitter is wide enough to matter with the cost of one more tile and one more lookup, what separates a self-shadowed receiver from a true contact edge, and which fields of GpuLight hold an emitter's extent.

**2026-11-23T17:00:55Z**

Step 1's measure (no engine change) is on the branch t1-af5w-shadow-softness: docs/research/t1-af5w-shadow-softness/README.md. It gives, per view and light, the gap, the true width and the rendered width; the GpuLight fields that hold the emitter's extent; how well TileLookup.bounds.y gives the gap (within 20 % of the blockers' mean at 88 % of the lamps' edge pixels); the register counts of three candidates; stills and 1:1 crops; and the line orchestrator's four additions. The sun's least ramp after design L is not measured, so its gain at contact is at most about 3 cm. No reading separates a self-shadow from a contact edge at gaps under 5 cm. The far cascades' ramp in mars-day is ticket t58-7645.

**2026-11-23T17:17:45Z**

## Two designs (the line orchestrator of the Design X line, 2026-11-23, 12:17 New York time). No build before the orchestrator's choice.

The measure is docs/research/t1-af5w-shadow-softness/README.md. This note uses its terms: the gap, the light's angular size, the true width, the rendered width. Three more terms:

- **The footprint** is the square on a lamp's tile, centered on the lookup, whose lit share the lamp's share at the receiver is. Its width is in tile texels. Today it is 4 texels wide for every lamp and every gap.
- **The own-plane span** is the depth in m, toward the light, that the receiver's own plane covers inside the bounds window. It is 3 texels times the size of a texel at the receiver times tan θ, with θ the angle between the receiver's normal and the direction to the light.
- **The trusted gap** is the gap minus the own-plane span minus the depth bias. A caster with a trusted gap above 0 lies nearer the light than any point of the receiver's own plane inside the window can lie.

### What the measure decides before any design

1. **The sun gets no design.** Its edge at contact can narrow by at most about 3 cm (0.088 m to 0.058 m), and by nothing if the least ramp after design L is 1.5 texels. Variant C (the cascades' ramp width from a uniform) costs no register, so the sun can follow later at no cost if a still asks for it. The far cascades of mars-day are ticket t58-7645.
2. **The lamps need both halves.** By pixel count the too-soft edges are the larger group: 23 to 98 % of a lamp class's edge pixels are more than 2 times too soft, and near casters are 10 to 200 times too soft. The too-sharp edges are the panels with far casters: the 1.2 m panels are 6 to 13 times too sharp where the gap is 8 to 11 m, and the 0.15 x 0.5 m panels 2.6 to 4.3 times.
3. **A wide edge is beyond the taps.** A true width of 0.5 to 2.2 m is 8 to 70 tile texels. A footprint of 30 to 70 texels holds 900 to 4,900 texels, against the 25 that today's nine gathers read.
4. **t1-hdtm is not this ticket's test.** On the beam's face the window's only caster is the face itself, at sin θ 0.98. Its own-plane span is 3 x 0.024 m x 4.9 = 0.35 m, and the gap reads 0.078 m. So no gap rule may sharpen there, and the leak keeps its own ticket. This corrects fact 2 of the back-face line's note of 2026-11-23.

### The narrow half, shared by both designs

One model: a lamp's share is the lit share of a footprint whose width is the true width, but at least the least ramp that hides the texel grid. Up to 4 texels, a remap of today's 16 taps gives that share for a straight edge, with no new read:

```glsl
// w: the footprint's width in texels, from the least ramp to 4. lit: tileLitFraction's result.
float share = clamp(0.5 + (lit - 0.5) * 4.0 / w, 0.0, 1.0);
```

- **The ramp stays centered on the map's edge.** It ends at the lit fractions 0.5 - w / 8 and 0.5 + w / 8. So for w under 4 the ramp has a dead band at both ends. Acne that fails up to (4 - w) / 8 of the taps gives the share 1, where today it darkens the pixel. The refused rule of t1-gout ended its ramp at the lit fraction 1, which is why it raised the acne by 8/3. The light inside the shadow reaches w / 2 texels, where today it reaches 2.
- **How the design knows that the caster is not the receiver's own surface** (the orchestrator's point 3): w follows the true width only where the trusted gap is above 0. Where it is 0 or less, w is 4 and the share is today's. Between a trusted gap of 0 and one own-plane span, w fades from 4 to the true width, so no seam shows. The measure found no separator at gaps under 5 cm, and this rule needs none: under the own-plane span it does not sharpen.
- **What this costs in the image, to be seen in the probe:** a contact edge keeps today's width for about one ramp's length around the point of contact, and it is sharp beyond that. A receiver in grazing light never sharpens.
- **The gap** comes from `TileLookup.bounds.y`, with no new read: the caster's distance is `d * ndc.z / bounds.y`. It is within 20 % of the blockers' mean at 88 % of the lamps' edge pixels, and where it differs it is the larger one, so the edge is the softer one.
- **The emitter's size as the caster sees it** is the side of the square with the emitter's projected area: the square root of the area times the cosine between the emitter's axis and the direction to the caster, for a rect; 2 R for a sphere. `GpuLight` holds all of it. (My choice. The measure used the largest width.)
- **The least ramp of a tile is not known.** The probe takes 1, 1.5 and 2 texels.
- **Registers:** variant A is this remap without the trust rule, and it puts `direct_key_fill.frag` at r116, 1 over. It holds `bounds.y` and the texel's size across the taps. The first thing to try is to fetch the bounds texel again after the taps, so that nothing new is held across them.

### The wide half: the three candidates against the situation that each was made for

The sources are named from memory. The designer of the chosen one reads its paper before the build.

| Candidate | Made for | Ours | Verdict |
|---|---|---|---|
| Several views across the emitter, shares averaged | Accumulated hard shadows (Haeberli and Akeley 1990; Heckbert and Herf 1997): tens to hundreds of points per emitter, for still scenes or textures made ahead. The count of points gives the quality | k points per axis draw k copies of an edge, the true width / k apart, each as wide as the rendered width. They merge only where the true width / k is at most the rendered width. The 1.2 m panels with far casters need k of 6 to 13 per axis, 36 to 169 points of 6 tiles each. With k = 2, the widest that the atlas and the lookups can pay, an edge of 2 m is two lines 1 m apart | Right where the true width is under about 0.5 m. Steps beyond |
| A prefiltered tile (variance shadow maps, Donnelly and Lauritzen 2006; variable widths by Lauritzen 2007 and by Yang and others 2010) | One light, one map of 1024 x 1024, a width of up to hundreds of texels at a cost that does not grow with the width. Scenes of a few casters over a ground | Up to 8 lamps of 6 cached tiles. Widths of 8 to 70 texels, which no count of taps reaches. Its fault is light where two casters at different depths overlap inside the footprint, and our cabins have such casters | Applies. The only candidate whose cost does not grow with the width. The fault's case is common here, so the cabins' stills decide |
| A screen-space blur of the key light's share (Gumbau and others 2010; MohammadBagher and others 2010) | One key light in a deferred renderer, widths of tens of pixels | Each pixel looks up a shadow for up to 16 listed lights, so it needs one share image per light. A width of 2 m at 5 m is about 400 pixels | Does not apply |

A fourth idea, coarser copies of the depth tile read with today's taps, fails on thin casters. A coarse texel holds one depth, so a rail 5 cm wide is either lost or as dark as a wall, and its far shadow must be a faint band.

### Design 1: a pyramid of the tile gives the wide footprint

The footprint keeps one model for every width: the lit share of a square of the true width. Up to 4 texels the taps and the remap give it. Above 4 texels one filtered read of a pyramid gives it.

- **Stores, owned by `ShadowAtlas`:** a second array image, `Moments`, RG32F at half the tile's size with 5 levels (256 down to 16). A texel holds the mean and the mean square of the casters' distance from the light, as a share of the light's reach. The bounds image gets the same 5 levels, each the least and greatest of the level below. 64 tiles cost 45 MB of moments and 11 MB of bounds levels. RG32F with a linear filter is a required feature on every desktop driver and on lavapipe.
- **Writes, in `LocalShadowPass`, after a tile's draw and beside `shadow_bounds.comp`:** level 0 of the moments from the tile's depth, then 4 levels of the moments (the mean) and of the bounds (the least and the greatest). A cached tile keeps all of it, so a still scene pays nothing.
- **The lookup:**

```glsl
float tileShadow(GpuLight light, vec3 position, vec3 L, float d, vec3 geometricNormal, float sinTheta)
{
    TileLookup t = tileLookupOf(light, position, L, d, geometricNormal, sinTheta);
    // The footprint's width in texels: the true width from the nearest caster inside the emitter's
    // reach on the tile (one read of a bounds level), and 4 where the gap is not trusted.
    float w = footprintTexels(t, light, d, sinTheta);
    if (w <= 4.0)
        return rampOf(tileLitFraction(t), w);            // the narrow half
    return pyramidShare(t, d / light.reach, w);          // one filtered read of Moments at level log2(w / 4)
}
```

- **What it must know:** the gap. It takes the caster nearest the light inside the window, so where two casters overlap in depth it draws the softer edge.
- **Where it goes wrong:** the share from two moments is exact for one caster over one receiver plane. With a third depth inside the footprint it lets light through. The usual cut of the low shares, clamp((share - b) / (1 - b)), trades that light for darker edges. The probe picks b from the cabins' stills, or shows that no b is clean.
- **Cost:** a lit pixel near a wide caster takes 1 more bounds read. A wide edge pixel takes 1 filtered read in place of 9 gathers.

### Design 2: the emitter gets several views

A lamp's share is the mean of the shares from k points of its emitter. The geometry makes the width.

- **Stores:** a wide emitter gets k sets of tile views, one per point. `AtlasTiles(First, Count)` holds k x 6 views. The points are the centers of k equal parts of the emitter: 2 x 2 for a 1.2 m panel, 2 along the long axis for a 0.15 x 0.5 m panel, 1 for a sphere of 0.05 m.
- **Writes:** `ShadowTiles.Update` grants a wide light k x 6 layers, and its rank counts them. Each point's views are drawn and cached as today's.
- **The lookup:**

```glsl
float share = 0.0;
for (uint point = 0u; point < points; point++)
{
    // The direction and the distance to this point of the emitter, in place of its center.
    vec4 toPoint = emitterPoint(light, point, position);
    share += tileShadowOf(light, point, position, toPoint.xyz, toPoint.w, geometricNormal, sinTheta);
}
return share / float(points);
```

- **What it must know:** nothing about the gap in the wide half. Casters that overlap in depth are right, and a panel of 0.15 x 0.5 m gets its two different widths.
- **Where it goes wrong:** steps, by the table above. With k = 2 it is smooth for the 1.2 m panels of exit-cabin (true width 0.21 m at the median), exit-deck (0.07 m) and rect-lamps (0.38 m), and it draws two lines for the far casters of the cabins and the pads (1.3 to 2.2 m).
- **Cost:** exit-cabin's 8 lamps go from 48 tiles to 144, and the atlas holds 64. 144 tiles are 144 MB of atlas and 72 MB of bounds. A panel's lookups are 4 times today's, a small panel's 2 times: about 3 times the lamps' lookups in exit-cabin. A moving caster redraws up to 4 times the tiles.

### The two designs side by side

| | What makes a wide edge wide | What it must know | Where it goes wrong | New memory | Reads at a wide edge pixel | Widths that it draws smoothly |
|---|---|---|---|---|---|---|
| Design 1, the pyramid | One filtered read of a prefiltered tile | The gap, from the bounds | Light through casters that overlap in depth | 56 MB | 1 in place of 9 gathers | Any, up to about 64 texels |
| Design 2, the views | The mean of k real views | Nothing | Steps where the true width is above k times the rendered width | 120 MB for exit-cabin at k = 2 (216 MB against today's 96 MB) | 4 times 9 gathers | Up to about 0.5 m at k = 2 |

Both share:

- The narrow half above: the remap, the trusted gap, the least ramp.
- The sun and design L stay as they are. No noise in the direct light, and no path trace.
- The register limit of r115 in `direct_key_fill.frag`. Each design's lookup gets a register count before its first still.
- The texts that carry the corrected reading: the comment above `tileShadow`, the paragraph "The lit side of a cascade's edge" in ARCHITECTURE.md, and t1-hdtm's text.
- The tests: the acne counts of `ALitFloorUnderASpotLightHasNoAcne` at 4 m and 12 m do not rise, and one new behavior test: under one lamp, the edge of a caster that touches its receiver is narrower than the edge of a caster 2 m away.

### The order of the work

The narrow half is the same in both designs, and neither design changes it. So it is probed first, and it can merge first if its stills are clean. The wide half follows on the orchestrator's choice.

### My recommendation

Design 1 for the wide half, for one reason: the widths that the measure found are 8 to 70 texels, and only the pyramid's cost does not grow with the width. Design 2 is the honest mechanism, and I would take it if k = 2 were enough. By the arithmetic it is enough in three of the measured views and draws two lines in the others. The probe shows both, and the stills of the cabins decide whether the pyramid's fault shows.

**2026-11-23T17:19:30Z**

2026-11-23, 12:19 New York time, line orchestrator of the Design X line. The probe of the two designs starts now, on my own decision (answer 102), with the lead of the measure in its worktree t3-52bfcd29-81249085ce80dd9f, branch t1-af5w-shadow-softness: scratch patches only, a register count before each part's first still. Part 0 is the narrow half, which both designs share. Part 2 is design 2 at k = 2. Part 1 is design 1 with the cut b at 0, 0.2 and 0.4. Task node:delegated-task:command%3Amcp%3A7a182238-7151-4406-a5ac-7cc160bd69e2%3Athread-send%3A8e26546c-b33f-4c67-8935-6912f44c15b1, child thread thread:delegated-task:command%3Amcp%3Aa061e2e4-17a4-45d5-ac91-153bf5752d2c%3Adelegate-task%3Adesign-x-line-t0-gbgj-lead-1.

**2026-11-23T17:21:46Z**

2026-11-23, 12:21 New York time, line orchestrator of the Design X line: the orchestrator's answer to the two designs. 1. Its lean for the views across the emitter is withdrawn: at k = 2 an edge of 1 to 2 m is two lines about 1 m apart, a visible fault, and where k = 2 is enough the pyramid serves too. Design 2 can win only if design 1 bleeds visibly. 2. The probe's order: the narrow half, then design 1's pyramid, then design 2 only if design 1's light through overlapping casters shows in a cabin still at 1:1. 3. Design 1's deciding take is a cabin view with casters that overlap in depth, at 1:1, main beside the probe. If it bleeds, the lead tries the known reductions in the same probe (a floor on the lit share after the moment test, or exponential moments) with their cost in registers and in ms. 4. The sun gets no design in this ticket. 5. The narrow half is a first merge on its own if it fits r115; if not, the fork goes to the orchestrator with its cost. To try first: 4.0 / w out of the lookup that already yields the bounds. 6. The stills for the user at the end: a near and a far caster of a lamp, main beside the build, frozen world, settled exposure, 1:1. Answer 102 stands with this order. Sent to the lead at 12:21.

**2026-11-23T17:39:30Z**

2026-11-23, 12:39 New York time, line orchestrator of the Design X line: part 0 of the probe, the narrow half. Registers of direct_key_fill.frag (limit r115): 4 / w as one float out of tileLookupOf without the trust rule r115; with the trust rule r117 (the angle from dot(geometricNormal, L) after the fetch r117, the rule in tileShadow from two floats r116, the two packed in one uint r116). The cause is one more value that lives across the bounds fetch. Stills (the lead's pics/part0): with the trust rule on, every view equals main within 0.24 % of the pixels (exit deck, least ramp 1), and the beam face of t1-hdtm does not change. With the rule off at 1.5 texels, 0.43 % of the exit deck's pixels change, the lit patch on the beam face ends about at the beam's edge, and a frame in grazing light gets a slightly darker soft band. The acne test: main 4 of 25,784 at 4 m and 2 of 135,815 at 12 m; rule on 4 and 2; rule off 0 and 0. No texel staircase at a least ramp of 1. The niche of rect-lamps does not change in any column, also with the rule off: the cause is asked of the lead. My reading: the narrow half is clean and shows close to nothing, so the visible softness has to come from the wide half. The fork went to the orchestrator: the rule on (ruled out by me: r117 for a picture that equals main), the rule off at r115 (breaks the orchestrator's rule of point 3; open risk: crawl in grazing light), or no narrow half now (my recommendation, and the rule off only after a film of a slow camera move over grazing surfaces).

**2026-11-23T17:42:18Z**

2026-11-23, 12:42 New York time, line orchestrator of the Design X line: the orchestrator decided part 0's fork. No narrow half now. The trust rule on is out: r117 and 0.10 to 0.36 ms for a picture that equals main. The trust rule off waits for a film of a slow camera move over the exit deck's frame and a floor in grazing light, main beside the rule off; if nothing crawls it merges as its own small change, with the leak ticket's still (t1-hdtm) as its evidence. The softness that the user asked for comes from the wide half, so the pyramid's scratch probe is the work now, and its deciding take is the cabin view with casters that overlap in depth. In the pyramid's lookup, widths of 4 texels or less use today's ramp. For the engine design: the far-field store's writer becomes the lights' shadow views with a color beside the depth (ter-6fka, main 7de78028f), which the back-face line probes. Two probes add data to a shadow tile, and neither merges yet. The pyramid's engine design must name the one owner of a tile's contents and freshness, so that a second attachment has a place to go. Disk rule from the orchestrator until the disk recovers: no new worktree and no full build under 22 GiB free; an incremental build that adds under 0.5 GiB may run down to 18 GiB; under 18 GiB every lead stops.

**2026-11-23T18:03:28Z**

2026-11-23, 13:03 New York time, line orchestrator of the Design X line: part 1 of the probe, the pyramid (the lead's pics/part1). 1. The first stills showed three faults: hard four-sided patches at lamp 15's far caster, light through the shadow at the exit deck's wall base, and a wash-out of the floor's bands. 2. The hard patches were my lookup's fault: the width came from one read of a coarse level of the bounds and was constant over a coarse texel of 32 tile texels. A bilinear read of that level (width form 1) gives a smooth width, and the same footprint drawn by a brute-force filter of 8 x 8 comparisons is smooth and clean. The blockers' mean from the moments (form 2) fails: the width saturates at 64 texels. 3. The wash-out is the width, not the moments: the brute-force filter at the same footprint washes out as much. The footprint's width under form 1 is 3.8 / 5.7 / 10.8 texels (10th / 50th / 90th percentile) at the far caster, 3.6 / 11.1 / 13.3 on the deck's floor, 4.7 / 6.7 / 8.3 on the cabin's floor; the measure's true width at the far caster is 14.6 texels. 4. The moments fail: they light the wall base's dark band where the brute-force filter keeps it, and they are brighter than that filter at the same footprint. The cut b up to 0.4 and exponential moments at c = 40 clean neither. Registers r116 in both direct shaders. 5. An error of my design's premise: I ruled out direct taps for widths of 30 to 70 texels, which holds only for the few far casters of the cabins and pads; on the deck and the cabin floor the footprints are 4 to 18 texels. My decisions (answers 105 and 106): no more work on the moments; the views stay out; a third form is probed, the footprint by direct taps with a cap (cap 8: a box of 8 texels by 64 corner means, with the box of 4 from the same reads; cap 16: the brute-force filter, for the picture only), with its register count, the direct pass's time and the share of pixels that take the 64 reads; and a reference crop by the back-face line's Cycles export decides whether the wash-out is the right look.

**2026-11-23T18:22:56Z**

2026-11-23, 13:22 New York time, line orchestrator of the Design X line: the reference and the capped taps (the lead's pics/part1/ref-*.png and cap-cabin-floor.png). The reference is the back-face line's Cycles export of exit-deck, 2,048 samples; the take with direct light only and no sun answers the edge question (the take with all bounces is 3.1 times as bright as the engine and nearly flat; the engine's image is 1.86 times the direct reference's mean). At lamp 15's far caster the truth is one broad dark band where main's is, with much softer edges; cap 8 and cap 16 have its shape. On the floor under the glass frames the truth is broad soft gradients; cap 16 is the closest, then cap 8. At the wall base the direct reference's band is black with sharp edges, a contact shadow under a ledge, and every engine column draws a soft grey band: a contact case, not a wide one. The moments also draw hard stripes and steps on the cabin's floor. Cap 8 as probed: r127 in both direct shaders (the width logic alone r116 and r119), and 92.5 % of exit-deck's pixels and 99.6 % of exit-cabin's take the 64 reads, because the skip reads bounds of 16 texels. My decisions (answers 107 and 108): the lead builds two cuts before the timing (each lamp skips by its own window; only the lamps whose footprint can pass 4 texels enter the wide path) and diagnoses what holds the registers in the width logic; and it checks the wall base like for like, the engine's direct light only beside the direct reference, for main and for the trust rule off.

**2026-11-23T18:54:09Z**

2026-11-23, 13:54 New York time, line orchestrator of the Design X line: the cuts of cap 8, the registers and the like-for-like check (the lead's sheets in /private/tmp/t0-gbgj/p1c/sheets, to be committed with the timing). 1. The cuts: per lamp and pixel, first the width from one bilinear read of level 1's nearest caster (w of 4 or less runs today's filter unchanged), then the exact least and greatest depth of the cap's own 9 x 9 texels from four level-0 windows, and only the rest takes the wide reads. The share of pixels where at least one lamp takes them falls from 92.5 % to 26.3 % in exit-deck and from 99.6 % to 77.0 % in exit-cabin; the 1.2 m panels hold it (19.6 % and 76.1 %), the 0.15 x 0.5 m panels 4.3 % and 4.5 %, lamp-hall-16's spheres 3.7 %. A probe bug was fixed on the way: the width took the trust rule's untrusted term without the clamp at 4 and sent contact pixels to the wide path. 2. Registers (direct.frag / direct_key_fill.frag, main r112 / r115): cut cap 8 by 64 corner means r124 / r126; by 25 gathers r115 / r117; with one shared lookup r119 / r122 and r116 / r118. The wall in key and fill is the second run of tileLookupOf (r117 with nothing else), the 64 means are a wall of their own (+8), the 25 gathers are none, and the width logic adds at most 1. So the best count of the wide half is r117, two over. The M1 runs today's box of 4 as 16 corner means, because MoltenVK prices held values (0.37 ms faster than nine gathers in the deck; on NVIDIA the gathers were 0.8 ms faster). 3. Like for like, direct light only and no sun, engine against the Cycles direct reference: the engine's direct light is black at the wall base in every column, with edges as sharp as the reference's, so the contact shadow does not leak, and the grey band of the full image is the engine's indirect light. The trust rule off changes that band only slightly, so this check gives the narrow half no case. The mean brightness agrees: engine over reference 1.00 at the wall base, 1.05 at the far caster, 1.01 over a square of 330 pixels. At the far caster main's band has hard edges, the reference's is broad and soft, and cut cap 8 lies close to the reference. I opened direct-deck-far.png and direct-deck-wall-base.png and read them the same way.

**2026-11-23T19:24:54Z**

2026-11-23, 14:24 New York time, line orchestrator of the Design X line: the timing of the cut cap 8 (the GPU lock held alone, medians of interleaved repetitions, ms). The sum of key and fill, the direct light and the light pyramid: exit-deck at scale 2/3, main 4.30, 64 means +0.75, 25 gathers +0.70; exit-cabin at 0.96, main 8.14, means +1.75, gathers +1.31. The transparent pass and FSR 3.1: +0.39 to +0.81. The frame span: exit-deck +1.09 (gathers), exit-cabin +2.36. The repetitions agree within 0.05 ms in the direct sums. The gathers are cheaper than the means in the direct pass, also on the M1. The cost follows the share of pixels that enter the wide path (exit-cabin's 1.2 m panels: 76 %). The fork went to the orchestrator: no wide half; the capped taps as probed; or the capped taps with a later entry w0 (the wide path starts at a footprint of w0 texels, the blend to the box of 8 runs over w0 to 8). My decision (answer 109): before the film the lead takes the floor of the wide path (its reads replaced by one value, against main) and the share of pixels over w0 = 4, 5, 6 and 7, and times the largest w0 whose direct-only crops still look like cap 8 beside the reference. If the floor alone is about 0.5 ms, I recommend no wide half.
