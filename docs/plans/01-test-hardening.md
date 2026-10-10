# 01 Test hardening

Status: planned. It starts after the refactor workflow `wf_231d1c74-922` finishes.

## Goal

Make sure that the tests of every module really test the behavior that they claim to test. The orchestrator (the top-level Claude session) does this work itself. Implementation agents never run coverage or mutation testing.

## Decisions

- One coverage or mutation run at a time, never two in parallel. (User)
- Mutation thresholds: 80 high, 70 low, break at 70. (The test-hardening skill)
- Coverage is a map, not a goal. The floor is 90 percent of lines on core logic.
- A surviving mutant gets a test that kills it, or a written reason. Never ignore one silently.

## Steps

1. Make sure that the workflow output builds with 0 warnings in Debug and Release, and that every test project passes.
2. Commit the results (see the chores in the README).
3. Find out why Stryker took 30 to 40 seconds per mutant in an earlier run. The MTP runner can keep the test host alive across mutants. Fix the cause and measure seconds per mutant before and after.
4. Write `stryker-config.json` for Simulation: the `mtp` test runner, `perTest` coverage analysis, and the thresholds above.
5. Run coverage for Simulation. Read the uncovered lines file by file. Add tests through public behavior, delete dead code, or comment why defensive code stays.
6. Run Stryker for Simulation. Triage every survivor and every mutant with no coverage, as the test-hardening skill says.
7. Lock in the decisions from the CLAUDE.md table and ARCHITECTURE.md. Test the behavior that depends on each decision, not the constant.
8. Run a seeded chaotic workload with `WorldOptions.DebugChecks` on, for a long soak. Report the exposure, the failures and the MTBF (mean time between failures).
9. Repeat steps 4 to 7 for Physics, Core and the CPU parts of Rendering.

## Report

The report gives exact numbers: line and branch coverage before and after, and the mutation score with the killed, survived, timeout and no-coverage counts. It lists every equivalent mutant and every judgment call with its reason, and every bug found with its regression test.
