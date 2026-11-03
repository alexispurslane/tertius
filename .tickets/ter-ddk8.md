---
id: ter-ddk8
status: open
deps: []
links: []
created: 2026-11-03T22:15:18Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [testing, rendering]
---
# GPU tests of two configurations write the same capture file at once

tools/test-all.sh runs the Checked and the Release test processes at the same time in one worktree. Gpu.CapturePath (tests/Tertius.Rendering.Tests/TestSupport.cs:92) puts every capture at <repo>/captures/<name>.png, so both processes write the same file. On 2026-11-03, during the merge of ter-ohqr into main, Checked's ImpostorTests.ACapturedImpostorLooksLikeTheGeometry failed with 'System.IO.IOException : The process cannot access the file .../captures/impostor-accuracy-tile.png because it is being used by another process', at ImpostorTests.SaveTile (ImpostorTests.cs:1197). Release passed the same test. The comment at TestSupport.cs:9 says that GPU tests share the captures folder and run one at a time, but that holds only inside one process. The class of bug: any test that writes a fixed path that a second test process also writes.

## Acceptance Criteria

Two test processes of different configurations never write the same capture path, for example a folder per configuration under captures/. Every reader of the captures (the docs, the reference image review and the tools) points at the new place.

