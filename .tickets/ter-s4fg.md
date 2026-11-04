---
id: ter-s4fg
status: in_progress
deps: []
links: []
created: 2026-11-04T20:39:56Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
---
# Machine locks: one record format and one liveness test for the GPU lock and the Stryker lock

Found in the review of ter-r88x on 2026-11-04. tools/gpu-lock.sh (bash) and tools/stryker/run.py Lock (Python) are two directory locks with an owner record, each with its own identity test and stale policy. The GPU lock records pid, UTC ps start time, token and label, and a waiter clears a stale lock. The Stryker lock records pid, folder and start time, checks ownership at its release, and on a stale lock stops and prints the rm command. A fix of one rule (for example the time zone of the start time, pid reuse or a recovery command) must be found and made twice. Decide whether both locks share one record format and one liveness test. If they must differ (for example because the Stryker lock has no queue and must not clear), tools/README.md states the reason once. Also: the GPU lock's queue tests a waiter's liveness by pid only (kill -0), so a reused pid keeps a dead waiter's ticket live and blocks every waiter. It does not break exclusion. Give the tickets the same pid and start identity as the record.


## Notes

**2026-11-04T21:20:48Z**

2026-11-04: started as a workflow, run 3a445525-f3d0-40fd-b0ea-07d0f73fafa7 (version 869b0b07), branch ter-s4fg-machine-lock-records from db0e11171. Settled by the orchestrator: one record format (holder: pid, UTC ps start, token, label) and one fail-safe liveness test for both locks, release by token in both; the stale policies stay different (the GPU lock's queue gives one safe clearer; the Stryker lock has no queue and stops), stated once in tools/README.md; two implementations of the one rule (bash and Python) kept in step by cross-tests; the GPU queue's tickets get pid and start identity with the fork rate kept bounded.
