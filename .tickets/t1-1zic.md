---
id: t1-1zic
status: closed
deps: []
links: []
created: 2026-11-11T21:45:29Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
---
# The survey's run takes the GPU lock per chunk even under TERTIUS_SURVEY_HOLDS_LOCK=1, so a run inside a take waits for its own lock

**2026-11-25 · Retired.** The old take, trace or survey workflow is off the critical path for the three film reproductions. Retain its failure report if that specific tool is used again.

tools/renderer-survey.py run wraps each chunk of cases in tools/gpu-lock.sh unless --chunk 0 is given (about line 939). A caller that holds the lock and sets TERTIUS_SURVEY_HOLDS_LOCK=1, as the split and the render helpers honor, still queues the chunk for the lock that it holds. On 2026-11-11 this stalled the shared GPU queue for about 11 minutes (t1-4hij, take A). Make run treat TERTIUS_SURVEY_HOLDS_LOCK=1 as --chunk 0, or refuse the combination with a message.

