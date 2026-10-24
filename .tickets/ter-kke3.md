---
id: ter-kke3
status: open
deps: [ter-ghts]
links: []
created: 2026-10-24T19:35:50Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [analyzers]
---
# Style analyzer: a switch statement over a union names every case

Add a rule to analyzers/Tertius.Style: a switch statement over a closed set names every case. The C# compiler checks a switch expression for missing cases but not a switch statement, and IDE0010 covers only enums. So today a new union case can slip past every switch statement over that union.

The rule covers a switch statement whose governing type is a [GeneratedUnion] struct, a built-in union, or a closed record hierarchy. Each case of the set needs a section with a label that covers the case: an unguarded type pattern of the case, alone or in an `or` pattern. A guarded label does not cover its case. A union without a ZeroCase also needs a `null` label, as a union switch expression ends in a `null` arm today (STYLE.md, "Switches"). A default section, if present, holds only `throw new UnreachableException()`.

The diagnostic names the missing cases. Count the hits in today's code first, report them, and fix them in the same branch, with names for the quiet cases as Carry.cs does. Update STYLE.md "Switches" and ARCHITECTURE.md "Switches over closed sets" to name the new rule. Tests and hand mutations as for the other TS rules.

