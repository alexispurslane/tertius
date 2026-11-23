---
id: t1-gout
status: in_progress
deps: []
links: []
created: 2026-11-22T19:25:27Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, shadows]
---
# A window opening's edge lights up with sunlight at some positions of the sun's shadow texel grid: a bright line and a glow on the jamb that depend on the camera's aspect

Found on 2026-11-22 by the Design X line's combined-check lead as the jamb leak of its bug ticket `t58-i3sn` (written on that line's branch, not on main yet). The orchestrator moved the jamb leak into the back-face line on 2026-11-22, because the sun's shadow lookup is this line's (`t8-rjno`, with its step 2 `t1-af5w`). Priority: behind the wobble's fix `ter-qt72`, ahead of the ceiling's speck cloud `ter-fe63`.

## What the user sees

In the cabin of the scene `orbit` (camera `person`, 60 ticks, the lamps on), the nearest window openings of the side walls lie at the picture's left and right edges. At some shapes of the picture, a bright vertical line stands where the jamb ends and the dark opening begins. Light glows from it onto the jamb's face, and bright dots lie along the sill. At other shapes the jamb is grey. With the sun off, nothing lights up.

- Crops, the dynamic scale's sizes on main `a819b10b2`: /Users/alexispurslane/.t3/worktrees/tertius/t3-52bfcd29-81249085ce80dd9f/docs/research/t0-gbgj-combined-check/pics/ramp-M3-openings.png
- Crops, a window of 1800 x 1008 against 1920 x 1080 at a render scale of 1: .../pics/window-1800x1008-M3-W.png in the same folder.
- Chart of the jamb light against the aspect error: /Users/alexispurslane/.t3/worktrees/tertius/t3-1b834d57-evidence/jamb/jamb-light-against-aspect-error.png

Before the wobble's fix, the dynamic render scale changes the camera's aspect from draw to draw, so the line flickers. After that fix, the aspect is the window's. A 16:9 window then shows a grey jamb at this opening, and a window of 1800 x 1008 shows the line all the time. So the wobble's fix hides the fault at 16:9 and does not cure it.

## The measurements so far

"Jamb light" is the display-linear mean of a crop of the jamb: right jamb x 1770 to 1807, y 495 to 584 of a 1920 x 1080 picture, left jamb x 113 to 145, y 500 to 579. "Aspect error" is (width / height) / (16/9) - 1 of the size that the camera's aspect comes from.

The ramp over the 181 render sizes of a 1920 x 1080 output (tools/sizeramp.sh and sizeramp.py of the Design X acceptance record, 40 draws per size):

| Code | Sizes with a jamb light above 0.08 | Largest jamb light |
|---|---|---|
| main `c4c1f1f2f`, without the back-face layer | 74 | 0.77 |
| main `a819b10b2`, with the layer and the walk fix | 32 | 0.24 |
| the wobble fix's branch `839bce1b5` | 0 | 0.0468 |

A window at a render scale of 1, so that the render size is the window:

| Code | Window | Aspect error | Right jamb | Left jamb |
|---|---|---|---|---|
| main `a819b10b2` | 1800 x 1008 | +0.45 % | 0.0977 | 0.0582 |
| main `a819b10b2` | 1920 x 1080 | 0 | 0.0436 | 0.0435 |
| main `a819b10b2` | 1784 x 1008 | -0.45 % | 0.0437 | 0.0437 |
| main `a819b10b2` | 1792 x 1008 | 0 | 0.0438 | 0.0433 |
| main `a819b10b2` | 1808 x 1008 | +0.89 % | 0.0435 | 0.0431 |
| main `a819b10b2` | 1816 x 1008 | +1.34 % | 0.0510 | 0.0591 |
| the wobble fix's branch `839bce1b5` | 1800 x 1008 | +0.45 % | 0.0977 | 0.0582 |

On the ramp of main `a819b10b2`, the jamb light is a sawtooth in the aspect error, on one curve for every render width. It jumps up at -0.34 %, +0.45 % and +1.25 % and falls back to the floor (about 0.045) within about 0.3 %. So it does not grow with the error: it wraps.

## The model (the line orchestrator, from the code; the period is checked against the measurement)

The texel size of the sun's cascades depends on the camera's aspect, and the texel grid is fixed in the world (src/Tertius.Rendering/Scene/ShadowCascades.cs, `Fit`):

```csharp
            (float centerDepth, float radius) = BoundingSphere(
                near: near,
                far: far,
                tanHalfX: camera.TanHalfX,
                tanHalfY: camera.TanHalfY
            );
            float texel = 2f * radius / (mapSize - 2 * MarginTexels);
            ...
            center.X = (float)(anchorX + Math.Floor((center.X - anchorX) / texel + 0.5) * texel);
            center.Y = (float)(anchorY + Math.Floor((center.Y - anchorY) / texel + 0.5) * texel);
```

where

- `near`, `far` = the view depths where the cascade's slice of the view starts and ends, in meters. Cascade 0 runs from 0.05 m to 50.64 m.
- `camera.TanHalfX`, `camera.TanHalfY` = the tangents of half the camera's horizontal and vertical field of view. `TanHalfX` is `TanHalfY` times the camera's aspect.
- `radius` = the radius of the smallest sphere around that slice, in meters. For cascade 0 it is `far * sqrt(TanHalfX² + TanHalfY²)`.
- `mapSize` = the side of a cascade's shadow map, 2048 texels. `MarginTexels` = 4.
- `texel` = the world size of one texel of the cascade, in meters: 0.05847 m for cascade 0 at 16:9 and a vertical field of view of 60 degrees.
- `anchorX`, `anchorY` = the place of the origin of the camera's reference frame (here the ship's center) on the light's two lateral axes. The texel grid passes through it.
- `center` = the cascade box's center in light space, snapped to that grid.

So the grid's lines lie at whole multiples of `texel` from the ship's center. A change of the aspect changes `texel`, and the grid slides against every edge that is not at the ship's center. An edge a lateral distance D from the anchor sees the grid's phase, D / texel modulo 1, wrap once whenever texel changes by one part in D / texel.

The check: the three teeth are 0.79 and 0.80 % of aspect apart. With the formula above, one texel of phase between the teeth at -0.34 % and +0.45 % needs D = 9.75 m, and between +0.45 % and +1.25 % it needs D = 9.73 m. The side walls of the cabin run from x = 9.5 m (`Cabin.X`) to x = 10 m (`Hull.X`) from the ship's center, so the middle of a jamb is at 9.75 m. The match says that the leak follows the grid's phase at the wall. It does not yet say which edge leaks (the wall's middle, where a pane can sit, or a face seen at an angle to the light's axes).

On main before the wobble's fix, the projection, the view rays, the culling, the cascades, the motion and FSR's constants all read one camera view. So no consumer with a second aspect is involved (the audit of `ter-qt72`).

## The class of the bug

A lookup whose answer depends on where a fixed texel grid falls against a thin edge. The sun's cascade lookup is one member. The local lights' shadow tiles keep the symmetric filter on their own texel grid: step 1 says whether they have a member.

## Step 1: the cause, measured. No remedy is built in this ticket before the orchestrator's word.

1. The numbers of the lookup at the leaking line, at 1800 x 1008 against 1920 x 1080, both as windows at a render scale of 1: which cascade, its texel size, the grid's phase at the jamb on both lateral axes, the sun's direction in the ship's axes at tick 60, and which surface the bright line is (the jamb's outer end, a pane's edge, or the hull's outer face).
2. Which comparison lets the light through: the one-sided rule `lightOutsideShadow` of `t8-rjno` (a lit fraction just above one half), the slope-scaled bias of a caster that the sun grazes, the constant bias, the normal offset, the wall's thickness of 0.5 m against a texel of 0.058 m, or a cascade's border. Say whether a thin strip at that place is truly sunlit: if it is, the fault is that the strip shows at some phases and not at others, and not that light passes a wall.
3. A CPU test without a GPU that makes the leak red: the cascade's rasterized depths of a thin wall edge and the lookup's 4 x 4 bilinear comparisons, with the grid's phase moved through one texel in small steps. The result at the receiver must not jump with the phase.
4. Why the jamb light at the render size 1800 x 1008 is 0.230 on the ramp (upscaled by FSR to 1920 x 1080) and 0.098 in a window of 1800 x 1008. Check first how the window test placed its crop. Then say what spreads the line's light after the lookup: the screen rays, bloom, FSR, or a clamp that cuts rare bright samples at a smaller size (`ter-18hc`). The ramp's amplitude also rises with the render width at one aspect error.
5. What the back-face layer changes: 74 leaking sizes and 0.77 without it, 32 and 0.24 with it. The line itself is direct light, so say which part of the jamb light is the line and which part the screen rays add.
6. Whether the sun's slow turn in the orbit moves the phase too, so that such a line comes and goes over minutes in a 16:9 window. If so, find a time and an opening where 16:9 shows it, as the tracked demo case.
7. Whether the local lights' tiles show a member of the class in any scene of the survey.

The deliverable is docs/research/shadow-grid-phase/README.md with the numbers, the stills, and one tracked demo script that shows the line in a still at a fixed size. A fault over time goes to the user as a side-by-side video.

## Step 2: the two designs (the line orchestrator, 2026-11-22, after the toggles and the red test)

### The measured cause

A texel of a shadow map holds a caster only if the caster covers the texel's center. So a caster's edge in the map lies on a texel border between half a texel inside the caster's real edge and half a texel outside it. "Gap" g is how far the map's edge lies inside the real edge, in texels: -0.5 < g <= 0.5. The cascade filter's lit fraction is 1/2 at the map's edge and changes by 1/4 per texel across an edge that runs along the grid. `lightOutsideShadow` starts the sun's share at a lit fraction of 1/2, which is the map's edge. So a receiver at the real edge gets the share g / 2 when g > 0, up to 0.25. The toggles showed that no bias decides it, and the red test measured (place - 0.1) / 2 at ten places of the edge.

### The one model

A shadow map knows a caster's edge only within two doubts: the filter's blur, and the rounding of the edge to texel centers. A lookup that must put no light under a caster gives every doubt to the shadow. `t8-rjno` did this for the blur. This ticket does it for the rounding. The property that both designs must give: the lookup's zero reaches the caster's real edge at every phase of the grid.

### The two designs

| | Design L: the lookup takes the rounding's doubt | Design M: the map holds every texel that a caster touches |
|---|---|---|
| Where the change is | One function of the shader, `lightOutsideShadow` | A new pass after each shadow pass, plus both lookups' coordinates, the slope bias and the maps' margins |
| The mechanism | The share starts at a lit fraction of 5/8, not 1/2. 1/8 is half a texel of rounding times the filter's 1/4 per texel | A second image per map: each of its texels is centered on a corner of four map texels and holds the nearest of the four depths. A texel of it holds a caster if the caster covers any of its own four corners. The lookups read this image |
| The map's edge against the real edge | As today: from 0.5 texel inside to 0.5 outside | From 0 to 1 texel outside, never inside |
| Where the sun's share starts, outside the real edge | 0 to 1 texel, mean 0.5 (today: -0.5 to 0.5, mean 0) | 0 to 1 texel, mean 0.5 |
| Where the share is full | 1.5 to 2.5 texels, mean 2, as today | 2 to 3 texels, mean 2.5 |
| Width of the edge's ramp | 1.5 texels (today 2): the edge is a quarter sharper | 2 texels, as today |
| Sunlight lost at a true edge, more than today | 0.25 texel of full light: 1.5 cm at cascade 0, 0.58 m at the farthest cascade | 0.5 texel: 2.9 cm at cascade 0, 1.15 m at the farthest cascade |
| A lit slot shows light when it is wider than | 2.5 texels (today 2) | 3 texels |
| Cost per draw | None: the same 16 comparisons | One pass over 4 x 2048 x 2048 cascade texels and the atlas, 4 reads each, in every draw that draws shadows |
| Depth side effect | None: the change is lateral only | A texel takes a depth from half a texel away on each axis, so the casters' slope bias must cover one texel more (2.5 to 3.5) and the margins grow by one texel. More bias lets shadows leave their casters |
| The tiles | The same function on the tiles' lit fraction: the tiles have the same 4 x 4 filter. One mechanism for every shadow map | The tiles need the image and also the one-sided rule: with the symmetric filter the share at the real edge is still 0.25 to 0.5 |

The texels of the four cascades at 16:9 and 60 degrees are 0.0585 m, 0.1259 m, 0.3201 m and 2.3091 m.

Design L in use, the whole change of the cascade lookup:

```glsl
// The lit fraction below which a caster can lie over the lookup: one half for the filter's blur, plus one eighth for the
// half texel by which the map's edge can lie inside the caster's real edge (t1-gout).
const float DoubtfulLitFraction = 0.625;

float lightOutsideShadow(float litFraction)
{
    return clamp((litFraction - DoubtfulLitFraction) / (1.0 - DoubtfulLitFraction), 0.0, 1.0);
}
```

where

- `litFraction` = the share of the filter's 5 x 5 texels, by weight, that hold no caster nearer the light than the receiver, from 0 to 1.
- `DoubtfulLitFraction` = the largest lit fraction that a lookup at a caster's real edge can have, for an edge along the grid.
- the result = the sun's share at the receiver, from 0 to 1.

and for a tile, `tileShadow` returns `lightOutsideShadow(lit)` in place of `lit`. Its two shortcuts through the bounds stay right, because a lit fraction of 0 gives 0 and one of 1 gives 1.

Design M in use: a pass `CoveredShadowPass` after `ShadowPass` and after `LocalShadowPass` writes `shadowCascadesCovered` and `shadowAtlasCovered`, and `filteredCascade` and `tileShadow` read those with their coordinate moved by half a texel. `ShadowBias.SlopeBias` becomes 3.5 and `ShadowCascades.MarginTexels` 5.

What both designs share:

- The red test `ShadowTests.AWallsSideFaceUnderARoofsEdgeTakesNoSunlightWhereverTheEdgeStandsWithinItsTexel` loses its skip and passes at all ten places.
- New red tests first: the same case with the edge at 45 degrees and at one odd angle to the grid, and the same case for a spot lamp's tile.
- The one-sided rule of `t8-rjno` stays. Lit areas get a little narrower, which the user's rule allows.
- The phase still moves the place where the light starts by up to one texel when the texel size changes. It never moves it inside the real edge.
- Reference images with a sun's shadow edge change. Each one is reviewed as CLAUDE.md says.

### Rejected

- Conservative rasterization (VK_EXT_conservative_rasterization) for the casters. The Mac's Vulkan does not offer it (vulkaninfo on the M1 Max, 2026-11-22: the extension is absent). It would need a second path for the Mac, and two paths can disagree.
- A 3 x 3 growth of the map. It moves the map's edge a whole texel outward (0.5 to 1.5 outside the real edge), so it loses twice the light of design M for the same pass.
- Casters drawn four times with a shift of half a texel. It gives design M's coverage at four times the shadow passes' geometry cost.

### The recommendation, by the orchestrator's rule

Design L. Both designs give the property for the sun's cascades. For the tiles, the property needs the one-sided rule in either design, because a symmetric filter puts light under a caster by its definition. With that, design L is one mechanism for every shadow map, and design M is two. Design L also loses half as much true sunlight at an edge (0.25 against 0.5 texel), costs nothing per draw and touches no bias.

What design L costs the look: the sun's edge ramp is 1.5 texels wide in place of 2. And the tiles change more than the cascades: a lamp's shadow edge goes from the symmetric filter's 4 texels of ramp to 1.5, and a lamp's lit area loses about 1.75 texels at each edge. The user's rule against hard edges was stated for the sun. So the build shows the lamps' shadows before and after in stills, and the orchestrator confirms the tiles' part on those pictures. If the lamps' edges look too hard, the tiles keep the symmetric filter and their light under an edge (a share of 0.5 to 0.625 at the real edge, reaching up to 2.5 texels inside) stays a known limit with its own ticket.

What I did not derive: the largest lit fraction at a real edge that runs at an angle to the grid. My estimate gives 5/8 again at 45 degrees and less at other angles, because the rounding averages along such an edge. The red tests at 45 degrees and at an odd angle measure it first. If one of them needs more than 5/8, the constant takes the measured worst case, and the ticket records the number.

## Step 3: the build of design L (the orchestrator confirmed design L on 2026-11-22)

The orchestrator's reasons: design L passes the rule at every phase through one function for every shadow map, it loses half as much true sunlight at an edge as design M (0.25 against 0.5 texel), it costs nothing per draw, and it touches no bias. The cascades' part is confirmed. The tiles' part is built behind the same function and decided on pictures.

### The system design

- The shadow maps stay as they are. They own the casters' depths, rounded to texel centers.
- `lightOutsideShadow` owns one decision: which lit fractions can lie under a caster. No other code knows the filter's blur or the rounding. Every lookup of a shadow map turns its lit fraction into a light's share through this one function.
- The filter's size owns the constant. The constant is computed from the number of comparisons per axis, so a change of the filter's size cannot leave it stale.

### The signatures

```glsl
// The bilinear comparisons of the cascade filter along one axis, one texel apart. One texel of a straight edge's travel
// changes the lit fraction by 1 / FilterTapsPerAxis.
const int FilterTapsPerAxis = 4;

// The largest lit fraction of a lookup that lies under a caster: one half for the filter's blur, plus the half texel by
// which the map's edge can lie inside the caster's real edge, at the filter's change per texel (t1-gout).
const float DoubtfulLitFraction = 0.5 + 0.5 / float(FilterTapsPerAxis);

float lightOutsideShadow(float litFraction)
{
    return clamp((litFraction - DoubtfulLitFraction) / (1.0 - DoubtfulLitFraction), 0.0, 1.0);
}
```

where

- `FilterTapsPerAxis` = the count of comparisons along one axis of the cascade filter, 4 today. `filteredCascade`'s two loops and its division by 16 use it too.
- `DoubtfulLitFraction` = 0.625 today. If a red test at an angle measures a larger worst case, the constant's formula takes the measured term, the comment gives the derivation or the measurement, and the test pins it.
- `litFraction` = the share of the filter's texels, by weight, that hold no caster nearer the light than the receiver, from 0 to 1.

For the tiles, the lit fraction and the share are two functions, so that one line holds the tiles' decision:

```glsl
float tileLitFraction(GpuLight light, vec3 position, vec3 L, float d, vec3 geometricNormal, float sinTheta);

float tileShadow(GpuLight light, vec3 position, vec3 L, float d, vec3 geometricNormal, float sinTheta)
{
    return lightOutsideShadow(tileLitFraction(light, position, L, d, geometricNormal, sinTheta));
}
```

where

- `tileLitFraction` = today's body of `tileShadow`: the lit fraction of the tile's 5 x 5 texels, with both ways of reading them and the two shortcuts through the bounds.
- `tileShadow` = the lamp's share at the receiver. If the orchestrator judges the lamps' edges too hard, this line returns `tileLitFraction(...)` unchanged, and the tiles' light under an edge gets its own ticket. No setting and no specialization constant ships for this choice.

The lead finds every other copy of the rule (a C# reference of the lookup in the tests, the far shadows, the environment faces' lookups) and keeps them on the one function. The lead changes a signature when the code shows a better one, and records the change.

### The settled decisions

1. Red first. The skipped test `ShadowTests.AWallsSideFaceUnderARoofsEdgeTakesNoSunlightWhereverTheEdgeStandsWithinItsTexel` loses its skip and passes at all ten places. New red tests come first for an edge at 45 degrees to the grid, for an edge at an odd angle, and for a spot lamp's tile.
2. The constant is computed in the shader from the filter's constants. The source does not hold 0.625.
3. The cascades and the tiles are two decisions in one build. The tiles' part waits for the orchestrator's judgment of the pictures, by the user's rule: narrower lit strips are fine, hard-edged shadows everywhere are not.
4. No bias, no margin and no map changes.

### Acceptance

- The red tests above, red first and then green, through the real shadow passes and shaders.
- For the sun: the ramp of the 181 sizes, or a part of it that covers the three teeth (aspect errors of -0.350 %, +0.420 % and +1.195 %), with the fix. No size leaks at tick 6380 and at tick 100. The window of 1800 x 1008 at both ticks.
- A still of bay-sunbeam at 16:9 before and after, at one exposure, with a crop of a sun shadow's edge at 1:1.
- The survey's stills that hold the sun get no new fault.
- For the tiles: stills of the lamps' shadows before and after in the cabin, on the deck and in one scene with a spot lamp close to a wall, at one exposure, with crops of the edges at 1:1. The "before" is the tile line with `tileLitFraction` alone.
- The takes run under the dynamic render scale, at the scales 2/3 and about 0.96. A fault over time goes to the user as a side-by-side video.
- No added cost: the frame's GPU time inside main's spread, on the Mac at a quiet load.
- Checked and Release, lavapipe for the changed shadow classes, mutants and coverage of the changed lines, one reviewer, and ARCHITECTURE.md's shadow section.
- Each changed reference image is reviewed as CLAUDE.md says.

### A stated limit of the rule: receivers that a caster covers only partly (the orchestrator accepted it on 2026-11-22)

The rule trades light under partly covered receivers for no leak at a rounded edge. The quarter texel of lost light holds at a straight edge of a wide caster. A thin caster, or a narrow lit band between two casters, loses more, because the share falls faster with the lit fraction: a lit fraction of 0.7 gives a share of 0.2 under design L, where it gave 0.4.

The table below is the line orchestrator's arithmetic, not a measurement. It holds for a straight strip along the texel grid. The 4 bilinear comparisons per axis act as a box of 4 texels over the map's texels, so the lit fraction is the uncovered share of that box. "n" is the number of texels across the strip that hold the caster. "Shadow weight" is the light that the shadow removes, summed across it, over the light that the caster truly blocks (n texels of full light).

| Caster width n (texels) | Share under its middle, today | The same, design L | Shadow weight, today | Shadow weight, design L |
|---:|---:|---:|---:|---:|
| 0.5 | 0.75 | 0.67 | 2 | 2.67 |
| 1 | 0.5 | 0.33 | 2 | 2.67 |
| 1.5 | 0.25 | 0 | 2 | 2.67 |
| 2 | 0 | 0 | 2 | 2.25 |
| 4 | 0 | 0 | 1.5 | 1.63 |
| n of 4 or more | 0 | 0 | 1 + 2 / n | 1 + 2.5 / n |

- A caster narrower than 1.5 texels of its cascade casts 8/3 of its true shadow, against 2 today. It is fully dark under its middle from 1.5 texels on, against 2 today.
- A lit band of m texels between two casters shows light at its middle only when m is above 2.5 (today 2). At m = 3 it shows a third of the sun there, against a half today. From m = 4 on its middle is fully lit under both rules.
- The cascades' texels at 16:9 are 0.058 m, 0.126 m, 0.320 m and 2.309 m. So the limit reaches hair, struts and thin slots near the camera, and whole objects of a few meters in the farthest cascade.

The orchestrator's reasons: the hair shadow of `hair-bent-cards` was a blob at this texel size on main too, and the fix makes it larger and darker while its edge stays soft. The seam's step keeps the same soft form and stays under its derived bound (2.564 m against 2.812 m). Both are lost light inside the user's rule (narrower lit strips are fine, hard-edged shadows everywhere are not). The user can reverse this. The pictures: /Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7-evidence/step3/stills/hair-bent-cards.png, stills/box-cabin-pair.png and seam/seam-zoom.png.

The comment of `lightOutsideShadow` states the limit in one sentence, so that a reader of the function finds it.

## The rules that bind the designs

- The user's rule for the sun's shadow: lost sunlight and narrower lit strips are fine. Hard-edged shadows everywhere are not.
- The one-sided filter of `t8-rjno` is the built rule that the user chose on 2026-11-21.
- The game runs under the dynamic render scale, and nothing holds the render size or the window's shape to help this fix.
- Takes run in two window shapes at least: 1920 x 1080 and 1800 x 1008.

## Out of scope

The wobble's fix `ter-qt72`. The speck cloud on the ceiling, `ter-fe63`. The other faults of `t58-i3sn` (the specks per box at a render scale near 1), which stay with that ticket.



## Notes

**2026-11-22T20:26:52Z**

2026-11-22, the line orchestrator. Step 1 is launched as a continuation of the research take's lead, in its own worktree, because the disk allows no new worktree with a full build (27 GiB free, 30 needed). Child thread: thread:delegated-task:command%3Amcp%3A1606afbd-dec8-4bf8-a33c-bc8fce97f948%3Adelegate-task%3Aslabs-3-t1-r9vj-lead-1. taskId of the continuation: node:delegated-task:command%3Amcp%3A34861019-e8fc-4982-9f60-62510de9c334%3Athread-send%3Aslabs-3-continue-t1-gout-step1. clientRequestId: slabs-3-continue-t1-gout-step1. Target as before: claudeAgent, claude-opus-5-5, effort high. Worktree /Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7, branch t3/fade-lit-front-trace, fast-forwarded to main d1e0349b8. No reviewer stage: step 1 changes no engine code, and the line orchestrator inspects the record. Cloud money: $0.20. One choice of mine in the brief: the red test runs without a GPU if the cause needs only the map's depths and the lookup's comparisons, and through the real pass and shader if it needs the rasterizer's own depth bias. It is committed skipped, with t1-gout as the reason.

**2026-11-22T20:52:59Z**

2026-11-22, the line orchestrator, from the orchestrator. The user handed the design choices to the orchestrator on 2026-11-22 ("use your best judgement and try to hill climb toward a complete/good lighting system"). So step 2's choice is the orchestrator's, by this rule: the remedy must make the lookup's zero reach the caster's real edge at every phase of the grid, for the sun's cascades and for the local lights' tiles through one mechanism if one exists. Among the remedies that pass, the one that loses less true sunlight at a shadow edge wins. The user's rule for the sun's shadow holds: narrower lit strips are fine, hard-edged shadows everywhere are not. For each design the line orchestrator states what it does to the tiles, whether one rule serves the cascades and the tiles alike, and its cost in sunlight at a true shadow edge in texels and in metres at cascade 0 and at the farthest cascade. The build starts when the recommendation passes the rule and the orchestrator confirms it. The cause as accepted on 2026-11-22: a caster's edge in the map lies on a texel border up to half a texel inside the caster's real edge, because a texel holds a caster only if the caster covers its center, and the one-sided rule starts its ramp at the map's edge. Figure: /Users/alexispurslane/.t3/worktrees/tertius/t3-1b834d57-evidence/jamb/shadow-map-edge-gap.png. The pulsing line in a 16:9 window is part of step 1.

**2026-11-22T21:39:28Z**

Step 1, first part: the cause, the four toggles and the red test.
- Cause: the map holds the roof in the texels whose centers lie under it, so the map's edge lies up to half a texel inside the hull's outer edge at x = ±10 m. The jamb's outer strip between the two edges takes sunlight. The sun's share there is d/2, where d is the strip's width in texels (lightOutsideShadow of the 4x4 lit fraction 1/2 + d/4). The phase of the edge on cascade 0's grid is 10/texel mod 1: 0.038 at 1920x1080 (no leak) and 0.460 at 1800x1008 (share 0.23 at the edge, over 2.7 cm). This gap model puts the teeth at -0.350 %, +0.420 % and +1.195 % (measured: -0.34, +0.45, +1.25). No size of the ramp with a gap of 0 or less leaks.
- The sun stays in the ship's YZ plane and turns about the ship's X axis, so the side walls' phase does not change over the orbit.
- Toggles, scratch build of d1e0349b8 (docs/research/shadow-grid-phase/tools/scratch-toggles.patch), window 1800x1008, scale 1, 4.08 stops, tick 6380. Jamb crops, right / left: base 0.193 / 0.161; slope bias 0: 0.193 / 0.161; constant bias 0: 0.193 / 0.161; normal offset 0: 0.179 / 0.160; symmetric filter: 0.960 / 0.972; screen trace off: 0.092 / 0.119. At tick 100: base 0.098 / 0.058 (= the window test), screen trace off 0.047 / 0.047. 1920x1080 at ticks 100 and 6380: 0.044 / 0.044.
- Reading: no bias decides it. The lateral filter does, together with where the map's edge falls. The screen rays add about half of the crop's light at tick 6380, and nearly all of it at tick 100.
- Red test: ShadowTests.AWallsSideFaceUnderARoofsEdgeTakesNoSunlightWhereverTheEdgeStandsWithinItsTexel, through the real shadow pass. Sun share 0.1 texel inside the edge, by the edge's place in its texel: 0.0 and 0.1 give 0.000, 0.2 gives 0.051, 0.3 gives 0.106, 0.4 gives 0.143, 0.5 gives 0.197, 0.6 to 0.9 give 0.000. Committed skipped. It is a GPU test because no C# copy of the cascade lookup exists, so a test without a GPU would only test its own model.
- Still to do: the record and the rest of step 1 (the 16:9 pulse check, the tiles' case, the tracked script).

**2026-11-22T22:16:15Z**

Step 1 done: docs/research/shadow-grid-phase/README.md, scripts/jamb-grid-phase.json, and the skipped test of 9b84e5a6c.
- The line is the jamb face's outer strip at x -> +-10 m. The roof's map edge lies up to half a texel inside the hull's outer edge, and the strip between them takes a share of gap/2 (0.23 at 1800x1008, 0.019 at 16:9). No strip there is truly sunlit.
- No bias decides it. The lateral filter does, together with where the map's edge falls. The symmetric filter is far worse (0.96).
- The ramp's 0.230 against the window test's 0.098 is mostly the tick (N.L 0.120 at tick 6380 against 0.0019 at tick 100). The same window reads 0.193 at tick 6380.
- The screen's rays make almost all of the excess at tick 100 and half at tick 6380.
- At 16:9 the side walls' phase is fixed for the whole orbit. The nose's moving phase opens at most a 6.7 mm strip before tick 120. A 360-draw film at 1920x1080 shows no line.
- Tiles: the symmetric filter gives 0.50 to 0.625 at a caster's real edge, with light up to 2.5 texels inside it. Their phase moves only with a lamp or a caster.

**2026-11-22T22:28:44Z**

2026-11-22, the line orchestrator. Step 1 is merged into main at deba5bdfd (fast-forward) and its record docs/research/shadow-grid-phase/README.md is read. Step 3, the build of design L, is launched as a continuation of the same lead, because it was the first free lead and holds the red test and the scene. Child thread: thread:delegated-task:command%3Amcp%3A1606afbd-dec8-4bf8-a33c-bc8fce97f948%3Adelegate-task%3Aslabs-3-t1-r9vj-lead-1. taskId: node:delegated-task:command%3Amcp%3A34861019-e8fc-4982-9f60-62510de9c334%3Athread-send%3Aslabs-3-continue-t1-gout-step3-build-design-l. clientRequestId: slabs-3-continue-t1-gout-step3-build-design-l. Target: claudeAgent, claude-opus-5-5, effort high. Worktree /Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7, branch t3/fade-lit-front-trace, review base deba5bdfd. One reviewer stage. Cloud money: 0.20 dollars for the lavapipe run. A choice of mine, for the orchestrator to reverse: the ray census ter-8pco and ter-yglw leave this lead's queue and go to the wobble fix's lead when step 2 of ter-qt72 is merged.

**2026-11-22T23:15:29Z**

2026-11-22, the line orchestrator. The lead's first numbers of step 3 and my three answers, for the orchestrator to reverse. Numbers: the four tests are green with a sun share of 0.000 at every place (aligned, 45 degrees, 17 degrees, a spot lamp's tile), and no angle needs more than 5/8. Two tests fail on measured facts. (a) ALitFloorUnderASpotLightHasNoAcne at 60 degrees with a 45 degree turn: with the tiles on design L, 634 of 25,784 pixels differ at 4 m (main 4) and 5,505 of 135,815 at 12 m (main 2), about 2.3 percent darker, largest 8/255. Main's tiles already hold the same ring pattern, and the rule's slope 1 / (1 - 5/8) = 2.67 amplifies it. ShadowBias.SlopeBias documents the shortfall on a slope diagonal to the grid. (b) TheEdgeOfAShadowDoesNotStepWhereTwoCascadesMeet: the step at seam 2 grows from 1.982 m to 2.564 m against a bound of 2.346 m, with texels of 0.259 m and 1.865 m. Answers: 1. The build splits into two commits. Commit A holds the cascades' part, with tileShadow returning tileLitFraction so that the tiles behave as on main, and it merges on its own gates. Commit B holds the tiles' one line and its test changes, after the orchestrator's decision. 2. The step at the far seam is the accepted lost light of a quarter texel, seen where two texel sizes meet. The test's bound is derived from the rule (the half-share crossing lies 2 D texels outside the map's edge), not loosened by a number, and a still of seam 2 goes to the orchestrator. 3. No bias changes in this ticket. The tiles' acne is evidence for the tiles' decision, shown at the scene's own exposure. A bias fix for the diagonal slope becomes its own ticket if the tiles go on design L.

**2026-11-23T02:32:23Z**

2026-11-22, the line orchestrator, from the orchestrator. The sun's evidence of commit A is accepted, and commit A goes on, on its gates: the timing at a quiet load, Checked, the review, then Release before the merge. The orchestrator looked at the openings sheet, the ramp chart, the seam at 6x, the box-cabin pair, the hair crop and the bay pair at 0.96, and accepted both look changes: the seam's step and the darker, larger shadows of thin casters and narrow lit bands. The section 'A stated limit of the rule' holds the bound and the reasons. The numbers of the evidence: at 20 window shapes across the three teeth, main leaks at 1 of 20 at tick 100 and at 8 of 20 at tick 6380, and the fix at none (largest jamb light 0.044 against 0.172). Of 75 survey stills, 18 are identical and 57 changed with no new speck, leak, line or band by the lead's read. The stills walk and airlock-door lose a fault of main. The sun's acne does not rise. Commit B, the tiles, stays the orchestrator's decision on stills. Its lean: the tiles keep the symmetric filter unless the stills show a leak that the user would see.
