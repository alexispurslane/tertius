---
id: ter-t2w6
status: in_progress
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

**2026-10-31T14:32:10Z**

Settled: SkinProfile.WidthScale = 4 is applied once, as the unit ProfileMillimeter = WidthScale · 1 mm. The reach is 8 of those units (32 mm) and every variance is in their square (x16). The shader keeps no copy of the reach: scatterAlong takes it from the kernel's outermost tap, kernel[2n].a, and SkinScatter.Pass from ScatterKernel.Reach, the same value. This costs one uniform read and removes the second store, so no test that compares two copies is needed.

**2026-10-31T14:32:10Z**

Measured the red ring beside the nose (2026-10-31, head capture of scripts/skin-and-hair.json, ring = head pixels on the lit side within 30 px of the nose's edge). 16 taps per side against 8: mean red change under 0.1 of 255, so it is not the tap steps, and the default stays 8. Rejecting every tap across a depth step of 4 mm: mean red -5.7, -6.0, -3.1 of 255 in the bands 0-10, 10-20, 20-30 px (max -22). The same rejection in a control scene without the nose: -0.4 to -0.6. So the ring is light across the nose's depth step of about 11 mm, which the slope limit lets through for every tap past 6.4 mm at the 32 mm reach. Reported as a fork.

**2026-10-31T14:32:10Z**

The capture of the head and the ear at 8 taps matches cap-wide of the experiment bit for bit. The C#-against-GPU tolerance of SkinScatterRenderTests now scales with the largest of the light before, D and the expected result, because the HDR image holds the result as a half float and the wider scatter can double a dark pixel's light.

**2026-10-31T14:58:43Z**

Settled (user, 2026-10-31, option B): the slope rule measures each tap's offset in the measured profile, run = |offset| / SkinProfile.WidthScale, in SkinScatter.Pass and in scatterAlong. The shader gets the scale as the specialization constant skinWidthScale (constant_id 3), which ShaderConstants.Specialization sets from SkinProfile.WidthScale for every pipeline, so the scale stays written once and no new constant exists. A specialization constant was chosen over a field in the kernel buffer because it is the existing path for C# values fixed for the renderer's life.

**2026-10-31T14:58:43Z**

Measured on 2026-10-31, head capture of scripts/skin-and-hair.json, lit side within 30 px of the nose's edge. The ring is the nose's share of the red change against the measured width: (wide - measured with the nose) - (wide - measured without the nose). Bands 0-10, 10-20, 20-30 px: +1.76, +1.02, +0.47 of 255 (per-pixel max +5) under the new rule, against +7.10, +6.68, +3.03 at 099a36cd. Below 2 in each band, not zero: the outermost taps, past 25.4 mm, still cross the 11 mm step, as taps past 6.4 mm did at the measured width. Against the capture with every tap across a 4 mm step rejected: +0.38, +0.38, +0.56. A capture of this code at WidthScale 1 matches now-cap bit for bit.

**2026-10-31T14:58:43Z**

Head capture against cap-wide/head.png: 106,471 pixels differ, 93,814 of them by 1 of 255 across the whole frame, because the auto exposure moved by 0.001 stops (-7.7872 against -7.7882). 12,657 pixels differ by more than 1, all on the head (x 423-843, y 135-577). The nose and its 30 px ring hold 8,000 of them, mean red -1.49, min -22, max +15. Within 10 px inside the head's silhouette (radius about 225 px): 1,801 pixels by more than 1, mean red +0.42 and +0.53, max change 11 of 255. The ear capture differs by at most 1 of 255. In terms of slope, a tap now keeps all its light up to tan 60° / 4, about 23°, and none from about 41°.
