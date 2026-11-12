---
id: ter-zkdz
status: closed
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

## Notes

**2026-11-12T16:15:53Z**

The fix (branch ter-zkdz-same-machine-checks). A new helper, `RecordedReference` in tests/Tertius.Testing, says whether this process runs on the architecture that recorded the checked-in results (arm64), and gives the skip reason, which names arm64 and this machine's architecture.

- The two puff tests compare two runs first, as before, and check the errors. Then they call `Assert.SkipUnless(RecordedReference.Applies, RecordedReference.SkipReason)` and compare with the pinned hash on arm64 only.
- The two script tests generate the scripts twice, into two folders, and compare the two runs file by file (blue-danube.json with every cut). Then they skip off arm64, and on arm64 compare with the checked-in scripts as before.
- ARCHITECTURE.md, "Invariants and determinism" of the puffs, now says that the same run gives the same puffs on one machine, and that the pin applies on arm64.

Red and green: the red run is the x86-64 run of ter-libf, where all four failed on the pinned values. On the Mac all four still compare with the recorded values and pass. A script generation takes about 30 s, so the two script tests now take about a minute together.
