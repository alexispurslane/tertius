---
id: ter-2ku2
status: closed
deps: []
links: []
created: 2026-10-24T16:12:09Z
type: task
priority: 2
assignee: Alexis Purslane
---
# Style analyzer: named tuple elements, named arguments past 3 parameters, no ! in src

A new analyzer project analyzers/Tertius.Style, referenced from every project like Tertius.Unions, with three error rules from STYLE.md: TS0001 a tuple type with an element that has no name; TS0002 a call or object creation whose target has more than 3 parameters, where an argument has no name (arguments that bind to a params parameter are exempt); TS0003 the null-forgiving operator ! in src. Generated code is never analyzed. TS0001 and TS0002 cover src and tests, TS0003 only src. A code fix for TS0002 names the arguments, so that dotnet format can fix the existing code. The same branch fixes every existing hit, so that the solution builds clean.


## Notes

**2026-10-24T17:01:22Z**

Built on branch ter-2ku2-style-analyzer. Rules TS0001-TS0003 are warnings (an error through TreatWarningsAsErrors), per the user's change. Hits before the fix (Checked build): TS0001 39 (src 5, tests 34), TS0002 6753 (+73 in Release-only code; src 1195, tests 5472, benchmarks 73, analyzers/Tertius.Unions 13), TS0003 123 (all src: Physics 47, Simulation 28, Rendering 20, Host 14, Demo 12, Core 2). Decisions: params parameter does not count toward the 3 and its arguments stay positional; attributes are calls; function pointer and dynamic calls bind no method symbol and are not reported; TS0001 covers written tuple types and every declaration with an inferred type (var, foreach, out var, patterns, lambda parameters), names that C# infers from a literal count. TS0003 scope through .editorconfig. One suppression: FrameContext's four per-frame values (#pragma with reason). Follow-ups worth a ticket: BodyRecord's primary state as a union instead of nullable World/Character fields guarded by contracts; ScriptCommand.Options as a union of free play and script run.
