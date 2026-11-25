---
id: t10-ck8w
status: closed
deps: []
links: [t58-rcd6, t10-ogon]
created: 2026-11-23T17:21:52Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, exposure]
---
# After a cut in a film or a window, the automatic exposure adapts at its own rate from the view before the cut

**2026-11-25 · Folded into ter-fl8r.** The 28.60 s cut now supplies the missing production reproduction. Compare the first exterior frames against a settled exposure at the identical pose.

Found by the list of eye-dependent stores of t58-rcd6 (ARCHITECTURE.md, "The frame history"), not seen in a picture. The exposure meters what the eye sees. Only a headless draw at a step of 0 or a long step meters anew with an endless step (docs/plans/auto-exposure.md, the metering draw). A cut at a film step, or in a window, keeps the exposure of the view before the cut, and it adapts at its rate: a cut from the sunlit exterior into the cabin starts too dark, and the reverse too bright. Decide first whether a cut should meter anew (as a film cut does) or adapt (as an eye does); the user's bar is a picture without a visible fault after a cut.

