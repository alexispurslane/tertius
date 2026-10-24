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


## Notes

**2026-10-24T20:55:01Z**

TS0015 built on branch ter-kke3-switch-statement-rule. Hits before the fix: 18 diagnostics in 12 switch statements (src: Core 1, Demo 2, Host 4, Physics 2, Rendering 1, Simulation 2; tests: Physics.Tests 2, Simulation.Tests 4; tools and benchmarks 0). No real missing case: every missing case was unreachable (Fly returns at once in Standby, Docked and Halted; CheckSteps and MakeReady leave no null step; the senders refuse an empty target) or fell to a default that did what the named case now does. Decisions: the cases of a union are the parameter types of its public one-parameter constructors, as the compiler reads them; ZeroCase comes from [GeneratedUnion]; a closed hierarchy is read from the closed modifier in source, so one of another assembly is out of sight with Microsoft.CodeAnalysis 4.14; a label names a case when its subpatterns match every value (var, discard, or the input's own type when the declared type cannot be null), as the compiler reads a switch expression; the default section may give UnreachableException a message. Survivors of the hand mutations: the two filters in ZeroCase(), equivalent because [GeneratedUnion] has one named property.
