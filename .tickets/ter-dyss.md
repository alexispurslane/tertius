---
id: ter-dyss
status: open
deps: [ter-tf17]
links: []
created: 2026-10-27T17:20:51Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-9gj5
---
# Class metrics: cohesion, interface size, data classes, constructors and missing interfaces

The user's request (2026-10-27): measure the complexity of types, not only of functions: how many members a type shows, how its fields relate to its methods, public fields, properties that only get and set, constructors that do too much, and behavior that several types repeat and that one shared type or, preferably, an interface could hold.

Sources: Chidamber and Kemerer 1994 (IEEE TSE 20(6)), Hitz and Montazeri 1995 (LCOM4), Bieman and Kang 1995 (TCC), Lanza and Marinescu 2006, "Object-Oriented Metrics in Practice" (NOPA, NOAM, WOC, and the God class and data class detection rules).

The proposed home of each metric. The split: the style analyzer (analyzers/Tertius.Style) holds a finding on one type that needs exact symbols and says what to change. Tertius.Slop holds numbers that need the whole repository or Slop's own CC (the erosion's), and keeps their history in refs/notes/slop. Configuration covers a rule that the .NET SDK already has.

| Metric | What it counts | Home |
|---|---|---|
| Interface size (NOPM) | The visible methods, properties and events of a type, plus the parameters of each | Style analyzer |
| LCOM4 | The separate groups of a type's methods, where two methods link when they use one field or one calls the other. Above 1, the finding names the groups to split. | Style analyzer |
| TCC | The share of pairs of visible methods that use a field in common, 0 to 1 | Style analyzer |
| Public fields (NOPA) | Visible instance fields of a class | Configuration: CA1051 with its exclude_structs option, if the struct-of-arrays tables are structs. Otherwise the style analyzer, with the plain data structs and the tables left out. |
| Plain accessors (NOAM) and WOC | Properties that only get and set, and the share of visible members that do real work. Low WOC is the data class pattern. | Style analyzer, only on a type that has methods: a record or struct of plain properties is a payload, which is data by design. |
| A public setter on a type with methods | A boundary whose stock any caller can change | Style analyzer |
| Constructor complexity | The CC of a constructor | Already measured: the erosion, since ter-tf17 turned CA1502 off |
| Constructor parameter count | The parameters of a constructor | Style analyzer |
| A guard that can move into a setter | A constructor check that reads only one parameter. A check that reads two or more stays. | Style analyzer |
| WMC | The sum of the CC of a type's functions | Tertius.Slop, so that one CC definition serves both the erosion and WMC |
| Depth (Ousterhout) | Implementation SLOC divided by interface size | Style analyzer, beside the interface size that it divides by |
| CBO | The number of distinct types that a type uses | Configuration: CA1506, its limit in analyzers/CodeMetricsConfig.txt. ter-tf17 removed that file and its AdditionalFiles entry in Directory.Build.props with CA1502, so this ticket adds both again. |
| Inheritance depth (DIT) | The number of base types above a type | Configuration: CA1501 |
| Textual clones | Repeated blocks of 70 tokens | Already measured: jscpd, and the verbosity |
| Missing interface | Types in any project that share 3 or more public method signatures (name, parameter types, return type) and no interface | Tertius.Slop, because it needs every project at once |

## Design

Not settled. The table above is the orchestrator's proposal. By the user's design rule, this ticket starts with two designs in the chat, which the user hybridizes. The points that the designs differ on include at least: the split between the analyzer and Slop (an exact semantic home for every type metric, or Slop's syntax-only reading for all of them, with name matching for the fields), the limits of each finding, and whether the commit report gets a section for types.

By the software-design skill: interface size is counted in one place only, and CC has one definition. ter-tf17 settled that: it turned CA1502 off, so only Slop counts CC, and this ticket depends on it.

The findings go to the commit report the way MA0051's do (tools/commit_report.py, report_length): suggestions in obj/Checked/<framework>/analyzers.sarif, filtered to the types that a commit touches. They never stop a commit. ter-tf17 removed the CA1502 list of the report, so MA0051's list is the model.

## Acceptance Criteria

- Each metric of the table has a home, as the designs settle, with tests of its counting on small example types.
- A commit that touches a type over a limit sees the finding in the commit report.
- tools/README.md and docs/plans/code-quality-tooling.md give each metric, its definition, its source and its limit.

