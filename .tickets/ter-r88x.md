---
id: ter-r88x
status: in_progress
deps: []
links: []
created: 2026-11-04T17:28:26Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
---
# GPU lock: record its holder, so that no other process can release it

Found in ter-q58r on 2026-11-04: during one timed run, tools/gpu-lock.sh printed "rmdir: failed to remove '/tmp/tertius-gpu-measure.lock': No such file or directory". Another process removed the lock while this run held it, so two timed runs could share the GPU without knowing. The lock is a bare directory (mkdir to take, rmdir to release). tools/gpu-lock.sh is the one helper on main, but agents also take it by hand (mkdir/rmdir in their briefs) and spike scripts on branches take it (tools/sftw/top-run.sh, gpu-locked.sh). Nothing records who holds the lock, so a release cannot check that it is its own, and a stale lock after a crash has no owner to test. Fix: the lock directory holds its holder's PID and start time; release removes it only if it is the caller's; a waiting taker can clear a lock whose holder process is gone. Every brief and tool takes the lock only through tools/gpu-lock.sh (or a small take/release form of it for runs that span several commands).


## Notes

**2026-11-04T17:30:08Z**

2026-11-04: cause of the incident found. The ter-9nrc hardening agent ran its own mkdir of the lock and an rmdir after ';' in one command line. The mkdir failed because ter-q58r held the lock, and the rmdir still ran and removed ter-q58r's lock. ter-q58r's first try (round 3 base, /private/tmp/q58r-fsr-timing) ran its last seconds without the lock; its acceptance used the second try. So the class of bug is a release that does not check ownership; a lock that records its holder makes it impossible.

**2026-11-04T19:05:31Z**

2026-11-04: a second consequence, found in ter-q58r and first written in ter-ty4s, which duplicates this ticket. The EXIT trap of tools/gpu-lock.sh runs rmdir on the lock path whenever held=1, even when the lock vanished and another run took it since. So a run whose lock was removed then removes the next holder's lock in turn, and the class of bug spreads from one wrong release to the next run. The fix of this ticket must cover the helper's own release too: it removes the lock only when the lock's record is its own, and it writes a line into the run's log when its lock vanished or belongs to another run, so that the run's timing can be discarded.

**2026-11-04T20:08:12Z**

2026-11-04: started as a workflow, run 9a5cc5f9-05a2-42e6-acbe-cd8ad0ea42ef (version 869b0b07), branch ter-r88x-gpu-lock-holder from 93e46dd7f, Opus builder at high, Opus reviewer at xhigh. Settled: the lock stays the mkdir directory with the queue; the holder writes a record (process id, its start time, a token, a label) into it; every release removes only its own lock and logs a vanished or foreign lock; a waiter clears only a lock whose holder process is gone, race-safe; a lock without a record is never cleared; old and new helpers exclude each other during the change; no take/release form; tests use temp paths through environment variables, never the real lock.
