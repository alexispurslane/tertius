---
id: ter-0pj8
status: in_progress
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

Ticket 1 of ter-jw7g's design (docs/plans/environment-map-distances.md, 'Ticket 1' and 'The search'). MapSearch.Hit and IInverseDistanceField: the march of 8 samples, the 3 regula falsi steps, the cone's level and the reach, against analytic test fields (the sky, a plane, a box before a wall, a pole before a wall, a far planet, a sphere room). Owns src/Tertius.Rendering/Shading/MapSearch.cs and tests/Tertius.Rendering.Tests/MapSearchTests.cs. Acceptance: tests 1 to 12 of the design pass in Checked and Release, each red first; Samples, SecantSteps and ConeWidthPerAlpha are named once.


## Notes

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

A disagreement inside the design, reported as a fork: a hit nearer the shaded point than the first march sample is never found. The first sample is at t = 1/8, and front first discards it when it lies behind. Every later sample also lies behind the wall, so the search finds no hit and reads today's direction. Example: a floor point 4.35 m from the eye finds no wall nearer than about 0.55 m along its ray (a probe found no hit at gaps of 0.1, 0.3 and 0.5 m, and a hit at 0.7 m). The design's decision C ("A rough floor 10 cm from a wall: the wall's base sharp") and "The cone at each sample" ("a floor beside a wall finds the wall's base") say otherwise. Nothing gets worse than today, because a miss reads today's image. The code follows "The steps" exactly.

ARCHITECTURE.md: no change. "Doc changes" names no change for ticket 1, and no engine code reads MapSearch yet.

**2026-11-02T13:43:00Z**

2026-11-02, builder, the near sample (the orchestrator's answer to the fork above): MapSearch.Hit takes one more sample before the even ones, at t0 = MapSearch.NearSample = 1/64, then the 8 even samples t = k / 8 with the same rules. Front first, the first bracket and the 3 regula falsi steps are unchanged. Each pixel reads the field at most N + K + 1 = 12 times.

Why 1/64: the ray's point at t0 lies |x| / 63 from the shaded point, which the eye sees at most about 1/63 rad from x, about one level-0 texel at W = 128. A nearer hit falls in the shaded point's own texel. The near sample changes no hit of the even samples: in front, it is an earlier front end that a later front sample replaces, and behind, front first ignores it. It adds only the bracket [t0, 1/8].

Tests (20 now, green in Checked):
- New: ARoughFloorsRayFindsTheBaseOfAWallNearerThanTheFirstEvenSample, a theory over walls 0.1, 0.3 and 0.5 m beyond a floor point 4.35 m from the eye, with α = 0.36. It asserts a hit within 10⁻⁹ rad of the ray's meeting with the wall, because the clearance is linear on a plane. Red first on all three gaps: "Expected boolean to be True ... but found False".
- Changed: TheConeReadsLevelZeroNearTheShadedPointAndCoarserLevelsFartherAlongTheRay now counts Samples + 1 reads for a sky ray, the near sample and every even one. Red first: 8 reads where 9 were expected. Its bounds did not change.
- Test 5, ARayThatStartsBehindAStoredSurfaceCountsOnlyTheCrossingsAfterItComesOutInFront, already covers a near sample behind: the point 5/63 m along its ray lies behind the panel. A comment now says so. No other test changed.

Design doc: "The steps" (pseudocode, the start, no sample at t = 0, the march, the near sample's effect, the settled date and the two rejected forms), decision 15, decision A's table, "Cost" (12 reads), the tests list, "Known limits" (a hit nearer than |x| / 63 is lost), the C# sketch and ticket 3's work line for mapHit.
