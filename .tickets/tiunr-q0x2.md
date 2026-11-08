---
id: tiunr-q0x2
status: open
deps: []
links: []
created: 2026-11-07T15:30:42Z
type: bug
priority: 2
assignee: Alexis Purslane
---
# Diagnose dotnet test MTP handshake returning zero tests while standalone xUnit runs

Observed before and after the uncapped-default implementation: dotnet test --project tests/Tertius.Rendering.Tests -c Checked returns exit 5, zero tests and handshake failure, even without filtering. Discovery finds 2,436 tests, and the same assembly runs through standalone xUnit with real assertions. This also blocks the normal Microsoft.Testing.Platform coverage command. Preserve /private/tmp/tertius-uncapped-default-20261107/red.log, red-accumulator.log, checked-coverage.log and discovered-tests.log. Unrelated to the history policy change; do not waive it or report coverage as passed.

## Acceptance Criteria

The normal project test command executes discovered tests and the repository coverage command emits a valid report; a standalone full-suite result is retained as a control.

