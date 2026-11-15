---
id: t8-rjno
status: in_progress
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

### The bar (the user, 2026-11-15)

The renderer must look good and show no bug. Path-traced accuracy is not the goal. A bug is light through a shadow, a blotch, a daub or a firefly. Lost light is acceptable, and it is no bug. The user's own words are in the notes of the epic `ter-kh4g`.

So the engine's stills and films decide: no light through a shadow, no specks, no sparkle, no blotches, no flicker, and a good look. A darker room after a fix is acceptable. A path trace is a tool to find and explain a bug. It is not the bar, so order no new path trace for this ticket.

### Acceptance

- The jamb of `rect-lamps`, with the screen tracer off: the engine's direct sunlight on output columns 1800 to 1809 has a mean under 0.03 W/(m² sr). Main has 0.222.
- The box's shadow of `bounce-raised-box`, in a still beside main: its edge has no blur that eats into the shadow, and the lit floor has no acne. The lit share's error against the line cast is a number for the report. It is not a condition.
- The cost against main on the M1 Max and on the RTX 2000 Ada: the lighting passes' time and the frame, in the deck, the cabin and `rect-lamps`.
- The leak that stays, in m, on the jamb.
- The films of the edge in motion, with a plain verdict for each cascade.
- Checked, then Release. Each changed reference image gets a real review: the reference, the actual image and the FLIP heat map. The report describes each one. The images of `puff-yard` are expected to change at the shadows' edges.
- A run of the shadow tests on a Linux driver in the cloud.
- Coverage and the mutants of the changed lines follow CLAUDE.md, "Testing".
- ARCHITECTURE.md, "Shadows": the filter's reach, and the known limits. They must name the filter's reach past a caster's edge, which none of the four names today.
- Two to four stills in the report: the jamb before and after, and one outdoor shadow before and after.

## Resume note (2026-11-15), from the ticket lead at the lab's pause

The line orchestrator copied this note from `RESUME-t8-rjno.md` in the lead's worktree, because the lead's branch holds no ticket file. A backup of the uncommitted worktree is the commit `7b8ce3e79` under `refs/scratch/pause-2026-11-15-t8-rjno`. It is not tested and not for a merge. Restore it with `git checkout 7b8ce3e79 -- .` on the branch.

The lab paused on 2026-11-15 at the user's order. The work stopped in the build stage. Nothing is committed.

### Paths

- Worktree: `/Users/alexispurslane/.t3/worktrees/tertius/t3-7e999be0`
- Branch: `t8-rjno-sun-shadow-one-tap`, made from main with `git switch -c t8-rjno-sun-shadow-one-tap main`. The worktree was clean before the switch.
- HEAD: `ab54c785eaf39135702cab01d77e1fff8524beb4`, equal to main at the branch point. No commit of this ticket exists yet.
- Scratch folder: `/private/tmp/t8-rjno`. It holds `red.log`, the output of the red run below.
- The ticket and the diagnosis are in the line orchestrator's worktree: `/Users/alexispurslane/.t3/worktrees/tertius/t3-1b834d57/.tickets/t8-rjno.md` and `.../docs/research/back-face-depth-layer/stage-2/shadow-leak.md`.
- Cloud jobs: none sent. Cloud dollars: $0.
- The GPU lock: no process of this ticket holds it.

### What is done, uncommitted in the worktree

The changed files:

- `src/Tertius.Rendering/Shaders/shadows.glsl`: the new `filteredCascade(vec2 coordinate, float layer, float reference, float reach)` and `cascadeComparison(...)`, and the GLSL constant `MinFilterReachTexels = 1.0`. The 4 × 4 taps lie `(reach - 1) / 1.5` texels apart. At reach 1 the spacing is 0, and the function takes one bilinear comparison. At reach 2.5 the taps lie one texel apart, as on main. So the result changes continuously with the reach, as step 2 (`t1-af5w`) needs. `sampleCascade` passes `frame.shadowParams.w` as the reach. The tiles keep their filter. The header and tile comments name the largest reach.
- `src/Tertius.Rendering/Scene/ShadowBias.cs`: `MinFilterReachTexels = 1`, `FilterReachTexels = 2.5`, `CascadeFilterReachTexels = MinFilterReachTexels`, each with its reason. `FilterClearanceTexels` is now `FilterReachTexels - 0.5f`, still 2.
- `src/Tertius.Rendering/Scene/SceneBatcher.cs`: the batcher writes `ShadowBias.CascadeFilterReachTexels` into `ShadowParams.W` in every frame. This replaces a GLSL twin of the reach, so the reach has one store in C#.
- `src/Tertius.Rendering/Scene/GpuTypes.cs` and `src/Tertius.Rendering/Shaders/frame.glsl`: the doc of `shadowParams.w`.
- `tests/Tertius.Rendering.Tests/HairRenderTests.cs`: the CPU copy in `AHairCardLitFromBehindKeepsTheShadowOfACasterCloseBehindIt` takes the taps' spread as `CascadeFilterReachTexels - MinFilterReachTexels` in place of 1.5.
- `tests/Tertius.Rendering.Tests/ShadowTests.cs`: three new tests and the roof scene (`RoofOverGroundScene`, `SunSharesUnderTheRoof`):
  - `GroundUnderARoofTakesNoSunlightFromOneAndAHalfTexelsInsideTheShadowOfItsEdge`. The ticket asked for 2 texels inside. The test starts at 1.5 texels. One bilinear tap reaches 1 texel, and the map's edge lies within half a texel of the geometric edge.
  - `TheShadowOfARoofsEdgeLiesAtTheGeometricEdgeAndTurnsFromLitToDarkWithinATexelOfIt`: the mean share along the edge is 0.5 ± 0.1, and the share 1.5 texels outside is above 0.99.
  - `TheEdgeOfAShadowDoesNotStepWhereTwoCascadesMeet`: the 0.5 crossing moves less than a texel of the coarser cascade plus a pixel across each seam.

### What is proven

The red run, on main's filter with the new tests, in Checked. Log: `/private/tmp/t8-rjno/red.log`.

```bash
nix develop --command bash /Users/alexispurslane/Development/tertius/tools/gpu-lock.sh --shared /private/tmp/t8-rjno/red.log dotnet test --project tests/Tertius.Rendering.Tests -c Checked --no-build --filter-method "*GroundUnderARoof*" --filter-method "*TheShadowOfARoofsEdge*" --filter-method "*TheEdgeOfAShadowDoesNotStep*" --output detailed
```

- 1 passed, 2 failed, for the right reasons.
- The roof test failed: shares of 0.093 to 0.139 at 1.5 texels inside, 0.018 to 0.027 at 2 texels, and 0 at 3 and 5 texels.
- The edge test failed on the lit side: 0.841 at 1.5 texels outside. Its mean share at the edge passed.
- The seam test passed on main. It is a lock, so that result is expected.

The C# build of the tests with the new filter succeeded in Checked. No run of the new filter has a result yet. A run with `--filter-class "*ShadowTests" --filter-method "*AHairCard..."` selected 0 tests, because the two filters combine as AND. Use one filter kind per run.

### What is not done

1. The green run of the new tests, the whole `ShadowTests` class and the hair test with the new filter.
2. The four existing tests that the ticket names: read their values, and change their remarks or limits with a reason each. `WallOnFloorScene`'s remark (ShadowTests.cs, near line 896) still says that the filter reaches 2.5 texels.
3. The full Checked suite, then the full Release suite, one after the other.
4. The review of each changed reference image (the `puffs-*.png` images are expected to change at shadow edges): open the reference, the actual image and the FLIP heat map.
5. The measurements of the brief. They are the jamb of `rect-lamps` and the still of `bounce-raised-box`. They also include the films and stills of a sun shadow's edge in motion in every cascade. The rest are the costs on both GPUs, the cloud run of the shadow tests, coverage and the mutants.
6. ARCHITECTURE.md, "Shadows": the filter's reach and the known limits.
7. The commit, the early message to the line orchestrator, and the review stage.

### The exact next step

Build, then run the new tests and the shadow class with the new filter:

```bash
cd /Users/alexispurslane/.t3/worktrees/tertius/t3-7e999be0 && nix develop --command bash -c 'dotnet build tests/Tertius.Rendering.Tests -c Checked -nologo -v:q 2>&1 | tail -5'
nix develop --command bash /Users/alexispurslane/Development/tertius/tools/gpu-lock.sh --shared /private/tmp/t8-rjno/green.log dotnet test --project tests/Tertius.Rendering.Tests -c Checked --no-build --filter-class "*ShadowTests" --output detailed
nix develop --command bash /Users/alexispurslane/Development/tertius/tools/gpu-lock.sh --shared /private/tmp/t8-rjno/hair.log dotnet test --project tests/Tertius.Rendering.Tests -c Checked --no-build --filter-method "*AHairCardLitFromBehindKeepsTheShadowOfACasterCloseBehindIt" --output detailed
```

Then follow the brief's stages from item 2 of "What is not done".
