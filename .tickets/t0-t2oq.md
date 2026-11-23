---
id: t0-t2oq
status: open
deps: []
links: [t1-af5w]
created: 2026-11-23T21:39:17Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, shadows]
---
# A lamp's shadow edge of a far caster is 2.6 to 13 times too sharp: the fixed ramp of 4 tile texels, and no form of a wider read fits the direct pass's cost

Left open by the shadow softness ticket t1-af5w on 2026-11-23. A lamp's share at a receiver is the lit share of a square of 4 tile texels, for every gap between caster and receiver. The measure (docs/research/t1-af5w-shadow-softness/README.md) found the 1.2 m panels 6 to 13 times too sharp where the gap is 8 to 11 m, and the 0.15 x 0.5 m panels 2.6 to 4.3 times. The Cycles direct-only reference shows the same shadow bands with much softer edges (pics/part1/ref-deck-far.png, direct-deck-far.png, lean-deck-far.png). Near casters and the contact band at a wall's base are right: in direct light only, the engine draws them black and sharp like the reference.

## Design

What was tried, with the record's numbers. (1) A pyramid of depth moments: lets light through where casters overlap in depth; out. (2) Views across the emitter: draws a double edge beyond about 0.5 m; out. (3) Capped direct taps (a box of 8 texels, 25 gathers, blended with today's box by the footprint's width): clean and close to the reference, but its lean form costs 1.89 ms in the exit cabin's direct passes (key and fill, direct light, light pyramid) against the clean main, and 1.41 ms with its reads replaced by one value. The line's rule was about 0.5 ms. direct_key_fill.frag sits at its register limit r115, and every form measured r116 or more. (4) Coarser depth levels: refused by argument, a thin caster is lost or as dark as a wall. This ticket reopens the question only when one of these changes: the direct pass's register budget, the cost of a second lookup in key and fill, or the user's bar for the lamps' far edges. A design then names one owner of a tile's contents and freshness (the record's section 'The per-tile data that the probe writes and reads'), because the far-field probe of ter-6fka adds a color beside a shadow tile's depth.

## Acceptance Criteria

The far caster's edge in exit-deck is as soft as the Cycles direct-only reference at 1:1, a near caster's edge and the contact band stay sharp, and the direct passes cost at most about 0.5 ms more in exit-cabin against the clean main.


## Notes

**2026-11-23T21:41:54Z**

## Where the registers go in direct_key_fill.frag: what the records hold (the line orchestrator, 2026-11-23, 16:41 New York time). Knowledge only, no design.

The orchestrator's order of 16:40: a card job tells the form's work apart from the Mac's register step (the lead of the shadow softness probe runs it: the clean main against the lean form with the shared lookup and the 25 gathers, exit-deck and exit-cabin, 5 runs per side after a warm-up). If the card's cost is small, what blocks this ticket is the direct shader's register budget on Apple's GPU. This note collects what is known about that budget.

### The step

- On Apple's M1 the GPU keeps 448 threads of a fragment shader resident while the shader's highest register is 115 or less, and 384 from register 116 (tools/README.md, "The registers of the direct shaders"). The highest register is the largest 32-bit register index that an instruction of the compiled shader names, read offline by tools/shader_registers.py.
- Main: direct.frag is at r112, direct_key_fill.frag at r115, with no room. The pre-commit hook's guard holds 115 for both.
- Measured costs of crossing the step: 0.36 ms of the direct pass on the deck (ter-8esk), 0.13 to 0.28 ms in every view (t1-gout).
- A larger measured gap, not split into its causes: the shadow softness probe's binary with its switch off (r127 in both shaders, and an RGBA32F bounds image in place of RG32F) is 0.9 to 1.7 ms slower than the clean main in the sum of key and fill, direct light and light pyramid.
- My arithmetic, not a measurement: 448 / 384 is 1.167. If a pass's time went with the count of resident threads, the step would cost up to 16.7 % of the pass: up to 0.60 ms of exit-deck's 3.60 ms and up to 1.13 ms of exit-cabin's 6.79 ms. The lean floor measured +0.82 and +1.41 ms there. So the step can be most of the lean form's cost on the Mac. The card's take tests this.
- Not known: whether the count of resident threads falls again above register 116, for example near r127. No take measured a second step.

### What holds the registers at the peak (docs/research/t2-x53j-register-room, main 3f906862f)

direct_key_fill.frag has 23,022 instructions. Its peak is 111 live registers at a texture_sample of the sun's cascade filter inside a light loop. The tile filter of the local lights reaches 110. At the peak the registers hold:

- about 30 shadow-map coordinates and results of the filter's 16 bilinear comparisons, which the compiler issues before it sums them;
- about 30 loop invariants that the compiler hoisted before the loop (a 3 x 3 basis, several 1 - x terms, powers of one value);
- the 6 running sums of the direct light, the loop counter, the light path's fields, the surface and the lobes;
- 6 values that the rectangle loop carries from one iteration to the next.

### The finding that closed the search for room

Apple's scheduler sets the peak, not the source. The variant that removed the 6 carried registers raised the peak from 111 to 112 live registers and the shader from r115 to r116, with 84 fewer instructions: the scheduler spent the freed registers on more shadow reads in flight. Twelve forms of the source that keep the picture found no room. Two choices were left out there, and neither was timed: a filter form with fewer reads in flight (the tiles' nine gathers in place of the 16 corner means, or a cascade filter by gathers), and the form that crosses the step on its own merit.

### What each feature needed (docs/research/t1-af5w-shadow-softness, direct.frag / direct_key_fill.frag)

| Feature | Form | Registers |
|---|---|---|
| The sun's lit-side rule in one step (t1-gout) | clamp((litFraction - D) / (1 - D), 0, 1) in shadows.glsl | direct_key_fill.frag r116, the guard stopped it |
| The narrow half of the lamps | the remap as one float 4 / w out of the lookup, trust rule off | r115 / r115 |
| The narrow half with the trust rule | the same with the receiver's angle | r115 / r117, or r116 / r116 with two floats out of the lookup |
| The wide half, a second lookup after the taps | the probe's own tileLookupOf only | r115 / r117 |
| The wide half, 64 corner means of the box of 8 texels | cut cap 8 | r124 / r126 |
| The wide half, 25 gathers of the same box | cut cap 8 | r115 / r117 |
| The lean form, one lookup shared by both paths, 25 gathers | lean | r116 / r119 |
| The lean form, the same with the reads replaced by one value | lean floor | r115 / r118 |

Read together:

- The live range of a value across a texture fetch is what costs: the trust rule costs 2 registers only because the receiver's angle lives across the bounds fetch.
- A second lookup costs about 2 registers in key and fill. The 64 corner means cost about 8. The 25 gathers cost none over the lookup.
- Near variants move by one register either way, so one register is inside the allocator's noise.
- On the M1 the 25 gathers were 0.44 ms faster than the 64 corner means in exit-cabin's direct passes, for the box of 8 texels. VulkanRenderer.HeldValuesCost chose the corner means for today's box of 4 texels on this driver, from an earlier take.

### Three features against one wall

The sun's lit-side rule, the narrow half with its trust rule, and the wide half each need 1 to 4 registers above 115 in direct_key_fill.frag.
