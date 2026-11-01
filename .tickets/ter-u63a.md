---
id: ter-u63a
status: closed
deps: []
links: [ter-hxca]
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


## Notes

**2026-11-01T18:52:38Z**

Trial result (2026-11-01, orchestrator). Code: ter-pjza at 0995e35f in its worktree. Stryker.NET 5.0.0, MTP runner, perTest coverage, concurrency 2, 79 changed-line spans, test project = a scratch csproj that compiles only the 8 intended test files (AssemblyName Tertius.Rendering.Tests, xunit maxParallelThreads 1).

Runs: the first run (concurrency 5) drove the 1-minute load to about 233 and disturbed another ticket's GPU trace, so I stopped it after 10 min. The second run took 21 min 58 s. Each test host used about 4 cores, so the load stayed near 100 with two runners.

Result in the spans: 76 mutants. 11 Killed, 65 Timeout, 2 RuntimeError, 0 Survived, 0 NoCoverage, 0 that did not compile. Stryker prints a score of 100 %, because it counts a timeout as a kill. That score is not evidence: the per-test coverage capture timed out for most tests (the log marks them Dubious), so most mutants ran all 37 to 51 intended tests, and those runs passed Stryker's limit (2 x the initial time + 10 s) under the load. The 65 timeouts include 9 string mutations of Vulkan debug names and many removed statements in pass setup. No test can see a debug name, so under a quiet machine these would most likely be survivors to triage, not kills.

Against the hand mutations on the same code (27 mutants, 25 killed, 37 min, about 140k tokens of one agent): Stryker mutates no GLSL, so it could not make the 15 shader mutants, which include both hand survivors (the writePixel guard and the insideHalf guard). Of the C# lines, the 11 real kills are the blend refusal in GraphicsPipelineDesc and the fragment-stores feature check in VulkanDevice, which the hand table also killed. Orchestrator tokens for the trial were not measured exactly; the scratch project, the stopped run and this triage took several orchestrator turns.

Recommendation: do not use Stryker in place of hand mutations for Rendering. Its GPU tests are slow and depend on the load, so most mutants end as timeouts, and the shaders, where most of the risk is, stay out of reach. Stryker on changed lines stays useful for Core, Simulation and Physics, whose tests are fast and CPU-only. A rerun on a quiet machine with a larger additional timeout can settle the 65 timeouts, at about 20 to 40 min of machine time. The user decides.

**2026-11-01T23:57:17Z**

The user's decision (2026-11-01, after the trial note above): the rendering tickets' hardening keeps mutants that the agent chooses, for C# and GLSL alike, but a new batch script runs them, so the agent writes one list, runs one command and triages the survivors, instead of 3 to 5 tool calls per mutant. Work that is not rendering uses Stryker on its changed lines (tools/stryker). The script is ticket ter-hxca.
