---
id: t8-rjno
status: open
deps: [t1-3j2j]
links: []
created: 2026-11-15T12:54:02Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [rendering, shadows]
---
# Sunlight leaks onto the outer quarter of rect-lamps' side window jambs: 0.37 W/(m² sr) of direct light where the wall above blocks the sun

Found by t1-g4p1 (docs/research/back-face-depth-layer/stage-2/reference.md, part 3). The view is rect-lamps: scripts/rect-lamps.json, the person's camera, film frame 239, output 1920 x 1080, fixed exposure 2.7868438 stops. The far jamb of the nearest right side window shows a bright vertical strip on its outer quarter. The strip spans output x 1799 to 1809 and rows 500 to 580. With --screen-trace off the strip stays: 0.008 W/(m² sr) at x 1799 and 0.374 at x 1808, against 0.007 on the rest of the jamb. With the sun removed (probe switch TERTIUS_NO_SUN=1) no column shows it. So it is direct sunlight. The sun stands 0.26 degrees from the vertical, and the 0.5 m wall above the opening blocks it from the jamb. A Cycles reference of the exported scene has the sun and a black Earth, with 2,560 samples from two seeds. It shows 0.07 to 0.11 W/(m² sr) across the whole jamb and no strip. The strip starts at 0.75 to 0.79 of the jamb's width from the room side, at three output sizes. So it covers about the outer 0.12 m of the wall. Candidates: the cascade's depth bias or normal offset near the hull's outer face, or the map's texel size at 14 m. Stage 1b's 'sunlit sill' (t1-gxzg, part 1) can be the same leak. Before you rely on it, render a reference to find out whether the sun lights the sill.


## Notes

**2026-11-15T13:25:26Z**

2026-11-15, from the parent agent: the diagnosis comes first, as ticket t1-3j2j on the back-face line's branch. The fix then goes on its own branch from main, as the half-float store fix did. It needs both suites and a review of each changed reference image. The line orchestrator then merges it into main in the main checkout by exact paths, pushes, and tells the parent agent. The line orchestrator writes the fix's design into this ticket after the diagnosis.

## The design of the fix (the line orchestrator, 2026-11-15)

Read first: `docs/research/back-face-depth-layer/stage-2/shadow-leak.md`, parts 1 to 3. This section is the design. The fix goes on its own branch from main.

### The cause, in one sentence

The filter of the sun's cascades reaches 2.5 texels from the lookup at every receiver. Past a roof's edge the map holds no caster. So a receiver under the roof within 2 texels of that edge takes up to half of the sun's light. No bias has a part in it: with all three biases at 0, the strip keeps its light.

### The model

The sun's light at a receiver is the share of the sun's disc that the receiver sees. The edge of a shadow is a penumbra, and geometry gives its width: the gap between the caster and the receiver, times twice the tangent of the sun's angular radius. Under a roof 5.86 m above, that is 0.055 m.

Today one fixed reach of 2.5 texels stands for two different things:

| What the filter stands for | Its right width | Its width today in cascade 0 |
|---|---|---|
| The penumbra of the sun | The gap times 2 tan(angular radius): 0.055 m at 5.86 m, 0.93 m at 100 m | 0.29 m at every gap |
| The hiding of the map's texel grid | About one texel: one bilinear comparison | 2.5 texels |

The fix gives each its own width. The reach of the filter's taps is the penumbra's half-width at the receiver's gap. It is never less than the reach that hides the texel grid, and never more than today's 2.5 texels. The biases and the margins are sized for 2.5 texels, so they still hold.

What causes what: the light's record gives the angular radius. A search around the lookup gives the mean depth of the casters nearer the light, and so the gap. The gap and the angular radius give the reach. The taps then compare as today. No new state exists: the filter stays in `shadows.glsl`, and its constants stay in `ShadowBias`.

### Signatures

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

C#, in `ShadowBias`: `MinFilterReachTexels` beside the constants of today, with its reason. The CPU copies of the filter in the tests follow the shader.

The search reads raw depths, so the cascades need a sampler with no comparison beside the comparison sampler. Both are core Vulkan. The implementation is the lead's: the count of the search's texels, and whether a receiver with no blocker skips the taps.

### Settled decisions

- The source is percentage-closer soft shadows (Fernando, 2005). It was made for area lights whose penumbra spans many texels. Our sun has an angular radius of 0.00467 rad, and cascade 0 has texels of 0.058 m. So in rooms, where the gap is under 12.5 m, the reach is the least reach. The search pays off where a caster is far from its receiver: a station's part that shadows a deck from 30 m or more.
- The fix acts on the sun's cascades only. The local lights' tiles keep their filter.
- The fix reads the sun's angular radius from the light. The probe's constant 0.00467 is not the code.
- Measure the cost before the review: the lighting passes' time on the M1 Max and on the RTX 2000 Ada, in the deck, the cabin and `rect-lamps`, against main. Report a second number too: the same with the reach fixed at its least value and no search. That is the simplest form of the fix, and the parent agent decides between the two with the costs.
- The probe's switch `TERTIUS_SHADOW_FILTER=2` in `stage-2/probe-shadow-leak.patch` is a starting point, not the code.

### Tests first

- A receiver under a roof: a point 2 texels inside the roof's edge, with the roof 100 texels above, takes no sunlight. It takes up to half today, so the test is red first.
- The penumbra's width follows the gap: under a caster at 100 m, the lit share rises from 0 to 1 over the penumbra's width within one texel of tolerance.
- The shadow's edge lies at the geometric edge: the lit share is one half there, at a small and at a large gap.
- Lit ground at a grazing angle has no acne, as `LitGroundHasNoAcne` asks today.
- Two cascades meet with no step in the lit share at the seam.

### Acceptance

- The jamb of `rect-lamps`, with the screen tracer off: the engine's direct sunlight on output columns 1800 to 1809 has a mean under 0.03 W/(m² sr). Main has 0.222.
- The box's shadow of `bounce-raised-box`: the mean error of the lit share against the line cast is under 0.13, where main has 0.207.
- Checked, then Release. Each changed reference image gets a real review: the reference, the actual image and the FLIP heat map. The report describes each one. The images of `puff-yard` are expected to change at the shadows' edges.
- Coverage and the mutants of the changed lines follow CLAUDE.md, "Testing".
- ARCHITECTURE.md, "Shadows": the filter's reach, and the known limits that change.
- Two to four stills in the report: the jamb before and after, and one outdoor shadow before and after.

### Not measured yet

- A shadow's edge in motion. A narrow edge can show the texel grid as the camera or the sun moves. Film one moving shadow before and after, and describe it.
- The cost of the search on either machine.
