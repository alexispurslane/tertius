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

**2026-10-31T14:22:50Z**

2026-10-31: launched through the workflow MCP (ultracode-ticket-completion, version 88962f00-4bb7-41be-89dd-bb3951a51fd8), run 18ca0692-0168-4304-b5d3-e2dd322a4375, branch ter-t2w6-skin-scatter-wide from cd8b233c. The experiment's diff and captures are in ~/Library/Caches/tertius/ter-t2w6-experiment/.
