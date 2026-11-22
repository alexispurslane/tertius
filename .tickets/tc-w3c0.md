---
id: tc-w3c0
status: open
deps: []
links: []
created: 2026-11-22T10:24:18Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# The survey's probe patch does not apply on the line's head after the region's remedy

tools/renderer-survey/probe.patch does not apply to the line's head 6d8a68332: 'patch -p1' rejects its hunks in src/Tertius.Rendering/Scene/GpuTypes.cs and src/Tertius.Rendering/Shaders/accumulate.glsl (t0-9fo0 part B, tools/build.sh, 2026-11-22). The remedy (t0-acxm) added the push constant regionSlot after retainedSigmas and an argument drawIndex to AccumulatePushConstants.Of, where the probe's two hunks insert its own push constants and rename Of to OfParameters. So 'renderer-survey.py probe-build' of the line's head fails. The remedy's lead fitted the patch as docs/research/t0-acxm-region-evidence/probe-acxm.patch (it passes drawIndex through OfParameters and puts the probe's push constants after the remedy's), and t0-9fo0 part B builds X with that patch. The fix: bring probe-acxm.patch's two hunks into tools/renderer-survey/probe.patch on the line, so that 'renderer-survey.py probe-build' of the line's head gives the build of the merge decision. Found by the room lead, 2026-11-22.

## Acceptance Criteria

renderer-survey.py probe-build of the line's head builds, and its still rect-lamps equals the build from probe-acxm.patch byte for byte.

