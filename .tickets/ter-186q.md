---
id: ter-186q
status: in_progress
deps: []
links: [t53-eyrv]
created: 2026-11-04T21:53:20Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
---
# The walk misjudges a far slab near a vanishing point: a planet's reflection misses or fades at a fifth of its pixels

Found in ter-gayt, part 6 (2026-11-04), when ScreenRayTests.EveryFloorPixelWhoseVanishingPointAPlanetCoversHitsThePlanet moved from the old trace to ScreenRay.Walk.

Near a vanishing point, the rounding of the ray's inverse depth, StartInverseDepth + StepInverseDepth · s (about 3e-8), is larger than the slab of the planet (a rectangle 10,000 km away, κ · 1e-7). Of the test's 3,613 rays:

1. 209 rays leave the segment at the vanishing point without meeting the slab. They take the miss's closed form: the map's planet while the map is on, the hemisphere while it is off.
2. 531 rays hit the planet at the vanishing point's pixel, but with an entry share q from 0.05 to 0.59 (median 0.08). The walk takes q from the inverse depth at s, where the ray enters the cell, so q is noise, and the hit's fade 1 − q dims those pixels.

The old cone trace judged no slab by the inverse depth at a solved entry, and its test asserted a hit with q = 0 on every ray. The skipped test ScreenRayTests.EveryFloorPixelWhoseVanishingPointAPlanetCoversHitsThePlanetThroughTheFrontOfItsSlab holds that behavior. ARayFromANearFloorHitsAPlanetAtItsVanishingPoint, one ray, passes. A fix changes walkScreen in screen_ray.glsl and ScreenRay.Walk together, and the GPU pin tests of ScreenTraceRenderTests must stay green. One idea: at a hit whose s a descent solved, take q from the side that the ray came from when the coarse level solved the entry (0 from in front, 1 from behind), where the inverse depth is far from the slab.


## Notes

**2026-11-22T13:53:36Z**

2026-11-22, the line orchestrator. The design of the fix. It covers this ticket and t53-eyrv, because both are one class of bug. One ticket lead does both.

## The class of the bug

The walk (walkScreen in screen_walk.glsl, and its C# copy ScreenRay.Walk) keeps the ray's parameter s. At each cell it computes the ray's inverse depth again from s: inW = startW + stepW * s. A descent sets s to the entry of the parent's slab, which slabEntry solved for one bound of that slab (towardW: the front from in front, the back from behind). So the true inverse depth at that s is that bound. The recomputed inW is that bound plus a rounding, and the rounding scales with the ray's start w, not with the slab.

The child's test then compares a rounded inW with a slab that is thinner than the rounding:
- ter-186q: a planet 10,000 km away has a slab of about 1e-7 in w, and the rounding is about 3e-8. Of 3,613 rays, 209 leave without a hit, and 531 hit with an entry share of 0.05 to 0.59 that is noise, so the hit's fade dims them.
- t53-eyrv: a sheet that faces the eye or lies edge-on has a slab of no depth since the layer (t1-7b4x). The rounded inW falls one unit in the last place behind it, and the walk passes the pixel: 8 of 4,559 hits.

The class: a value that a descent solved exactly is stored only as s and derived again with a rounding. Every thin slab can show it.

## The model

The walk's state holds the ray's inverse depth at s beside s. One rule sets it:
- After a crossing, it is startW + stepW * s, as today.
- After a descent that solved an entry, it is the bound that the entry was solved for (slabEntry's towardW), with no recomputation.

The cell's test and the entry share read that stored value in place of the recomputed inW. A child's slab lies inside its parent's slab (DepthPyramid.AssertInvariants holds this). So a ray that enters the parent at its front bound stands at or in front of every child's front, and a child whose front is the parent's front takes the ray at its front, with an entry share of 0. The same holds at the back bound.

This makes the class impossible for solved entries: the walk no longer compares a rounded copy of a bound with the bound.

## Signatures

GLSL, screen_ray.glsl. The solved parameter and the bound come from one place:

```glsl
// The parameter where the ray enters a slab that it meets, and in `entryW` the ray's inverse depth there: the slab's
// front from in front, its back from behind, or s and inW when the ray is already inside.
float slabEntry(ScreenRay ray, RayReciprocals inverse, float s, float inW, float low, float high, out float entryW);
```

walkScreen keeps a local for the inverse depth at s (a name such as sW) beside s. The C# copy ScreenRay.Walk changes the same way. The lead can choose another shape, such as a small struct of (s, w), if the code reads better. Both copies change together.

## Settled decisions

- One fix for both tickets, in the walk. No tolerance in the pyramid's build, because the rounding does not scale with the slab.
- The 209 rays of ter-186q that leave without meeting the slab can have a second cause at the coarse levels (outW is recomputed too). Find it with the skipped test. If the stored inverse depth does not cover them, say what does, and fix it in the same model if the fix is small. A larger change is a report to me first.
- Write to the Vulkan spec: no reliance on a fused multiply-add or on its absence. On lavapipe, ScreenWalkBoundaryTests.TheShaderHitsTheNextWallPixelOnlyWithinItsSegmentAndStepCap (channel 3) returns WalkMiss where WalkHit is expected, on main too (t70-0pr5, item 1). It can be this class. Run that test class on lavapipe before and after the fix, and record the result in t70-0pr5.
- The ray kinds, the hair-card rule and the cap of steps do not change.

## Acceptance

- Red first, then green: ScreenRayTests.EveryFloorPixelWhoseVanishingPointAPlanetCoversHitsThePlanetThroughTheFrontOfItsSlab and ScreenWalkTests.RandomRays.OnSheetSlabsTheWalkFindsTheHitOfTheMarchOnAMillionRandomRays lose their skip and pass.
- The GPU pin tests of ScreenTraceRenderTests stay green. A reference image that changes gets a real review: the reference, the actual image and the FLIP heat map.
- Before the fix, reproduce ter-186q on main f7d606fec in the engine: a still of a planet's reflection near a vanishing point on a reflective floor. If the layer already fixed the picture, say so with the still, and the fix still lands for the tests. After the fix, the same still. Two to four stills with a caption each.
- The walk's cost: the frame cost in the deck and rect-lamps on the Mac, main against the branch, one exclusive hold per scene. A change above the run-to-run spread is reported with its number.
- Checked, then Release. Coverage of the changed lines, and the mutants of the changed lines through tools/mutate.py (Rendering code is outside Stryker's modules).
- ARCHITECTURE.md, 'The screen tracer', says what the walk stores.

Out of scope: the one-image pyramid (ter-y3k4), the miss's closed form, and any change of the pyramid's build.

**2026-11-22T13:55:46Z**

2026-11-22, the line orchestrator. One ticket lead is launched for this ticket and t53-eyrv. taskId and child thread: node/thread:delegated-task:command%3Amcp%3A74ca186e-009d-4ae5-9d02-543595124443%3Adelegate-task%3Aslabs-2-ter-186q-t53-eyrv-lead-1. clientRequestId: slabs-2-ter-186q-t53-eyrv-lead-1. Target: claudeAgent, claude-opus-5-5, effort high, role implementation, isolation worktree. Its reviewer: the same target, role review. Base and review base: main 821481a5c. Cloud money: $0.35.

**2026-11-22T14:28:10Z**

2026-11-22, the ticket lead. The fix (both copies), on branch t3/preserve-ray-inverse-depth from 821481a5c.

The walk keeps sW, the ray's inverse depth at s, beside s. A crossing sets it to outW = startW + stepW · exit, the value its test already computed. A descent sets it to the entryW of slabEntry (GLSL: an out parameter; C#: a tuple), the bound that the entry was solved for, or s's own sW when the ray is already inside. The cell's test, the entry and the entry share read sW. No other change.

Red first, on 821481a5c with the C# copy unchanged (Checked, filter-method):
- ScreenRayTests.EveryFloorPixelWhoseVanishingPointAPlanetCoversHitsThePlanetThroughTheFrontOfItsSlab: failed, first ray floor pixel (105, 159) RayLeftTheSegment at VanishingPoint after 6 steps.
- ScreenWalkTests+RandomRays.OnSheetSlabsTheWalkFindsTheHitOfTheMarchOnAMillionRandomRays: failed, ray 6763 walk hits (67, 31), march (67, 34): the ticket's ray.
After the C# change both pass. The stored inverse depth covers all 209 misses: no second cause showed (outW at the coarse levels stays a computation, and no ray of the test needed more).

A new GPU test for the shader copy: ScreenWalkSlabTests.EveryRayThatDescendsOntoASheetThatFacesTheEyeHitsIt (probe screen_walk_sheet_probe.comp). A sheet (Primitives.Plane, open, so a slab of no depth) fills a 128 × 72 screen 20 m away and faces the eye. From each pixel 16 px inside the edges, four rays start at half its depth and reach its w at s = 1, 12 to 13 px away. Red on MoltenVK with the old shader: 1,107 of 15,360 rays missed. Green after the GLSL change.

Classes ScreenWalk*, ScreenRay*, ScreenTrace* and BackFaceSlabWalkTests after both changes: 127 passed, 0 failed. The GPU pins of ScreenTraceRenderTests pass with no reference image changed.

Full Checked suite (dotnet test --solution Tertius.slnx -c Checked): 11,044 tests, 11,018 passed, 26 skipped, 0 failed (Rendering 3,301 passed, 10 skipped, 19 m 24 s).

Stills (reflections scene, scratch planet: a sphere of radius 3,500 km about 10,800 km away, Celestial, behind the mirror strip; patch /Users/alexispurslane/.t3/worktrees/tertius/t3-f57a4dae-afc864dc100ee0f6-evidence/scratch-planet.patch, script /Users/alexispurslane/.t3/worktrees/tertius/t3-f57a4dae-afc864dc100ee0f6-evidence/planet-reflection.json, Release, headless):
- before/low.png and after/low.png in /Users/alexispurslane/.t3/worktrees/tertius/t3-f57a4dae-afc864dc100ee0f6-evidence. Before: the planet's reflection on the rough strips beside the red box's reflection holds a darker band. After: that band takes the planet's blue evenly. Side by side: /Users/alexispurslane/.t3/worktrees/tertius/t3-f57a4dae-afc864dc100ee0f6-evidence/low-before-after-crop.png. 27,205 pixels differ by more than 8 of 255, at most 30.
- before/front.png and after/front.png: the mirror strip's reflection of the planet. 24 pixels differ by more than 8 of 255. No visible change.
- ARCHITECTURE.md, 'The walk', says what the walk stores. The two known limits of ter-186q and t53-eyrv are gone.

**2026-11-22T17:18:32Z**

2026-11-22, the ticket lead. Hardening, cost, lavapipe and the first review. E = /Users/alexispurslane/.t3/worktrees/tertius/t3-f57a4dae-afc864dc100ee0f6-evidence.

Release suite at f6b55d811 (dotnet test --solution Tertius.slnx -c Release): 11,031 tests, 11,005 passed, 26 skipped, 0 failed (Rendering 3,300 passed, 10 skipped, 34 m 37 s at a load of 26 to 50).

Two GPU tests from 4548576a1, red on the old rule by mutants S05 and S07 below:
- ScreenWalkSlabTests.EveryRayTowardAVanishingPointThatAFarBoxCoversHitsTheBoxThereThroughTheFrontOfItsSlab (probe screen_walk_far_box_probe.comp): a box 1,000 km deep whose front is 10,000 km away fills a 64 × 36 screen. From each pixel, a ray from 2 m away runs to the middle of the center pixel, where its w reaches 0 at s = 1. Every ray beyond 2 px of the center must hit the center pixel with an entry share of 0. S05 (a descent computes w from s again, the old rule) and S07 (the entry share reads a w computed from s) fail it.
- ScreenWalkSlabTests.ARayThatPassesBehindAWallInsideItsOwnPixelMissesTheWallsNextPixel (fourth channel of screen_walk_slab_probe.comp): S01 (the first sW is startW, not the w at the first crossing) fails it.

Coverage of the changed C# lines (Debug, ScreenRayTests and ScreenWalkTests, tools/coverage/changed-lines.py): lines 11/11, branches 2/2. The GLSL lines have no coverage tool. The mutants below cover them.

Mutants (tools/mutate.py, list $E/mutants/list.json, runs $E/mutants/out, out2, out3, out4). Each row gives its last valid run. In run out2, from S02 on, the Vulkan driver was missing (vkCreateInstance failed: ErrorIncompatibleDriver: the dev shell's MoltenVK store path was gone and was built again). So those rows ran again in out3. S11 timed out in out3 at a load of 68, and it ran again in out4.

| id | place | change | result | killed by |
|---|---|---|---|---|
| C01 | ScreenRay.cs:170 | first sW: the w at 0, not at s | killed | ScreenWalkTests+RandomRays (binary fractions, sheets, solid) |
| C02 | ScreenRay.cs:185 | cell test reads a w computed from s | killed | ScreenRayTests.EveryFloorPixel...ThroughTheFrontOfItsSlab, ScreenWalkTests+RandomRays.OnSheetSlabs... |
| C03 | ScreenRay.cs:187 | entry from a w computed from s | killed | same two |
| C04 | ScreenRay.cs:196 | descent computes w from s (the old rule), as InverseDepthAt(entry + 0 * entryW) | killed | same two |
| C05 | ScreenRay.cs:196 | descent keeps the earlier sW (_ = entryW) | survived | judgment call, below |
| C06 | ScreenRay.cs:203 | entry share from a w computed from s | killed | ScreenRayTests.EveryFloorPixel...ThroughTheFrontOfItsSlab |
| C07 | ScreenRay.cs:208 | crossing keeps the earlier sW | killed | ScreenWalkTests+RandomRays (three tests) |
| C08 | ScreenRay.cs:287 | entry from in front gives the back | killed | ScreenRayTests.ARayFromANearFloorHitsAPlanetAtItsVanishingPoint and others |
| C09 | ScreenRay.cs:289 | entry from behind gives the front | killed | ScreenWalkTests+RandomRays (three tests) |
| C10 | ScreenRay.cs:290 | inside: w computed from s | killed | ScreenRayTests.EveryFloorPixel..., ScreenWalkTests+RandomRays.OnSheetSlabs... |
| S01 | screen_walk.glsl:87 | first sW: startW | killed | ScreenWalkSlabTests.ARayThatPassesBehindAWallInsideItsOwnPixelMissesTheWallsNextPixel |
| S02 | screen_walk.glsl:104 | in-front test reads a w computed from s | survived | judgment call, below |
| S03 | screen_walk.glsl:105 | behind test reads a w computed from s | killed | ScreenWalkSlabTests.EveryRayThatDescendsOntoASheetThatFacesTheEyeHitsIt |
| S04 | screen_walk.glsl:110 | entry from a w computed from s | killed | same |
| S05 | screen_walk.glsl:117 | descent computes w from s (the old rule) | killed | ScreenWalkSlabTests far box and sheet tests |
| S06 | screen_walk.glsl:117 | descent keeps the earlier sW | survived | judgment call, below |
| S07 | screen_walk.glsl:125 | entry share from a w computed from s | killed | ScreenWalkSlabTests far box test |
| S08 | screen_walk.glsl:130 | crossing keeps the earlier sW | killed | ScreenTraceRenderTests.InAClosedBoxTheDiffuseLightRisesWithNoOvershootTowardTheSumOfTheBounces |
| S09 | screen_ray.glsl:166 | entryW computed at the solved entry | killed | ScreenWalkSlabTests sheet test |
| S10 | screen_ray.glsl:166 | inside: entryW is a bound | killed | ScreenTraceRenderTests.AMirrorFloorShowsAGlowingPanelAtThePlaceOfItsMirrorImage |
| S11 | screen_ray.glsl:166 | outside: entryW is inW | survived | equivalent to S06, below |

21 rows: killed 17, timeout 0, survived 4, no-coverage 0, build-failed 0. (C04 and C05 first failed to build, because they left entryW unused, IDE0059. They were rewritten to keep it read.)

The triage of the four survivors:
- C05 and S06, judgment call. After a descent from in front, the earlier sW lies in front of the parent's slab, so at or in front of the child's front. The stored bound gives the same in-front, behind and entry-share results, except in one tie: the child's front equals the parent's front and the w computed at the child's exit rounds in front of it. Then the earlier sW lets the ray cross into the next cell of the same surface and hit there, one pixel on. The same holds from behind. No constructed ray reaches that tie on purpose. The walk keeps the bound because it is the ray's true w at s, which ARCHITECTURE.md states.
- S11, equivalent to S06. With entryW = inW on both sides of the select, a descent leaves sW at its earlier value on every path, as S06 does.
- S02, judgment call. The in-front test differs from the original only in the same tie as C05. The behind test of the same line (S03) and the entry (S04) are killed.

The walk's cost, on the cloud's RTX 2000 Ada, because the Mac's 1-minute load stood at 100 to 170 in the morning, by the line orchestrator's answer. One job (efbaae12-8168-4ccb-9eb8-c706c33f5005-u1, 276 s, $0.0553 by the log) ran cloud-cost.sh's method for the deck (scripts/temporal-upscale-timing.json) and rect-lamps (scripts/ter-kh4g-rect-lamps-timing.json): one automatic-exposure run of main, then six interleaved rounds of main 821481a5c and the branch f6b55d811 at that fixed exposure. The table gives the median GPU frame span without the capture pass, in ms, over rounds 2 to 6 (cost_report.py cloud):

| Scene | Stops | Main: mean (spread) | Branch: mean (spread) | Branch minus main (lowest to highest round) | Reflection rays, branch minus main |
|---|---:|---|---|---|---:|
| Deck | 0.7153527 | 8.521 (0.031) | 8.539 (0.012) | +0.018 (+0.006 to +0.034) | +0.013 |
| rect-lamps | 3.86539235 | 7.039 (0.028) | 7.045 (0.024) | +0.006 (−0.012 to +0.020) | +0.008 |

Both differences lie inside main's run-to-run spread. The reflection-ray pass is 0.008 to 0.013 ms slower in every round, and the diffuse rays 0.005 ms faster. A Mac take was started at a load of 6 and stopped unrun, because another agent's test suite held the GPU lock for more than 16 minutes.

Lavapipe (ScreenWalkBoundaryTests and ScreenWalkSlabTests, Mesa 26.2.4 llvmpipe, through lavapipe-tests.sh in cloud CPU jobs): main 821481a5c 14 tests, 1 failed; f6b55d811 15 tests, 1 failed; 4548576a1 17 tests, 1 failed (job 417681a9-e044-453f-8f00-b8bd79a39765-u2, $0.0061). The failure is always ScreenWalkBoundaryTests channel 3, which t70-0pr5 item 1 holds. The three new walk tests pass on lavapipe.

Film: the band of the before still stands still. 180 frames of the low view at one fixed exposure (-8.104065 stops, the median of the before still's exposure.json), main and branch, each with the scratch planet: the mean of the band region (x 100 to 560, y 440 to 580) stays at 122.1 to 122.4 of 255 on main and 129.3 to 129.6 on the branch, with a frame-to-frame change of 0.16 on both. So stills show it, and no video is needed. First and last frames: $E/film-main/ and $E/film-branch/.

The first review (reviewer task 04313fae): correctness pass, design pass, fulfillment unverified for missing records. Its findings and what became of them:
1. Gap: no Release run and no run at HEAD recorded. Recorded here, and the suites ran again at the final code (below).
2. Gap: the new tests had not run on lavapipe. Run at 4548576a1 (above) and again at the final HEAD (below).
3. Nit, the comments on sW: fixed in both copies and in ARCHITECTURE.md. sW is the bound after a descent from outside the slab, also where the entry is clamped to s or to the exit, and it stays after a descent from inside.
4. Nit, the probe's name: renamed to screen_walk_far_box_probe.comp, as the test calls the box.
5. Nit, the C# SlabEntry ran on every step and divided by a StepInverseDepth of 0 where the ray does not meet the slab (the result was dropped). Fixed: the C# walk calls SlabEntry only for a met slab, as before the fix. The guarded input is a ray at one inverse depth (StepInverseDepth exactly 0) at a cell whose slab it does not meet. Tests reach it: with a throw on that input and no guard, six ScreenWalkTests failed (for example BehindABlockAWalkTakesFewerStepsThanTheMarchTestsPixels and OverEmptySpaceAWalkTakesFewerStepsThanThePixelsItCrosses). With the guard and the same throw, the 24 tests of ScreenRayTests and ScreenWalkTests passed. So a ray at one inverse depth meets only a slab that holds it, and SlabEntry never divides by 0. GLSL already guards it in parameterAt.
6. Nit (optional), an (s, w) pair type: rejected. Each copy writes s and sW together on three adjacent lines, and a pair type adds one type to each copy for three writes. The reviewer accepted the locals.
7. Nit, "12 to 13 pixels": fixed to "12.6 to 13.6 pixels", the lengths of the four steps.

Checked suite at the final code (the nit fixes on 4548576a1): 11,046 tests, 11,020 passed, 26 skipped, 0 failed (Rendering 3,303 passed, 10 skipped, 40 m 17 s at a load of 40 to 130).
