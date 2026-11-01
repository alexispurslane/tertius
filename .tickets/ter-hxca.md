---
id: ter-hxca
status: open
deps: []
links: [ter-u63a]
created: 2026-11-01T23:57:17Z
type: task
priority: 1
assignee: Alexis Purslane
tags: [testing, tools]
---
# The mutation step of hardening: tools/mutate runs listed mutants, and one official Stryker run covers the code outside rendering

The user's decisions of 2026-11-01 (ter-u63a and the conversation after it):
- Rendering work (Tertius.Rendering's C# and its GLSL, whose tests run on the GPU) keeps mutants that an agent chooses, but a script runs them. Today an agent runs each hand mutation itself (edit, build, test, restore, touch): 3 to 5 tool calls per mutant, about 140k tokens for ter-pjza's 27 mutants. With the script, the agent writes one list, runs one command, reads one table and triages the survivors.
- Work outside rendering uses Stryker on its changed lines, and it needs an official run format: one command, fixed settings, one report and one triage rule, so that every hardening brief can name it.

The one model: a ticket's mutation step makes one table of mutants with one result each. Stryker generates and runs the mutants for the changed C# lines outside rendering. The agent lists the mutants that Stryker cannot make: every rendering line in C# and GLSL, lines that the branch removed, and lines where every Stryker mutant failed to compile. tools/mutate runs that list. Both tools print the same table, and the agent triages every survivor the same way: a test that kills it, or a one-sentence reason why it is equivalent.

## Acceptance Criteria

1. tools/mutate runs a list of mutants (C# and GLSL), one at a time, as CLAUDE.md's Testing section says, restores every file, and prints the shared table. Its pure parts have tests. A replay of at least 10 of ter-rjts's hand mutations (commit 71e6428b's message), C# and GLSL, with at least one survivor, gives the same results on main.
2. One command runs Stryker on a branch's changed lines outside rendering with the official settings, under a machine-wide lock, and prints the shared table, with timeouts, NoCoverage and lines without a compiled mutant listed apart. A calibration run on a recent ticket outside rendering with a known hand-mutation table reports wall time, load, results and what each method found or missed.
3. tools/README.md documents both, and CLAUDE.md's Testing section states the decision and the procedure, so that a hardening brief only names the commands.
