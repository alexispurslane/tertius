---
id: ter-2k3b
status: closed
deps: []
links: []
created: 2026-10-24T17:07:50Z
type: task
priority: 2
assignee: Alexis Purslane
---
# ScriptCommand.Options: a union of free play and a script run

ScriptCommand.Run tests its options at the outer shell to tell free play from a script run (ter-2ku2 added the test to remove the ! operator). Make Options a union of the two cases, so that each case holds only its own fields. ScriptCommand.Run is also the sixth heaviest function by erosion mass (CC 27, 121 lines), so split it per case. Found by the style analyzer work.


## Design sketch (2026-10-24)

STYLE.md, "Make illegal states unrepresentable", uses this as its example. ReadOptions parses the arguments into the precise type, and Run switches on it with one method per case.

```csharp
// Sketch
internal union RunOptions(FreePlay, ScriptRun);
internal sealed record FreePlay(string Scene, double Speed, string? Record);
internal sealed record ScriptRun(string Script, string Out, ScriptDisplay Display);
internal union ScriptDisplay(InWindow, Headless);
internal sealed record InWindow(double Speed);
internal sealed record Headless(VideoFile? Video);
```

The UnreachableException in Run ("A script run has its script and its output folder") goes away, and so do the problem rows of ReadOptions that the type now refuses by shape: --speed with --headless, --video without --headless.

## Notes

**2026-10-24T19:39:05Z**

The user approved the design sketch above as it stands on 2026-10-24. Build from it. The forks that it lists stay open: design each one twice when the ticket starts, and let the user choose.

**2026-10-27T10:02:57Z**

2026-10-27: merged. RunOptions is a [GeneratedUnion] of FreePlayRun and ScriptRun; a ScriptRun's display is InWindow or Headless; the eight headless renderer overrides travel as one RendererOverrides. Coverage of the changed lines 226/235 (96.2%); the uncovered lines are the window runs and the null arms that STYLE.md requires. A command line with two or more problems can now report a different one of them first (strays before values); each line with one problem keeps its old message (47 lock-ins). The orchestrator renamed the case FreePlay to FreePlayRun, because Tertius.Host.Windowing.FreePlay has the same name.
