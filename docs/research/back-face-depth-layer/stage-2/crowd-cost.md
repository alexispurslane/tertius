# Stage 2: the cost of the back-face pass by vertex count

This record holds the experiment of ticket `t1-kiaw`, of 2026-11-15. The ticket's section "Design of the experiment (stage 2, 2026-11-15)" defines its three parts. The method and the terms are those of [../stage-1b/cost.md](../stage-1b/cost.md) and [../stage-1b/probe.md](../stage-1b/probe.md).

Each section gives the measured facts first. A paragraph that starts with "Reading:" holds my interpretation. A review on 2026-11-15 corrected the record in one fix commit.

## Terms

- The pass is the back-face pass of stage 1b. It draws the one-sided solid runs of the opaque draws a second time, with front faces culled.
- The cards pass is the pass of [cards.md](cards.md), part 1. It draws every opaque run with no face culled, and its fragment shader drops the front faces of closed one-sided instances.
- A drawn vertex is one vertex of one drawn instance: the mesh's vertex count times its instances, summed over the pass's draw commands.
- The own time is the median time of the `back faces` pass over a run's 120 filmed frames, from the card's time stamps.
- The frame cost of a side is its GPU frame span minus that of the side `off` in the same round. The card's span leaves out the `capture` pass.
- A figure is one skinned cylinder of `rigs-crowd`: 20,202 vertices, the size of a person in [https://github.com/alexispurslane/tertius/wiki/skinning-and-morphs](../../https://github.com/alexispurslane/tertius/wiki/skinning-and-morphs), "Memory and cost".

## Summary

- The pass's cost grows in a straight line with the drawn vertices. On the RTX 2000 Ada its own time is 0.011 ms plus 0.238 ms per million drawn vertices.
- On the M1 Max its frame cost rises by 0.356 ms per million drawn vertices. Between 10 and 100 figures, the slope lies between 0.322 and 0.393 ms per million.
- In the crowd, the vertex work alone is 0.450 of the card's 0.489 ms. The fragment work is 0.026 ms and the fixed work 0.007 ms.
- Real views of the demo draw 100,000 to 125,000 vertices in the pass. One figure adds 20,202 vertices: 0.005 ms on the card and 0.007 ms on the Mac.
- No remedy with a reason makes the crowd cheaper. The cards pass costs more, and packed positions save 0.019 ms of the card's 0.489 ms.
- Two remedies of the ticket's table leave out no instance today, and the engine holds no coarser copy of a mesh.
- So the crowd keeps its cost: 0.47 ms of the card's frame and 0.80 ms of the Mac's. The remaining trade is a question for Alexis, in part 3.

## The probe

The probe is a throwaway build on the cards tree of stage 2, `refs/scratch/ter-kh4g-2-cards-fix`. [probe-crowd.patch](probe-crowd.patch) is its diff, and `refs/scratch/ter-kh4g-2-crowd-probe` holds it. Three environment variables select its parts, on top of `TERTIUS_BACK_FACE_PROBE` and `TERTIUS_BACK_FACE_PASS`:

| Variable | Value | What the pass does |
|---|---|---|
| `TERTIUS_BACK_FACE_SPLIT` | `scissor` | It sets a scissor of one pixel. The vertex work and the triangle setup stay, and almost no fragment runs. |
| | `clipped` | Its vertex shader moves every vertex right of the clip volume, so clipping rejects every triangle. Only the vertex work stays: the index reads, the vertex stage, the primitive assembly and the clipping. |
| | `empty` | It begins and ends its three rendering instances, with their clears and barriers, and draws nothing. |
| `TERTIUS_BACK_FACE_FETCH` | `pulled` | Its vertex shader reads each position from the global vertex buffer as a storage buffer, with no vertex input. |
| | `packed` | As `pulled`, but a deformed vertex reads this frame's kept positions: 12 bytes per vertex, end to end. |
| `TERTIUS_BACK_FACE_COUNT` | a file | It appends one line of draw counts per drawn frame: commands, instances, indices, vertices and deformed vertices. |
| `TERTIUS_CROWD_COUNT` | n | `rigs-crowd` holds the first n figures of its grid of 100. |

The sides of the timing runs are modes of one binary. Every side but `off` uses `TERTIUS_BACK_FACE_PROBE=ten`. Under the 10 % rule the walk reads no back share. So a side changes the pass's own work, and on the card also the surface test after it, as stage 1b found.

| Side | Variables | What its difference to `off` holds |
|---|---|---|
| `off` | `TERTIUS_BACK_FACE_PROBE=off` | Nothing: the base. The frame has no pass. |
| `pass` | `ten` | The pass of stage 1b |
| `cards` | `ten`, `TERTIUS_BACK_FACE_PASS=cards` | The cards pass |
| `scissor` | `ten`, `TERTIUS_BACK_FACE_SPLIT=scissor` | The pass without its fragments |
| `clipped` | `ten`, `TERTIUS_BACK_FACE_SPLIT=clipped` | The pass's vertex work and fixed work |
| `empty` | `ten`, `TERTIUS_BACK_FACE_SPLIT=empty` | The pass's fixed work |
| `pulled` | `ten`, `TERTIUS_BACK_FACE_FETCH=pulled` | The pass with positions from a storage buffer |
| `packed` | `ten`, `TERTIUS_BACK_FACE_FETCH=packed` | The pass with the deformed positions from the kept positions |

## The method

The method is that of stage 1b, with two changes. Each take has more sides, and every side gets its own warm-up run.

- The card: two cloud jobs ran `tools/bf2/job-crowd.sh` on the tree of `refs/scratch/ter-kh4g-2-crowd-probe`. One build served every side. Each take ran one untimed warm-up run of every side, then rounds in the rows of a Williams square of its sides.
- The warm-up of every side answers bug ticket `t1-s5c1`. No round is dropped on the card.
- The Mac: one take is one exclusive hold of the GPU lock for one scene, with `tools/crowd/mac-take.sh`. It also warms every side once, then runs rounds in the rows of a Williams square.
- Every Mac run used `--video none --in-flight 2 --pass-timestamps off` beside the GPU clock sampler. A round whose runs differ in mean clock by more than 1 MHz is dropped.
- Every run of a scene had one fixed exposure: the stops of stage 1b's main on that machine. Every frame of every run held it.

| Scene | Script | Stops on the card | Stops on the Mac |
|---|---|---:|---:|
| Crowd, also with 1, 10, 25 and 50 figures | `scripts/ter-kh4g-crowd-timing.json` | −9.2693568 | −9.2687040 |
| Cabin of `demo-exit` | `scripts/ter-kh4g-cabin-timing.json` | −0.4267480 | −0.4218524 |

A cell gives the mean, then the lowest and the highest value in brackets. The marks are those of stage 1b. The mark ‡ means that the per-round range of a difference holds 0. The mark † means that the size of the mean difference is at most `off`'s run range: its slowest kept run minus its fastest.

| Take | Machine | Sides | Rounds kept | The run range of `off`, ms |
|---|---|---:|---|---:|
| Crowd | RTX 2000 Ada | 8 | 8 of 8 | 0.082 |
| Crowd with 1, 10, 25 and 50 figures | RTX 2000 Ada | 2 | 4 of 4 each, one fewer than the ticket's five | |
| Cabin | RTX 2000 Ada | 6 | 6 of 6 | 0.176 |
| Crowd | M1 Max | 8 | 7 of 8: round 3 dropped, `pass` at 1293.47 MHz | 0.080 |
| Crowd with 10, 25 and 50 figures | M1 Max | 2 | 12 of 12 each | |
| Cabin | M1 Max | 6 | 12 of 12 | 0.039 |

Every kept Mac run held 1295.67 to 1296.00 MHz. No run failed.

The ticket asks for at least five kept rounds per side. The card's takes of 1, 10, 25 and 50 figures ran four. Their frame differences spread by at most 0.014 ms, and their own times by at most 0.001 ms. So I did not run them again.

On the card, round 1 of the crowd holds the lowest `pass` run, 9.592 ms, and so the lowest `pass` minus `off`, +0.348 ms. It sets the low end of the card's slope range in part 1. I did not find its cause. The card's own time does not show it: `pass` ran 0.488 to 0.489 ms in all eight rounds.

## Part 1: the cost model

### The parts of the pass in the crowd and the cabin

On the card, each value is the median of the kept runs' own times, in ms. Each own time spread by at most 0.003 ms over the runs of a side.

| Own time on the card | Crowd | Cabin |
|---|---:|---:|
| `pass` | 0.489 | 0.052 |
| `scissor` | 0.463 | 0.025 |
| `clipped` | 0.457 | 0.024 |
| `empty` | 0.007 | 0.007 |

So the card's own time splits into four parts:

| Part, on the card | Difference | Crowd | Cabin |
|---|---|---:|---:|
| Fixed work | `empty` | 0.007 | 0.007 |
| Vertex work | `clipped` minus `empty` | 0.450 | 0.017 |
| Triangle setup | `scissor` minus `clipped` | 0.006 | 0.001 |
| Fragments | `pass` minus `scissor` | 0.026 | 0.027 |

On the Mac, each value is a frame cost against `off`, in ms. The Mac has no valid pass times.

| Frame cost on the Mac | Crowd | Cabin |
|---|---|---|
| `pass` | +0.799 (+0.760 to +0.836) | +0.041 (+0.015 to +0.074) |
| `scissor` | +0.721 (+0.668 to +0.751) | +0.024 (−0.038 to +0.069) ‡ † |
| `clipped` | +0.674 (+0.597 to +0.744) | +0.020 (−0.022 to +0.048) ‡ † |
| `empty` | +0.074 (+0.037 to +0.108) † | +0.076 (+0.044 to +0.094) |

- In the crowd on the Mac, `clipped` costs +0.674 ms against `off`. That is the vertex work plus whatever fixed work the pass has.
- In the crowd on the Mac, `scissor` minus `clipped` is +0.047 ms † and `pass` minus `scissor` +0.078 ms †. Both lie inside `off`'s run range, but every round of the second is above 0.
- On the Mac, `empty` costs +0.074 ms in the crowd and +0.076 ms in the cabin. So in both scenes, a pass that draws nothing costs about 0.075 ms.
- In the cabin on the Mac, the full pass costs less than that: `pass` minus `empty` is −0.034 ms (−0.059 to −0.019) †. I did not find the cause.
- So on the Mac I do not split the fixed work from the vertex work. On the card the side `empty` costs 0.007 ms in both scenes.

The crowd's other passes on the card take these times under `pass`. The deformation takes 1.275 ms, the shadow maps 1.847 ms and the G-buffer pass 1.303 ms. The surface test after the pass takes 0.076 ms. No side changed any of these four by more than 0.001 ms.

In the cabin on the card, the surface test takes 0.135 ms under `off` and 0.148 ms under `pass`. Each side's runs spread by at most 0.001 ms there.

| Surface test in the cabin, card | `off` | `empty` | `clipped` | `scissor` | `pass` | `cards` |
|---|---:|---:|---:|---:|---:|---:|
| Median, ms | 0.135 | 0.131 | 0.138 | 0.138 | 0.148 | 0.147 |

So the pass adds 0.013 ms to the surface test in the cabin, beside its own 0.052 ms. Stage 1b saw a rise of 0.007 to 0.013 ms in the rooms. In the crowd the surface test does not change.

Reading: in the crowd, the vertex work is the pass's cost on both machines. That is 92 % of the card's own time and 84 % of the Mac's frame cost, from `clipped`. The fragments cost 0.026 to 0.027 ms on the card in both scenes. The back faces cover every surface pixel: 47 % of the crowd's pixels and all of the cabin's.

### The cost per million drawn vertices

The crowd with 1, 10, 25, 50 and 100 figures gives five vertex counts in one scene. The pass draws 20,202 vertices per figure, plus 1,249 static vertices of the floor and the lamp.

| Figures | Drawn vertices | Card: own time | Card: frame cost | Mac: frame cost |
|---:|---:|---:|---|---|
| 1 | 21,451 | 0.016 | +0.020 (+0.013 to +0.027) | not measured |
| 10 | 203,269 | 0.052 | +0.050 (+0.047 to +0.052) | +0.151 (+0.121 to +0.174) |
| 25 | 506,299 | 0.138 | +0.138 (+0.135 to +0.141) | +0.266 (+0.237 to +0.294) |
| 50 | 1,011,349 | 0.256 | +0.256 (+0.255 to +0.258) | +0.478 (+0.444 to +0.515) |
| 100 | 2,021,449 | 0.489 | +0.466 (+0.348 to +0.495) | +0.799 (+0.760 to +0.836) |

A least-squares line through the points gives the cost model. The slope's range uses the lowest and the highest round of the sizes 10 and 100.

| Cost | Line | Largest residual | Slope from the extreme rounds of 10 and 100 figures |
|---|---|---:|---|
| Card: own time | 0.0112 ms + 0.2378 ms per million vertices | 0.008 ms | 0.240 to 0.241 ms per million |
| Card: frame cost | 0.0161 ms + 0.2257 ms per million vertices | 0.012 ms | 0.162 to 0.246 ms per million |
| Mac: frame cost | 0.0901 ms + 0.3562 ms per million vertices | 0.027 ms | 0.322 to 0.393 ms per million |

The pass draws about two triangles per vertex in these meshes. The crowd's pass draws 11,886,948 indices for 2,021,449 vertices, and the cabin's 631,788 for 116,747.

### The vertex counts of real views

Each count comes from one run of the view's script with the pass on and `TERTIUS_BACK_FACE_COUNT`, on the Mac. A line holds the draw commands of the pass in one drawn frame. The counts of the cards pass are the same in every view below.

| View | Script | Drawn frames | Drawn vertices: lowest / median / highest | Of them deformed |
|---|---|---:|---|---:|
| Deck of `demo-exit` | `scripts/temporal-upscale-timing.json` | 152 | 116,979 in every frame | 0 |
| Cabin of `demo-exit` | `scripts/ter-kh4g-cabin-timing.json` | 152 | 116,747 in every frame | 0 |
| `rect-lamps` | `scripts/ter-kh4g-rect-lamps-timing.json` | 152 | 101,319 in every frame | 0 |
| `demo-docking` | `scripts/demo-docking.json`, all seven films | 4,069 | 17,168 / 117,027 / 125,012 | 0 |
| `station` | `scripts/station.json`, all six captures | 199 | 99,867 / 99,891 / 100,083 | 0 |
| One person | The crowd script, `TERTIUS_CROWD_COUNT=1` | 152 | 21,451 in every frame | 20,202 |
| Ten persons | The crowd script, `TERTIUS_CROWD_COUNT=10` | 152 | 203,269 in every frame | 202,020 |
| The crowd | The crowd script | 152 | 2,021,449 in every frame | 2,020,200 |

- No demo scene holds a skinned person yet. The scenes with a person use rigid meshes, and the only deformed meshes are the cylinders of `rigs` and `rigs-crowd`.
- So the views of one and ten persons use the crowd's figures. One figure has the vertex count of a person in the skinning plan.

### What each real view pays

The card's prediction is the own-time line. The Mac's prediction is the slope alone, 0.356 ms per million vertices, for the reason below the table.

| View | Card: predicted own time | Card: measured own time | Mac: slope times vertices | Mac: measured frame cost |
|---|---:|---:|---:|---|
| Deck | 0.039 | 0.042 (stage 1b) | 0.042 | −0.001 (−0.079 to +0.057) ‡ † (stage 1b) |
| Cabin | 0.039 | 0.052 (stage 1b and here) | 0.042 | +0.041 (+0.015 to +0.074), and +0.028 ‡ † in stage 1b |
| `rect-lamps` | 0.035 | 0.043 (stage 1b) | 0.036 | +0.040 (−0.011 to +0.075) ‡ † (stage 1b) |
| `demo-docking`, median frame | 0.039 | not measured | 0.042 | not measured |
| `station`, median frame | 0.035 | not measured | 0.036 | not measured |
| One person | 0.016 | 0.016 | 0.008 | not measured |
| Ten persons | 0.059 | 0.052 | 0.072 | +0.151 (+0.121 to +0.174) |
| The crowd | 0.492 | 0.489, and 0.485 in stage 1b | 0.720 | +0.799 (+0.760 to +0.836), and +0.801 in stage 1b |

- On the card, every prediction lies within 0.013 ms of the measured own time. The rooms lie above the line by 0.003 to 0.013 ms.
- The Mac's full line adds 0.090 ms to every view. It then predicts 0.132 ms for the cabin, three times the measured +0.041 ms.
- The slope alone gives 0.042 ms for the cabin, near the measured value. In the crowd series it gives 0.072 ms for ten figures against 0.151 ms measured.

- The slope alone lies inside the measured spread of each room on the Mac: the deck, the cabin and `rect-lamps`. The full line lies above the highest round of each room.

Reading: on the card the line holds for the own time. A room also pays the surface test's rise. In the cabin that gives 0.052 + 0.013 = 0.065 ms, near stage 1b's frame cost of +0.062 ms.

Reading: on the Mac, the crowd series has a fixed part of about 0.09 ms, close to the 0.074 ms of the crowd's `empty`. The cabin's `empty` shows the same 0.076 ms, but the cabin's full pass costs less than it. I do not know the cause. So the Mac has no single line for both kinds of scene. The slope prices each added figure vertex, and a room's own measurement prices the room.

Reading: a figure of 20,202 vertices costs 0.0048 ms on the card and 0.0072 ms on the Mac. The demo's real views pay 0.04 to 0.05 ms on the card. A view with ten figures pays about 0.05 ms more on the card and 0.07 to 0.15 ms more on the Mac.

## Part 2: the remedies

Each row compares the remedy with the pass as it is. Card cells give the change of the own time, from the medians. Mac cells give the frame difference, remedy minus `pass`, per round.

| Remedy | Built | Card: crowd | Card: cabin | Mac: crowd | Mac: cabin | Back share against the pass |
|---|---|---|---|---|---|---|
| One pipeline with both faces: the cards pass | Yes, in stage 2 | +0.008 (0.497 against 0.489) | +0.019 (0.071 against 0.052) | +0.159 (+0.094 to +0.215) | +0.068 (+0.051 to +0.087) | Equal, byte for byte, in both views |
| Leave out an instance whose thickness cannot change the walk | No: no instance qualifies | 0 | 0 | 0 | 0 | Unchanged |
| Draw a coarser copy of a mesh | No: the engine holds none | | | | | |
| Leave out every instance that an impostor replaces | No: the pass already leaves them out | 0 | 0 | 0 | 0 | Unchanged |
| Read the deformed positions from the kept positions: mine | Yes | −0.019 (0.470 against 0.489) | not measured: no deformed vertex | +0.006 (−0.027 to +0.032) ‡ † | not measured | Last bits differ, no pixel gains or loses a back face |

The card's frame differences of the two built remedies lie inside the noise. In the crowd, `cards` minus `pass` is +0.009 ms (−0.056 to +0.077) ‡ †. In the crowd, `packed` minus `pass` is −0.003 ms (−0.036 to +0.048) ‡ †. In the cabin, `cards` minus `pass` is +0.011 ms (−0.018 to +0.051) ‡ †.

### One pipeline with both faces

The cards pass draws every face, and its fragment shader discards the front faces of closed one-sided meshes. So it rasters about twice the triangles of the pass, and it runs the fragment shader on the front faces too.

- On the card it costs 1.6 % more own time in the crowd and 37 % more in the cabin.
- On the Mac it adds 0.159 ms to the crowd's frame, 20 % of the pass's +0.799 ms. It adds 0.068 ms to the cabin's frame, more than the pass's own +0.041 ms.
- In both views on the Mac, it stores the same back share as the pass, byte for byte. The crowd's figures and the cabin's meshes are all closed.

Reading: culled draws cost less than one pipeline. The cards ticket needs both faces only for two-sided and open meshes. A pass that culls front faces on the closed one-sided runs and draws both faces on the rest keeps the cheaper cost. That needs a run key for a closed mesh, as [cards.md](cards.md) notes.

### Leave out an instance whose thickness cannot change the walk

The ticket's candidate leaves out an instance whose bound is thinner along the view ray than the plane span of its pixels. Two facts of geometry stop it:

1. A sheet's slab has no thickness under the chosen model, by Alexis's answer 2. Under a plane-span rule, a pixel that faces the camera has a plane span of 0. A closed instance with a facing pixel is thicker than both, so no closed instance qualifies.
2. A left-out instance's pixels do not become sheets. With no fragment of its own, each pixel takes the first back face behind it. That is the borrowed back face of stage 1b.

In the crowd, a figure is 0.30 m thick, and its distance from the eye is 6.6 to 20.8 m. A pixel at 720 rows and a vertical field of view of 60° spans 1.6 milliradians. So a figure spans 9 to 28 pixels across, and its back face lies 9 to 28 pixel widths behind its front.

Reading: the remedy leaves out no instance in any view, so it saves nothing. A rule that leaves out an instance must still give its pixels their own fragment, and that fragment costs the vertices that the rule saves.

### Draw a coarser copy of a mesh

The engine holds no coarser copy of a mesh. [https://github.com/alexispurslane/tertius/wiki/asset-pipeline](../../https://github.com/alexispurslane/tertius/wiki/asset-pipeline), decision 15, plans LODs as coarser index lists over the same vertices. No code builds them yet. So I built none.

### Leave out every instance that an impostor replaces

The pass already leaves them out. `ImpostorManager.EmitDraws` moves every object with a capture from the visible list to the impostor draws, before the batcher builds the opaque runs. The pass draws only the opaque runs.

A deformed copy never becomes an impostor. `ImpostorManager.MayBecomeImpostor` excludes it, because its shape changes in every frame. So every figure of the crowd stays geometry at any distance.

### Mine: read the deformed positions from the kept positions

The reason: the pass reads 12 bytes of each 52-byte vertex, and its interleaved stride pulls in whole memory sectors. The deform pass already writes this frame's positions packed, 12 bytes per vertex, into the kept positions. By my arithmetic, the deformation's 1.275 ms on the card moves about 290 MB, near the card's memory bandwidth of 224 GB/s. So a pass that reads 24 MB instead of an estimated 80 MB of positions looked worth a test.

- On the card, `packed` saves 0.019 ms of own time against `pass`, and 0.004 ms against `pulled`. `pulled` alone saves 0.015 ms.
- On the Mac, neither differs from `pass` beyond the noise. `pulled` minus `pass` is +0.007 ms ‡ †, and `packed` minus `pulled` is −0.001 ms ‡ †.
- On the Mac, `packed` and `pulled` store the same back share, byte for byte, in both views. So the kept positions hold the copy's exact positions.
- Against `pass`, both change the low bits of the back share on 203,453 of 433,306 surface pixels in the crowd. The median relative change of a back distance is 1.2e-7, one unit in the last place. The largest is 3.1e-5, about 260 units.
- In the cabin the same holds on 122,879 of 921,600 pixels, with a largest change of 2.4e-6. In both views no pixel gains or loses a back face.
- The pyramid's finest level and its level 1 differ too, because they hold the back share. The front distance and the shading do not differ.
- No control compared the walk's hits or the image under `pulled` or `packed`.

Reading: the memory traffic of the positions is not what the pass waits on. The `clipped` side shows that the vertex work costs 0.450 ms with no raster at all. The low-bit changes come from a different compiled vertex shader, not from different positions.

## Part 3: what remains

After the remedies with a reason, the pass costs what it cost before. The cards pass costs more and is needed only on open and two-sided meshes.

| View | Card: own time | Card: frame cost | Mac: frame cost |
|---|---:|---|---|
| The crowd: 100 figures | 0.489 | +0.466 | +0.799 |
| The cabin | 0.052, plus 0.013 in the surface test | +0.022 ‡ †, and +0.062 in stage 1b | +0.041 |
| Ten figures | 0.052 | +0.050 | +0.151 |

A real trade remains for views with many skinned figures. I write it as the question for Alexis:

Each skinned figure of 20,000 vertices costs the pass 0.005 ms on the RTX 2000 Ada and 0.007 ms on the M1 Max. A crowd of 100 costs 0.47 and 0.80 ms. How must the pass treat skinned figures?

| Option | What Alexis gets | Cost on the card | Cost on the Mac |
|---|---|---|---|
| A. Keep the real back faces of every figure | The slab of each figure ends at its real back face, as the chosen model says. | 0.24 ms per million figure vertices: 0.05 ms for 10 figures, 0.47 ms for 100 | 0.36 ms per million: about 0.07 to 0.15 ms for 10 figures, 0.80 ms for 100 |
| B. Leave deformed instances out of the pass, and mark their pixels as sheets | A figure's pixels get no thickness. Rays pass behind every figure, and the sheets lose real hits, as stage 1b measured. | Saves the figures' share: about 0.47 ms for 100. The G-buffer needs a mark per pixel, not built or measured. | Saves about 0.7 ms for 100. The mark's cost is not measured. |
| C. Draw the figures' back faces from a coarser LOD, after the asset pipeline builds LODs | Each figure's slab ends at a coarser back face, with an error up to `lods.maxError`, 2 % of the mesh size. At a silhouette, the coarse back face can lie in front of the front. | The figures' share falls with the LOD's vertex count: about half per level. Not built. | The same. Not built. |

My recommendation is A. The demo's real views pay 0.04 to 0.05 ms on the card, and a view of ten figures pays about 0.05 ms more. The figures already pay 4.4 ms on the card for the deformation, the shadow maps and the G-buffer pass in the crowd. The pass adds 11 % to that.

## What was not measured

- A skinned person. No scene holds one, so the figures stand in for persons by their vertex count.
- The pass's own time on the Mac: no Metal System Trace ran.
- The cause of the Mac's fixed part of about 0.09 ms in the crowd series. The cause of the Mac's `empty` side, which costs more than the full pass in the cabin.
- The Mac with one figure, and the packed and pulled sides in the cabin on either machine. The cabin has no deformed vertex, so `packed` equals `pulled` there.
- The image of options B and C. No code of either exists.
- The scenes `demo-docking` and `station` were counted, not timed.
- Any GPU other than the RTX 2000 Ada and the M1 Max.
- The card's clock per run. Each take's `clocks.csv` holds the samples of the whole take, 2130 to 2460 MHz, idle states included.
- The image controls on the card. They ran on the Mac only.
- Which mesh holds most of a room's vertices. The counts sum each frame's commands and name no mesh.

## The commands, the jobs and the dollars

The tools are in [tools/crowd](tools/crowd):

| File | What it does |
|---|---|
| `job-crowd.sh` | The cloud job: the takes `crowd`, `cabin` and `counts` on the card |
| `mac-take.sh` | One Mac take of one scene under one exclusive hold of the GPU lock |
| `crowd_report.py` | The report of one take: the kept rounds, each side, and the differences |
| `fit.py` | The cost model from the takes of several crowd sizes |
| `counts.sh`, `counts_report.py` | The draw counts of the real views, and their summary |
| `controls.sh`, `share_diff.py` | The dumps of the back share under `layer`, and their comparison |
| `scratch-ref.sh` | The scratch commit of the exported probe tree |
| `crowd-one.json`, `cabin-one.json` | The one-capture scripts of the controls |

The build, in the dev shell, from an export of `refs/scratch/ter-kh4g-2-crowd-probe` in `/private/tmp/t1-kiaw/src`:

```bash
dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false -o /private/tmp/t1-kiaw/bin
```

The tools read the probe binary at `/private/tmp/t1-kiaw/bin/Tertius.Demo`. `controls.sh` reads its two scripts from `/private/tmp/t1-kiaw/scripts/`, so copy them there first:

```bash
mkdir -p /private/tmp/t1-kiaw/scripts && cp docs/research/back-face-depth-layer/stage-2/tools/crowd/crowd-one.json docs/research/back-face-depth-layer/stage-2/tools/crowd/cabin-one.json /private/tmp/t1-kiaw/scripts/
```

The Mac takes, from the worktree's root, with `t=docs/research/back-face-depth-layer/stage-2/tools/crowd`:

```bash
nix develop --command bash -c 'bash $t/mac-take.sh /private/tmp/t1-kiaw/mac crowd scripts/ter-kh4g-crowd-timing.json -9.2687040 0 8 "" off pass cards scissor clipped empty pulled packed'
nix develop --command bash -c 'bash $t/mac-take.sh /private/tmp/t1-kiaw/mac cabin scripts/ter-kh4g-cabin-timing.json -0.4218524 0 12 "" off pass cards scissor clipped empty'
nix develop --command bash -c 'bash $t/mac-take.sh /private/tmp/t1-kiaw/mac crowd-<n> scripts/ter-kh4g-crowd-timing.json -9.2687040 0 12 TERTIUS_CROWD_COUNT=<n> off pass'
```

The controls and the counts ran under `tools/gpu-lock.sh --shared`, with `bash $t/controls.sh` and `bash $t/counts.sh`. The reports, from the worktree's root:

```bash
python3 -I $t/crowd_report.py <mac|cloud> <take folder> off pass:empty clipped:empty scissor:clipped pass:scissor cards:pass pulled:pass packed:pulled packed:pass
python3 -I $t/fit.py <mac|cloud> <takes folder> /private/tmp/t1-kiaw/counts
python3 -I $t/counts_report.py /private/tmp/t1-kiaw/counts
```

The cloud jobs, with the agent name `t1-kiaw-crowd` and the tree `t=refs/scratch/ter-kh4g-2-crowd-probe`:

| Job | Command | Result | Counted |
|---|---|---|---:|
| `83faec51-53c5-47a5-b8c6-ccae9b6ee68c-u1` | `bash t/tools/bf2/job-crowd.sh crowd counts` | Passed, 511 s | $0.0930 |
| `312e5325-5a04-441c-b6f2-72263a9f825d-u2` | `bash t/tools/bf2/job-crowd.sh cabin` | Passed, 290 s | $0.0576 |

The cloud cost of this ticket is $0.1506 by the job log's estimates, of a budget of $0.80.

## The raw runs

All under `/private/tmp/t1-kiaw/`:

| Folder | What it holds |
|---|---|
| `mac/<take>/<side>/run-<round>/` | `gpu-timings.json` with the clock record, `exposure.json` and `run.log` of each Mac run |
| `mac/<take>/take-0.log`, `mac/takes.log` | The lock's wait and one line per run with its position, exit code, seconds and load |
| `mac/report.txt`, `mac/fit.txt` | The outputs of `crowd_report.py` and `fit.py` for the Mac |
| `cloud/jobA/out/`, `cloud/jobB/out/` | `<take>/<side>/run-<round>/` with `gpu-timings.json` and `exposure.json`, and each take's `take.log` and `clocks.csv` |
| `cloud/report.txt`, `cloud/fit.txt` | The outputs of `crowd_report.py` and `fit.py` for the card |
| `counts/<view>/counts.txt`, `counts/report.txt` | The draw counts of each view, and their summary |
| `controls-lock.log`, `controls/share-diff.txt` | The byte comparisons of the dumps, and the counts of changed pixels |

I deleted the dumps of the controls after I recorded their comparison.

## Notes on the method

- The ticket asks for a scissor of one pixel. I added the side `clipped`, which removes the triangle setup too, and `empty`, for the fixed work.
- The ticket's view of one and ten persons uses the crowd's figures, because no scene holds a skinned person. The same series of sizes gave the slope.
- The card's own times spread by at most 0.003 ms over a side's runs, and its frame differences by up to 0.15 ms. So the card's cells use the own time first.
- On the Mac, `empty` does not measure the fixed work. So the Mac's cost model uses the series of sizes and its slope, not `empty`.
- The cloud jobs warm every side once before the rounds, so no round is dropped for bug ticket `t1-s5c1`.
- I sent the two cloud jobs one after the other, for bug ticket `t2-a59b`.
- On the Mac, `fit.py` needs a clock match of `pass` and `off` to keep a round. `crowd_report.py` needs a clock match of every side. Both give +0.799 ms for the crowd, because the one dropped round's slow run was `pass`.
- `job-crowd.sh` and `mac-take.sh` each hold a copy of the sides and of the Williams square. The header of `job-crowd.sh` names a part `fetch` that it does not have. I left the file as the jobs ran it.
- The probe's counter gives −1 vertices for a command whose first index names no static mesh. No command can do that: every command draws a static mesh or a deformed copy of one, and a copy keeps its mesh's first index.
- The image controls compare the back share under `layer` in the probe's dump, not the capture. The headless runs with `--video none` wrote no image. The log `controls-lock.log` holds a line `*.png DIFFERS` per side: the script compared the unmatched glob itself, not a file.
