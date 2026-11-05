# Corrected environment reads in the tracer

Ticket `ter-7tnv`, 2026-11-05. Review base: `34c28466e641a864fd6a915c029e08f07b3b9ccc`.
The work stays on `ter-7tnv-corrected-environment-misses` in
`/Users/alexispurslane/.t3/worktrees/tertius/ter-7tnv-corrected-environment-misses`.
The parent owns coverage, mutation testing, integration and ticket closure.

## Scope and source situation

This is Design A of [the temporal renderer](../../plans/temporal-renderer.md), decision 35 and section 14, item 8.
Every specular reader searches the distance cube from its receiving surface point.
A distance cube stores the inverse distance to the nearest surface along each direction from the eye.
The receiving surface point is relative to the eye, in meters and world axes.
The search corrects parallax: the change in a nearby surface's apparent direction between two receiving points.
Ordinary diffuse misses still use their original directional read at roughness 0.5.
Irradiance, lit puffs and the hemisphere path stay unchanged.

The source is the accepted branch `ter-s6zr-corrected-read` at `570fdbbf8`.
Its S + Q2 choice was accepted on 2026-11-02, including its known misses.
The port uses its search and near-surface entry rule without restoring its reflection or occlusion pipelines.
`environment.glsl` is byte-identical to that source, with SHA-256
`9e049b3f5fc6409621664be0742eb08af3e08be8f65af1ab310829fc92bf6231`.

The accepted source uses this renderer's local cubes on the same M1 Max.
Normal captures use cube faces of 128 × 128 texels; focused GPU regressions use 32 × 32 texels.
The source needs neither a denoiser nor new history or images.
Its previous full-screen ambient cost was about 2.7 ms, not the old 0.2–0.6 ms estimate.
The current Design A estimates 0.1–0.4 million searches per 1280 × 720 frame.
Each search takes at most 12 distance reads: one near sample, eight even samples and three secant refinements.
This port adds no further algorithm and changes no ray budget, clipping, denoising or history setting.

## Implementation and review

`ShadingInput.position` gives the shared receiving position to each specular reader.
The G-buffer reader reconstructs it from the view ray and radial distance.
Forward shading takes its existing camera-relative position.
The tracer reuses that position instead of reconstructing it a second time.
Direct lighting and forward lighting also read that field, without an independent receiving-position argument.
The deferred direct pass uses `materialAt` for its position and derives its direction toward the eye from that position.

The corrected readers are reflection misses, composite closed forms, tracer-hit closed forms and forward lighting.
Forward lighting includes glass, fringe surfaces and impostor captures.
Each reader passes its own map identity, geometric normal, roughness and footprint.
The geometric normal is the surface normal before normal mapping.
The footprint is the angular size used to filter reflected stars.
Reflected stars keep their original world direction, roughness and footprint because they lie at infinity.
A search without a stored surface takes the original directional read exactly.

The search and screen walk share `ThicknessShare` through `thickness.glsl`.
`specular_cone.glsl` owns only the search's cone calculation.
`MapSearchCone` is its small C# numerical twin, not the deleted reflection-cone API.
The CPU search retains the accepted geometric-normal rejection and near-surface entry rule.

The review used the software-design rules of User, Ousterhout and Muratori.
One receiving-position field serves all lighting operations that accept a `ShadingInput`; none also accepts a position.
The original implementation left parallel direct-light arguments in place; the review fix removes them.
No new mode is added.
The shared cone owns a calculation used by the CPU search and its GPU twin without unrelated reflection operations.
`MapSearchCone` keeps only its stored tangent, factory and level calculation because those are the search's actual uses.
Its numerical-value role justifies that small interface; extra methods or branches would not improve its design.
The shader uses existing Vulkan cube-array sampling and explicit finite-level bounds.
No MoltenVK-only feature or new device feature was added.
This macOS validation does not establish Linux runtime behavior; that work remains deferred under `ter-cgif`.

## Red-green regressions

The real-renderer tests first ran against the integrated base, before the search was implemented.
Their failures were image-position errors, not compiler or setup errors.
Both no-surface fallback controls already passed and remain unchanged.

| Behavior | Integrated-base result | Required result |
|---|---|---|
| Mirror floor reflects the near wall's stripe | Angular error 0.4596871 rad | Error below one level-zero cube texel, 0.0625 rad |
| Glass reflects the near wall's stripe | Angular error 0.4596871 rad | Error below 0.0625 rad |
| Rough floor reflects a bright opening | Central ray meets the wall at y = −0.51041454 m | Ray meets the opening centered at y = 2 m, within 0.4 m |
| No stored surface, floor and panel | Both controls pass | Bit-exact directional fallback, including reflected stars |

The red log is `captures/ter-7tnv/logs/red-render.log`.
The first green run passed all 44 accepted search/render cases in `logs/green-core.log`.
The final focused run passed 59 search, renderer and anti-aliasing cases in `logs/focused-and-captures.log`.
That log also retains a later failure of an auxiliary provenance tool caused by a relative output path.
The corrected tool passed separately in `logs/capture-history.log`.

Additional regressions exercise a reflection miss finding a stripe behind the camera and an unchanged diffuse miss.
The shader search also matches its CPU twin over a read-back copy of the same rendered distance cube.
The accepted numerical bounds remain 0.001 for direction and reach, or 0.005 for alpha at least 0.5.
Alpha is the squared, floored roughness.
The wider bound covers the CPU approximation of cube-edge filtering at the two coarsest levels.

The initial full Checked run found two fixture expectations already changed on accepted source `570fdbbf8`.
The SMAA format comparison now turns the environment map off on both renderers.
The airlock floor uses the accepted summed-channel minimum of 6 rather than 10.
The corrected floor is approximately RGB (1, 2, 5), not (2, 4, 8).
An isolated copy of the original binary passed the original SMAA case.
The new off-screen stripe fixture also gained a complete dark wall behind the stripe.
These changes do not introduce new brightness or rendering policy.

## Matched accepted-scene captures

Three bounded scripts capture the terminal floor, the floor film and pbr-spheres before and after the port.
Each script captures the next simulation tick as well.
All captures use 1280 × 720 pixels, no upscaling, SMAA and the default scene environment map.
The map has 128 × 128 texels per face and updates one face per rendered frame.
Screen tracing uses roughness limit 0.5, 256 reflection steps and 62 diffuse steps.
Bloom and subsurface scattering retain their defaults; impostors and validation are off in these CLI captures.
The behavior tests use validation separately.

Auto exposure retains its defaults: −14 to 10 stops, no compensation, dark and bright shares of 0.1,
and adaptation times of 3 seconds into darkness and 1 second into light.
Different exposure values below are measured responses to the corrected light, not changed exposure settings.

| Scene | Script | First capture tick | Time, seconds | Camera | Base exposure, stops | Branch exposure, stops |
|---|---|---:|---:|---|---:|---:|
| Terminal | `scripts/ter-7tnv-terminal.json` | 314 | 5.233333333333333 | `terminal-camera` | −2.966791 | −2.9894311 |
| Floor film | `scripts/ter-7tnv-floor.json` | 3167 | 52.78333333333333 | `follow-camera` | 3.68828 | 3.7050803 |
| PBR spheres | `scripts/ter-7tnv-pbr.json` | 10 | 0.16666666666666666 | `front-camera` | −9.079193 | −9.081167 |

The actual renderer advances through warm-up draws 0–31, first capture draw 32 and next-tick draw 33.
Only draw 0 resets history.
The regression also reads reflection history and requires a history length greater than one.
This is not repeated rendering at a frozen draw index.

An auxiliary non-rendering observer records actual `FrameContext` draw indices and resets for both binaries.
Its source is `captures/ter-7tnv/history/HistoryProbeTool.cs`.
Settings and history records are `{base,branch}-{terminal,floor,pbr}-{settings,history}.json` under that capture folder.
The observer changes no renderer option.
All twelve observer PNG files are byte-identical to the corresponding CLI files, including the next-tick captures.
The ordinary final test build excludes this auxiliary observer.

Before images are in `captures/ter-7tnv/base-{terminal,floor,pbr}/`.
After images are in `captures/ter-7tnv/branch-{terminal,floor,pbr}/`.
The first image names are `terminal.png`, `floor.png` and `front.png`; each has a `-next.png` partner.
Both sets were opened and inspected.
The inspected vertical pairs are `comparison-{terminal,floor,pbr}.png`, with base above branch and no rescaling.
The terminal still shows severe tracer grain and a bright doorway; the floor retains grain and sharp object feet.
PBR spheres retain their mirror-to-rough progression and grain.
Parallax correction does not fix the unresolved tracer grain.
The accepted source's ghosts, doorway overshoot, sharp pillar foot and acceptance-box misses remain expected limits.

Separate clock-recorded CLI reruns are in `clocked-{base,branch}-{terminal,floor,pbr}/`.
All twelve PNG files are byte-identical to the inspected captures and their observer copies.
Each two-frame timed span contains only one 100 ms clock sample, so these clocks document provenance, not performance.
The clock logs are `logs/capture-clock-{base,branch}-{terminal,floor,pbr}.log`.

| Capture | Base clock, MHz | Branch clock, MHz |
|---|---:|---:|
| Terminal | 753.9 | 955.0 |
| Floor film | 880.2 | 821.3 |
| PBR spheres | 858.2 | 701.7 |

## Cost measurements

The measurement uses Release binaries from this checkout, without changing its source or switching branches.
The base was preserved before implementation; the control is its byte copy.
The branch binary contains this port.
The launch adapters in `captures/ter-7tnv/` retain the official timing and trace tools but select a preserved binary.
They skip rebuilding and change no renderer argument or measurement logic.
The runner is `captures/ter-7tnv/run-costs.sh`.

Seven rounds rotate base, control and branch over both 120-frame demo-exit windows.
The deck window begins at film frame 399; the floor window begins at film frame 2412.
Film frame numbers count rendered frames, not simulation ticks.
Each run uses 1280 × 720, no upscaling and two frames in flight to keep the GPU busy.
The GPU lock comes only from `tools/gpu-lock.sh`; its log records the one-minute load at each start.
`tools/gpu-clock.sh` records the clock over each timed span.
Pairs differing by more than 3% are excluded through `tools/gpu_clock.py compare`.

No screenshot timing is used as a performance measurement.
MoltenVK's raw individual-pass timestamps are not valid own-pass times.
Only the frame span, neighboring-pass sums and the separate Metal traces are used.

All 42 timing runs completed at 1296 MHz over their timed spans.
All seven base/control pairs and seven base/branch pairs remain for each window; none was excluded.
The table reports the median of seven per-run means, in milliseconds.
The difference columns report the median within-round difference, not a subtraction of the two medians.
The control difference measures run-to-run noise for a byte-identical binary.

| Window and measured span | Base, ms | Control, ms | Branch, ms | Branch − base, ms | Control − base, ms |
|---|---:|---:|---:|---:|---:|
| Deck, full frame | 11.342 | 11.342 | 14.105 | +2.762 | +0.005 |
| Deck, ray plan through composite | 6.040 | 6.034 | 7.875 | +1.839 | +0.001 |
| Deck, haze through exposure | 0.934 | 0.939 | 1.845 | +0.914 | +0.008 |
| Floor, full frame | 10.656 | 10.648 | 12.832 | +2.175 | −0.004 |
| Floor, ray plan through composite | 5.700 | 5.694 | 7.873 | +2.174 | −0.009 |
| Floor, haze through exposure | 0.125 | 0.124 | 0.124 | −0.001 | −0.000 |

The full-frame increase is about 2.76 ms on the deck and 2.18 ms on the floor.
Both are larger than section 11's old corrected-read allowance of 0.16–0.49 ms.
The deck frame is above the 12 ms target even before the later upscaling stage.
This is measured cost, not a reason to change the accepted algorithm or ray/history policy within this ticket.
The parent retains budget integration; no performance improvement or grain fix is claimed.

The three no-counter traces run base, branch, base at 1296 MHz.
Each trace includes 32 actual warm-up draws and 120 film draws, for 152 frames.
The table reports each pass's own mean encoder busy time over that full trace, in milliseconds.
These are not raw Vulkan timestamp values, and they are not converged-only 120-frame means.

| Pass | First base, ms | Branch, ms | Second base, ms |
|---|---:|---:|---:|
| Reflection rays | 2.769 | 4.935 | 2.767 |
| Diffuse rays | 1.017 | 1.150 | 1.011 |
| Composite | 0.142 | 0.139 | 0.139 |
| Transparent | 0.796 | 1.730 | 0.814 |
| Environment prefilter | 0.441 | 0.441 | 0.440 |

Reflection and transparent pass increases exceed the drift between the two base traces.
The diffuse miss stays directional, but diffuse-ray hits can still call a corrected specular closed form.
The tiny composite difference is within base-trace drift and does not establish a gain.
No paired-mode claim is made for a difference below that drift.

Every per-pass mean and median, including unchanged passes, is retained in
`captures/ter-7tnv/traces/deck-{1-base,2-branch,3-base}/passes.{json,md}`.
Each run's frame data, neighboring-pass sums, clock, load and pair comparison remain in `timing/round-{1…7}/`.
`measurement-summary.json` records the retained counts, medians, differences and trace values.

## Original implementation validation and identity

The original implementation's code/test/build-input tree is `f7733d3c09d2b3e9768cd51f41b9001a7a9b26e4`.
The captures and cost measurements used `dc82b223727ec1599c05826c32cc95acbb41864b`.
The only later code-tree change removes an obsolete test comment about the deleted reflection-delta pipeline.
The production Rendering DLL remains byte-identical; complete Checked and Release checks are rerun on the final tree.
The preserved base code/test/build-input tree is `ac2935f9bb7174bce611a509668980ea04887da1`.
`tools/code-tree.sh` gives the same identity for working and staged code.
It includes build inputs and scripts but excludes Markdown and ignored capture tools.
Both complete configurations passed on the final tree through the Nix dev shell and GPU lock.
Checked ran 9,971 cases successfully, with 20 explicit skips and no failures.
Release ran 9,958 cases successfully, with 20 explicit skips and no failures.
The different totals come from configuration-specific tests.
Both solution builds reported zero warnings and zero errors.

| Module | Checked passed / total | Release passed / total | Skips per configuration |
|---|---:|---:|---:|
| Core | 1,207 / 1,207 | 1,175 / 1,175 | 0 |
| Simulation | 2,001 / 2,001 | 2,020 / 2,020 | 0 |
| Physics | 1,492 / 1,500 | 1,493 / 1,501 | 8 |
| Rendering | 2,323 / 2,330 | 2,322 / 2,329 | 7 |
| Host | 2,016 / 2,021 | 2,016 / 2,021 | 5 |
| Unions | 59 / 59 | 59 / 59 | 0 |
| Style | 276 / 276 | 276 / 276 | 0 |
| Slop | 597 / 597 | 597 / 597 | 0 |
| Total | 9,971 / 9,991 | 9,958 / 9,978 | 20 |

The exact full-check command was:

```sh
bash tools/gpu-lock.sh captures/ter-7tnv/logs/readiness-all.log nix develop --command bash tools/test-all.sh -c Checked,Release -j 1 -o captures/ter-7tnv/readiness-tests
```

The full-check log is `captures/ter-7tnv/logs/readiness-all.log`.
Per-module logs and exit codes are in `captures/ter-7tnv/readiness-tests/{Checked,Release}/`.
The ordinary Host suite contains 2,021 cases, without the auxiliary capture observer.
All processes finished before the worker reported readiness.

`nix develop --command dotnet build benchmarks/Tertius.Benchmarks -c Release --nologo` passed with zero warnings and errors.
All 83 built Release engine, test and directional-reference SPIR-V modules passed `spirv-val --target-env vulkan1.3`.
The log is `captures/ter-7tnv/logs/final-benchmark-and-spirv.log`.
SPIR-V validation establishes valid shader modules, not Linux runtime execution.

| Preserved Rendering DLL | SHA-256 |
|---|---|
| Base and byte-copy control | `8b0315d198b89813ae1008dd963b32c7b85ae498e8495d17b3e5716208bbf718` |
| Branch | `725e4a6e591cdf385d4f039e34e1d11fd08d7f985818a09fb31bcd074c2355b6` |

The shared build input Git blob identities are:

| Input | Git blob identity |
|---|---|
| `flake.nix` | `44bf6b9a230cafe9015bd3a6b03e106654e92cf8` |
| `flake.lock` | `78b476d4eab9dfecda32972244c51b420065e406` |
| `Directory.Build.props` | `5fb1cea187cf499dff5bcd2b7ee3571b34f65e9c` |
| `Directory.Build.targets` | `c2df69519e81df0984e9ab2b1a0dc64e11e23a7b` |
| `Directory.Packages.props` | `2eddc1ae5cf92ce5fa41cdb568e3ae33203be15e` |
| `global.json` | `3140116df397728023ec76c8db41310c2c87e18f` |

The original launch brief is preserved verbatim in `.tickets/ter-7tnv.md` before the final commit.
No coverage or mutation pass was run in this worker.

## Review fix, 2026-11-05

The review identified one ownership defect and a missing red result for the final off-screen tracer fixture.
Decision 22 already settles the ownership fix; no new design or rendering policy is chosen here.

The original report said:

> One receiving-position field serves all readers; there is no parallel position store or new mode.

That claim was incomplete at `d42045d1ee44f4cc64a6e7f99736f523f8f2bb75`.
Direct lighting still accepted a position beside `ShadingInput.position`.
The direct pass also reconstructed that second position separately from `materialAt`.
This was demonstrated ownership leakage, not a reproduced visible lighting defect.

The fix removes the redundant argument from `lightPath`, `modelLight`, `directLight`, `lobesRadiance`,
`surfaceRadiance` and `directRadiance`, including every deferred and forward caller.
The direct pass uses the position from `materialAt` for both lighting and its direction toward the eye.
Helpers without a `ShadingInput` still accept a position; the tracer's offset ray origin remains a distinct quantity.
No search, diffuse-miss, hemisphere, exposure, history, ray-budget or denoising rule changes.

The direct pass now reads:

```glsl
    ShadingInput s = materialAt(pixel);
    vec3 V = -normalize(s.position);
```

where

- `s` = the shared shading input of the receiving surface, including its position.
- `materialAt(pixel)` = the G-buffer reader that reconstructs that position and reads the material at the pixel.
- `pixel` = the integer column and row inside the G-buffer image.
- `V` = the unit direction from the receiving surface toward the eye.
- `normalize` = the GLSL function that turns a nonzero vector into a unit vector.
- `s.position` = the receiving surface point relative to the eye, in meters and world axes.

The existing positive-distance guard excludes an eye position before these lines run.

The final complete-wall tracer fixture was run with only reflection misses restored to the original directional read.
The temporary overload is the four-argument `environmentSpecular` body from review base `34c28466e641a864fd6a915c029e08f07b3b9ccc`.
Other specular readers retained the corrected search, so this isolates the tracer's reflection-miss input.
`AReflectionThatLeavesTheScreenFindsTheStripeBehindTheCamera` failed with angular error 0.3666097 rad,
against the unchanged limit of 0.0625 rad.
The other seven cases passed, including both bit-exact open-sky fallback controls and the diffuse-miss control.
The patch is retained as `captures/ter-7tnv/review-fix/tracer-directional-baseline.patch`.
The execution log is `captures/ter-7tnv/review-fix/logs/red-final-tracer.log`.
The temporary overload was removed, and `screen_trace.glsl` was verified identical to the reviewed implementation.

### Assessment limits and ownership

This worker executes the macOS checks rather than relying only on the first worker's retained logs.
Linux runtime remains deferred under `ter-cgif`; valid Vulkan shader modules do not establish Linux runtime behavior.
The parent owns changed-line coverage, mutation testing, integration, budget acceptance and ticket closure.
The original paired cost increases, 2.762 ms per frame on deck and 2.175 ms on floor at 1296 MHz, remain above the old allowance.
This fix does not replace the settled search or claim that its cost meets that allowance.
New captures are visual checks, not frame-cost measurements.
Parallax correction still does not fix the unresolved tracer grain.

### Matched review-fix captures

The preserved pre-fix Release Rendering DLL is from `d42045d1ee44f4cc64a6e7f99736f523f8f2bb75`.
Its SHA-256 is `725e4a6e591cdf385d4f039e34e1d11fd08d7f985818a09fb31bcd074c2355b6`.
The fixed Release Rendering DLL has SHA-256 `bf54986295051c005ed52560a58464e6e8dc02f337a9b26b13da40338fa84065`.
Its code/test/build-input identity is `d7daa2215acd2055df7d75b581258be4678e5212`.
The accepted `environment.glsl` SHA-256 remains unchanged.

Both binaries reran all three bounded `scripts/ter-7tnv-{terminal,floor,pbr}.json` scripts through the real renderer.
The captures are under `captures/ter-7tnv/review-fix/{pre-fix,fixed}-{terminal,floor,pbr}/`.
Every settings record and simulation snapshot is identical across each matched pair.
The dimensions, cameras, simulation times, exposure settings and renderer settings remain those recorded above.
The auxiliary observer source remains `captures/ter-7tnv/history/HistoryProbeTool.cs`.
Its draw records match between binaries for every scene: indices 0–33, with a reset only at index 0.
All twelve observer PNG files are byte-identical to the corresponding ordinary CLI PNG files.
The observer is excluded from both final full-suite runs.

`capture-provenance.json` in the review-fix folder records settings equality, snapshot equality, draw records,
image SHA-256 values and the actual exposure values.
The terminal exposure changes from −2.9894311 to −2.9894981 stops.
The floor exposure changes from 3.7050803 to 3.7050846 stops.
The PBR exposure remains −9.081167 stops.
Those are measured exposure responses, not changed exposure settings.
All six matched before/after PNG byte streams differ; none is claimed to be an exact image match.

The clock records are `logs/{pre-fix,fixed}-{observer,terminal,floor,pbr}-clock.log` in that folder.
Capture clocks vary; these records are not matched fixed-clock frame-cost measurements.
The original seven-round cost evidence is retained for the original implementation, not relabeled as a new measurement.

All six primary before/after images were opened and inspected.
No new visible displacement or artifact was identified, and no reference image was updated.
The terminal retains its bright doorway and severe tracer grain.
The floor retains grain and sharp object feet.
PBR spheres retain the mirror-to-rough progression.
The three fixed primary images were shown inline with captions.

### Review-fix validation and final identity

The final code/test/build-input identity is `d7daa2215acd2055df7d75b581258be4678e5212`.
Both Rendering test-output stamps contain that identity.
The source identity was checked unchanged before and after the validation commands.
Only shader and Markdown files change in this fix, so there are no touched C# files for cleanup.
The earlier implementation's C# cleanup remains recorded above.

| Check executed in this fix stage | Result | Retained log under `captures/ter-7tnv/review-fix/logs/` |
|---|---|---|
| Final tracer fixture with only directional reflection misses | Expected angular-placement failure, 1 failed and 7 passed | `red-final-tracer.log` |
| Restored corrected read and position-consolidation focus | 47 passed, no failures or skips | `green-map-search.log` |
| Complete Checked solution, one test module at a time | 9,971 passed, 20 skipped, zero failed; 9,991 total | `Checked-all.log` |
| Complete Release solution, one test module at a time | 9,958 passed, 20 skipped, zero failed; 9,978 total | `Release-all.log` |
| Release benchmark compilation | Passed, zero warnings and errors | `benchmark-build.log` |
| All built Release shader modules, Vulkan 1.3 | 83 passed | `readiness.log` |

Every build, test, capture and probe command ran in the Nix dev shell.
GPU execution held the project GPU lock; the worker ran one build or test command at a time, in the foreground.
The driver is `captures/ter-7tnv/review-fix/validate.sh`.
The ordinary image-reference checks passed without changing a reference or weakening an assertion.

An initial combined-class filter used an unsupported union expression and selected zero tests, returning exit code 5.
That run, `green-focused.log`, is not passing evidence.
The corrected single-pattern run selected all 47 search cases and passed, followed by both complete solution runs.
The first preparation attempt lacked the patch command on PATH and was canceled while waiting for the GPU lock;
it ran no tests and is not red or green evidence.
The harness's bundled patch tool was then used for every source edit.

Every worker-owned command finished before the final commit.
The original launch brief was compared byte for byte with the copy preserved in `.tickets/ter-7tnv.md`.
No agents, workflows, checkout changes, coverage or mutation runs, merges, pushes, PR publication or ticket closure occurred.
No unresolved in-scope fork remains; the parent's hardening and integration decisions remain with the parent.
