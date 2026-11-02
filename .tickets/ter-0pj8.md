---
id: ter-0pj8
status: closed
deps: []
links: []
created: 2026-11-02T12:51:56Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, environment-map]
---
# The map's ray search in C#: MapSearch.Hit against an inverse distance field

Ticket 1 of ter-jw7g's design (docs/plans/environment-map-distances.md, 'Ticket 1' and 'The search'). MapSearch.Hit and IInverseDistanceField: the march of a near sample and 8 even samples, the 3 regula falsi steps, the cone's level and the reach, against analytic test fields (the sky, a plane, a box before a wall, a pole before a wall, a far planet, a sphere room). Owns src/Tertius.Rendering/Shading/MapSearch.cs and tests/Tertius.Rendering.Tests/MapSearchTests.cs. Acceptance: tests 1 to 12 of the design pass in Checked and Release, each red first; Samples, NearSample, SecantSteps and ConeWidthPerAlpha are named once.


## Notes

**2026-11-02T12:54:15Z**

2026-11-02: launched as workflow run 06debe04-2619-49f9-a711-de3c1095d8dc, version 869b0b07-3413-4b06-8707-a88a91bf6e91 (build, review, fix), branch ter-0pj8-map-search, base 6c40880fa.

**2026-11-02T13:10:05Z**

2026-11-02, builder: MapSearch.Hit, MapHit, IInverseDistanceField and MapSearch.InverseDistance in src/Tertius.Rendering/Shading/MapSearch.cs, with 17 tests in tests/Tertius.Rendering.Tests/MapSearchTests.cs.

Tests. Each one failed first on its assertion, against stubs that returned 0 or default: tests 1 to 12 of the design, and five more. ARayThroughTheEyeTakesNoReadThere failed first against an implementation without the 2⁻¹⁰ guard, because it read along a zero vector. ASurfaceNearerThanTheNearPlaneHoldsTheNearPlanesInverseDistance failed first against 1000 / D without the near-plane clamp. The other three extra tests: TheInverseDistanceOfASurfaceIsAThousandMetersOverItsDistance, ARayThatMeetsNoSurfaceIsReadAtItsOwnRoughness (decision 19) and AHitFartherFromTheShadedPointThanFromTheEyeIsReadNoRougherThanOne (the min with 1). The analytic scenes are planes, boxes and spheres. A scene gives the inverse distance of the nearest surface, ignores the level and records every read.

Constants: Samples = 8, SecantSteps = 3, ConeWidthPerAlpha = 2.57, each named once. Private: ShortestLookup = 2⁻¹⁰ and KilometersPerMeter = 0.001.

How I read the design where it was unclear:
- Precision. The twin computes in double, with Double3. The design's sketch has Vector3 and float. In float, t* near 1 rounds in steps of 6 · 10⁻⁸, so test 2's 1 % bound on a parallax of 6 · 10⁻⁷ rad cannot hold: the error is up to about 6 %. Test 2's bound is binding and the sketch is a sketch. The GPU's float search has the same rounding, which is under 1/12,000 of a texel.
- The field goes in by ref, not by in. STYLE.md's TS0012 reports a call on an in parameter of a generic struct type.
- A sample that passes through the eye counts as in front with c = 1 − t. That is the limit of c as |L| goes to 0, because w is at most 20,000 km⁻¹.
- The reach of the hit divides by max(|L(t*)|, 2⁻¹⁰). The design says that the search never divides by a lookup vector shorter than 2⁻¹⁰.
- The search takes x and d already in the field's axes. The GLSL turns them first with inMapAxes.
- The search does not refuse a position at the eye. No shaded point lies nearer than the near plane.
- MapSearch.InverseDistance is the C# twin of the face shader's w (ticket ter-55xu's test 13 can use it): 1000 / max(D, DepthRanges.NearPlane).

A disagreement inside the design, reported as a fork: a hit nearer the shaded point than the first march sample is never found. The first sample is at t = 1/8, and front first discards it when it lies behind. Every later sample also lies behind the wall, so the search finds no hit and reads today's direction. Example: a floor point 4.35 m from the eye finds no wall nearer than about 0.55 m along its ray (a probe found no hit for floor points 0.1, 0.3 and 0.5 m from the wall's base, and a hit at 0.7 m). The design's decision C ("A rough floor 10 cm from a wall: the wall's base sharp") and "The cone at each sample" ("a floor beside a wall finds the wall's base") say otherwise. Nothing gets worse than today, because a miss reads today's image. The code follows "The steps" exactly.

ARCHITECTURE.md: no change. "Doc changes" names no change for ticket 1, and no engine code reads MapSearch yet.

**2026-11-02T13:39:51Z**

2026-11-02: run 06debe04 built MapSearch (72a011cd9, 17 tests green) and stopped at a fork: the march starts at t = 1/8, so a hit nearer than about |x|/7 is lost, against the design's decision C and 'The cone at each sample'. The orchestrator settled it inside the chosen design: one more sample at t0 = 1/64 before the uniform march (the nearest place where a level-0 texel at W = 128 can see a separate surface). It changes no hit that the uniform march finds and adds the bracket [t0, 1/8]. Reads per pixel: 12. Rejected: quadratic spacing (coarse far end) and a test at t = 0 (a real root on the shaded point's own surface). Continuation: workflow run 81bbe30d-fbbe-4df5-862e-8dd96173cd40, same worktree.

**2026-11-02T13:43:00Z**

2026-11-02, builder, the near sample (the orchestrator's answer to the fork above): MapSearch.Hit takes one more sample before the even ones, at t0 = MapSearch.NearSample = 1/64, then the 8 even samples t = k / 8 with the same rules. Front first, the first bracket and the 3 regula falsi steps are unchanged. Each pixel reads the field at most N + K + 1 = 12 times.

Why 1/64: the ray's point at t0 lies |x| / 63 from the shaded point, which the eye sees at most about 1/63 rad from x, about one level-0 texel at W = 128. A nearer hit falls in the shaded point's own texel. The near sample changes the result only where it lies in front and t = 1/8 lies behind: there the first crossing in [t0, 1/8] is the hit, in place of the even samples' later hit or their miss (corrected after the review, see the next note). Everywhere else the result is the even samples' result, bit for bit.

Tests (20 now, green in Checked):
- New: ARoughFloorsRayFindsTheBaseOfAWallNearerThanTheFirstEvenSample, a theory over floor points 0.1, 0.3 and 0.5 m from the wall's base, each 4.35 m from the eye, with α = 0.36. It asserts a hit within 10⁻⁹ rad of the ray's meeting with the wall, because the clearance is linear on a plane. Red first on all three distances: "Expected boolean to be True ... but found False".
- Changed: TheConeReadsLevelZeroNearTheShadedPointAndCoarserLevelsFartherAlongTheRay now counts Samples + 1 reads for a sky ray, the near sample and every even one. Red first: 8 reads where 9 were expected. Its bounds did not change.
- Test 5, ARayThatStartsBehindAStoredSurfaceCountsOnlyTheCrossingsAfterItComesOutInFront, already covers a near sample behind: the point 5/63 m along its ray lies behind the panel. A comment now says so. No other test changed.

Design doc: "The steps" (pseudocode, the start, no sample at t = 0, the march, the near sample's effect, the settled date and the two rejected forms), decision 15, decision A's table, "Cost" (12 reads), the tests list, "Known limits" (a hit nearer than |x| / 63 is lost), the C# sketch and ticket 3's work line for mapHit.

**2026-11-02T14:10:25Z**

2026-11-02, fixer, the review of the whole diff from 6c40880fa:
- The claim that the near sample changes no hit of the even samples was false. It replaces a later even hit where the near sample lies in front and t = 1/8 lies behind. Corrected in the NearSample comment, in 'The steps' of the design doc and in the note above. New test ARayThatGoesBehindANearBarBeforeItsFirstEvenSampleStopsBeforeThatSample: a bar hides the ray's point at t = 1/8, and the hit lies between the near sample and t = 1/8 (decision 16), not on the far wall that the even samples alone find. It passed when written, because the near sample was already in the code. A Python port of the search outside the repo (/tmp/0pj8-fix) shows its red reason: without the near sample the hit lies on the far wall, about 17 level-0 texels beyond the bar's edge and outside the arc.
- The bar test first asserted a hit within one level-0 texel of the bar's edge and failed: 0.031 rad, about 2 texels short, on the floor's side. Pure regula falsi converges slowly at a jump of the clearance when the front end's clearance is small, here c(t0) about 1/64. The port gives up to about 3 texels short in [t0, 1/8] and up to about 1.3 texels in [1/8, 1/4]. The code follows 'The steps', so 'Known limits' now records this. An Illinois step (halve the kept end's clearance) cut it to about 1 to 2.4 texels in the port. That changes decision 15, so it is not done here.
- The NearSample comment is now 33 words and points to 'The steps'.
- ARayThatPassesBehindANearPoleMeetsThePole now bounds the hit's distance from the pole's edge on both sides.
- ARayUnderAnOpenSkyReadsTheMapAlongItsOwnDirection is a theory over its three rays, one search for each.
- The design doc also qualifies 'The start' near a face's corner (up to about 3 texels there) and says in 'The measurement plan' that t0 stays at 1/64 for every N.
- Follow-up ter-bytc: the specular lobe's cone has two owners with different widths, ReflectionCone and MapSearch.
- Tests: 23 in MapSearchTests (the theories count each case).

**2026-11-02T14:23:54Z**

2026-11-02: run 81bbe30d ended ready for integration (HEAD 2afdcb200): the near sample, the review's six findings fixed, new ticket ter-bytc for the two definitions of the lobe's cone. Hardening runs as a delegated task (ter-0pj8-harden-2026-11-02-a), with the rename of the test parameter gap to distanceToWall that the user asked for (the floor and the wall meet; it is the floor point's distance from the wall's base).

**2026-11-02T14:42:56Z**

2026-11-02, test sub-agent, the hardening.

Rename (the user's request): in ARoughFloorsRayFindsTheBaseOfAWallNearerThanTheFirstEvenSample the parameter gap is now distanceToWall, the floor point's distance from the wall's base along the floor. The floor and the wall meet. The design doc and the notes above say the same. Behavior did not change.

Coverage of the changed lines (MapSearchTests in Debug and Release, the only tests that reach MapSearch): 44/44 lines and 16/16 branches of src/Tertius.Rendering/Shading/MapSearch.cs. Coverlet counts no branch in Math.Max, so the 2⁻¹⁰ floor of the reach's division is line-covered but never decides a result (mutant C23).

Mutants: 30 in /tmp/ter-0pj8-mutants.json, class MapSearchTests, run with tools/mutate.py. The first run had 3 build failures (C15 and C16 left an empty else, C28 left lastLevel unread), now rewritten to compile. Four new tests killed C12, C15, C28 and C29, each red on its mutant first with --only. The table is the last full run:

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| C01 | src/Tertius.Rendering/Shading/MapSearch.cs:90 | march: c >= 0 -> c > 0 | killed | MapSearchTests.ASurfaceBeyondTheHalfFloatsNormalRangeLosesUnderAHundredthOfATexelOfParallax, MapSearchTests.ARayThatMeetsNoSurfaceIsReadAtItsOwnRoughness, MapSearchTests.TheConeReadsLevelZeroNearTheShadedPointAndCoarserLevelsFartherAlongTheRay | 4.1 |
| C02 | src/Tertius.Rendering/Shading/MapSearch.cs:94 | no front first: a behind sample brackets from t = 0 | killed | MapSearchTests.ARayThatStartsBehindAStoredSurfaceCountsOnlyTheCrossingsAfterItComesOutInFront | 4.0 |
| C03 | src/Tertius.Rendering/Shading/MapSearch.cs:84 | march: the last bracket, not the first | killed | MapSearchTests.ARayThatGoesBehindANearBarBeforeItsFirstEvenSampleStopsBeforeThatSample, MapSearchTests.ARayFindsTheBoxBeforeTheWallBehindIt, MapSearchTests.AFlatWallIsFoundExactlyByTheFirstSecantStep | 4.0 |
| C04 | src/Tertius.Rendering/Shading/MapSearch.cs:46 | NearSample 1/64 -> 1/32 | killed | MapSearchTests.ARoughFloorsRayFindsTheBaseOfAWallNearerThanTheFirstEvenSample(distanceToWall: 0.10000000000000001) | 6.3 |
| C05 | src/Tertius.Rendering/Shading/MapSearch.cs:46 | NearSample 1/64 -> 1/128 | survived |  | 6.3 |
| C06 | src/Tertius.Rendering/Shading/MapSearch.cs:46 | NearSample 1/64 -> 0 | killed | MapSearchTests.ARayThatGoesBehindANearBarBeforeItsFirstEvenSampleStopsBeforeThatSample, MapSearchTests.ARoughFloorsRayFindsTheBaseOfAWallNearerThanTheFirstEvenSample(distanceToWall: 0.29999999999999999), MapSearchTests.ARoughFloorsRayFindsTheBaseOfAWallNearerThanTheFirstEvenSample(distanceToWall: 0.5) | 6.0 |
| C07 | src/Tertius.Rendering/Shading/MapSearch.cs:87 | no near sample: k = 0 repeats t = 1/8 | killed | MapSearchTests.ARayThatGoesBehindANearBarBeforeItsFirstEvenSampleStopsBeforeThatSample, MapSearchTests.ARoughFloorsRayFindsTheBaseOfAWallNearerThanTheFirstEvenSample(distanceToWall: 0.29999999999999999), MapSearchTests.ARoughFloorsRayFindsTheBaseOfAWallNearerThanTheFirstEvenSample(distanceToWall: 0.5) | 6.4 |
| C08 | src/Tertius.Rendering/Shading/MapSearch.cs:85 | march: no sample at t = 1 | killed | MapSearchTests.ASurfaceBeyondTheHalfFloatsNormalRangeLosesUnderAHundredthOfATexelOfParallax, MapSearchTests.TheRefinementTakesEverySecantStepAfterTheFirstLandsOnAFlatWall, MapSearchTests.ARayTowardAFarPlanetMovesOnlyByThePlanetsParallax | 4.0 |
| C09 | src/Tertius.Rendering/Shading/MapSearch.cs:39 | Samples 8 -> 4 | killed | MapSearchTests.ARayThatGoesBehindANearBarBeforeItsFirstEvenSampleStopsBeforeThatSample, MapSearchTests.ARayThatMeetsARoundSurfaceFindsItWithinAHundredthOfATexel | 5.9 |
| C10 | src/Tertius.Rendering/Shading/MapSearch.cs:49 | SecantSteps 3 -> 0 | killed | MapSearchTests.AFlatWallIsFoundExactlyByTheFirstSecantStep, MapSearchTests.ARayThatMeetsARoundSurfaceFindsItWithinAHundredthOfATexel, MapSearchTests.ARoughFloorsRayFindsTheBaseOfAWallNearerThanTheFirstEvenSample(distanceToWall: 0.29999999999999999) | 5.9 |
| C11 | src/Tertius.Rendering/Shading/MapSearch.cs:49 | SecantSteps 3 -> 1 | killed | MapSearchTests.ARayThatPassesBehindANearPoleMeetsThePole | 6.0 |
| C12 | src/Tertius.Rendering/Shading/MapSearch.cs:108 | refinement: one step more | killed | MapSearchTests.TheRefinementTakesEverySecantStepAfterTheFirstLandsOnAFlatWall | 6.0 |
| C13 | src/Tertius.Rendering/Shading/MapSearch.cs:112 | refinement: c >= 0 -> c > 0 | survived |  | 3.7 |
| C14 | src/Tertius.Rendering/Shading/MapSearch.cs:113 | refinement: a front sample moves the back end | killed | MapSearchTests.ARayThatGoesBehindANearBarBeforeItsFirstEvenSampleStopsBeforeThatSample, MapSearchTests.ARayThatPassesBehindANearPoleMeetsThePole, MapSearchTests.ARoughFloorsRayFindsTheBaseOfAWallNearerThanTheFirstEvenSample(distanceToWall: 0.29999999999999999) | 3.7 |
| C15 | src/Tertius.Rendering/Shading/MapSearch.cs:115 | refinement: the back end never moves | killed | MapSearchTests.ARayThatMeetsARoundSurfaceFindsItWithinAHundredthOfATexel | 3.8 |
| C16 | src/Tertius.Rendering/Shading/MapSearch.cs:113 | refinement: the front end never moves | killed | MapSearchTests.ARayThatPassesBehindANearPoleMeetsThePole, MapSearchTests.ARoughFloorsRayFindsTheBaseOfAWallNearerThanTheFirstEvenSample(distanceToWall: 0.29999999999999999), MapSearchTests.ARoughFloorsRayFindsTheBaseOfAWallNearerThanTheFirstEvenSample(distanceToWall: 0.5) | 3.7 |
| C17 | src/Tertius.Rendering/Shading/MapSearch.cs:117 | last place: the front end, not the secant place | killed | MapSearchTests.ARayTowardAFarPlanetMovesOnlyByThePlanetsParallax, MapSearchTests.ARayThatMeetsARoundSurfaceFindsItWithinAHundredthOfATexel, MapSearchTests.ARayThatPassesBehindANearPoleMeetsThePole | 3.7 |
| C19 | src/Tertius.Rendering/Shading/MapSearch.cs:131 | secant place -> bisection | killed | MapSearchTests.ASurfaceBeyondTheHalfFloatsNormalRangeLosesUnderAHundredthOfATexelOfParallax, MapSearchTests.AFlatWallIsFoundExactlyByTheFirstSecantStep, MapSearchTests.ARayTowardAFarPlanetMovesOnlyByThePlanetsParallax | 3.9 |
| C20 | src/Tertius.Rendering/Shading/MapSearch.cs:171 | clearance without (1 - t), with 1 | killed | MapSearchTests.ASurfaceBeyondTheHalfFloatsNormalRangeLosesUnderAHundredthOfATexelOfParallax, MapSearchTests.ARayFindsTheBoxBeforeTheWallBehindIt, MapSearchTests.AHitFartherFromTheShadedPointThanFromTheEyeIsReadNoRougherThanOne | 3.8 |
| C21 | src/Tertius.Rendering/Shading/MapSearch.cs:171 | clearance without \|L\| | killed | MapSearchTests.AFlatWallIsFoundExactlyByTheFirstSecantStep, MapSearchTests.ARayThatStartsBehindAStoredSurfaceCountsOnlyTheCrossingsAfterItComesOutInFront, MapSearchTests.AHitNearTheShadedPointIsReadSharperThanAHitFarAway | 3.8 |
| C23 | src/Tertius.Rendering/Shading/MapSearch.cs:120 | reach without the 2^-10 floor | survived |  | 3.6 |
| C24 | src/Tertius.Rendering/Shading/MapSearch.cs:120 | reach 1 / \|L\| in place of t / \|L\| | killed | MapSearchTests.AHitNearTheShadedPointIsReadSharperThanAHitFarAway, MapSearchTests.TheReachOfAHitIsItsDistanceFromTheShadedPointOverItsDistanceFromTheEye, MapSearchTests.AMirrorThatFacesTheEyeReflectsWhatLiesBehindTheCamera | 3.8 |
| C25 | src/Tertius.Rendering/Shading/MapSearch.cs:165 | no short-lookup guard | killed | MapSearchTests.ARayThroughTheEyeTakesNoReadThere | 3.9 |
| C26 | src/Tertius.Rendering/Shading/MapSearch.cs:166 | short lookup counts as behind | killed | MapSearchTests.ARayUnderAnOpenSkyReadsTheMapAlongItsOwnDirection(x: 0, y: 0, z: -5, alongX: 0, alongY: 0, alongZ: 1), MapSearchTests.AMirrorThatFacesTheEyeReflectsWhatLiesBehindTheCamera | 3.7 |
| C27 | src/Tertius.Rendering/Shading/MapSearch.cs:170 | cone level without the max with 1 | killed | MapSearchTests.TheConeReadsLevelZeroNearTheShadedPointAndCoarserLevelsFartherAlongTheRay | 3.8 |
| C28 | src/Tertius.Rendering/Shading/MapSearch.cs:170 | cone level: the min with log2 W + 1 | killed | MapSearchTests.AVeryRoughLobeReadsNoLevelBeyondTheLastOfTheChain | 3.7 |
| C29 | src/Tertius.Rendering/Shading/MapSearch.cs:55 | ConeWidthPerAlpha 2.57 -> 1.285 | killed | MapSearchTests.AVeryRoughLobeReadsNoLevelBeyondTheLastOfTheChain, MapSearchTests.AtTheRaysEndTheReadsTexelSpansTheFullWidthOfTheGgxLobeAtHalfItsPeak | 5.8 |
| C31 | src/Tertius.Rendering/Shading/MapSearch.cs:28 | read roughness r rho, not r sqrt(rho) | killed | MapSearchTests.AHitNearTheShadedPointIsReadSharperThanAHitFarAway | 5.7 |
| C32 | src/Tertius.Rendering/Shading/MapSearch.cs:28 | read roughness without the min with 1 | killed | MapSearchTests.AHitFartherFromTheShadedPointThanFromTheEyeIsReadNoRougherThanOne | 3.8 |
| C33 | src/Tertius.Rendering/Shading/MapSearch.cs:69 | InverseDistance without the near plane | killed | MapSearchTests.ASurfaceNearerThanTheNearPlaneHoldsTheNearPlanesInverseDistance | 3.6 |
30 rows: killed 27, timeout 0, survived 3, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

Triage of the survivors:
- C05, NearSample 1/64 -> 1/128, judgment call. An analytic field has no texels, and the reason for 1/64 is the texel argument. A Python model of point-sampled level-0 texels at W = 128 (/tmp/ter-0pj8-port/texel.py, floor and wall, 256 floor points across one texel, the wall's base 0.3 m away) finds the wall's base for 216/256 floor points with 1/64 and 176/256 with 1/128. Every miss is a near sample inside the shaded point's own texel that reads behind the floor. A test would encode a tuned threshold between two rates, so ticket 3's measurement on the GPU owns t0. Note for the orchestrator: with 1/64, about 16 % of floor points in that model still lose the wall's base, as today's read.
- C13, the refinement's c >= 0 -> c > 0, equivalent. A secant sample with c exactly 0 puts the next secant place at its own t from either end of the bracket, so the hit moves by at most one rounding of t.
- C23, the reach without the 2⁻¹⁰ floor, judgment call. |L(t*)| < 2⁻¹⁰ needs t* within 2⁻¹¹ of 1/2, because |L(t)| ≥ |1 − 2t|. There ρ* exceeds 500 with or without the floor, and ReadRoughness, the only reader of Reach, gives 1 for every r ≥ GgxLobe.MinRoughness = 0.045 either way. The floor only keeps an exact hit at the eye finite.

New tests (MapSearchTests: 27 now):
- TheRefinementTakesEverySecantStepAfterTheFirstLandsOnAFlatWall: a wall 1 km ahead brackets in [7/8, 1], and the search reads Samples + 1 + SecantSteps times. It locks in the one bound of reads with no early stop. Kills C12.
- ARayThatMeetsARoundSurfaceFindsItWithinAHundredthOfATexel: a ball 2 m in radius, where a secant place lands behind the ball. Kills C15, whose hit lies 0.16 texel off, against 2 · 10⁻⁵ texel.
- AVeryRoughLobeReadsNoLevelBeyondTheLastOfTheChain: α = 1 reads no level above log₂ W. Kills C28.
- AtTheRaysEndTheReadsTexelSpansTheFullWidthOfTheGgxLobeAtHalfItsPeak: the last read's level against GGX's own half-maximum angle, not the constant. Kills C29.

Decisions that existing tests already lock in: the sky gives d bit for bit with reach 1 (ARayUnderAnOpenSkyReadsTheMapAlongItsOwnDirection), the near bracket's hit replaces a later hit (ARayThatGoesBehindANearBarBeforeItsFirstEvenSampleStopsBeforeThatSample), a ray that starts behind searches from where it comes out (ARayThatStartsBehindAStoredSurfaceCountsOnlyTheCrossingsAfterItComesOutInFront), and one secant step finds a plane exactly (AFlatWallIsFoundExactlyByTheFirstSecantStep).

**2026-11-02T14:54:01Z**

2026-11-02: merged into main (306fb8f75) after the hardening (e9adda5b7: 44/44 lines, 16/16 branches; 30 mutants, 27 killed, C05 and C23 judgment calls, C13 equivalent). tools/test-all.sh passed in Checked and Release on the merge. The orchestrator added the hardening's texel model to the design's 'Known limits'. Closed.
