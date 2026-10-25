# Rendering upgrade

Ticket: `ter-sglu`. The user grouped these features into one epic on 2026-10-21.

## Goal

Bring the renderer to its intended look in one epic: a PBR BRDF, an HDR pipeline with automatic exposure, a tone curve and bloom, shadows for every light, and screen-space effects. Every feature in this epic changes the look of a frame. The snapshot tests and the asset pipeline come after it, so that reference images and imported materials meet the final look.

## Members

| Ticket | Feature | Waits for | Design |
|---|---|---|---|
| `ter-ptft` | A PBR BRDF: GGX with metallic-roughness materials | Nothing | Two designs, in the ticket |
| `ter-zzyr` | Automatic exposure | PBR | Two designs, in the ticket |
| `ter-2t5o` | The Hable tone curve | PBR | Two designs, in the ticket |
| `ter-63wr` | Bloom | PBR | Two designs, in the ticket |
| `ter-gcm3` | Anti-aliasing with SMAA 1x | PBR | In the ticket |
| `ter-b3h5` | Screen-space ambient occlusion | PBR | Two designs, in the ticket |
| `ter-a24f` | Screen-space global illumination | PBR, SSAO | Two designs, in the ticket |
| `ter-jxv9` | Screen-space reflections | PBR | Two designs, in the ticket |
| `ter-s9cv` | Point and spot light shadows, and lights instead of one sun (`ter-bpvl`) | PBR, exposure, the tone curve, bloom | [local-light-shadows.md](local-light-shadows.md) |

## Order

- PBR comes first. The look and the tuning of every other feature depend on the BRDF. SSR weights its reflections by the Fresnel term and blurs them by the roughness. SSAO and SSGI scale the diffuse and ambient light. Exposure, the tone curve and bloom are tuned against the radiance that the BRDF sends.
- The lights epic waits for exposure, the tone curve and bloom. Its move to SI units needs exposure and pre-exposed HDR values. The user also wants the HDR pipeline in place before the lights change.
- SSGI waits for SSAO, as before.
- The snapshot tests (`ter-rtxu`) wait for this whole epic. A snapshot locks in a look, and it cannot drive red-green TDD, because its reference image exists only after the feature works.
- The asset pipeline (`ter-kge6`) waits for this whole epic. Its decisions 12 and 13 convert glTF metallic-roughness materials to Blinn-Phong and accept that metals look wrong. After PBR, the importer takes glTF materials as they are.

## Rules for every member

- A `RendererOptions` switch for the feature, and its cost measured at 1080p, as "Measuring the cost of a feature" below says.
- Red-green TDD for the C# parts and the math that CPU tests can reach.
- The look is reviewed by eye in headless screenshots of the demo scenes. No member adds reference images. The snapshot tests epic adds them after this epic.
- A temporal effect stays off under the deterministic switch that the snapshot tests epic adds. Until that switch exists, the feature's own switch turns it off.
- The last step of each member updates ARCHITECTURE.md to match the code.

### Measuring the cost of a feature

The renderer times each pass on the GPU (ARCHITECTURE.md, "GPU timings"), and a headless run of the demo writes the summary of those times. A headless run never turns on the validation layers, so it measures the renderer alone. Measure in a Release build, at 1920x1080, with a script whose frames show the feature. `scripts/demo-exit.json` films 3,879 frames in about 33 s. The renderer never reads the last 2 of them (`FramesInFlight`), so each pass reports 3,877 frames. A video keeps the run short, because the PNG encode of each frame takes far longer than its draw, and the GPU times do not depend on the target.

1. In the checkout of the branch, run the script at 1080p:

   ```sh
   nix develop --command dotnet run --project src/Tertius.Demo -c Release -- --script scripts/demo-exit.json --headless --out captures/gpu-branch --size 1920x1080 --video captures/gpu-branch/run.mkv
   ```

   The run writes `captures/gpu-branch/gpu-timings.json` and prints one line per pass, such as `GPU G-buffer: mean 0.859 ms, median 0.861 ms, p95 1.086 ms over 3877 frames`.

2. Make a checkout of the base, the commit of main that the branch starts from, beside the repository, and run the same script there into its own folder:

   ```sh
   git worktree add ../tertius-base "$(git merge-base main HEAD)"
   (cd ../tertius-base && nix develop --command dotnet run --project src/Tertius.Demo -c Release -- --script scripts/demo-exit.json --headless --out "$OLDPWD/captures/gpu-base" --size 1920x1080 --video "$OLDPWD/captures/gpu-base/run.mkv")
   git worktree remove ../tertius-base
   ```

3. Compare the two files pass by pass: the difference of the means is the feature's cost per frame, and the 95th percentiles show its worst frames. A new pass appears only in the branch's file. Run each build twice, and trust a difference only when it is larger than the difference between the two runs of one build.

Run one measurement at a time, with no other build or test on the machine, because the GPU and the CPU are shared. The base must hold the GPU timings, so a branch that starts before them has no base to compare with.

## Out of scope

Reflection probes and image-based lighting. SSR reflects only what is on screen, and a ray that leaves the screen fades to the ambient light. The asset pipeline plan also names environment reflections as a later renderer plan.
