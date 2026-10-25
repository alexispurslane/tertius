---
id: ter-muln
status: closed
deps: []
links: []
created: 2026-10-24T23:52:12Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [analyzers]
---
# Style analyzer: no discard arm in a switch expression over a closed set, with a fix, and IDE0066 back on

The user wants IDE0066 (switch statement to switch expression) in the cleanup. Its fix carries the default section of a switch statement, throw new UnreachableException(), over as a discard arm. Over a union the compiler rejects that arm as unreachable (CS8510). Over an enum it compiles and hides a missing member from CS8509. STYLE.md, Switches, already forbids a discard arm over a closed set, but no rule enforces it for switch expressions. Add TS0016: a switch expression whose input is a [GeneratedUnion] struct, a built-in union, a closed record hierarchy or an enum has no discard arm (_ or var _ without a guard). Add a code fix that deletes a discard arm whose only content is a throw of UnreachableException, so that dotnet format applies it in the pass after IDE0066. Then set IDE0066 back to suggestion, run the cleanup on every file, and make sure that the build passes and that a second run changes nothing. IDE0066 was turned off on 2026-10-24 as the quick fix.

