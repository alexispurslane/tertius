---
id: t8-rjno
status: closed
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

**2026-11-21T17:16:39Z**

2026-11-21, the line orchestrator: one tap fails the merge condition, and the design changes.

Measured by the lead on main 13f4714a9, in a throwaway scene with a turning wall and a panning camera, 8 views with two per cascade. At one tap (reach 1 texel) the upper edge at 5 m in cascade 0 is a scalloped staircase of about one shadow texel per step, and it crawls as the wall turns. Main is soft there. The other views are clean, but for a small notch at the blend into cascade 3 and a comb of streaks at 290 m that main also has. The jamb's mean on columns 1800 to 1809 with the screen tracer off: main 0.2221 over 11 columns, reach 2 0.1375 over 8, reach 1.5 0.0583 over 6 with a faint wave at 5 m, reach 1 0.0171 over 4. The crops are in /Users/alexispurslane/.t3/worktrees/tertius/t3-7e999be0-evidence/crops/.

The reading: a symmetric filter puts half of its blur inside the shadow, so its reach trades the leak against the stair steps. The reach that hides the texel grid is above 1.5 texels, and the reach that meets the jamb's target is under 1.5.

The changed design (the line orchestrator's answer, reported to the orchestrator, who can reverse it): the blur that hides the texel grid is not physical, so we choose where its error lies. The user's bar chooses it: light inside a shadow is a bug, and lost light is acceptable. The filter keeps main's reach of 2.5 texels and only takes light away. A filter centered on a straight shadow edge is half lit. So the sun's share is clamp(2 * litFraction - 1, 0, 1): 0 up to a lit fraction of one half, rising to 1. GLSL: float lightOutsideShadow(float litFraction), applied by sampleCascade to the result of filteredCascade. ShadowBias.CascadeFilterReachTexels becomes FilterReachTexels. The costs, as known limits: lit areas shrink by about half the reach at each edge (about 0.07 m in cascade 0), a sunlit slit narrower than about the reach goes dark, a thin caster's shadow is about twice as dark, and a small leak can stay at a concave corner of a shadow. The merge condition does not change: no stair steps and no crawl in any cascade, the jamb's mean under 0.03, and no acne on lit ground. If one fails, the work stops and the fork goes up with crops. The lead's roof test at 1.5 texels inside the edge was accepted for the one-tap form. The new form's roof test starts 0.5 texels inside.

Step 2 (t1-af5w) needs a new design note before it starts: the blocker search must combine a physical, symmetric penumbra with this rule.

**2026-11-21T17:28:33Z**

2026-11-21, the early result of the changed design (reach 2.5 texels, share = clamp(2 * litFraction - 1, 0, 1), applied per cascade before the blend), on main 13f4714a9. The edge at 5 m in cascade 0 shows no texel staircase and no crawl, and it is sharper than main's. The edge at 290 m is clean, and main's comb of streaks at the shadow's far end is gone. The other six views are clean. The jamb's mean on columns 1800 to 1809 is 0.0001 W/(m² sr) with 0 columns above 0.01, against 0.2221 on main. ShadowTests and FarShadowTests: 49 ran, all passed in Checked, with LitGroundHasNoAcne among them. Red first on the symmetric filter at 2.5: 0.495 at the geometric edge and up to 0.375 half a texel under the roof. The line orchestrator looked at the crops upper-v0-main-vs-rule.png, upper-v0-reach1.5-vs-rule.png and sheet-rule-c6.png and agrees. The orchestrator keeps the changed design. It ordered that the merge into main waits for its look at the cost views: a thin caster and a narrow sunlit opening of 0.1 to 0.3 m, from real scenes where they exist, each beside main. If the edge had shown a wave, the softer form clamp((litFraction - t) / (1 - t), 0, 1) with the least t that keeps the jamb under 0.03 was to be measured. It was not needed.

**2026-11-21T20:48:20Z**

2026-11-21, the lead finished its checks and its review at e93f970b0 (three commits on 13f4714a9). Full Checked before the first commit and full Release on HEAD (10,931 passed, 0 failed). puffs-lit.png approved again after a review. The shadow classes on Linux in the cloud: 112 passed. Mutants: 12 of 12 killed. Costs against main: RTX 2000 Ada deck +0.032 ms, cabin +0.024 ms (noise), rect-lamps -0.164 ms. M1 Max deck +0.001 ms (noise), cabin -0.103 ms, rect-lamps -0.284 ms. Cloud $0.128. The rule also removes main's bright specks along the window's sill of rect-lamps: 161 pixels above 60 of 255 on main, none under the rule.

Answers of the line orchestrator: (1) the reviewer's open point is settled: the reach parameter (shadowParams.w, CascadeFilterReachTexels, MinFilterReachTexels, the taps' spacing) has no caller and leaves the code. The filter is main's 16 taps plus lightOutsideShadow. The image must stay equal byte for byte, and Checked and Release run once more. (2) The cost view surface-in-cabin shows sunlit strips that get narrower with distance, because the rule takes 1.25 texels at each edge and a texel is larger in each farther cascade. The lead films the cabin's sunlit floor with a moving camera, to see whether a strip's width changes visibly as it crosses from one cascade to the next. The merge waits for the orchestrator's look at the cost views and that film.

**2026-11-21T20:50:03Z**

2026-11-21, the orchestrator's look: the leak is gone and the thin casters look right, but the cabin view (surface-in-cabin) is a fork for the user. The shadows of the window frames are several times wider than the frames can cast, and the floor loses a large part of its sunlight. The shadow fix does not merge into main until the user answers. The removal of the reach parameter is on hold, because one option needs another reach. The lead measures two variants from scratch builds: A, the rule at one half with a reach of 1.5 texels, and B, the reach of 2.5 texels with the ramp clamp((litFraction - 0.25) / 0.75, 0, 1). Each gets the cabin view beside main and the rule, the 5 m edge in motion, the jamb's mean, and the sill's count of bright pixels. A table gives the width of a near and of a far sunlit strip of the cabin's floor for main, the rule, A and B, beside the opening's width. The film of the cabin's floor with the cascade crossing stays ordered. The acceptance takes (t1-8b4w) wait for the user's answer too.

**2026-11-21T21:10:55Z**

2026-11-21, the user's answer, through the orchestrator: lost light does not matter ("if the light strips become smaller, that's fine, I don't think anyone will notice"). What matters is that the shadows do not all become hard-edged. A soft edge like main's is the look to keep. So the target is: no sunlight inside a shadow, and an edge as soft as main's. The rule at one half with a reach of 2.5 texels has a ramp of 2.5 texels, half of main's 5. Variants A (reach 1.5) and B (the ramp from one quarter) are dropped. The lead measures the rule at one half with a wider reach from scratch builds: C1 with a reach of 5 texels on the same 16 taps, C2 with a reach of 3.5 to 3.75 texels on 16 taps, and a form with more taps (for example 6 x 6 at a spacing of 1) if C1 shows a comb and C2 stays sharper than main. Each gets the cabin view, the 5 m edge in motion, the rigs crop, the jamb's mean, the sill's count, the ramp's width in m and the frame cost. The margins and the slope bias are sized for 2.5 texels, so the lead also reports acne and faults at the cascades' borders. The hold stays: no merge into main, and the reach parameter stays in the code.

**2026-11-21T22:15:54Z**

2026-11-21, the wider-reach variants, measured by the lead as scratch builds on e93f970b0 (the branch stays clean at e93f970b0). No variant passes the orchestrator's stop rule. C1 (reach 5, 16 taps): acne stripes over all lit ground, LitGroundHasNoAcne fails 4 of 4. C2 (reach 3.75, 16 taps): a comb, a thin shadow's tip breaks into blocks, the test fails 3 of 4. C3 (6 x 6 taps, reach 3.5): clean edge in motion and no comb, jamb 0.0001 and sill 0 px, but acne on slopes diagonal to the texel grid (the test fails 2 of 4), the seam from cascade 2 to 3 steps 3.03 m against a limit of 2.35 m, and it costs +0.42, +0.37, +0.08 ms on the Mac and +0.47, +0.39, +0.25 ms on the RTX 2000 Ada (deck, cabin, rect-lamps). The ramp near the camera: main 0.29 m, the rule 0.15 m, C2 0.22 m, C3 0.20 m, C1 0.29 m. The cause of the acne and the seam step: the slope bias and the margins are sized for 2.5 texels. The crossing film (sunlit gaps of 0.5, 1 and 2 m at 42.7 to 62.6 m): under main nothing steps, under the rule the 0.5 m strip narrows by about 0.05 m and stays, under C3 it fades out from about 51 m. Texels at the demo's view: 0.059, 0.126, 0.320 and 2.31 m for cascades 0 to 3. The cabin film: every strip of the cabin is in cascade 0, which reaches 50.6 m, so no strip crosses a cascade there. The line orchestrator's first reading of the cabin view was wrong on that point. A gather form of C3's window (16 gathers for 7 x 7) is not built. Cloud: $0.232 of $0.50.

The line orchestrator's reading: under the rule the ramp is as wide as the reach, so main's softness needs a reach of 5 texels, which fails on the bias, the margins, the cost and the far cascades together. Ordered: variant C4, the committed rule on a shadow map of 1024 (RendererOptions.ShadowMapSize), so that 2.5 texels are about 0.29 m. Its expected costs: coarser shadows, a slit under about 0.3 m goes dark, a shadow starts about twice as far from its caster's base, and a caster thinner than 0.12 m can break or flicker.

**2026-11-21T22:40:34Z**

2026-11-21, the user's decision, through the orchestrator: "yeah, new filter as built". The rule at one half with the reach of 2.5 texels, as committed at e93f970b0, goes into main. The user saw the four-panel pictures of the cabin floor and the rigs, the table of the variants, and the option to wait for C4. The known costs that the user accepted: every sun shadow's edge has a ramp of 2.5 texels on the lit side, so it is about twice as sharp as before. A lit area loses about 1.25 texels at each edge. A sunlit slit of 0.1 m goes dark. C4 (the map of 1024) is stopped. The hold on the reach parameter ends: the lead removes it, proves with two stills compared byte for byte that the image does not change, runs Checked and Release once more, and records the decision in ARCHITECTURE.md. The orchestrator's look before the merge is done. The line orchestrator merges into main when the lead's final report passes the gates. Step 2 (t1-af5w) stays the user's decision. Its purpose from the user: softness that varies, sharp near the caster and soft far from it.

**2026-11-22T00:11:16Z**

2026-11-21, merged into main as dcd738552 and closed. The branch t8-rjno-sun-shadow-one-tap ends at 04ce9d082, four commits on 13f4714a9: the lit-side rule, the lock-in tests, the review's answers, and the removal of the reach parameter. Main differed from the base only in .tickets, so the tested code is the merged code.

The last commit's checks, by the lead: the shadow, far-shadow, cascade and hair classes 98 of 98 in Checked. Full Checked passed, then full Release passed, with no reference image changed. Mutants S10 and S11 killed, and six rows dropped with their lines. The removal changed the image by 1 of 255 levels in 34 pixels of the test scene's view 0 and in 3 pixels of view 6. rect-lamps and view 3 are byte for byte equal, and each build equals itself on a second run. So the difference comes from the compiled shader, where the stride is now a constant and one branch is gone. That cause is the lead's guess. The line orchestrator accepted the difference.

The variant C4 (the committed rule on a shadow map of 1024) finished before it was stopped: the jamb's mean is 0.0037 W/(m² sr) with 2 columns above 0.01, so the coarser texel leaves a small leak. It is a record only.

The last check of the diff by the line orchestrator: the filter is main's 16 taps plus the one-line rule lightOutsideShadow, with one store of the reach (ShadowBias.FilterReachTexels). No defect found. One nit for the next change of shadows.glsl: the comment above the tile filter's corner in tileLookupOf holds one line that is longer than the file's width.

The comb of streaks at 290 m in cascade 3 that main showed is gone with this fix, so it gets no bug ticket. Evidence: /Users/alexispurslane/.t3/worktrees/tertius/t3-7e999be0-evidence/. Cloud dollars: $0.232 of $0.50 at the lead's last count.
