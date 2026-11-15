---
id: t8-r6dw
status: closed
deps: []
links: []
created: 2026-11-15T04:51:47Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# The t0-mzhr scratch probe never fills the 18-draw window of the diffuse signal, so region, window 18 holds every diffuse history at 4 draws

Found 2026-11-14 by t0-y46f (docs/research/t0-y46f-window-18-excess/README.md, section "The window bug").

The scratch patch docs/research/t0-mzhr-regional-test-cycle/scratch-probe-on-73f4a6548.patch writes the window ring only under TERTIUS_LIGHT_AGE in accumulate_temporal.comp. The diffuse signal has the half-precision history mode, so only its spatial dispatch takes the light-age variant (ShaderVariant.LightAge holds accumulate_spatial.comp alone). Its temporal dispatch never writes the ring, and its spatial dispatch reads the unwritten mean as the fast estimate. The regional test then fires at every diffuse pixel in every draw and holds the length at the fast cap of 4 draws.

Measured in the lamp hall still without jitter: the diffuse length is below 8 draws at 100.0 % of the ceiling and wall pixel draws. The mean is 4.00 and 3.92 draws, at the tolerances 10, 20, 40 and 80 %. With the t0-y46f switch TERTIUS_X_WINDOW_ALL=1 the window fills, and the mean length is 28.6 and 22.8 draws.

So every t0-mzhr record of region, window 18 measured a diffuse history held at 4 draws, not the 18-draw test. These are the stills, the bay film, the cabin switch and the lag films. Take them again with the t0-y46f patch and TERTIUS_X_WINDOW_ALL=1 before a remedy relies on them.

The t0-y46f patch also passes the specialization constants to the accumulation pipelines, which the t0-mzhr patch did not. With every switch unset this changes no image. But TERTIUS_X_LEAST_MOTION (constant 13, sample_age.glsl) now also reaches the accumulation. So a retake that sets it measures more than the same take on the t0-mzhr patch.


## Notes

**2026-11-15T08:44:14Z**

Line orchestrator, 2026-11-15: the corrected scratch patch is docs/research/t0-y46f-window-18-excess/scratch-probe-on-73f4a6548.patch with TERTIUS_X_WINDOW_ALL=1. The t0-mzhr record carries a correction at its top. The new takes of the films and the switch with the corrected window are part of t0-xlcu. The probe is scratch code, so nothing in src changes, and this ticket closes.
