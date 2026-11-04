---
id: ter-s4fg
status: open
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

