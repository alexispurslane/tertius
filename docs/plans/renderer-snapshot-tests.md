# Renderer snapshot tests

Ticket: `ter-rtxu`.

## Goal

Catch visual regressions in the renderer. A snapshot test renders a scene headlessly and compares the image with an approved reference image. The comparison uses FLIP, a perceptual image difference metric from NVIDIA. Everything else in the renderer that is C# keeps ordinary CPU tests.

## Decisions (from the user)

- Use FLIP, not a per-pixel compare.
- A failure writes the actual image and a FLIP heat map next to the reference image.
- An environment variable re-approves a reference image on purpose.
- Some agents add or re-approve a reference image, or see a snapshot failure. Each such agent opens the reference, the actual image and the heat map with the Read tool. Its report describes what it saw.
- The first approval of a reference image is a real review against the scene description, because a snapshot only proves that nothing changed.
- One switch in `RendererOptions` makes rendering deterministic for tests: interpolation alpha 1, fixed seeds, impostor captures forced or off, and no temporal effects.
- A snapshot test locks in a look, so it comes after the look is what we want. A snapshot cannot drive red-green TDD, because its reference image exists only after the feature works. So this epic waits for the rendering upgrade epic (`ter-sglu`, [rendering-upgrade.md](rendering-upgrade.md)), which holds every rendering feature that changes the look.
- The user decided this order on 2026-10-21.
- Automatic exposure comes first (`ter-zzyr`, [auto-exposure.md](auto-exposure.md)). The snapshot tests set `FixedExposure`, so the deterministic switch has no exposure of its own to turn off. A switch that overrode the setting would be a second store of it. Every other temporal effect that lands before this epic is still turned off by the switch.

## Steps

1. Add the deterministic-rendering switch to `RendererOptions`, test-first.
2. Add FLIP. Prefer a C# port or a small native build from the dev shell. Check the license first.
   Built in ticket `ter-721k`: `Flip` in `tests/Tertius.Rendering.Tests`, a C# port written from the paper. It uses no code from NVIDIA's reference implementation.
3. Write a snapshot test helper: render a scene description, compare, write the failure files, and honor the approval variable.
   Built in ticket `ter-721k`: `ReferenceImages.Verify`. If any pixel's FLIP value is above the tolerance, 0.1 by default, it fails. `TERTIUS_APPROVE_SNAPSHOTS=1` approves. ARCHITECTURE.md, "Snapshot tests", gives the details.
4. Write the first scenes. Each scene tests one feature:
   - A sun-lit cube with cascaded shadows.
   - An eclipse.
   - The seam between two depth ranges at planet scale.
   - The switch between geometry and impostor.
   - A point light's falloff.
   - A spot light's cone.
5. Review and approve each reference image, and describe each one in the report.

## Out of scope

Cross-GPU tolerance. The references come from this machine with MoltenVK.
