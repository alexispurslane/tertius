---
id: ter-u63a
status: in_progress
deps: []
links: []
created: 2026-11-01T16:19:40Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [testing, tools]
---
# Try Stryker on the changed C# lines with only the intended test classes, in place of C# hand mutations

The user's idea (2026-11-01): a targeted Stryker.NET run on just the changed lines, running only the test classes that are supposed to cover them, with NoCoverage treated as survived, can cost fewer tokens and less time than an agent's hand mutations. tools/stryker already mutates only the changed lines (config.py), runs only the tests that cover each mutant (coverage-analysis perTest) and lists NoCoverage beside Survived (survivors.py). The missing part is the restriction to the intended test classes: Stryker.NET 5.0.0's MTP test runner has no test-case filter (only its VSTest runner has one). The trial builds that restriction as a scratch test project that compiles only the intended test files. Calibration: ter-pjza's changed Rendering lines at 0995e35f (base ef3b6ae5), whose hand mutations are known (27 mutants, 12 of them in C#, 37 minutes and about 140k tokens of one agent). Intended classes: the 8 test files that the branch changed. Known limits: Stryker mutates no GLSL (15 of ter-pjza's 27 hand mutants were in shaders), and it cannot put back a line that the branch removed (the independentBlend survivor came from such a mutant).

## Acceptance Criteria

A ticket note compares the Stryker run with the hand mutations on the same code: mutants, score, survivors and NoCoverage with their triage, wall time and orchestrator tokens, and which hand findings each method found or missed. It ends with a recommendation for the rendering tickets' hardening, which the user decides.

