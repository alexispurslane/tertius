---
id: ter-uhd8
status: open
deps: []
links: []
created: 2026-10-26T21:23:47Z
type: feature
priority: 2
assignee: Alexis Purslane
tags: [rendering, tools]
---
# A headless option that selects the far rays per texel (fork 2's option B) for captures and traces

Found in ter-izsu (2026-10-26): no headless option selects VisibilityBitmaskLight.FarRays, so the captures and traces of FarRays.OnePerTexel needed a scratch change of VisibilityBitmaskLight.Default. Part 7 of the bounce light (ter-7s3g) must capture and measure fork 2's options A and B on the same build. Extend --screen-space-light of ScriptCommand (for example '--screen-space-light <meters>[,per-texel]', or a separate --far-rays per-slice|per-texel), with the setting's check, and the usage line.

## Acceptance Criteria

dotnet run --project src/Tertius.Demo -- --script scripts/demo-exit.json --headless --out <dir> with the option captures option B, and the default run option A, on one build; tools/gpu-trace.sh can pass it; a ScriptCommand test covers the parse and the refusal of an unknown value.

