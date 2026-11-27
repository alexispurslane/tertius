# A guard shell around the outer light grid (issue #25)

## Summary

- The outer grid of the directional light volume now stores 18 cells per axis at 1 m. It keeps a reach of 16 cells and one guard cell on each face. The 0.25 m and 0.5 m grids keep 16 cells per axis.
- The outer grid's share of a receiver's light now fades by the receiver's distance from the continuous camera position. So the share changes continuously while the camera walks.
- On the hallway walk of #21, the nose wall's drop at 6.98 m goes from −16.13 to −0.59 of 255 in luma between two frames. The wall now darkens over about 1.1 m of walking, at most about 1 per frame.
- The other large wall step of main, +9.25 at 58.33 m, comes from the environment map, not from the light grid. The same holds for the small wall steps of 1.6 to 2.1 between 17 and 42 m. With the environment map off, both disappear on main and on the change.
- GPU cost on the RTX 2000 Ada: the sum of GPU pass times rises by a paired median of 0.166 ms (2.6%), and its p95 by 0.170 ms (2.7%). The buffer grows by 1,903,360 bytes to 15,291,200 bytes (14.58 MiB).

## The model

A level's reach is a cube of 16 cells per axis. A level gives its share of a receiver's light first, and the coarser levels give the rest. `levelWeight` in `volume_receive.glsl` computes the share:

```glsl
float levelWeight(int level, vec4 grid, vec3 p)
{
    float side = float(sideOf(level));
    vec3 gridCenter = vec3(0.5 * side);
    vec3 center = clamp(-grid.xyz / grid.w, gridCenter - float(guardOf(level)), gridCenter + float(guardOf(level)));
    vec3 offset = abs(p - center);
    return clamp(0.5 * float(Reach) - 0.5 - max(offset.x, max(offset.y, offset.z)), 0.0, 1.0);
}
```

where

- `levelWeight(...)` = the share of a receiver's indirect light supplied by one grid level.
- `side` = the number of stored cells per axis at this level.
- `offset` = the absolute distance from `center` to `p` along each grid axis, in cells.
- `float(...)` = conversion to a GLSL floating-point number.
- `vec3(...)` = a three-component vector, repeating its argument when given one number.
- `clamp(value, low, high)` = restrict each component of `value` to the interval from `low` to `high`.
- `abs(...)` = the absolute value of each component.
- `max(a, b)` = the larger of two numbers.
- `level` = the grid: 0 for 0.25 m cells, 1 for 0.5 m, 2 for 1 m.
- `grid` = the grid's lowest corner relative to the camera (m) in `xyz`, and its cell width (m) in `w`. The grid snaps to whole cells of a lattice that is fixed in the camera's reference frame.
- `p` = the receiver's place in the grid, in cells from the lowest corner.
- `sideOf(level)` = the cells per axis that the level stores: 16 for the inner grids, 18 for the outer grid.
- `gridCenter` = the grid's center, in cells.
- `guardOf(level)` = the cells that the level stores beyond its reach on each face: 0 for the inner grids, 1 for the outer grid.
- `-grid.xyz / grid.w` = the camera's place in the grid, in cells. It lies at most half a cell from `gridCenter`.
- `center` = the center of the reach. It follows the camera as far as the guard lets it: it is `gridCenter` for an inner grid, and the camera for the outer grid.
- `Reach` = 16.
- The result = the share, 1 up to 6.5 cells from `center` along every axis, 0 from 7.5 cells.

For the inner grids this is the old rule, so their light does not change. For the outer grid the fade now ends 7.5 m from the camera along each axis of its frame. Before, it ended 7.5 m from the snapped center, which lies up to 0.5 m from the camera. So a surface near 7 m jumped between full and no grid light when the grid moved by one cell.

The guard makes the continuous fade safe to read. A receiver reads the grid 0.45 cells farther out along its geometric normal and interpolates the 8 nearest cell centers. At the end of the fade it reads at most 7.95 m from the camera. The outer grid's cell centers reach at least 8.0 m from the camera along each axis at every snap: 18 cells of 1 m, with the grid's center within 0.5 m of the camera. A 16-cell grid would reach only 7.0 m at the worst snap.

The diffuse light, the broad reflection and the environment transmission of the screen tracer's misses use the same share.

## The layout

`volume_layout.glsl` holds the grids' shape, and `LightVolumePass` the C# copy of the same numbers:

| | 0.25 m and 0.5 m grids | 1 m grid |
|---|---:|---:|
| Cells per axis | 16 | 18 |
| Cells | 4,096 each | 5,832 |
| Lighting groups per axis | 4 | 6 |
| Lighting groups | 64 each | 216 |

- The volume shading pass evaluates the lights once per lighting group and surface side. A group's faces lie at whole multiples of 4 cells from the grid's center. So the 16 central cells of the outer grid keep the groups of a 16-cell grid, and the shell forms groups one cell deep. With groups that start at the grid's lowest cell, every group of the outer grid would move by one cell against the old ones. Each group would then shade other surfaces at another place.
- The passes over all cells run 64 invocations per work group. 14,024 cells need 220 groups, and the invocations of the last group past the last cell return at once.
- The sweep runs 9 × 9 invocations per work group. A slice of 18 or 16 cells takes 2 × 2 blocks of 9 × 9 cells. An invocation with no cell in a block skips it. Every invocation still reaches the barrier between slices. The two slices in shared memory take 2 × 18 × 18 × 16 = 10,368 bytes, within the 16,384 bytes that Vulkan guarantees.
- Every level rasterizes its surfaces into 72 × 72 pixels, 4 × 4 per cell. A 16-cell grid uses the lower 64 × 64 pixels, so its samples do not change.
- The light list keeps every light whose reach sphere can reach the outer grid: within 9.5 m of the camera along each axis, plus its reach.

## Bounds and Vulkan requirements

The outer shell has 1,736 cells: 1,536 on face interiors, 192 on edges and 8 at corners. Its 152 added lighting groups comprise 96 face groups, 48 edge groups and 8 corner groups. These counts exclude the central 16³ cells and 4³ groups. The shell renderer tests close all six faces and light a reflector captured only in the shell.

| Operation | Valid work | Unused work and its bound |
|---|---|---|
| Source and filter | 14,024 cells, indices 0 to 14,023 | 220 groups of 64. The final 56 invocations return before any buffer access. |
| Restrict, middle level | 4,096 parent cells | 64 groups of 64, with no tail. Every child read also checks the child grid's bounds. |
| Restrict, outer level | 5,832 parent cells | 92 groups of 64. The final 56 invocations return before any buffer access. |
| Shade | 64 groups per inner level, 216 outer groups, six surface orientations each | Inner rows return past group 63. Partial groups skip cells outside their own level. |
| Sweep | 256 cells per inner slice, 324 outer cells | Four 9 × 9 blocks. The inner grids skip coordinates 16 and 17, then every invocation reaches the slice barrier. |
| Receiver interpolation | Eight neighboring cell centers for each sample | Each coordinate clamps to its own level's last cell. The outer fade and normal offset leave 0.05 m before the minimum supported cell center. |

Transport addresses occupy disjoint intervals: 0 to 131,071 for the fine level, 131,072 to 262,143 for the middle level, and 262,144 to 448,767 for the outer level. The lighting groups occupy intervals 0 to 63, 64 to 127, and 128 to 343. Parent lookups use the parent level's dimensions. The grid origins keep the same world lattice and cell widths as before.

The sweep uses 81 invocations and 10,368 shared bytes per work group. Vulkan guarantees at least 128 invocations and 16,384 shared bytes. There is no subgroup-size assumption, warp operation or divergent barrier. The transport retains its existing finite half-float cap.

Linux NVIDIA executes the measured production shaders. The Mac rendering tests use validation with synchronization checks. Normal validation does not prove that every shader buffer access is in bounds. The bounds above also follow from the dispatch limits and checked coordinate ranges.

## Regression evidence

[The hardening report](hardening.md) gives the test cases, coverage on changed C# lines, the final 27-row mutation table and every survivor decision. The production renderer remains the version measured on Runpod.

## Films

All films run `scripts/hallway-walk.json` of #21 on the `orbit` scene at 1280 × 720 and 60 fps, gray box, fixed exposure 3.79 stops, and the renderer's defaults otherwise: screen tracer on, FSR 3.1 at 2/3, bloom on. Main is b0f8bbf1b. Its film reproduces the merged film of #21 exactly: both surfaces' luma differs by 0.0 in every frame.

- [hallway-guard.mp4](hallway-guard.mp4): the change alone, the full view, with the distance to the nose wall.
- [hallway-main-vs-guard.mp4](hallway-main-vs-guard.mp4): main on the left and the change on the right, synchronized, 2560 × 720.

Both are H.264 at CRF 12, so that one 8-bit level stays visible. The analysis reads the lossless films.

## Final film verification

The restored production renderer was rebuilt in Release after the last mutation. A fresh complete walk at test commit `61a12d8c2` produced 1,190 frames. Each decoded RGB24 frame has the same SHA-256 as the corresponding frame of the original lossless guard film: 1,190 matches, zero differences. The measured production source remains `975d99e51`, so the labeled films and their surface measurements above still describe the final renderer.

The new film streamed through a named pipe into a frame-hash calculation, an H.264 copy and four lossless stills. It did not retain another 626 MB lossless file. [The comparison](evidence/final-film/comparison.json), per-frame hashes, [provenance](evidence/final-film/provenance.json) and build/render logs remain in `evidence/final-film/`. Mac timing output in that render log is diagnostic only. The Linux measurements below determine the performance cost.

The four stills were opened and inspected:

| Frame | View and visual finding |
|---:|---|
| [38](evidence/final-film/frame-38.png) | The full hallway, with the nose wall beyond every light grid. The remaining environment-map step is outside this change. |
| [1064](evidence/final-film/frame-1064.png) | The nose wall immediately before the old outer-grid jump, 7.03 m away. |
| [1065](evidence/final-film/frame-1065.png) | The same wall after a 5 cm step, 6.98 m away. The wall continues its gradual fade. |
| [1077](evidence/final-film/frame-1077.png) | The wall after the fade, 6.38 m away. The window and floor remain coherent. |

## Steps on the tracked surfaces

[analyze.py](analyze.py) reuses the camera path, the projection and the two surfaces of #21: a nose-wall patch right of the window, and a floor patch 1 to 3 m before the nose wall. It also follows 13 world-fixed floor patches of 3 × 2 m, one every 4 m along the path, while each lies 3 to 12 m ahead. So every patch crosses the outer fade on screen. A change counts only where the surface's pixel box is at least 8 rows tall in both frames. The floor patch of #21 is only 3 rows tall near 24 m, and its crop moves by whole pixels there. Its "steps" of ±5 luma at 24 m in #21 came from that crop, not from light.

Frame-to-frame changes of luma over the walk, frames 32 to 1129 ([summary.json](analysis/summary.json), [nose-wall.svg](analysis/nose-wall.svg), [floor.svg](analysis/floor.svg)):

| Surface | Frames | Largest, main | Largest, change | p95, main | p95, change | Over 0.6, main | Over 0.6, change | Over 2, main | Over 2, change |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Nose wall | 1,086 | 16.13 | 9.25 | 0.332 | 0.584 | 38 | 53 | 3 | 2 |
| Nose floor | 225 | 1.74 | 1.75 | 0.745 | 0.749 | 25 | 25 | 0 | 0 |
| 13 floor patches, pooled | 2,135 | 1.75 | 1.88 | 0.429 | 0.426 | 32 | 26 | 0 | 0 |

The three steps of main that the issue names:

| Frame | Distance to nose wall | Main | Change | Cause |
|---:|---:|---:|---:|---|
| 1065 | 6.98 m | −16.13 | −0.59 | The outer fade. The wall now darkens over frames 1054 to 1077. |
| 38 | 58.33 m | +9.24 | +9.25 | The environment map, isolated below |
| 975 | 11.48 m | −1.08 | −1.08 | The wall lies 11.5 m out, beyond every grid; the step also disappears with the environment map off |

- The change's largest wall step is now the environment map's +9.25. Its next largest is +2.06 at 17.83 m, which main shows too.
- The wall's count over 0.6 rises from 38 to 53, and its p95 from 0.33 to 0.58. The added counts are the frames of the fade, about 0.9 per frame over 20 frames. Main has one step of 16 there.
- The pooled floor patches show no step over 2 on either side. In the band 6 to 8 m ahead, where the outer fade lies, the largest change is 1.19 on main and 1.38 on the change.

## What causes the remaining steps

Each diagnosis isolates one term: the grid's weight, the grid's field, or a system outside the grid.

1. The far wall at frame 38. The whole wall region and a margin of 6 px around it brighten by the same +9.3. So the crop of 22 × 16 px, which grows by one column at that frame, does not cause it. Short walks of frames 0 to 50 on the change give the wall's luma at frames 33 to 42. Each run turns one system off:

   | Run | Frames 33 to 37 | Frames 38 to 42 |
   |---|---|---|
   | Defaults | 59.45 to 59.63 | 68.88 to 69.51 |
   | Light volume off | 59.43 to 59.63 | 68.88 to 69.50 |
   | Bloom off | 57.88 to 58.00 | 67.47 to 68.02 |
   | Screen tracer off | 58.83 to 59.06 | 68.30 to 68.90 |
   | Environment map off, −3 stops | 229.77 to 229.81 | 229.79 to 229.82 |

   The step stays with each of the other systems off and goes with the environment map off. The wall lies 58 m away, beyond every grid, so the composite lights it with the environment alone. Legacy ray tracing lights it with screen-traced rays, and its step there is −0.02.

2. The small wall steps from 17 to 42 m. The full walk with the environment map off at −3 stops ([analysis-environment-off](analysis-environment-off/summary.json)) leaves main with 3 wall changes over 0.6, all at the grid's step near 6.98 m. With the environment map on, main has 38. So the environment map causes the staircase that [nose-wall.svg](analysis/nose-wall.svg) shows in both films, about one step per metre of walking. This change cannot reach them. [Issue #26](https://github.com/alexispurslane/tertius/issues/26) tracks the environment-map staircase.

3. The grid's own steps. Without the environment map, the ambient light is a bright constant sky, so every error of the grid's transmission shows magnified:

   - Main steps every floor patch by −11 to −13 when it passes 6.48 m ahead: one step every 4 m of walking, from the snapped outer fade. The change's largest change there is 5.57.
   - Both sides step the floor near 3.25 to 4.5 m ahead, and the nose-floor patch dips every 5 frames. These are the inner grids' snapped fades at 4 m and 2 m. This change keeps those grids as they were.
   - The change steps the floor patch at z = −24 m by −39.65 at frame 1025, and the floor stays there. The nose wall and the hull beyond it then enter the outer grid's shell and shut out the sky beyond the grid's face. Main has the same step one cell later, at frame 1045, when the wall enters its 16th cell. This is the grid's field, not its weight: beyond the grid's faces the transmission is open.
   - With the environment map on, these steps nearly vanish. The map is captured at the eye, so the "environment" beyond the grid is the cabin itself. At frame 1025 the same floor patch changes by −0.17 on the change and −0.10 on main.

Steps 3b and 3c lie outside this change. The same guard and continuous fade would apply to the inner grids, and a closed boundary for the field would need a design of its own.

## Reverse direction and camera cut

A second script ([reverse-cut.json](reverse-cut.json)) walks the same path without filming and holds 30 frames at the nose wall. It walks back for 100 frames to 8.73 m and stops for 10 frames. Then it cuts to the `far` camera and back to `person`:

- Walking back, the largest frame-to-frame wall change is +16.76 at 7.13 m on main and 1.31 on the change.
- After the cut, the change's wall reads 61.16 to 61.20 in the first six frames. Main reads 61.16 to 61.20 too. The last frame before the cut read 61.33 on the change and 61.34 on main, and the forward walk read 61.10 at the same place.

The [reverse measurements](evidence/reverse.txt) also compare the reverse and forward samples at matched distances. The largest difference is 2.52 luma levels on the change. This comparison includes screen-tracer history and is not a proof of a history-free whole image.

`LightVolumeRenderTests` isolates the volume on small scenes: a wall in the fade gets the same light whichever way the camera came, and each test frame there is a cut.

## GPU cost on Linux

Five paired rounds on the same RTX 2000 Ada, 1920 × 1080, 1,190 frames per run. All runs use the same hallway script and renderer settings as the films except resolution and video output. Every round runs main and the change, and alternate rounds reverse their order. A third variant, `guard8`, keeps the 8 × 8 sweep work group of the first guard implementation.

The frame values below are the sum of GPU pass durations per frame from `--gpu-run deformation..outlines`. They exclude the headless capture/readback pass and the gaps between passes. The median and p95 come from those per-frame sums, rather than sums or differences of pass medians. The console log reports the larger GPU frame span including capture and gaps: its medians range from 8.544 to 8.593 ms on main and 8.690 to 8.766 ms on the change. Neither quantity includes CPU frame time.

NVIDIA RTX 2000 Ada Generation, 1920x1080, 1190 frames per run, 5 pairs

| Value | main runs (ms) | guard runs (ms) | Paired change, median | Range | Pairs that rise |
|---|---|---|---:|---|---:|
| GPU pass sum per frame, median | 6.237, 6.273, 6.282, 6.290, 6.288 | 6.408, 6.435, 6.447, 6.453, 6.457 | +0.166 ms (+2.6%) | +0.162 to +0.171 | 5 of 5 |
| GPU pass sum per frame, p95 | 6.349, 6.383, 6.388, 6.397, 6.395 | 6.518, 6.553, 6.559, 6.567, 6.572 | +0.170 ms (+2.7%) | +0.169 to +0.178 | 5 of 5 |
| directional light volume, median | 0.714, 0.718, 0.719, 0.720, 0.720 | 0.826, 0.829, 0.831, 0.831, 0.832 | +0.111 ms (+15.5%) | +0.111 to +0.112 | 5 of 5 |
| ray plan, median | 0.501, 0.503, 0.503, 0.503, 0.503 | 0.540, 0.542, 0.542, 0.542, 0.542 | +0.039 ms (+7.8%) | +0.039 to +0.039 | 5 of 5 |
| reflection rays, median | 0.679, 0.682, 0.682, 0.683, 0.682 | 0.680, 0.683, 0.683, 0.683, 0.684 | +0.001 ms (+0.2%) | +0.000 to +0.002 | 5 of 5 |
| screen trace resolve, median | 0.122, 0.122, 0.122, 0.122, 0.122 | 0.122, 0.122, 0.122, 0.122, 0.122 | +0.000 ms (+0.0%) | -0.000 to +0.000 | 4 of 5 |
| composite, median | 0.267, 0.268, 0.268, 0.269, 0.269 | 0.280, 0.281, 0.281, 0.281, 0.281 | +0.012 ms (+4.6%) | +0.012 to +0.013 | 5 of 5 |

Pass p95 is the median of each run's 95th percentile for that pass. Each paired change subtracts main from the change within the same round.

| Pass | Main p95 (ms) | Change p95 (ms) | Paired change, median (ms) |
|---|---:|---:|---:|
| directional light volume | 0.725 | 0.837 | +0.112 |
| ray plan | 0.512 | 0.551 | +0.039 |
| reflection rays | 0.701 | 0.702 | +0.001 |
| screen trace resolve | 0.126 | 0.126 | +0.000 |
| composite | 0.305 | 0.323 | +0.017 |

The light-volume pass's paired median grows by 0.111 ms, from about 0.719 to 0.831 ms. `ray plan` and `composite` also read the grid and pay for the continuous weight calculation. All five pairs rise, so this is a measured cost.

Using 9 × 9 invocations reduces the guarded volume pass by a paired median of 0.130 ms against `guard8` in this same job. The 18-cell slice then takes four blocks per invocation, instead of nine with 8 × 8 invocations. A work group has 81 invocations, below Vulkan's required limit of 128, and uses 10,368 bytes of shared memory, below the required 16,384 bytes. The barrier lies after the loop over blocks. Inactive cells skip the block, so every invocation reaches the barrier.

The [job result](runpod/job2/result.json), [submission provenance](runpod/job2/provenance.json), [clocks](runpod/job2/out/clocks.csv) and individual timing files remain in this report. The measured snapshot is commit `0c4847b4136fd5fab9f98f68bd87935665412990`. It and implementation commit `975d99e51f8d86ca3773e321b6c76749ba8ef7b7` share Git tree `ae965e161377b1bbe42ad28e20c271c3e93a7643`. The takeover reuses this completed job.

The buffer grows from 13,387,840 to 15,291,200 bytes: +1,903,360 bytes, or 14.22%. This includes 1,736 added cells, their six surface records, 32 transport directions, six bounce entries and twelve filtered entries, plus 152 added lighting groups with six light entries each.

## Reproduce

From the repository root:

```powershell
& { $env:GUARD_COMMIT = '975d99e51'; nix develop --command bash docs/research/outer-grid-guard/render.sh /tmp/guard }
& { $env:EXPOSURE = '-3'; nix develop --command bash docs/research/outer-grid-guard/render.sh /tmp/guard-noenv -- --environment-map off }
nix develop --command bash docs/research/outer-grid-guard/compose.sh /tmp/guard/film hallway-main-vs-guard.mp4 base guard
nix develop --command python3 -I docs/research/outer-grid-guard/analyze.py /tmp/guard/film /tmp/guard/analysis base guard
nix develop --command python3 -I docs/research/outer-grid-guard/timing.py docs/research/outer-grid-guard/runpod/job2 main guard
```
