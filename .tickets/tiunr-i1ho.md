---
id: tiunr-i1ho
status: open
deps: []
links: []
created: 2026-11-08T19:49:01Z
type: chore
priority: 3
assignee: Alexis Purslane
parent: ter-icn3
---
# Name the shader variants from a table, not from file-name prefixes

Review finding Q3 of 2026-11-08. TemporalSignal.HistoryShader builds variant file names from strings, with a StartsWith("screen_trace_") rule. A new screen_trace_* kernel without a _lightagediag_precise variant fails only at run time, and only under the diagnostic. A table of the variants that the csproj compiles, checked by a test against the embedded resources, would fail at test time instead.

