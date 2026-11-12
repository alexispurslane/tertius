---
id: t2-gfq5
status: closed
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

**2026-11-12T16:17:42Z**

The user chose on 2026-11-12: two exact filters, picked once per device by driver. The sixteen corner means run on MoltenVK and the nine gathers on every other driver. `LocalShadowAtlas.Reads` names a way, so the tests run both filters on every device.

What the branch holds:

- `LocalShadowReads` (`ForDriver`, `NineGathers`, `SixteenCornerMeans`) and `LocalShadowAtlas.Reads`.
- The rule by driver in `VulkanRenderer.HeldValuesCost` and `TileReadsFor`, beside `StampsLatchLate`, with its measured reason.
- The specialization constant `tileCornerMeans` (constant_id 11, `ShaderConstants.TileCornerMeans`), which every pipeline that samples a tile gets: the shading pipelines from `ShadingConstants`, the environment faces from `FaceConstants`.
- Both filters in `tileShadow` (`shadows.glsl`). The gathers are main's code, unchanged.

The mutants of the new lines, from `tools/mutate.py` in three takes alone on the GPU. The test classes are `LocalShadowRenderTests`, `LocalShadowSettingTests` and, in the third take, `ShaderConstantsTests`.

| id | place | change | result |
|---|---|---|---|
| S01 | shadows.glsl:312 | three rows of corners | killed |
| S02 | shadows.glsl:314 | the rows' weights swapped | killed |
| S03 | shadows.glsl:314 | the rows paired 0,1 and 2,3 | killed |
| S04 | shadows.glsl:315 | three columns of corners | killed |
| S05 | shadows.glsl:317 | the columns' weights swapped | killed |
| S06 | shadows.glsl:317 | the columns paired 0,1 and 2,3 | killed |
| S07 | shadows.glsl:318 | the read at a texel center along x | killed |
| S08 | shadows.glsl:318 | the reads one texel down | killed |
| S09 | shadows.glsl:318 | the reads transposed against the weights | killed |
| S10 | shadows.glsl:319 | no weight along y | killed |
| S11 | shadows.glsl:324 | a mean counted as one texel | killed |
| S12 | shadows.glsl:318 | the read at a texel center along y | killed |
| S13 | shadows.glsl:304 | the two filters swapped | survived |
| C01 | VulkanRenderer.cs:257 | the rule by driver reversed | killed |
| C02 | VulkanRenderer.cs:270 | asked gathers become means | killed |
| C03 | VulkanRenderer.cs:271 | asked means become gathers | killed |
| C04 | VulkanRenderer.cs:197 | the constant reversed | killed |
| C05 | VulkanRenderer.cs:196 | the constant always set | killed |
| C06 | ShaderConstants.cs:126 | the constant's value never sent | killed by the new test `TheWayToReadAShadowTileReachesItsConstant`, after it survived the second take |
| C07 | VulkanRenderer.cs:2082 | the faces' constants without the reads | killed |
| C08 | EnvironmentFacePass.cs:67 | the solid faces' pipeline without the constants | survived |
| C09 | EnvironmentFacePass.cs:91 | the cutout faces' pipeline without the reads | survived |

The three survivors, each a judgment call:

- S13: both filters give the same shadow by design (`BothWaysToReadATileGiveTheSameShadow`), so no image tells which one a shader runs. The swap changes only speed. The timed Mac take and the Linux A/B measure it.
- C08 and C09: the same reason for the environment faces. A face pipeline with the other filter draws the same face, a little slower on one of the two GPUs.

The mutant run also found two gaps, both fixed on the branch. `ShaderConstantsTests` did not list the new constant for the three shaders that declare it, and it now lists `puff.vert` too. The environment face pass made its pipelines with default constants, so its faces ran the gathers on every driver.

**2026-11-12T16:49:27Z**

The commit hook reported the renderer's constructor as too long (85 lines), so the making of the shading constants moved into VulkanRenderer.ShadingConstantsFor. The constructor is now 76 lines, still over the limit of 60: the rest of it is the creation order of the renderer's parts, which this ticket does not touch. The mutants C04 and C05 ran again on the moved lines (VulkanRenderer.cs:2077 and 2076), and both are killed again.

**2026-11-12T17:17:25Z**

The checks of the choice by driver, on the branch after the merge of main at 65cec716 (2026-11-12):

- Linux, the workbench's RTX 2000 Ada, five runs per side in one job per scene: the deck's frame differs from main's by +0.004 ms and the hall's by +0.015 ms, both within the noise. So Linux is unchanged. The two jobs counted $0.10.
- Mac, Apple M1 Max, one exclusive take, the deck three traces per build in turns: the direct pass takes 3.787 ms against 4.087 ms, the transparent pass 2.025 ms against 2.070 ms, and the frame span 20.187 ms (20.131 to 20.291) against 20.562 ms (20.543 to 20.584). Every trace of the branch lies below every trace of main.
- The hall, one trace per build: the direct pass takes 3.822 ms against 4.179 ms, and the frame span 17.002 ms against 17.441 ms. The first hall trace of main was not valid, because the GPU clock fell to 993 MHz in it, so the hall's pair ran again.
