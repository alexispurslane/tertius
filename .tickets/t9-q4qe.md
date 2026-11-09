---
id: t9-q4qe
status: open
deps: []
links: []
created: 2026-11-09T00:32:01Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-icn3
---
# Keep the mutants outside the spans out of Stryker's compiled assembly

Found in t9-al5u on 2026-11-08. On tiunr-e8xk's run3 of Simulation, Stryker created 5,870 mutants and compiled 1,175 of them into the mutated assembly ('Found mutant ... controlled by ConditionalInstrumentationEngine'), for 10 mutants in the changed-line spans. Every test runs through those switches, so the per-test coverage capture took 3,674 s against 44 s for the plain initial run, and a mutant run of every test took about 11 minutes. Find out which files the compiled mutants come from, and whether a Stryker.NET 5.0.0 option (mutate patterns, mutation-level, ignore-mutations) or a narrower project keeps them out. If it does, measure the capture and the mutant runs again, and reconsider coverage-analysis off (tools/README.md, 'Run time').

