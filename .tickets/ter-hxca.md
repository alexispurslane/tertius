---
id: ter-hxca
status: in_progress
deps: []
links: [ter-u63a, ter-773q]
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

## Notes

**2026-11-02T06:07:42Z**

Calibration and replay (continuation of 2026-11-01 evening; all runs in this worktree, no second checkout).

Official settings, from the runs below: concurrency 1 (Stryker.NET issue #3832), 4 xunit threads for the run through DOTNET_PROCESSOR_COUNT, Stryker at nice 20, perTest coverage, timeout-ratio 2, additional-timeout 300,000 ms, timeout share at most 25%. Stryker starts each MTP test host as `dotnet <tests>.dll --server --client-port <n>`, so no xunit option such as --max-threads reaches it. xunit's default thread count reads Environment.ProcessorCount: with DOTNET_PROCESSOR_COUNT=4, xunit printed "parallel mode = collections [4 threads]" against 10 threads without it.

| run | spans | concurrency | wall | capture | mutants | load mean / max | result |
|---|---|---|---:|---:|---:|---|---|
| cal-3 | ter-9xl7 Result.cs (Core) | 3 | 1,092 s | 990 s | seconds | 26.4 / 47.8 | 3 killed, 3 no-mutant |
| cal-6, official | ter-9xl7 Result.cs (Core) | 1 | 2,107 s | 2,017 s | seconds | 6.3 / 14.6 | 3 killed, 3 no-mutant, valid |
| cal-4 | ter-2y6i, 84d4a894b^1..84d4a894b (Simulation) | 3 | 3,094 s | 1,080 s | 1,938 s | 78.5 / 173.4 | 14 killed, 3 survived, 3 timeout |
| cal-7 | the same 16 spans | 1 | 8,656 s | 2,576 s | 5,951 s | 9.1 / 30.5 | 14 killed, 3 survived, 3 timeout |

Evidence of Stryker's tricks in cal-6 (official): one schemata build with 9 compile tries (8 failed, each removing the mutants that broke it), one initial test run of 4,593 tests in 26 s, 0 Dubious tests, and the 3 mutants ran a mean of 4.3 of 4,593 tests. cal-7: 10 compile tries of one build, one initial run of 4,534 tests in 35 s, 0 Dubious, a mean of 926 of 4,534 tests per mutant, no mutant ran all tests. So the ban on Stryker in subagents stays lifted, under the lock.

Load bound in ps: one test host at a time, nice 20, DOTNET_PROCESSOR_COUNT=4 in its environment. During the capture it used about 94% of one core. During cal-7's mutants it used 376% to 401% in most samples, with peaks to about 700% for some seconds. The peaks come from Jolt's native workers: `new JobSystemThreadPool()` sizes them from the hardware. Filed ter-773q.

#3832 check: tools/stryker/compare.py on cal-7 (concurrency 1) and cal-4 (concurrency 3) finds the same result for all 20 mutants. One run at concurrency 3 does not settle the fault, so CONCURRENCY stays 1. The 3 timeouts (1980 FrameReads.cs:44, 5246 World.Sync.cs:245, 4486 World.cs:423) repeat at a load of 9 and of 173, so these mutants make a test hang, and a hang is a kill. That is why the share is 25%: the 5% of the earlier run made a quiet, correct run NOT VALID. The ter-u63a trial had 65 of 76.

Wall time: the capture runs each test alone in one host, so it takes about 34 to 43 minutes for these 4,534 to 4,593 tests, whatever the size of the change. It was 96% of the official Core run. A hot-path mutant of Simulation takes about 10 minutes, because its Physics and Simulation tests run until Stryker's limit (about 335 s and 300 s). Shorter runs need a Stryker release that fixes #3832 (the capture spreads over the hosts), fewer test projects, faster cores, or a smaller additional-timeout (the capture needs 80 to 90 s for DeterminismTests).

Stryker against ter-9xl7's hand table (M1 to M10, all killed by hand): Stryker made and killed M3, M6 and M7 (lines 44, 66, 76). Lines 88, 100 and 112 (M8 to M10) had only mutants that did not compile, so they are no-mutant rows for the tools/mutate list. Stryker made no mutant at all on lines 26, 33, 50 and 51 (M1, M2, M4, M5): it has no mutator for an enum value in an assignment or for a switch arm's result. changed.py now lists such lines under the table: on Result.cs, 10 lines, among them all 4. tools/mutate killed M1 to M10 (/tmp/hxca/9xl7-mutate).

Replay of ter-rjts (71e6428b's table) with tools/mutate on this branch, whose src and tests equal the base: 15 mutants: 10 C# and GLSL mutants killed as in the table (C01, C03, C06, C08, S01, S02, S09, S10, S25, S28) and 5 survivors as in the table (C09, C10, S07, S27, S33). The GLSL kills S01, S02, S09, S10, S25 and S28 show that a shader mutant reaches the test run. C10 first gave build-failed, because the list's replacement did not compile; with `_ = range;` it survived as in the table. Mutants took 5 to 52 s; the three runs took 763 s with 7 baselines.

Also found: a finished Stryker run leaves its mutated module assembly in the test projects' bin (Tertius.Core.dll fbacac against the build's 578dba). changed.py now rebuilds the test projects after each module's run; after cal-7, every copy matched the build again.
