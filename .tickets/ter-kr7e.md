---
id: ter-kr7e
status: closed
deps: [ter-uqmn]
links: []
created: 2026-10-31T21:15:53Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [rendering, hair, shadows, tests]
---
# Test that a back-lit hair card keeps the shadow of a nearby caster under the sun's cascades

Found by the hand mutations of ter-uqmn (2026-10-31). In modelLight (lighting.glsl), a fiber passes abs(geometricNdotL), the cosine of its geometric normal turned toward the light, to lightVisibility (ter-k1ne, decision 27 of docs/plans/skin-and-hair.md). The mutant that passes the raw, negative cosine survives every test: HairRenderTests.AHairCardLitFromBehindDoesNotShadowItself, which the fixer named, still passes. The reason: tileShadow uses only sinTheta, which is even in the cosine, and sampleCascade uses the cosine only in the receiver-plane slope, slope = -n.xy / max(n.z, MinimumReceiverCosine = 0.1), added per tap as max(dot(slope, offset), 0). So a negative cosine takes the floor 0.1 and only makes the slope bias larger (about 0.37/0.1 against 0.37/0.93 for the test's sun), which can only make taps more lit. The card's own lighting does not change, but a caster close behind the card (toward the sun) loses part of its shadow on the card: light leaks. The mutant is not equivalent. The work: a render test with a back-lit hair card and an opaque occluder a few cascade texels behind it toward the sun, at a gap between the true slope bias and the floored one, that expects the card's pixel ahead to stay in the occluder's shadow. Watch it fail with the mutant (the raw cosine) and pass with abs. Pick the gap from the cascade's texel size, not by trial, and state the arithmetic in the test's comment.


## Notes

**2026-10-31T21:30:55Z**

Workflow run a781b559-effe-4bfc-8271-9453dfaa8bcc (version 869b0b07), branch ter-kr7e-backlit-fiber-shadow-test from d4bd48de. A render test with an occluder at a gap chosen by arithmetic between the true and the floored slope bias; it must fail with the raw-cosine mutant.

**2026-10-31T21:49:31Z**

Settled (2026-10-31): HairRenderTests.AHairCardLitFromBehindKeepsTheShadowOfACasterCloseBehindIt. Sun (0.2, 0.3, -0.93), the sibling test's: cosine 0.93 with the card's normal turned to it, tan 0.39, slope direction (-0.58, 0.81) in the light's axes. First-cascade texel 5.85 cm (sphere radius 59.6 m over 2,040 texels). The 4x4 kernel's receiver-plane term reaches 0.81 texel with the true cosine and 7.6 with the floor 0.1. The real code shadows every tap when the depth gap along the light, from the lookup point, exceeds 0.81 + 0.54 (2x2 footprint) + 0.5 (constant bias) + 0.58 (caster slope bias, upper bound) = 2.4 texels. Gap chosen: 3 texels (18.5 cm along the card's normal, with the 0.36-texel normal offset). With the floor, the 4 uphill taps with dot(u, offset) 0.93 to 2.1 pass over the caster. Measured: real code shows 0 at the pixel ahead; mutant (raw cosine) shows 0.0279 red against the unshadowed 0.093 (30%). Tolerance 2% of the unshadowed red. The hair card does not cast; the occluder is a standard card that casts. The test asserts the premises (true need < gap - 0.5, floored reach > gap + 4, card in the first cascade alone). The class passed 5 of 5 runs in Checked.

**2026-10-31T22:26:13Z**

Review fixes (2026-10-31). (1) The test now asserts that renderer.LastScene.CascadeSets[0][0].TexelSize equals FirstCascadeTexel() within 1e-5 relative, so the gap is in the renderer's real texels. (2) The mutant premise now states the derived condition for the most uphill tap: Reach(-c) - Reach(c) - footprint (across / c) + ConstantBiasTexels = 6.71 > GapTexels + SpareTexels = 3.5. The unexplained '+ 4' is gone. SpareTexels = 0.5 serves both premises: four times the 0.125-texel depth step of the 16-bit fallback map. The doc comment states the condition. (3) The leak is a named local. In Debug the mutant's failure message now names 'leak'. In Checked it still names 'lit.X', because optimized line information points AwesomeAssertions at the previous statement. Filed ter-fc0y for that project-wide issue. Mutant rerun: 0.0279 shown against the tolerance 0.00186, in Checked and in Debug. The shader was restored and touched.

**2026-10-31T22:52:19Z**

Orchestrator's pass before the merge. Coverage: no src line changed (one test and tickets), so changed-line coverage has nothing to measure. Hand mutations in the ticket worktree against HairRenderTests: k1 lighting.glsl passes the raw geometricNdotL to lightVisibility for a fiber (the ticket's mutant): killed by the new AHairCardLitFromBehindKeepsTheShadowOfACasterCloseBehindIt; k2 no flip of the shadow normal for a backlit fiber: killed by AHairCardLitFromBehindDoesNotShadowItself.
