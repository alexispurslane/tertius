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

The whole fix gives each its own width. The reach of the filter's taps is the penumbra's half-width at the receiver's gap. It is never less than the reach that hides the texel grid, and never more than today's 2.5 texels.

### Two steps, in this order (the parent agent, 2026-11-15)

The user's rule on design decisions says: if a redesign keeps the quick fix unchanged, build and merge the quick fix first. The whole fix keeps the quick fix, because one tap everywhere is the whole fix with the reach at its least value and no search.

| Step | Ticket | What it builds | Its merge |
|---|---|---|---|
| 1 | This ticket | The filter's taps take a reach that is a parameter. The reach is fixed at its least value: one bilinear comparison. | Into main. The condition: a shadow's edge in motion shows no stair steps and no crawl in any cascade. |
| 2 | `t1-af5w` | The search for the blockers, and the reach that follows the penumbra between the least reach and 2.5 texels | Only after the user's choice, with stills and costs |

This ticket is step 1 of the two. Another line's remedy and the back-face layer's acceptance takes wait for it. So when its checks pass, tell the line orchestrator at once.

### What causes what

The reach is one value that `sampleCascade` in `shadows.glsl` hands to its taps. In this step it is a constant. In step 2 a function of the gap gives it. The biases and the margins are sized for 2.5 texels, so they still hold at a smaller reach. No new state exists: the filter stays in `shadows.glsl`, and its constants stay in `ShadowBias`.

### Signatures

GLSL, in `shadows.glsl`:

```glsl
// The lit fraction of the cascade's texels around `coordinate` for a receiver at depth `reference`, with taps
// that reach `reach` texels from the lookup: one bilinear comparison at a reach of MinFilterReachTexels, and the
// 4 x 4 comparisons one texel apart at FilterReachTexels.
float filteredCascade(vec2 coordinate, float layer, float reference, float reach);
```

C#, in `ShadowBias`: `MinFilterReachTexels` beside the constants of today, with its reason, and the reach that the cascades use. The CPU copies of the filter in the tests follow the shader. How the taps spread between the two reaches is the lead's choice. Step 2 needs every reach between them, so do not build a switch of two fixed cases.

### Settled decisions

- The fix acts on the sun's cascades only. The local lights' tiles keep their filter.
- The margins of the cascades and of the faces (`ShadowCascades.MarginTexels`, `FilterClearanceTexels`) stay as they are in this step. They are sized for the largest reach, which step 2 still uses.
- The probe's switch `TERTIUS_SHADOW_FILTER=1` in `stage-2/probe-shadow-leak.patch` is a starting point, not the code.
- A shadow's edge in motion decides the merge. Film a sun shadow's edge in every cascade, also the far ones, where a texel is large: once with a moving camera and once with a moving sun or caster. Look at the films yourself, and describe each one. If an edge shows stair steps or crawl, this step does not merge alone: report it with a still and a short film.

### Tests first

- A receiver under a roof: a point 2 texels inside the roof's edge, with the roof 100 texels above, takes no sunlight. It takes up to half today, so the test is red first.
- The shadow's edge lies at the geometric edge: the lit share is one half there.
- Lit ground at a grazing angle has no acne, as `LitGroundHasNoAcne` asks today.
- Two cascades meet with no step in the lit share at the seam.
- The existing shadow tests that measure an edge or a reach change with a reason each: `AShadowReachesTheBaseOfAWallOnAFloorThatTheSunGrazes`, `ShadowIsContinuousAcrossCascadeSeamsAndDoesNotCreepOntoTheSunlitSide`, `GroundJustBelowAThinCasterUnderAHighSunIsInItsShadow` and `BoxCastsAShadowOnTheGroundAndTheEclipseTermStillApplies`.

### Acceptance

- The jamb of `rect-lamps`, with the screen tracer off: the engine's direct sunlight on output columns 1800 to 1809 has a mean under 0.03 W/(m² sr). Main has 0.222.
- The box's shadow of `bounce-raised-box`: the mean error of the lit share against the line cast is under 0.13. Main has 0.207.
- The cost against main on the M1 Max and on the RTX 2000 Ada: the lighting passes' time and the frame, in the deck, the cabin and `rect-lamps`.
- The leak that stays, in m, on the jamb.
- The films of the edge in motion, with a plain verdict for each cascade.
- Checked, then Release. Each changed reference image gets a real review: the reference, the actual image and the FLIP heat map. The report describes each one. The images of `puff-yard` are expected to change at the shadows' edges.
- A run of the shadow tests on a Linux driver in the cloud.
- Coverage and the mutants of the changed lines follow CLAUDE.md, "Testing".
- ARCHITECTURE.md, "Shadows": the filter's reach, and the known limits. They must name the filter's reach past a caster's edge, which none of the four names today.
- Two to four stills in the report: the jamb before and after, and one outdoor shadow before and after.
