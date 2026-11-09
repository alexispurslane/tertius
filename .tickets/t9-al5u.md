---
id: t9-al5u
status: closed
deps: []
links: []
created: 2026-11-08T23:55:05Z
type: chore
priority: 2
assignee: Alexis Purslane
parent: ter-icn3
---
# Turn off Stryker's per-test coverage capture in the official run

User decision 2026-11-08. The official Stryker run of the tube light (tiunr-e8xk, run1) spent most of its time in the per-test coverage capture: Core 2,517 s of 2,674 s with 0 mutants, Simulation 3,674 s of 3,813 s with 10 mutants. One full test pass takes 28 s (Core) and 44 s (Simulation). With coverage-analysis off, every mutant runs every test, so a module's run is faster while it has fewer than about 80 mutants (2,517 / 28 = 90, 3,674 / 44 = 83). Changed-line runs have far fewer. Set 'coverage-analysis': 'off' in tools/stryker/config.py, keep the evidence lines of changed.py right for a log without the capture, and update tools/README.md and CLAUDE.md ('Testing').


## Notes

**2026-11-08T23:56:37Z**

Shares the branch t3/tiunr-e8xk-tube-light-stryker with tiunr-e8xk. This change has its own commit, before the tube light's test commit. Tool tests (red first): the official configuration writes coverage-analysis off, and the evidence text says 'no per-test coverage capture' for a log without one, but keeps 'no end mark' for a capture that started and never ended. tools/test_stryker.py: 89 tests pass.

**2026-11-09T00:32:15Z**

Result 2026-11-08: coverage-analysis off was slower, so the official run went back to perTest. Run3 (off, the tube light's lines, stopped after 30 minutes): Core's tests took 3 to 12 s for each mutant, but each mutant's run of the Simulation tests and of the Physics tests reached Stryker's limit (314 s and 342 s), so a Simulation mutant took about 11 minutes. Physics finished only 361, 326 and 79 of its 1,492 tests in those runs. Cause: the mutated assembly. Stryker compiled 1,175 mutant switches (of 5,870 created) for 10 mutants in the spans, and every test runs through them. Not the load: the initial run of every test took 33 s at the same nice value and a similar load. Not the explicit test list: the initial run sent one too. So the per-test capture of run1 (Core 2,517 s of 2,674 s, Simulation 3,674 s of 3,813 s, against plain passes of 28 s and 44 s) is slow for the same reason, and a crossover of about 80 mutants was wrong. Kept: the evidence text for a log without a capture, and its tests. Follow-up: t9-q4qe. The test of the setting was changed together with the setting, so it was not red first.
