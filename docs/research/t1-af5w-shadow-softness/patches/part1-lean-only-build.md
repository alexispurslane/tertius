# The lean-only builds of 2026-11-23

Each lean-only binary is `part1-pyramid.patch` with these edits, built with
`dotnet build src/Tertius.Demo -c Release -o <bin> -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false`:

- `Shaders/shadows.glsl` is `part1-lean-only-shadows.glsl` (the shared lookup with the 25 gathers): the debug code is
  compiled out, and `tileShadow` takes the lean path always (`if (true)`). The floor build passes `false` for the reads.
  The separate-lookups build keeps `TERTIUS_PROBE_SHARED_LOOKUP 0` and passes `true`.
- `Shaders/shadow_bounds.comp`: the bounds image is `rg32f`, not `rgba32f`.
- `Frame/ShadowAtlas.cs`: `BoundsFormat = Format.R32G32Sfloat`.
- `Passes/LocalShadowPass.cs`: levels 0 and 1 only (`int levels = ProbePyramid >= 1f ? 2 : 1`), for the drawn tiles only
  (`if (false)` in place of `if (ProbePyramid >= 1f)`), as main builds level 0.

They run with `TERTIUS_X_PYRAMID=1095`, which turns the level-1 build on.
