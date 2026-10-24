---
id: ter-2ku2
status: open
deps: []
links: []
created: 2026-10-24T16:12:09Z
type: task
priority: 2
assignee: Alexis Purslane
---
# Style analyzer: named tuple elements, named arguments past 3 parameters, no ! in src

A new analyzer project analyzers/Tertius.Style, referenced from every project like Tertius.Unions, with three error rules from STYLE.md: TS0001 a tuple type with an element that has no name; TS0002 a call or object creation whose target has more than 3 parameters, where an argument has no name (arguments that bind to a params parameter are exempt); TS0003 the null-forgiving operator ! in src. Generated code is never analyzed. TS0001 and TS0002 cover src and tests, TS0003 only src. A code fix for TS0002 names the arguments, so that dotnet format can fix the existing code. The same branch fixes every existing hit, so that the solution builds clean.

