---
id: t0-gkuz
status: open
deps: []
links: []
created: 2026-11-15T08:36:14Z
type: task
priority: 2
assignee: Alexis Purslane
parent: t0-iido
---
# Two test gaps that the frame time cuts' mutants showed: a mature pixel beside young taps in the filter's feedback step, and a history that a fourth tap carries

Found by the mutation run of the frame time cuts (t0-89zi). Three mutants survive on lines that the ticket only moved, so the gaps are older than that ticket. They belong to the hardening of Design X.


## Design (line orchestrator, 2026-11-15)

Tests only. No change under `src`.

### Gap 1: a mature pixel beside young taps in the filter's feedback step

The feedback step of the diffuse filter (`diffuse_filter_wavelet.glsl` under `TERTIUS_FILTER_FEEDBACK`) keeps two sums over a pixel's taps:

```glsl
deviationSum += share * abs(light.a);
carriedSum += share * max(light.a, 0.0);
```

where

- `light.a` = the tap's deviation from the filter's input. A young tap (a history shorter than `YoungFrames`) carries its deviation with a negative sign.
- `share` = the part of the difference between the tap and the pixel that the step moves.
- `deviationSum` = the sum that reaches the next filter step: every tap counts, by its magnitude.
- `carriedSum` = the sum that reaches the store, the fast history's retained deviation: only mature taps count.

Two mutants survive: `abs(light.a)` replaced by `light.a` (S01), and `max(light.a, 0.0)` replaced by `abs(light.a)` (S02). No test has a mature pixel with young neighbors.

The test, in `DiffuseFilterRenderTests`: one flat surface. A center region holds a history of at least `YoungFrames` draws, and the pixels around it hold a young history with a known nonzero noise. After one draw, read the filter's output deviation and the fast history's retained deviation at a mature pixel on the border. Two statements, one test each:

- The retained deviation of a mature pixel takes nothing from its young neighbors.
- The deviation that a mature pixel hands to the next filter step counts its young neighbors' noise.

### Gap 2: a history that only the fourth tap carries

`reprojectionOf(place, bits)` (`signals.glsl`) weights the four texels around a last place by the bilinear weights, each times its pass bit. The mutant "`surfacePassBits` drops tap 3" (S18) survives: no test has a history where tap 3, the texel at (+1, +1), holds weight and its value differs from the other taps.

The test, in `AccumulatorRenderTests` or beside the surface test's tests, whichever holds the fitting scene builder: a last place whose fraction is near (1, 1), so that tap 3 holds nearly all the weight, over a slow history whose texels differ, with taps 0 to 2 failing the surface test (another surface) and tap 3 passing. Statement: a pixel whose surface lies only under the fourth texel of its last place keeps that texel's history.

## Acceptance

- Each new test is red on its mutant first (`tools/mutate.py` with a list of S01, S02 and S18 by their new line numbers), and the note holds the table.
- The Checked and Release suites of the Rendering tests pass.
