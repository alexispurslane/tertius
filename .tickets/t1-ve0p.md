---
id: t1-ve0p
status: open
deps: []
links: []
created: 2026-11-15T03:18:12Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
---
# tools/gpu-lock.sh printed 'rmdir: failed to remove /tmp/tertius-gpu-measure.lock: No such file or directory' at the end of a shared-mode survey render

Found by t0-mzhr on 2026-11-14 at about 20:59, at the end of a survey render in the shared mode (renderer-survey.py run --chunk 0, the film rect-bay). The render's result was correct. The message comes from one of the plain rmdir calls of the lock, probably remove_empty_shared (rmdir "$lock/shared" 2> /dev/null && rmdir "$lock"), whose second rmdir has no 2> /dev/null. The lock folder was gone between the two rmdir calls, so another process removed or renamed it in that interval, for example a stale-lock clear or another shared holder. Find out whether two holders can both pass the first rmdir, and whether a lock can vanish while a shared holder still runs.

