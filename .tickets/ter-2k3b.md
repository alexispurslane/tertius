---
id: ter-2k3b
status: open
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
