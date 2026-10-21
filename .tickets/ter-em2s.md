---
id: ter-em2s
status: closed
deps: [ter-dtw2]
links: []
created: 2026-10-11T13:52:10Z
type: chore
priority: 2
assignee: Alexis Purslane
parent: ter-se84
tags: [tests]
---
# Add a test-all command that runs every test project in Debug and in Release


## Notes

**2026-10-21T00:02:55Z**

The user postponed the testing work on 2026-10-20 to focus on the demo. A finished, unreviewed tools/test-all.sh waits on branch ter-em2s-test-all at 32c1737, not merged. Review it before a merge when this ticket comes back.

**2026-10-21T00:04:55Z**

Reviewed and merged by the orchestrator on 2026-10-20, at the user's request. The review found one defect: a relative -o resolved from the repository root instead of the caller's folder. It is fixed in 1360b33, and the module check now runs before the log folder is made. CLAUDE.md names the command.
