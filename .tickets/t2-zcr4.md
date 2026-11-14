---
id: t2-zcr4
status: closed
deps: []
links: []
created: 2026-11-12T20:56:34Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, performance]
---
# An environment face lights each texel once: 0.11 ms on the deck

Found by the frame-time sweep on 2026-11-12 (docs/research/frame-time-sweep-2026-11-11/addendum.md, section A13, the side finding). Built on 2026-11-12 to 2026-11-14 (section A14).

The environment faces paid 0.46 ms per frame on the deck, for one face of 128 x 128 texels. A forward face shaded each fragment of a surface with the loop over every kept light (environment_face.frag, directRadiance). A proxy build with one light loop per texel promised about 0.25 ms.

The first idea was a list of the lamps that reach a face. The count on the deck showed little room for it: the deck's own map keeps 22 of the frame's 22 lights. So the ticket built the other cut. The surfaces of a face write what the light loop reads into a surface record. One fullscreen triangle then runs the loop once per texel. The lamp list stays a candidate in the ranked list of ticket ter-b4ux.

Done when:

- A face lights each texel once, and the stills keep their light.
- The gain is measured on the M1 Max and on the workbench's card.
- The mutants of the changed lines are dead, and both suites pass.
- ARCHITECTURE.md follows, and the branch is merged and pushed.

## Notes

**2026-11-14, the build**

The acceptance record is `docs/research/frame-time-sweep-2026-11-11/acceptance-t2-zcr4/README.md`. Section A14 of the addendum holds the search and the timing tables.

The change. `FaceSurface` owns the surface record of a face: one RGBA32F image of six layers. The six layers hold position and model, both normals, roughness and metallic share, base color and reflectance, strand, and glow. `environment_face.frag` writes the record and shades nothing. `environment_face_light.frag` reads the record in one fullscreen triangle, runs the light loop once per texel and writes the face and its inverse distance. `environment_sky.frag` went away, because the light pass also writes the sky. A direct Metal test on the M1 Max accepted six and eight RGBA32Float targets in one pass. So the record needed no second pass on the Mac.

The gain. All times are the engine's timer of the pass `environment faces` on the deck, from traces with a clean GPU clock.

| Measurement | Result |
|---|---|
| The forward face on main at `657642432`, take 17, two traces | A mean of 0.457 and 0.461 ms, a median of 0.371 and 0.382 ms |
| The built pass, takes 17 to 19, seven traces | A mean of 0.338 to 0.367 ms, a median of 0.316 to 0.352 ms |
| The gain on the M1 Max | 0.11 ms of the mean frame, from 0.459 to 0.350 ms. About 0.04 ms in the median |
| The pass on an RTX 2000 Ada, one workbench job, five runs per build | 0.085 ms to 0.080 ms. The frame moves from 8.560 to 8.556 ms, inside the noise |

The proxy promised 0.25 ms. It shaded one made-up point for every texel, and its loop cost 0.057 ms. The loop over a real record costs about 0.2 ms, because the texels of a real face lie all over the room. So the forward face paid less than twice the price of one loop per texel.

The image.

| Measurement | Result |
|---|---|
| 79 stills of 13 scripts on the M1 Max, against main | 71 are equal byte for byte. The other 8 differ by rounding: at most 0.005 % of a still's pixels change by more than 4 levels |
| 791 files of the workbench's stills job, against main | All 791 are equal |
| 32 stills of 7 scripts, the final commit against the tree that takes 18 and 19 timed | All 32 are equal |

The tests. Ten new tests name the behavior. Three examples follow.

- A matte wall in a face sends its base color times the sun's irradiance.
- A polished metal in a face sends no diffuse light.
- A face shows the sky where the face before it saw a surface.

`FaceSurfaceTests` refuses a device that lacks one use of the record's format.

| Run | Where | Passed | Failed | Skipped |
|---|---|---:|---:|---:|
| Every suite in Checked, on commit `3dae7207b` | M1 Max | 10,857 | 0 | 26 |
| The seven test projects other than Rendering, in Release, on commit `3dae7207b` | M1 Max | 7,679 | 0 | 16 |
| The Rendering tests in Release, on commit `3dae7207b` | RTX 2000 Ada | 3,165 | 1 | 9 |
| Every suite in Checked, on commit `f36329c57` | M1 Max | 10,857 | 0 | 26 |
| Every suite in Checked, on the merge `cf1385d84` | M1 Max | 10,857 | 0 | 26 |
| The Rendering tests in Debug with coverage, on the merge `cf1385d84` | M1 Max | 3,166 | 0 | 10 |
| Every suite in Release, on the merge `cf1385d84` | M1 Max | 10,844 | 0 | 26 |
| Every suite in Checked, on the merge of key and fill `a82ed9aa2` | M1 Max | 10,926 | 0 | 26 |
| Every suite in Release, on the merge of key and fill `a82ed9aa2` | M1 Max | 10,913 | 0 | 26 |

The changed code is in Rendering, so the official Stryker run had no line. `tools/mutate.py` ran 15 listed mutants. Lists a and b ran on the workbench's card, and lists c and d ran on the M1 Max.

| Mutant | File | Change | Result |
|---|---|---|---|
| S01 | `face_surface.glsl` | A standard surface stores the mark of no surface | killed |
| S02 | `face_surface.glsl` | The model code is one too high | killed |
| S03 | `face_surface.glsl` | The roughness read as the metallic share | killed |
| S04 | `face_surface.glsl` | The metallic share read as the roughness | killed |
| S05 | `face_surface.glsl` | The base color with red and blue swapped | killed |
| S06 | `face_surface.glsl` | The strand dropped | killed |
| S07 | `face_surface.glsl` | The glow with red and blue swapped | killed |
| S08 | `face_surface.glsl` | The shading normal read as the geometric normal | survived first, then killed |
| S09 | `environment_face_light.frag` | The sky without its open-sky share | killed |
| S10 | `environment_face_light.frag` | The distance along one axis, not the radial distance | killed |
| S11 | `environment_face.frag` | The glow of an entity that holds a light stays | killed |
| C01 | `FaceSurface.cs` | A face keeps the record of the face before | killed |
| C02 | `FaceSurface.cs` | Each range clears the record of the ranges behind it | killed |
| C03 | `EnvironmentFacePass.cs` | Every face keeps only the root map's lights | killed |
| C04 | `DescriptorLayouts.cs` | The shared pool holds 11 sets that exist once, not 12 | survived, a judgment call |

S08 survived list b. The new test `AWallInAFaceTakesNoSunFromBehindThoughItsNormalMapFacesTheSun` was red on the mutant first and kills it in list c.

Two pool size lines of `DescriptorLayouts.cs` have no test that pins them. Both are judgment calls, and ticket ter-byim holds the cure: counts that come from the set layouts.

- The count of sampled images gains 1 for the face surface set. The pool leaves a type's size to the driver, and both Mac drivers give a set past it (ter-byim). So no test here fails on a size that is one too small.
- The count of sets. The mutant C04 lowers it by one, and `EnvironmentFaceTests` passes on it. The pool holds one spare set: eleven sets exist once per renderer, and the constant is 12.

The coverage run on the merged tree counts the lines that the branch changed in `src/`. Tests cover 187 of 189 changed lines and 22 of 24 changed branches. The rest are three failure paths, each a judgment call.

- `FaceSurface.cs`, line 34: the constructor refuses a device that cannot draw or sample the record's format. Every test device has both uses, and `FaceSurfaceTests` tests the rule that decides with made-up feature flags.
- `EnvironmentFacePass.cs`, line 79: the constructor disposes what it made after a pipeline fails. A test cannot cause that fault.
- `FaceSurface.cs`, line 112, one branch: `Dispose` runs after a constructor that made no image. A test cannot cause that fault.

The hook's quality report named two findings on the changed lines.

- A statement wall in `EnvironmentFacePass.RecordLight`, eleven statements in a row. Blank lines now group its three steps.
- A deconstruction of six pipeline layouts in `DescriptorLayouts`. The six-value tuple is main's own, and the branch had only joined its two lines. The branch now keeps main's text. A record struct for a group with one reader adds a type and no clarity, so the tuple stays.

The card's first Release run had one crash. The fifth of six test processes ended with exit 139 after 10 s and left no log. The same classes then passed one process per class, 519 of 519. They passed again as one process with the verbose reporter. The crash did not come back, and it stays unexplained.

The card's second Release run took its six shards from `tools/runpod/shards.py`, on commit `3dae7207b`. Of 3,175 tests in 205 classes, 3,165 passed, 9 were skipped and 1 failed. The 14 nested classes ran, and no process crashed. The failed test is the neighbor limiter test of ticket t8-v31a, which failed on main too. Main changed that test while the run was in flight, and the branch then merged main.

The merge of key and fill. Main gained key and fill (t2-vsqh) just before this ticket's merge. Both branches add one set to the shared descriptor pool, and both raised its count of sets from 10 to 11. Git kept 11 for two equal edits, with no conflict. The merge commit `a82ed9aa2` sets it to 12. By the count above, 11 was still enough, because main's constant already held one spare set. After the merge, 24 of 32 stills equal the new main's, and the same 8 as before differ in a few pixels. The takes did not run again, because key and fill does not change the face's pass.

The hook's report of that merge commit names two functions: a long method in `VulkanRenderer.cs` and `ScriptCommand.RendererOverrides.ApplyTo`. Both are the code of key and fill, which this ticket does not touch.

Two ideas of takes 18 and 19 did not merge.

- A face renders only the depth ranges that hold surfaces. It gains 0.02 ms, and 32 of 32 stills stay equal. It is not built, because 0.02 ms does not pay for one more rule.
- One rendering for the three depth ranges, with a depth clear between them. On the M1 Max the clear loses the color of the draws before it. The call is valid Vulkan. Ticket tz-na6d records the fault.

The cloud jobs of this ticket counted $1.69 in 21 jobs. The acceptance record lists them.
