---
id: t2-gfq5
status: in_progress
deps: []
links: []
created: 2026-11-12T06:39:03Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# The local shadow filter with nine bilinear comparison reads in place of nine gathers: 0.40 ms less in the deck's direct pass

The pair research of t2-mlvy (docs/plans/light-pairs.md, 'The nine-read shadow filter', and docs/research/light-pairs/pair/notes.md) found that the local shadow filter of shadows.glsl can read its 5 x 5 texels with nine bilinear comparison reads (textureGrad on the shadow sampler) in place of nine gathers of four comparisons each. The weights are the same in exact arithmetic. Measured on the deck on 2026-11-11, busy medians: the direct pass 4.093 ms on main and 3.696 ms with the filter, the transparent pass 2.085 ms and 2.125 ms, the frame span's median 24.382 ms and 24.206 ms, its 95th percentile 25.003 ms and 25.562 ms. The deck still differs from main by at most 8 steps of 255, with 99 % of the pixels within 1 step and 6.96 % of the pixels changed. The offline compile gives r112 as the highest register against r116 on main, which is one step up in the Asahi driver's table of shader copies at once; that fits the gain and does not prove its cause. The patch is docs/research/light-pairs/pair/c9-shadows.patch.

## Acceptance Criteria

The filter is in shadows.glsl with tests that state its weights against the exact 5 x 5 box filter. A film under motion shows no new flicker along shadow edges. A run on a Linux driver or lavapipe shows the shadow edges, because the hardware's rounding of the bilinear weights differs by driver (up to 2.3 % of full light in a constructed worst case at 4 bits). A trace of the deck and of the hall with the other frame-time cuts gives the frame span's median and 95th percentile. The reference images that change get a real review.


## Notes

**2026-11-12T15:12:13Z**

State of 2026-11-12, before the user's choice. Nothing is merged. The branch t2-gfq5-nine-read-shadow-filter holds the sixteen corner reads (76512c07b) and a merge of main.

The pair's nine bilinear reads do not go in: they fail TheShadowsEdgeRampsInProportionToTheReceiversPlaceWithNoSubTexelSteps (6.7e-4 off the line against 1.5e-4), because the hardware rounds one weight per axis to its sub-texel precision.

Three exact or near forms were measured against main's nine gathers. Mac: Apple M1 Max, exclusive takes, three deck traces per build in turns and one hall trace, busy medians. The first take ran on main 5c83ef6c with the stock MoltenVK, the second on main 8a71d1c1 with the patched MoltenVK. Linux: the workbench's RTX 2000 Ada, five runs per side inside one job.

| Form | Exact | Mac deck direct | Mac deck transparent | Mac deck frame span | Linux deck frame span | Linux hall frame span |
|---|---|---|---|---|---|---|
| nine gathers (main) | yes | 4.095 | 2.086 | 21.273 (21.022 to 21.746) | 8.896 | 6.148 |
| nine bilinear reads | no | 3.683 (first take, main 4.099) | 2.129 (2.099) | 21.765 (main 22.175) | +0.329, every run differs | +0.399, every run differs |
| sixteen corner reads | yes | 3.792 | 2.022 | 20.687 (20.663 to 20.703) | +0.803, every run differs | +0.785, every run differs |
| nine gathers, rows in a loop of a run-time bound | yes | 3.849 | 2.207 | 20.991 (20.849 to 21.178) | 0.000, within the noise | +0.051, every run differs |

The Mac pays for the values that the shader holds at once: the offline compile gives r116 for the gathers and r112 for the corner reads and for the row loop. The Linux card pays for each filtered comparison read, and a gather is its cheapest way to four comparisons. The row loop gains in the Mac's direct pass and loses 0.12 ms in its transparent pass, whose shader already spills.

The corner reads on the Mac: stills of the deck, the hall and the cabin and a film of the hall's moving lamps equal main's to within 2 pixels of one step of 255. All 34 local shadow tests pass, and they kill all 12 listed mutants (S01 to S12 of /private/tmp/t2-mlvy/c9/mutants.json: three rows or columns, swapped weights, corners paired 0,1 and 2,3, a read at a texel center, the reads one texel down or transposed, no weight along y, a mean counted as one texel).

The open choice: main as it is; the corner reads under MoltenVK and the gathers elsewhere; or the row loop in the direct pass on every device.
