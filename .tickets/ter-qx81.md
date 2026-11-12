---
id: ter-qx81
status: closed
deps: []
links: []
created: 2026-11-12T12:37:47Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
tags: [perf, mac]
---
# Two lamps per pass of the direct-light loop: does a partial unroll help the Mac?

The user asked on 2026-11-12 whether a partial unroll of our shader loops helps the M1 Max reach its arithmetic peak. Loops with a constant count (the 4-step loops in lighting.glsl) are already unrolled by Apple's compiler (air-opt, ter-3ug8). The loops that stay rolled have a count from a uniform, such as the light loop over frame.counts.x in lighting.glsl (lines 773 and 886 on main at 5c83ef6c8).

Known: the calibration probe's rolled loop of one dependent FMA per pass reaches 41 % of the peak, 77 % at 2 steps per pass and 93 % at 4 (docs/research/temporal-renderer/gpu-calibration.md). But that body is tiny. The one real loop tried, the screen walk's step loop (ter-dzxf), got much slower when unrolled at cap 62: 2.53 ms rolled, 4.55 ms at 2 steps, 4.80 ms at 4 (docs/research/temporal-renderer/walk-hill-climb/attempts.md, cap62-unroll), most likely from register pressure. The tile agent also found that the direct-light pass costs more with its parts together than apart (4.19 ms against 0.75 + 1.78 ms removed), which points at register pressure.

Test: one exclusive GPU take (at most about 20 minutes) with three deck builds from main: rolled, 2 lamps per pass (with a remainder step), and 2 lamps per pass with the shadow lookup kept outside the pair. Frame span and direct-light pass time, same driver for all three. Byte-identical stills required. If a build gains above the noise, check the reflection march the same way, and check the RTX 2000 Ada through the Runpod queue (ter-94uo), because the change must not cost time on Linux drivers. If nothing gains, close with the numbers.


## Notes

**2026-11-12T14:28:28Z**

Result: no gain. Two lamps per pass of the direct-light loop make the deck slower on this Mac. The direct-light pass costs about 1 ms more, and the frame span about 2.3 to 3.0 ms more. So the ticket closes, and the reflection march and the Linux check are not needed.

### The builds

All builds come from main at ad095e599 and differ only in `shapeLight` of `lighting.glsl` (the loop at line 773). The variants of the puffs' loop (`scatteredLight`, line 886) gave byte-identical images too, but the deck's passes do not run that loop, so the timed builds keep it rolled.

| Build | The loop over the lamps of one shape |
|---|---|
| a | main: one lamp per pass of the loop. |
| d (variant b) | Two lamps per pass, and one remainder step for an odd count. The loop body is copied as it is, and each lamp adds its terms to the sum right after its own work. |
| e (variant c) | Two lamps per pass. The path and the shadow lookup of each lamp run in turn first (a helper returns the visibility). Then the response of the first lamp and its add, then those of the second lamp. One remainder step. |

Two earlier forms of variants b and c computed both lamps first and added both lamps' terms after them. Those two forms changed the deck still in 3,779 pixels by up to 4 levels, and lamp-hall-16 in 1,086 pixels by 1 level. The two forms gave the same image as each other. So under the Metal compiler's fast math, moving the add after the second lamp changes the rounding. Builds d and e keep each add right after its own lamp, and both give byte-identical stills.

### Images

A headless still of the deck (demo-exit frame 399, a capture at 1280 x 720) and a still of lamp-hall-16 (its capture "hall") are byte-identical between builds a, d and e. Two runs of build a also gave byte-identical stills, so the comparison is deterministic.

### Times

The deck: demo-exit trimmed to frames 399 to 518 (120 frames), at 1920 x 1080 with `--in-flight 2`. Two exclusive takes under the GPU lock (09:00 to 09:04 and 09:22 to 09:25), five rounds in all, with the order of the builds rotated in each round. The driver was the dev shell's MoltenVK 1.4.2 from nixpkgs, without the patch of ter-2zga, for every run. The first take's first five runs have no clock record, because the worktree lacked the `gpu-clock` binary. Every run with a record ran at 1,296 MHz (p10 and p90 both 1,296 MHz).

The direct-light time from the GPU timer holds part of the next pass on MoltenVK. So the table also gives the busy time of the pass from one Metal System Trace per build without counters, taken in the second take at 1,296 MHz (about 150 frames each).

| Build | Frame span median, ms (median of 5 runs, range) | Frame span p95, ms (median of 5 runs, range) | Direct light, timer median, ms (median of 5 runs, range) | Direct light, trace busy median, ms | Clock |
|---|---|---|---|---|---|
| a, rolled | 20.917 (20.865 to 20.979) | 22.050 (21.497 to 23.379) | 3.237 (3.178 to 3.243) | 4.084 | 1,296 MHz in 4 runs, 1 run unknown |
| d, 2 lamps per pass | 23.238 (23.078 to 23.457) | 24.337 (23.846 to 30.765) | 4.182 (4.168 to 4.204) | 5.226 | 1,296 MHz in 3 runs, 2 runs unknown |
| e, 2 lamps per pass, shadow lookups first | 23.917 (23.839 to 24.046) | 24.846 (24.653 to 25.484) | 4.559 (4.554 to 4.564) | 5.695 | 1,296 MHz in 3 runs, 2 runs unknown |

The differences are far larger than the spread of the runs. Build d adds 2.3 ms to the frame span and about 0.95 ms (timer) or 1.14 ms (trace) to the direct-light pass. Build e adds 3.0 ms and about 1.3 ms or 1.6 ms. The rest of the span comes from the other passes that include the same loop. In the traces, the transparent pass grew from 2.09 ms to 3.05 ms (d) and 3.29 ms (e), and the environment faces from 0.42 ms to 0.51 ms and 0.64 ms. The reflection and diffuse rays did not change.

### Registers and code size

Apple gives no register count outside Xcode's shader profiler. As a proxy, a scratch compute kernel held the body of direct.frag, compiled by glslc and SPIRV-Cross 1.4.363 to MSL, then by Metal with fast math. Its binary archive gives the native code size, and its pipeline gives the most threads per threadgroup, which falls when a kernel needs more registers.

| Build | Threads per threadgroup | Native code, dropsFibers false | Native code, dropsFibers true |
|---|---|---|---|
| a | 384 | 161,408 B | 90,848 B |
| d | 384 | 506,672 B | 257,328 B |
| e | 384 | 514,560 B | 261,264 B |

The threads per threadgroup stay at 384 in all builds, so the proxy shows no change in the register limit. The native code grows by about 3.1 times, because each of the twelve copies of the loop (fiber or not, tinted or not, three shapes) now holds its body three times: two lamps and the remainder. Spills are not visible with this proxy. The likely causes of the loss are the larger code (instruction cache misses) and longer live ranges inside the pair. This measurement does not separate the two.

### Why the probe's gain does not carry over

The rolled FMA probe gains from an unroll because its body is one dependent FMA, so the loop's own compare and branch dominate its time. A lamp's body holds a shadow lookup with its texture reads and branches for the shape, the view and the visibility. The loop's overhead is a very small part of that time. Each lamp's work also sits behind its own branches, so the compiler most likely cannot interleave the two lamps (an inference, not measured). So the unroll adds code and gains nothing.

Recommendation: keep the light loops rolled. Do not test the reflection march this way, and no Linux check is needed.
