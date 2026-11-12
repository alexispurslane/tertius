---
id: ter-zkdz
status: open
deps: []
links: []
created: 2026-11-12T02:50:00Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [linux]
---
# Four tests hold one architecture's results: make them check the same result twice on one machine

The user's rule (2026-11-12): determinism holds on one computer, not across computers or architectures. A different result on x86-64 than on arm64 is not a physics bug, so do not chase the difference.

On the RTX 2000 Ada pod of ter-libf (x86-64, .NET 11 rc.1, 2026-11-12), four tests that pass on the Mac (arm64) fail, because each compares with a value that the Mac recorded:

- `PuffLifeTests.TheSameRunGivesTheSamePuffsBitForBit`: TableHash 4386313748531336162 instead of 13950460896152137838.
- `PuffSnapshotTests.TheSameRunGivesTheSameSnapshotPuffsBitForBit`: 1622472378787088234 instead of 2111527910242662902.
- `DemoExitScriptTests.TheCheckedInScriptIsWhatTheDemosCommandLineGeneratesForTheExitsScene`: a film of 733 frames where scripts/demo-exit.json has 740.
- `BlueDanubeScriptTests.TheCheckedInScriptIsWhatTheDemosCommandLineGenerates`: a film of 740 frames where scripts/blue-danube.json has 733.

The fix: each test checks what the rule requires, the same result from two runs on the same machine. A checked-in hash or script is a reference for the architecture that recorded it. On another architecture, the test compares two runs of its own, or skips with a reason that names the recording architecture. Logs: /private/tmp/ter-libf/pod-out/tests-checked.log.
