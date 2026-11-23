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
