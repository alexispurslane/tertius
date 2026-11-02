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

**2026-11-01T23:59:57Z**

2026-11-01: workflow run 7fcc4fb2-fde1-468c-b565-adf99b9497ea (version 869b0b07), branch ter-hxca-mutation-runners from d636add2. Settled by the orchestrator before the launch: the mutant list is JSON; Stryker's test projects are the module's own plus those that reference it, never Tertius.Rendering.Tests; NoCoverage counts as survived and a timeout as killed, each listed apart; a machine-wide lock /tmp/tertius-stryker.lock; the concurrency, the timeout and the share of timeouts that makes a run invalid come from a measured calibration. Brief: /tmp/briefs/hxca.txt.

**2026-11-02T00:01:47Z**

2026-11-01: run 7fcc4fb2 was stopped a few minutes into its build (cleanup complete, the worktree clean at d636add2) to add the user's condition: the ban on Stryker in subagents goes once the official run proves Stryker's own speed tricks, one compile through the mutant schemata, one initial test run, and each mutant scoped to the tests that cover it, with no Dubious tests. Continuation run ae26bb05-064e-4b63-b662-08ef1de51808 (version 869b0b07) on the same worktree, base and branch. Brief: /tmp/briefs/hxca2.txt.

**2026-11-02T02:05:37Z**

2026-11-01 21:06, orchestrator: during calibration cal-4 (Simulation, concurrency 3, all three test projects), the 1-minute load reached 173 on this 10-core machine, and the user saw 142. Six test hosts ran at once, each with xunit's default parallel threads. The orchestrator set nice 20 on the Stryker run and its test hosts (pids 84186, 84260, 84264, 84265 and their children), so cal-4's wall times are not a quiet-machine measurement. For the review and the fix: the official run format must bound the threads, not only the Stryker concurrency, for example xunit maxParallelThreads per test host times the concurrency at most about 4, and must start Stryker at a low priority (nice), so that a run never starves the builders and the GPU captures.

**2026-11-02T02:56:44Z**

2026-11-01 22:00, orchestrator: stopped run ae26bb05 during its calibration cal-5 and started continuation run 12e83b59-40f0-4274-a1ce-1df5a6fd3a04 (same branch and worktree, brief /tmp/briefs/hxca3.txt). Reason: ter-fz34's research found Stryker.NET issue #3832 (open): 5.0.0 under-reports kills nondeterministically under MTP at a concurrency above 1, so the calibrations at concurrency 3 and 2 are suspect. The continuation settles the official run at concurrency 1 unless per-mutant outcomes at a higher concurrency match concurrency 1 in two runs, bounds the test threads (concurrency times xunit threads at most 4) and starts Stryker at nice 20. The orchestrator stopped cal-5's orphaned processes with SIGTERM; run.sh's trap released the Stryker lock. The orchestrator's renice watcher is stopped, so the tool must lower its own priority.

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

**2026-11-02T07:53:37Z**

Fix stage of the review (2026-11-02). Each finding and its resolution:

1. survivors.unmutated_lines counted an Ignored mutant that a Stryker filter removed, over every line it spans. Now a mutant counts only for its first line, and a mutant whose reason starts with "Removed by " counts for no line. A mutant that an ignore comment disabled still counts. On ter-9xl7's Result.cs, the list now has 12 lines, with 64 and 74.
2. Analyzer lines had no route. CLAUDE.md and tools/README.md send them to tools/mutate.py, with tests/Tertius.Unions.Tests or tests/Tertius.Style.Tests. tools/mutate killed A01 (analyzers/Tertius.Style/InheritanceDepth.cs:31, `<=` to `<`) in 23 s (/tmp/hxca/analyzer-1).
3. The lock and the process settings now live in tools/stryker/run.py, the only script that starts Stryker. It takes the lock, starts Stryker at nice 20 with DOTNET_PROCESSOR_COUNT from the configuration's concurrency, and builds the test projects again. run_module refuses to start without the lock of this process. A lock folder without an owner file for 60 s stops the run with the rm command. run.sh is gone, and the README recipe is one command.
4. The hand list is now the complement of MODULES: every changed C#, shader stage or shader include file under src/ and analyzers/ outside the C# of Core, Simulation and Physics, with its removed places. A dry run on 7d71ce8c6 lists its GLSL lines.
5. applied() retries the restore when a signal arrives as the restore starts, and raises the first signal after the file holds its bytes.
6. A timeout now counts as a kill only if the 1-minute load stayed at or below 17.5 per core (cal-4 repeated cal-7's timeouts at 173.4 on 10 cores). The share stays at 25%, because cal-7's quiet run had 3 real hangs of 20 (15%), and a lower share would reject that valid run.
7. No-mutant and removed rows have the path in the id: Core-Unions/Result.cs-L88.
8. mutate.project_of stops at the repository root, and validate() reports a file that belongs to no project.
9. tools/processes.py holds run_logged, Stopped and stop_on_signals, and mutation_table.short_test_name formats the killed-by names.
10. The triage has three answers: a test, an equivalent reason, or a judgment call (the test-hardening skill).
11. A build of the worktree during an official run: the docs state the rule, and run.py checks it. Correction of the earlier note: Stryker.NET 5.0.0 does not leave its mutated assembly. It keeps the plain copy as Tertius.Core.dll.stryker-unchanged and copies it back about 1 s before its Time Elapsed line. So run.py checks each copy for Stryker's helper class MutantControl in every 15-s sample between the "Injected the mutated assembly file into" line and the Time Elapsed line, and a plain copy in two samples in a row makes the run not valid. Probes with Unions.Tests only and 2-s samples: a quiet run had 6 samples in that time with no plain copy; a run with `dotnet build tests/Tertius.Unions.Tests -c Checked` at the injection had 6 samples with the plain copy and printed NOT VALID.

Gaps of the review:
- A real SIGINT during the dotnet test step of A01 (Python with the default SIGINT handler) killed the test process group, restored the file, rebuilt the project and exited with 1 (/tmp/hxca/analyzer-int).
- The thread bound is now saved: run.py writes processes.log, and the evidence line gives the CPU of all test hosts of one sample together. Stryker starts one host per test project at once but runs one at a time.
- The final code ran end to end. cal-9 (official, `changed.py 'fde41c1a2^1' /tmp/hxca/cal-9 --head fde41c1a2`): valid, 2,155 s wall, coverage capture 2,056 s (95%), 9 compile tries of one schemata build, 1 initial test run of 4,593 tests in 24 s, 0 Dubious tests, 3 mutants with a mean of 4.3 tests, 1-minute load mean 5.4 and max 12.5, test hosts at nice 20 and together at most 276% and on mean 114% of one core. The table equals cal-6: 3 killed, 3 no-mutant. The lock was gone after the run.
- cal-8 first stopped in Stryker's initial test run, because DeterminismTests.TheRunOnEightWorkersReallyRunsGameplayOnSeveralThreads failed (it passed in cal-6, cal-7 and the next try). Filed ter-hn3d. The lock was released after that failure.
