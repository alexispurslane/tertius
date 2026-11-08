---
id: t9-al5u
status: in_progress
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
