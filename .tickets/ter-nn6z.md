---
id: ter-nn6z
status: open
deps: []
links: []
created: 2026-10-22T01:24:31Z
type: epic
priority: 2
assignee: Alexis Purslane
tags: [tooling, analyzers]
---
# Union tooling: an analyzer and a source generator

An analyzer that steers closed sets of types to the right form: a custom [Union] struct for hot paths, a built-in union or sealed records elsewhere. It flags tagged structs (a Kind enum plus per-kind fields) and closed hierarchies. A source generator that writes a custom [Union] struct (kind tag, storage, constructors, implicit conversions, HasValue, TryGetValue) from a list of record types. First step: a design doc in docs/plans/ with two designs for the user to choose between (how the analyzer knows a type is hot, and how the generator stores the cases).

