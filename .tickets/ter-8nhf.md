---
id: ter-8nhf
status: open
deps: []
links: []
created: 2026-11-10T13:33:54Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# A block that mixes two roughnesses shares reflection rays whose capped weights darken the reflection

Found by the review of ter-4oow, by static reasoning; not reproduced. The lobe fit (screen_trace_plan.comp, insideLobe) compares the two pixels' mirror directions against p's lobe width alone. At a block that holds two roughnesses, p can take the ray of a chosen pixel c whose lobe is much wider (a smooth p, about 0.004 rad, takes a rough c's ray drawn over about 0.3 rad) or much narrower. The resolve's weight f_p / pdf_c (screen_trace_resolve.comp, specularWeightOf) is then about 0 for most rays and is cut to MostRayWeight 4 for the rare large ones, so p's specular sample is biased dark, and the uncapped history keeps the bias. Expected look: a dark seam in reflections along material edges, such as polished tile beside grout, and dimmed reflections on surfaces with a fine roughness texture. The survey scenes and the plate have one roughness per surface and cannot show it. Under the original rule a rough pixel could take a smooth pixel's ray too; a smooth pixel could not.

## Design

Candidate: let insideLobe also require similar lobe widths, such as abs(log2(c.alpha / p.alpha)) <= 1. It changes sharing only where roughness varies within a block. Measure the deck's reflection rays again (2.65 ms with the lobe fit): a roughness texture on the deck would reduce sharing there.

## Acceptance Criteria

A GPU test with a floor whose roughness alternates per texel between 0.045 and 0.4 shows its converged specular history within a few percent of the same floor under own rays, and the deck's reflection-ray time is reported.

