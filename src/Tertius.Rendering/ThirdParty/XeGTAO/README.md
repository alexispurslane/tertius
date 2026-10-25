# XeGTAO reference files

These files come from GameTechDev/XeGTAO (https://github.com/GameTechDev/XeGTAO), commit `a5b1686c7ea37788eeb3576b5be47f7c03db532c` of 2024-04-22, under the MIT license in `LICENSE`. The build does not compile them. They are the reference for the GLSL port of GTAO. The design is in `docs/plans/ambient-occlusion.md`.

| File | Source | Change |
|---|---|---|
| `XeGTAO.hlsli` | `Source/Rendering/Shaders/XeGTAO.hlsli` | Line ends changed from CRLF to LF. |
| `XeGTAO.h` | `Source/Rendering/Shaders/XeGTAO.h` | Line ends changed from CRLF to LF. |
| `vaGTAO.hlsl` | `Source/Rendering/Shaders/vaGTAO.hlsl` | Line ends changed from CRLF to LF. |
| `LICENSE` | `LICENSE` | None. |

The license asks that the notice goes with every copy and every substantial portion of the software. So the GLSL port carries the notice, and so does a distributed build of the game.

## Where the port lives

| File | Holds |
|---|---|
| `src/Tertius.Rendering/Shaders/gtao.glsl` | Every function taken from XeGTAO, with the copyright line and the MIT license in its header |
| `src/Tertius.Rendering/Shaders/occlusion_depth.comp`, `occlusion_horizons.comp`, `occlusion_denoise.comp`, `occlusion_upsample.comp` | Engine glue after the shape of `vaGTAO.hlsl`. Each names `gtao.glsl` as the holder of the notice. |
| `THIRD-PARTY-NOTICES.md` (repository root) | The notice for the distributed game. `Tertius.Demo.csproj` copies it into every build and publish output. |
