---
id: ter-kba1
status: closed
deps: []
links: []
created: 2026-11-12T02:45:45Z
type: bug
priority: 1
assignee: Alexis Purslane
---
# Every trace leaves a 0.5 GB Instruments temp file, and 152 of them filled the disk

On 2026-11-11 at about 21:30 the Mac's disk filled (927 GB, ENOSPC) and every agent's builds, logs and GPU takes failed for about 10 minutes. The cause: each `xctrace record` (Metal System Trace) leaves its raw recording as instruments*.ktrace, 0.3 to 4 GB, in the user temp folder (getconf DARWIN_USER_TEMP_DIR). 152 of them, 82 GB, had piled up since Nov 8. The orchestrator deleted those that no process held open, and a stopgap janitor (/private/tmp/runpod-setup/ktrace-janitor.sh) deletes new ones older than 30 minutes. Fix: the trace launcher deletes its own run's leftovers when the recording ends, for example by noting the start time and deleting instruments*.ktrace files newer than it that no process holds open. Document it in tools/README.md beside the trace steps.


## Notes

**2026-11-12T09:31:14Z**

Duplicate of ter-g25a, which is older; closed in its favor.
