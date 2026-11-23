---
id: t10-ogon
status: open
deps: []
links: []
created: 2026-11-23T17:21:52Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, impostors]
---
# After a cut, an impostor that was drawn in the last frame keeps its capture of the old view until its recapture

Found by the list of eye-dependent stores of t58-rcd6 (ARCHITECTURE.md, "The frame history"), not seen in a picture. ImpostorManager.QueueCapture keeps a stale capture drawable when the impostor was drawn in the last frame (state.DrawnFrame == Frame - 1), until a recapture, at most CapturesPerFrame per draw. A cut jumps the eye, so every impostor that the view before the cut drew and the view after it draws is stale at once, and it shows the old view direction until its recapture. A stale capture should not stay drawable across a cut: the pending frame's cut (FrameHistory.CutsFrom) could make every capture wait as geometry, as an impostor that comes back into view does. Check first with a film that cuts between two views of the same impostor: a headless run has impostors off, so it needs a window run or a test.

