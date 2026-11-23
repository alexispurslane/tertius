---
id: t0-gg8m
status: open
deps: []
links: [t0-t2oq]
created: 2026-11-23T22:12:07Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, shadows, performance]
---
# Split the light loop: one loop makes every shadowed light's visibility before the response loops, so a shadow filter costs the direct shader no register

The direct shaders compute a light's visibility and its response in one loop body, written out 12 times. At the shadow filter's reads the body holds the filter's values and everything that the response needs afterwards. direct_key_fill.frag sits at register 115 on Apple's M1, the last register before the GPU keeps 384 threads resident in place of 448. Three features wait behind that step: the sun's lit-side rule in one step (t1-gout), the narrow half of the lamps' shadows, and the wide half (t0-t2oq). Every probe build of the direct shader runs 0.9 to 1.7 ms slower for it. The count of 2026-11-23 shows that with the visibility loop split off and all three features in it, direct.frag stands at r104 and direct_key_fill.frag at r108, and that the visibility loop alone needs r55.

## Design

Design by the line orchestrator of the Design X line, 2026-11-23, on the orchestrator's pick of 17:15 (design 2 of t0-t2oq's note "The direct shader's register budget: two designs"). The count that picked it is docs/research/t2-x53j-register-room/README.md, "The count for the direct shader's register budget", with its scratch split in count/split.diff.

### Terms

- A light's visibility at a point = the share of the light that its shadows let through there, 0 to 1.
- A light's response = what the surface sends to the eye per unit of the light's irradiance.
- A shadowed light of a view = a light that the view keeps (viewKeeps) and whose shadow slot is not NoSlot.

### The model

A light loop does two jobs, and each gets its own loop. First one loop makes the visibility of every shadowed light of the view at the shaded point. Then the response loops run as today and read each light's visibility in place of looking it up. A response loop holds no shadow read.

- Who computes: `directLight` (lighting.glsl) and `keyFillLight` (key_fill_light.glsl) each run the visibility loop first and then their response loops. No caller of theirs calls two functions in order.
- Where the result lives: an array of the fragment, local to the invocation. It has one writer, the visibility loop, and it dies with the invocation. No image, no pass.
- The place of a light in the array is its place among the shadowed lights of the view, counted along the light list. The visibility loop and every response loop count it the same way, in one shared function, so no light stores it and the two cannot fall out of step.
- One path for every caller of `directLight` (direct.frag, transparent.frag, fringe.frag, environment_face_light.frag, impostor_capture.frag and the composite's and the tracer's includes): no switch that keeps the old loop for some of them (my choice, answer 115). If a caller's pass gets slower by a measured amount on the M1 or on the card, stop and report it as a fork.

### Signatures

    // The most shadowed lights of a view: the shadow atlas's tiles and the cascade sets. One constant for C# and GLSL,
    // derived from those two and from nothing else.
    const uint ShadowedLights = ...;

    // The share of `light` that its shadows let through at `position`, by the rule of `shadowMaps`. It makes the
    // direction to the light, the distance, the geometric normal turned toward the light and the sine of its angle
    // itself. 0 where the light gives the point nothing by a cheap test: outside its reach, outside a spot's cone, and
    // behind the surface for a point light.
    float visibilityOf(GpuLight light, vec3 position, vec3 normal, vec3 geometricNormal, bool fiber, uint shadowMaps)

    // The visibilities of the shadowed lights of `view` at the point, in the order of the light list.
    void seeLights(ShadingInput s, bool fiber, uint shadowMaps, uint view, out float seen[ShadowedLights])

    // In a response loop, in place of pathVisibility(i, p, s.position, shadowMaps):
    float visibility = lights[i].shadow.x == NoSlot ? 1.0 : seen[place];

- `place` = the light's place among the shadowed lights of the view.
- The cheap tests of `visibilityOf` must skip the lookups that `lightPath` skips today wherever a few products can tell: today a surface that faces away from a lamp pays no lookup, and it must pay none after the split. The count's scratch had only the reach and the cone.
- The names and the array's form (a local, a parameter or a file-scope variable of the shader) are yours. The order of the two jobs and the one owner of the place are the design.

### Settled by the orchestrator (17:15)

1. The array's size is one constant shared by C# and GLSL, derived from the atlas's tile count and the cascade sets, not a second number that someone must keep in step.
2. The stack is a choice of Apple's compiler, and it flips: 16 gives r122, 32 gives r108. So the register guard covers both direct shaders after the split, compiled with the array's real size, and a comment at the array says why its size must not shrink below the point where the compiler keeps it in registers. A compiler or a size change that moves the array back then fails a check.
3. The lights with far shadows only: by a count. Their visibility stays in the response loop if that costs no register. If it costs, they join the array with the bound that exists in fact today, and the lead reports the number. No new limit that takes a shadow from a light that has one today.
4. No cap under the atlas's tile count.

### Out of scope

The soft shadow form, the sun's lit-side rule, the narrow half and any change of a filter: each is its own ticket after this one. The response loops' 12 written-out bodies stay as they are. The comments in keyShapeLight that explain what it must not hold across the shadow lookup are made true, and its code is not reshaped beyond the split.

### What stays exposed (recorded on the orchestrator's order)

The response's 12 written-out loop bodies hold the peak at r104 to r108. So growth of the response is the next thing that can fall off the step. No split of the visibility gives more than 11 registers of room. No work on that now.

## Acceptance Criteria

1. The M1 take comes first, before the gates, and goes to the line orchestrator at once: the settled GPU frame span and the direct sums (key and fill, direct light, light pyramid) of exit-cabin and exit-deck, the split against the clean main, under one exclusive hold of the GPU lock on a quiet machine. They do not rise. The table names its main as the clean main. 2. The picture changes by float rounding at most: the shadow snapshots hold, and no reference image is approved again without a report of the three images. 3. One card take of the usual size (tools/runpod/gpu_job.py, about 0.06 USD) shows no rise on the desktop card. 4. Tests that lock the order in: each lamp's shadow falls under its own caster in a list that mixes shapes, unshadowed lights and lights that the view drops, and the last light that the atlas can hold still casts its shadow. 5. The register guard counts both direct shaders with the array's real size, its table and tools/README.md name the new registers and the room, and the comment at the array names the size under which the compiler keeps it in registers. 6. The gates: Checked, then Release; the mutants of the changed lines by tools/mutate.py with timeouts named apart from kills; one review; ARCHITECTURE.md says that a light's visibility is made before its response and why.

