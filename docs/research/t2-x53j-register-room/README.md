# No room under the M1's step: Apple's scheduler sets the peak (t2-x53j)

Ticket `t2-x53j` asked for two or three registers of room in `direct_key_fill.frag`, so that an unrelated change does not cross the M1's step from 448 to 384 resident threads at register 116. Twelve forms of the source that keep the picture found none. The register guard of the pre-commit hook holds the limit instead (tools/README.md, "The registers of the direct shaders"). The line orchestrator closed the search on 2026-11-23 (choice 1 of the fork).

## Terms

- The highest register is the largest 32-bit register index that an instruction of the shader's machine code names, as `tools/shader_registers.py` reads it from an offline compile for `applegpu_g13s`.
- A live register at an instruction holds a value that a later instruction reads. The peak is the instruction with the most live registers.
- The room is the limit, 115, minus the highest register.

## The peak

`tools/liveness.py` solves the live registers of the decoded code backward over its jumps. On main `3f906862f`, `direct_key_fill.frag` has 23,022 instructions, and its peak is 111 live registers at a `texture_sample` of the sun's cascade filter (sampler `ts24`) inside a light loop. The tile filter of the local lights (`ts14`) reaches 110. At the peak, the registers hold:

- about 30 shadow-map coordinates and results of the filter's 16 bilinear comparisons, which the compiler issues before it sums them;
- about 30 loop invariants that the compiler hoisted before the loop: a 3 × 3 basis, several `1 − x` terms and powers of one value;
- the running sums of the direct light (6), the loop counter, the light path's fields, and the surface and the lobes;
- 6 values that the rectangle loop carries from one iteration to the next, which start at 0: the fields of `LightPath` that `lightIrradiance` writes only after its early returns (a rectangle's `halfWidth` and `halfHeight`). SPIRV-Cross carries a field that a path may leave unwritten as a value of the iteration before.

## The variants

Each variant is a scratch copy of the shader folder, counted with `tools/shader_registers.py <out> <folder> <label>`. None changes the picture beyond float rounding.

| Variant | direct_key_fill.frag | direct.frag | Instructions of direct_key_fill.frag |
|---|---|---|---|
| main 3f906862f | r115 | r112 | 23,022 |
| V recomputed in the loop from the position | r115 | r112 | the same code: the compiler already computes it once |
| the fill response before the shadow lookup | r116 | r112 | 23,018 |
| the tile's corner means summed by parity class, then weighted | r115 | r112 | 23,070 |
| the corner coordinates as a base plus a step | r115 | r112 | the same code |
| the accumulations written as explicit `fma` | r115 | r112 | 23,084 |
| a rectangle's and a tube's sides written before `lightIrradiance`'s early returns ("sides") | r116 | r113 | 22,938 |
| sides + parity | r116 | r113 | 22,986 |
| sides + fma | r116 | r113 | 23,000 |
| parity + fma | r115 | r112 | 23,132 |
| sides + parity + fma | r116 | r113 | 23,048 |
| `[[dont_unroll]]` on the cascade filter, the tile filter, or both | r115 | r112 | the same code: Metal unrolls the loops anyway |

## The finding: the scheduler sets the peak

The "sides" variant removes the 6 carried registers: the liveness count shows no zero-started values in the loop any more. Yet the peak rises from 111 to 112 live registers, and the shader from r115 to r116, with 84 fewer instructions. The scheduler spent the freed registers on more shadow reads in flight. So the live state that the source holds does not set the highest register; the backend's scheduling of the filter's reads does, and it takes back any register that the source frees. A form of the source cannot hold room, and the guard holds the limit instead.

Two choices were left out:

- A filter form with fewer reads in flight, such as the tiles' nine gathers (`tileCornerMeans` off) or a cascade filter by gathers. The arithmetic is the same, but the order of the sum changes, so the picture changes by rounding. `VulkanRenderer.HeldValuesCost` chose the corner means for this driver because they ran faster, and the scheduler can take the gain back. It would need a design pass and a timed take.
- The "sides" form on its own merit: it is the honest form of `lightIrradiance`, and it cuts 84 instructions, but it crosses the step.

## The guard's evidence

With the one-step form of the sun's lit-side rule, `clamp((litFraction - D) / (1 - D), 0, 1)`, staged in `shadows.glsl`, the hook stopped the commit:

```
register guard: pass: direct.frag is at register 113, the limit is 115, room 2 registers.
register guard: fail: direct_key_fill.frag is at register 116, above its limit of 115: from register 116 the M1 keeps 384 threads of the shader resident in place of 448, which cost the direct pass 0.10 to 0.36 ms of the frame in every view (t2-x53j, t1-gout).
register guard: counted 2 shader(s) in 9 s; ...
pre-commit: the register guard stops the commit.
```

A count of both shaders takes 8 to 14 seconds on the M1 Max.

## The count for the direct shader's register budget (t0-t2oq, 2026-11-23)

The orchestrator picked design 2 of `t0-t2oq planning notes`, "The direct shader's register budget: two designs": one loop over the shadowed lights fills a local array of visibilities before the response loops, and the response loops read the array and hold no shadow read. Its gate was this count: in the form with the three blocked features, both direct shaders keep at least 8 registers of room, so a highest register of 107 or less. If the form failed the gate at every size of the array, design 1 (the visibility in a pass of its own) became the build.

A count shows the compiler's choice, not a time. Nothing here was timed.

### The variants

Each variant is a scratch copy of the shader folder of the line's head `ba0858bf5`, counted with `tools/shader_registers.py <out> <folder> <label>`. The files are in `count/`.

- S0, the split alone: `count/split.py <folder> <size>` adds `visibilityOf`, `fillVisibilities` and `seenAt` to lighting.glsl under `VISIBILITY_FIRST`, and gives the loops of lighting.glsl and key_fill_light.glsl the array's read in place of `pathVisibility`. direct.frag and direct_key_fill.frag define `VISIBILITY_FIRST` and call `fillVisibilities(s.position, s.geometricNormal)` right after `materialAt`.
- `visibilityOf(light, position, geometricNormal)` makes the direction, the distance, the turned geometric normal and the angle itself, with lightPath's arithmetic. It returns 0 beyond the light's reach and outside a spot's cone, where the response is 0, and otherwise `lightVisibility`. Each response loop counts the light's place among the shadowed lights of the main view along the light list, before its test of the shape. Every visibility feeds the sums, so the compiler removes nothing.
- S1, the split with everything: the line head's shaders with `shadows.glsl` and `shadow_bounds.comp` of the lean tree `bf1a02a06` (the narrow half with its trust rule, and the lean wide form: the shared lookup with the 25 gathers), `lightOutsideShadow` in one step, `clamp((litFraction - D) / (1 - D), 0, 1)` (t1-gout), and then the split. `count/s1-base.diff` is S1 without the split against the head, and `count/split.diff` is the split.
- The array's size: `ShadowAtlas.Tiles` (64 by default) + `MaxCascadedLights` (4) + 4 far-shadows-only lights = 72. The far-shadows-only lights have no bound today: `FarShadows` gives that slot to every far light that waits for a cascade set. Design 2 would need a bound there.
- Two controls of S1 (`count/control-*.diff`): the visibility loop alone (the shader sums the array and returns), and the response loops alone (every visibility is 1, with no array).
- Design 1's direct shader (`count/design1.diff`): S1 with one `texelFetch` of an image array in place of the array's read, and no visibility loop. Design 1's pass, `visibility.comp`, loops over the shadowed lights and stores `visibilityOf` in layer n. `tools/shader_registers.py` counts only render pipelines, so `count/count_compute.py` counts the compute shader: metal-tt with a compute pipeline for `applegpu_g13s`, and `_agc.main` of the archive's `__compute` section.

| Variant | direct.frag | direct_key_fill.frag | Instructions (direct / key fill) | Room (direct / key fill) |
|---|---|---|---|---|
| Line head `ba0858bf5` | r112 | r115 | 22,455 / 23,022 | 3 / 0 |
| S1 without the split | r117 | r119 | 25,351 / 25,918 | −2 / −4 |
| S0, the split alone, array of 72 | r104 | r108 | 9,431 / 10,002 | 11 / 7 |
| S1, the split with everything, array of 72 | r104 | r108 | 9,730 / 10,301 | 11 / 7 |
| S1, array of 32 | r104 | r108 | 9,730 / 10,301 | 11 / 7 |
| S1, array of 16 | r118 | r122 | 9,821 / 10,396 | −3 / −7 |
| S1, array of 4 | r106 | r110 | 9,641 / 10,216 | 9 / 5 |
| Control: S1's visibility loop alone | r55 | r55 | 1,655 / 1,655 | |
| Control: S1's response loops alone | r100 | r104 | 7,850 / 8,430 | |
| Design 1's direct shader | r103 | r107 | 8,083 / 8,657 | 12 / 8 |
| Design 1's `visibility.comp` | r53 (compute) | | 1,631 | |

### What the compiler did

`count/liveness_dump.py` is `tools/liveness.py` with the live count of every instruction written to a file. `count/regions.py` splits that file at the array's store in the visibility loop.

1. Where the array lives. At 32 and at 72, Metal keeps it on the stack: one `stack_store` in the visibility loop and 12 `stack_load`s, one in each response loop (3 shapes, fiber or surface, with the air's tint or without). The other 13 stack instructions of key fill are the head's. At 16 and at 4, Metal keeps the array in registers, with no stack instruction, and the highest register rises to r122 and r110. So 32 is the least size that does not cost, and 72 costs nothing more than 32.
2. No fused loops. The visibility loop is a code region of its own, from the start to about instruction 1,670, and its peak is 81 live registers in key fill (77 in direct.frag), at a texture sample of the filter. Alone, the loop needs r55. The extra 26 or so are, by our reading, the surface's G-buffer values that the source reads before the loop (`materialAt`). The decoder names no source line, so "no hoisted response invariant" is an inference.
3. The response loops set the peak: 107 live registers in key fill (104 in direct.frag), at an `fmadd32` in a response loop. Alone, with every visibility 1, they need r104 and r100. The array's read costs them 4 registers, and design 1's image read 3.

### The verdict

S1 fails the gate at every size: at 32 and 72 it misses by one register in direct_key_fill.frag (r108), and at 16 and 4 by more. Design 1's direct shader passes at exactly 8 registers of room (r107), and its pass needs r53. By the orchestrator's rule, design 1 is the build. The split is then a pass and an image, not cheap to carry in a scratch branch, so the gather search of t0-t2oq runs on `bf1a02a06` as it is.
