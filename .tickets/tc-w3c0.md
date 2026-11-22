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


## Notes

**2026-11-22T11:19:34Z**

Fixed on the line's branch by the acceptance lead of t0-5tyj (2026-11-22), not closed. tools/renderer-survey/probe.patch is now the git diff of probe-acxm.patch applied to the line's head 57b4045ae: the same probe, with drawIndex passed through OfParameters and the probe's push constants after the remedy's. It applies to 57b4045ae with patch -p1 and no fuzz, and the patched tree equals the tree from probe-acxm.patch file for file. Acceptance: renderer-survey.py probe-build of 57b4045ae builds, and its still rect-lamps (image, reference and result) equals the build from probe-acxm.patch byte for byte (specks 0.009645, blotch 0.010206). The patch no longer applies to trees before the remedy (t0-acxm), such as main c4c1f1f2f, which keep their own probe.patch.
