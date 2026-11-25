---
id: t0-t2oq
status: closed
deps: []
links: [t1-af5w, t0-gg8m]
created: 2026-11-23T21:39:17Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, shadows]
---
# A lamp's shadow edge of a far caster is 2.6 to 13 times too sharp: the fixed ramp of 4 tile texels, and no form of a wider read fits the direct pass's cost

**2026-11-25 · Retired.** The historical light or shadow edge remains documented. The film review has no matching priority shot that would justify changing the lighting model or shadow budget.

Soft lamp shadows by the caster's distance are blocked by two costs, the register step of the direct shader on Apple's GPU and the form's 25 gathers, and each has its own work (the orchestrator's decision of 2026-11-23, after the card's take).

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

**2026-11-23T21:49:17Z**

The card's take (2026-11-23, recorded by the line orchestrator at 16:49 New York time; the lead's record is docs/research/t1-af5w-shadow-softness/README.md, 'The card's take', merged as 0abdfc2c5). One job through tools/runpod/gpu_job.py on an NVIDIA RTX 2000 Ada Generation, driver 595.112.07, 285 s, 0.0567 USD. The main is the clean main (72426c219). The lean form is the shared lookup with the 25 gathers (commit bf1a02a06 on 72426c219, the Mac's only-lean-shr build). One discarded warm-up per side and view, then 5 alternating rounds, the Mac take's scripts, exposures, render scales and 1920 x 1080. Medians of 5 in ms (the range in brackets). Direct = key and fill + direct light + light pyramid. exit-deck: direct 1.916 (1.898 to 1.916) against 2.171 (2.169 to 2.179), +0.255; frame span 12.083 against 12.488, +0.405. exit-cabin: direct 3.622 (3.619 to 3.666) against 4.210 (4.201 to 4.232), +0.588; frame span 23.232 against 24.307, +1.075. Transparent + FSR 3.1: +0.105 and +0.411. The same form on the M1, against the clean main: +0.94 (exit-deck) and +1.89 ms (exit-cabin). As shares of the clean main's direct sum: the card +13 % and +16 %, the M1 +26 % and +28 %. My reading: the form's own work is about 0.6 ms in the exit cabin on the card, at the edge of the rule's 0.5 ms, and the M1 pays about 1.3 ms on top of it there. If the form's work on the M1 were the card's share of 16 %, it would be 1.09 ms of the 1.89, and 0.80 ms would be left for the register step, under the arithmetic's upper bound of 1.13 ms. The orchestrator decides what this ticket's first line becomes.

**2026-11-23T21:51:21Z**

The orchestrator's decision on the card's take (2026-11-23, recorded by the line orchestrator at 16:51 New York time; reversible by the user). The card says two things, and both are taken. (a) The register step is a shared cause: about 0.8 ms of the lean form's 1.89 ms on the M1, it blocks three features, and it makes every probe build of the direct shader 0.9 to 1.7 ms slower. So the direct shader's register budget gets its own design, written by the line orchestrator as a note here: two designs in the compact form (a table of where they differ, what each costs on the M1, what it must not cost on a desktop card), with a recommendation. No lead and no build before the orchestrator picks. The model that both designs are held to: the direct shader keeps one job per function body that the compiler sees, and the fix must not be machinery that only the Mac needs and that the Linux path pays for. A design that only shuffles registers inside the same body and returns to 114 of 115 is not a design. (b) The form's own work is also too much: 0.59 ms on the card and about 1.09 ms on the M1 without the step. The rule bends to about 0.6 ms in the exit cabin on the M1 after the register work. The lever is the count of gathers: after the dither's build, the shadow lead searches the 25 gathers by halves in the probe binary for the least count at which the lamp crops beside the Cycles reference show no change that a viewer sees. Differences between counts are valid in the probe binary, because every count sits above the step. One take of at most 20 minutes, with pictures of the least count and the one below it beside 25. Money: one more card job of the take's size (0.057 USD) is allowed without asking; more needs the user's word.

**2026-11-23T21:55:14Z**

## The direct shader's register budget: two designs (the line orchestrator of the Design X line, 2026-11-23, 16:55 New York time). No lead and no build before the orchestrator picks.

### Terms

- **The direct shader** is direct_key_fill.frag, with direct.frag beside it. Metal compiles each as one function body, because it inlines every call.
- **A light's visibility at a pixel** is the share of the light that its shadows let through at the pixel's surface point, from 0 to 1 (`pathVisibility`, lighting.glsl). The sun's comes from the cascade filter and the far shadows, a lamp's from the tile filter.
- **A light's response at a pixel** is what the surface sends to the eye per unit of the light's irradiance: the fill response, and the full response by the key share (`fillResponse`, `pathLight`).
- **A shadowed light** is a light of the main view whose shadow slot is not `NoSlot`.
- **The room** is 115 minus a shader's highest register on the M1.

### The cause, as a model

The direct shader does two jobs for each light inside one loop body: the light's visibility and the light's response. So at the shadow filter's reads the body holds both jobs' values at once: the filter's coordinates and results (about 30 registers) and the response's inputs, which it needs after the lookup (the surface, the lobes, the eye's direction, the light's path, the sums, and about 30 hoisted invariants). Apple's scheduler then spends any register that the source frees on more shadow reads in flight (t2-x53j). A filter that grows, or a value that must live across the lookup, crosses the step.

The loop body is written out 12 times in direct_key_fill.frag (3 shapes, fiber or surface, tinted or not), and each copy holds both filters. That is most of its 23,022 instructions.

The visibility needs little: the light, the surface point, and the geometric normal. It needs no lobe, no eye direction and no sum. So the two jobs can part, and the only question is where a visibility lives between the code that computes it and the code that reads it.

### Design 1: the visibility in its own pass

A compute pass before the direct pass writes each pixel's visibility of each shadowed light into an image. The direct shader reads it and holds no shadow filter.

```glsl
// visibility.comp, one invocation per pixel: layer `n` is the n-th shadowed light of the main view.
imageStore(lightVisibility, ivec3(pixel, n), vec4(visibilityOf(lights[i], position, geometricNormal)));

// key_fill_light.glsl, in the response loop:
float visibility = shadowed ? texelFetch(lightVisibility, ivec3(pixel, n), 0).r : 1.0;
```

where

- `lightVisibility` = an R8 image array at the render size, one layer per shadowed light of the main view.
- `n` = the light's place among the shadowed lights of the main view, counted along the light list.
- `visibilityOf(light, position, geometricNormal)` = the light's visibility at the surface point `position` whose geometric normal is `geometricNormal`: today's `lightVisibility` with the direction, the distance and the angle made inside it.
- `shadowed` = whether the light is a shadowed light.

One owner: the pass writes the image, and the direct shader only reads it. The count of layers needs a cap, which `ShadowTiles` enforces when it grants tiles.

### Design 2: the visibility first, in the same shader

The direct shader runs one loop over the shadowed lights before its response loops. That loop computes each visibility into a local array. The response loops read the array and hold no shadow filter. No pass and no image.

```glsl
// Before the response loops, once for all shapes and for fiber and surface alike:
float visibility[MainViewShadowedLights];
uint n = 0u;
for (uint i = 0u; i < frame.counts.x; i++)
    if (viewKeeps(MainView, lights[i]) && lights[i].shadow.x != NoSlot)
        visibility[n++] = takesAny(lights[i], s) ? visibilityOf(lights[i], s.position, s.geometricNormal) : 0.0;

// In each response loop, in place of pathVisibility:
float seen = shadowed ? visibility[n++] : 1.0;
```

where

- `MainViewShadowedLights` = the most shadowed lights of the main view, a constant that C# and GLSL share and that `ShadowTiles` enforces.
- `n` = as in design 1. Each loop counts it again along the light list, so no light stores it.
- `takesAny(light, s)` = whether the surface point is inside the light's reach and cone: the cheap tests of `lightIrradiance`, so that a pixel outside a spot's cone pays no lookup.
- `seen` = the light's visibility at the pixel.

### The two designs side by side

| | Where a visibility lives between its writer and its reader | New resources | Cost on a desktop card | The M1's registers | What a later reader gets |
|---|---|---|---|---|---|
| Design 1, a pass | An image layer per shadowed light | One compute pass, one R8 image array (0.9 MB per layer at 1280 x 720, 8.3 MB at 3840 x 2160), a cap on the layers | One more pass over the G-buffer, one store and one fetch per shadowed light and pixel, the light's reach tests twice. It must stay under about 0.1 ms at 1080p, and a card take says whether it does | Two bodies, so the split is certain. My estimate for the direct shader: about 80 live registers at its peak | Any pass can read the image. A visibility at another rate than the pixels' becomes possible |
| Design 2, a loop | A local array of the fragment | None. A cap on the array | The reach tests twice for the shadowed lights. Nothing else | One body with two loops. My estimate: about 78 live registers in the visibility loop and about 97 in a response loop, if the 16 values stay in registers. Not certain: Metal can keep the array on the stack, and it can hoist the response's invariants above the first loop | Nothing. The visibility dies with the fragment |

Both share:

- The two jobs part. A response loop holds no shadow read, and the filter's code appears once in place of 12 times.
- One function, `visibilityOf`, makes the direction, the distance, the turned geometric normal and the angle from the light and the surface point. No caller hands them in, so no caller holds them across the lookup.
- A cap on the shadowed lights of the main view, enforced where `ShadowTiles` grants tiles. The atlas's 64 tiles allow 64 spot lamps today, so the cap is a new limit. Exit-cabin has 8 lamps.
- The picture changes by float rounding at most: each light's sum is still visibility times response.
- The forward shaders (the transparent pass, the puffs, the environment faces, the hit light) keep their own lookups.
- The first step of either is a count, not a build: the changed shaders in a scratch copy of the shader folder through `tools/shader_registers.py`, 10 seconds per count on the Mac, with no GPU work. The count says how much room the design gives before anyone builds it. The register guard then gets the new shaders and their limits.
- Neither is machinery for the Mac alone in its form: a renderer that parts visibility from response is the usual deferred layout. (From memory, not read again: engines that collect the sun's shadow into a screen image before shading. Their situation is one sun. Ours is a sun and up to about 10 shadowed lamps, so design 1's memory grows with the lamps, where theirs does not.)

Not known, and the count answers each in seconds:

1. The direct shader's highest register without its filters.
2. Whether Metal keeps design 2's array in registers, and what a stack array does to the count of resident threads.
3. The highest register of design 1's compute shader, and whether a compute shader has the same step.

### The forks

1. **Where the visibility lives**: an image that a pass writes (design 1), or a local array (design 2).
2. **The cap on the shadowed lights of the main view**: 16, or another number. Both designs need one. Today the limit is the atlas's tiles.

### My recommendation

Design 2, with the count as its gate, and design 1 if the count shows under about 8 registers of room.

- Design 2 costs the Linux path nothing and adds no resource whose lifetime someone must own. A visibility has one reader today, so an image for later readers is generality for a caller that does not exist.
- Design 1 holds the orchestrator's model to the letter: one job per body that the compiler sees. Design 2 holds it per loop, inside one body, and the scheduler's finding of t2-x53j is the reason to doubt a split inside one body. The count is cheap enough to settle that doubt before the choice binds anyone.
- If a second reader appears (a visibility at half rate, or the transparent pass), design 2 becomes design 1 by moving its loop into a pass. `visibilityOf` and the cap stay.

For the wide half's rule after this work (about 0.6 ms in the exit cabin on the M1): the gather search runs apart from this design, and its count of gathers goes into `visibilityOf`, wherever that function runs.

**2026-11-23T21:56:21Z**

The orchestrator's pick for the register budget (2026-11-23 17:00 by its clock, recorded by the line orchestrator at 16:56 New York time; reversible by the user). Fork 1: design 2, the local array filled by one loop before the response loops, with the count as its gate. Reasons: a visibility has one reader, so an image with its own lifetime would be a second store with no second reader, and design 2 becomes design 1 by moving one loop. The gate: with the visibility loop split off, the direct shader's highest register with the lean soft shadow form and the three blocked features in it (the sun's lit-side rule, the narrow half, the wide half) leaves at least 8 registers of room under 115. If Metal fuses the loops or hoists the response's invariants so that the room is less, design 1 is the build, with no further question. Fork 2: no new cap. The atlas decides which lights hold a shadow slot, and its tile count is the one limit; the bay has more lamps that want a slot than the atlas has tiles, so a cap of 16 would take shadows from lamps that have them today. The array's size is the atlas's tile count. If the count or a timing shows that an array of that size costs on the M1, the orchestrator gets that number with the least size that does not cost, before any cap goes in; a cap under the atlas is a change of what the user gets, decided with a picture of the bay. Who and when: the shadow lead does the count as a scratch change of the shaders, after the dither's build and before the gather search, with no GPU. The gather search then runs in the form that the count picked, with one Release build. After the count passes, the split is a build ticket with the usual gates. Its acceptance: the picture changes by float rounding at most (the shadow snapshots hold), the settled GPU span of exit-cabin and exit-deck on the M1 does not rise beside the clean main, and one card take of the usual size shows no rise on the desktop card.

**2026-11-23T22:09:49Z**

The register count (2026-11-23, recorded by the line orchestrator at 17:09 New York time; the lead's record goes to docs/research/t2-x53j-register-room/README.md on the branch t0-t2oq-register-count). Offline compiles for the M1 at the line's head ba0858bf5; a count shows the compiler's choice, not a time. direct.frag / direct_key_fill.frag: the head r112 / r115 (22,455 / 23,022 instructions); the three blocked features and the lean form with no split r117 / r119; design 2, the split alone with an array of 72, r104 / r108 (9,431 / 10,002 instructions); design 2 with everything in it (S1) at 72 or 32, r104 / r108, room 7 in key and fill; S1 at 16, r118 / r122; S1 at 4, r106 / r110; the visibility loop alone r55; the response loops alone r100 / r104; design 1's direct shader with one texelFetch per shadowed light r103 / r107, room 8; design 1's compute shader r53. At 32 and 72 Metal keeps the array on the stack (1 stack store, 12 stack loads), at 16 and 4 in registers. The loops are not fused: the visibility loop peaks at 81 live registers, a response loop at 107. 72 is the atlas's 64 tiles, 4 cascade sets and 4 lights with far shadows only, which have no bound today. My reading: the split works, and my estimate of 18 to 35 registers of room was wrong, because the response and not the filter holds most of the registers. Design 2 misses the gate of 8 by one register and design 1 passes by none, a difference that the shadow softness record calls the allocator's noise. Design 1's image without a cap is 72 layers, 66 MB at 1280 x 720 and about 600 MB at 3840 x 2160. Asked of the orchestrator: design 2 on the stack at room 7 (my recommendation), or design 1 as its rule says.

**2026-11-23T22:12:07Z**

The orchestrator's answer to the count (2026-11-23 17:15 by its clock, recorded by the line orchestrator at 17:12 New York time): option 1, design 2, the local array on the stack, sized 72. Its gate of 8 was a guess made before the count, and 7 against 8 is inside the allocator's noise. What decides: a shadow filter feature now lands in a body of r53 to r55 and costs the direct shader no register, and design 2 adds no pass and no image, where design 1's image without a cap is 149 MB at 1080p. Answer 114 stands. The build ticket is t0-gg8m, with the orchestrator's five conditions in it. Order on the shadow lead: the dither's build, the gather search on bf1a02a06, then the split. The soft shadows' own build comes after the split is on main and the search has its count, as one more ticket. What stays exposed: the response's 12 written-out loop bodies hold the peak at r104 to r108, so growth of the response is the next thing that can fall off the step. No work on that now.
