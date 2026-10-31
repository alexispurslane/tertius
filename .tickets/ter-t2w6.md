---
id: ter-t2w6
status: closed
deps: []
links: []
created: 2026-10-27T15:45:02Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-n7lw
---
# Skin scatter four times wider than the measured profile

The user's decision (2026-10-27): after a side-by-side capture of the skin test head, the user said "wider is much better in my opinion!" At the measured width of d'Eon, Luebke and Enderton 2007, the scatter on the 0.1 m head at 0.45 m is a band of about 3 px: the largest change against --subsurface off is +21 of 255 in red, over 974 px. Four times wider, the change is +61 in red over 18,844 px, and the shadow line of the nose turns clearly red and soft.

The experiment, in the scratch worktree exp-skin-scatter-wide (uncommitted): SkinProfile.Reach 0.008 -> 0.032, SkinProfile.SquareMillimeter 1e-6 -> 16e-6 (every variance x16, so every width x4), and skin_scatter.glsl Reach 0.008 -> 0.032. Captures: scratchpad cap-off, now-cap and cap-wide of session 901b2ee9.

## Design

Settled by the rule of the simplest mechanism: one named width scale of the skin profile, 4, applied in one place to the Gaussians' widths (variances x16) and to the reach (x4). No material parameter and no renderer option, because only one skin exists. The doc names it an artistic choice of the user over the measured profile.

Open points for the builder to measure, not to guess:
- At 4x reach, the default of 8 taps per side spaces the outer taps 4x farther apart. The wide capture shows a faint red ring on the head beside the nose: +4 to +7 of 255 in red within 30 px of the nose's edge, and none at the measured width. Find whether it is the steps of the outer taps or light across the depth step from the nose. If it is the taps, raise the default taps. If it is the depth step, report it as a fork.
- Tests that pin kernel values or Reach change with the scale. Rewrite them in terms of the scale, so that they still test the kernel's construction.

## Acceptance Criteria

The skin-and-hair head capture matches cap-wide/head.png of the experiment. docs/plans/skin-and-hair.md (the reach row near line 61, the profile near lines 579 and 601, the safe-offset row near line 812, the test near line 854) and ARCHITECTURE.md say the width scale and the user's reason. The reference images of skin, if any, are re-approved with the three-image review.


## Notes

**2026-10-31T14:22:50Z**

2026-10-31: launched through the workflow MCP (ultracode-ticket-completion, version 88962f00-4bb7-41be-89dd-bb3951a51fd8), run 18ca0692-0168-4304-b5d3-e2dd322a4375, branch ter-t2w6-skin-scatter-wide from cd8b233c. The experiment's diff and captures are in ~/Library/Caches/tertius/ter-t2w6-experiment/.

**2026-10-31T14:32:10Z**

Settled: SkinProfile.WidthScale = 4 is applied once, as the unit ProfileMillimeter = WidthScale · 1 mm. The reach is 8 of those units (32 mm) and every variance is in their square (x16). The shader keeps no copy of the reach: scatterAlong takes it from the kernel's outermost tap, kernel[2n].a, and SkinScatter.Pass from ScatterKernel.Reach, the same value. This costs one uniform read and removes the second store, so no test that compares two copies is needed.

**2026-10-31T14:32:10Z**

Measured the red ring beside the nose (2026-10-31, head capture of scripts/skin-and-hair.json, ring = head pixels on the lit side within 30 px of the nose's edge). 16 taps per side against 8: mean red change under 0.1 of 255, so it is not the tap steps, and the default stays 8. Rejecting every tap across a depth step of 4 mm: mean red -5.7, -6.0, -3.1 of 255 in the bands 0-10, 10-20, 20-30 px (max -22). The same rejection in a control scene without the nose: -0.4 to -0.6. So the ring is light across the nose's depth step of about 11 mm, which the slope limit lets through for every tap past 6.4 mm at the 32 mm reach. Reported as a fork.

**2026-10-31T14:32:10Z**

The capture of the head and the ear at 8 taps matches cap-wide of the experiment bit for bit. The C#-against-GPU tolerance of SkinScatterRenderTests now scales with the largest of the light before, D and the expected result, because the HDR image holds the result as a half float and the wider scatter can double a dark pixel's light.

**2026-10-31T14:50:52Z**

2026-10-31: run 18ca0692 stopped at a fork (the red ring beside the nose comes from the nose's depth step) and failed its output schema, because the service never showed the builder the schema. The user chose option B: the depth rule stays at the measured profile's size, through the same width scale. Continuation run 7210e798-27e9-4c1b-b4af-47b428953fda (version 3cde7d7d-f5f8-4f91-aee3-b2258cb90dc6) builds on 099a36cd.

**2026-10-31T14:58:43Z**

Settled (user, 2026-10-31, option B): the slope rule measures each tap's offset in the measured profile, run = |offset| / SkinProfile.WidthScale, in SkinScatter.Pass and in scatterAlong. The shader gets the scale as the specialization constant skinWidthScale (constant_id 3), which ShaderConstants.Specialization sets from SkinProfile.WidthScale for every pipeline, so the scale stays written once and no new constant exists. A specialization constant was chosen over a field in the kernel buffer because it is the existing path for C# values fixed for the renderer's life.

**2026-10-31T14:58:43Z**

Measured on 2026-10-31, head capture of scripts/skin-and-hair.json, lit side within 30 px of the nose's edge. The ring is the nose's share of the red change against the measured width: (wide - measured with the nose) - (wide - measured without the nose). Bands 0-10, 10-20, 20-30 px: +1.76, +1.02, +0.47 of 255 (per-pixel max +5) under the new rule, against +7.10, +6.68, +3.03 at 099a36cd. The ring of light across the nose's step is gone: with every tap across a 4 mm step rejected, the nose's share is +1.77, +1.05, +0.48, within 0.03 of the new rule's (corrected on 2026-10-31 after review: an earlier version of this note blamed the outermost taps past 25.4 mm). The remaining share has another cause, measured in a later note. A capture of this code at WidthScale 1 matches now-cap bit for bit.

**2026-10-31T14:58:43Z**

Head capture against cap-wide/head.png: 106,471 pixels differ, 93,814 of them by 1 of 255 across the whole frame, because the auto exposure moved by 0.001 stops (-7.7872 against -7.7882). 12,657 pixels differ by more than 1, all on the head (x 423-843, y 135-577). The nose and its 30 px ring hold 8,000 of them, mean red -1.49, min -22, max +15. Within 10 px inside the head's silhouette (radius about 225 px): 1,801 pixels by more than 1, mean red +0.42 and +0.53, max change 11 of 255. The ear capture differs by at most 1 of 255. In terms of slope, a tap now keeps all its light up to tan 60° / 4, about 23°, and none from about 41°.

**2026-10-31T15:23:15Z**

Cause of the remaining nose share (+1.76, +1.02, +0.47 of 255 in red on the lit side, 0-10, 10-20 and 20-30 px), measured on 2026-10-31 with a capture of the head without its nose and with --subsurface off (/tmp/t2w6-fix/nonose-off). It is the edge rule, the first known limit: a tap that lands on the nose gives way to the center's light, while without the nose it takes the head surface hidden behind the nose, which is darker (mean red 62 against 102 of 255 in the lit 0-10 px band, scatter off). A numpy model of only that rule (separable rows then columns, 2890 px/m, D = the no-nose scatter-off capture decoded from sRGB, every nose tap rejected) gives +0.47, +0.03, +0.00 at the measured width and +2.69, +1.39, +0.65 at 4x: a difference of +2.2, +1.4, +0.7 against the measured +1.76, +1.02, +0.47. At the measured width the captures give +0.39 and the model +0.47. No depth-step rule removes it, so declined option C would not either. Docs: decision 16, decision 34 and the known limits of docs/plans/skin-and-hair.md, ARCHITECTURE.md (Skin scatter).

**2026-10-31T15:23:42Z**

Review fixes (2026-10-31): finding 3 resolved by its second fix, a doc comment on SkinScatter.Pass that the slope rule reads every kernel's offsets at SkinProfile.WidthScale. Reason: both fixes give the same image, and by the user's rule a behavior-preserving redesign follows the quick fix. The first fix, the kernel that carries its width scale in its uniform block, is ticket ter-o0a6 for the user to decide. Finding 4: 'the slope rule' is the one name in ARCHITECTURE.md and the design doc. Finding 2: the cost is stated as the surface turning more than about 23° from the eye, fully at 41°, the outer 60 % of a round head's radius, with the measured bands.

**2026-10-31T15:34:01Z**

Orchestrator's pass before the merge (2026-10-31). Hand mutations, each built and run against its test class under timeout 120: (1) ShaderConstants sends 1 in place of SkinProfile.WidthScale: killed by ThePassesMatchTheirCopyInCSharp and AResizedRendererStillScattersAtItsNewSize. (2) skin_scatter.glsl drops the division by skinWidthScale: killed by the same two. (3) SkinScatter.Pass divides by twice the width scale: killed by ATapBetweenOneAndTwoSlopeLimitsMixesItsLightWithTheCenters, ATapWithinTheSlopeLimitKeepsItsOwnLight and ATapWithinTheSlopeLimitOfItsMeasuredOffsetKeepsItsOwnLight. (4) SkinScatter.Pass tests SkinProfile.Reach in place of kernel.Reach: killed by TheKernelsOwnOutermostTapDecidesWhetherItReachesPastThePixel. (5) skin_scatter.glsl tests the constant 0.032 m in place of the kernel's outermost tap: survived, equivalent, because the GPU only ever runs SkinProfile.Human's kernels, whose outermost tap is SkinProfile.Reach at every tap count; the C# copy's test with its own kernel covers the rule. Coverage of the changed lines: every changed executable line in src carries a killed mutation above, so the tests run each one; the other changed lines are constants and doc comments. Open: ter-o0a6 (the kernel carries its width scale) waits for the user.
