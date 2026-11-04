---
id: ter-r88x
status: open
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

**2026-11-04T20:22:46Z**

2026-11-04: built on branch ter-r88x-gpu-lock-holder. tools/gpu-lock.sh writes the holder's record (pid, start time from ps, token, label) into the file holder in the lock right after its mkdir, through a temporary file and a rename. The release removes the lock only when the record's token is the run's own. Otherwise it removes nothing and writes one line into the run's log that ends with "Discard this run's timing." Every removal renames the lock to a name of the remover's own first, so that no run sees a lock that lost its record halfway. Only the oldest waiter of the queue clears a stale lock, and only after the rename does it compare the renamed record with the record that it read. A lock without a record is never cleared. INT and TERM only mark the run until the record is written, so no signal leaves an empty lock. A failed write of the record removes the new lock and exits with 1. TERTIUS_GPU_LOCK and TERTIUS_GPU_QUEUE give the test paths. tools/test_gpu_lock.py (16 tests, about 10 s, folders under /private/tmp) went red on the old logic for the right reasons and is green. tools/README.md, the FSR timing test's command (it used mkdir and rmdir by hand) and docs/plans/temporal-renderer.md now name the helper. No tool on main needs a take/release form. Two limits stay: a SIGKILL between mkdir and the record leaves an empty lock for a person to remove, and an old helper that is the oldest waiter on a stale lock blocks the clear until it ends.

**2026-11-04T20:40:10Z**

2026-11-04: the review's fix stage. The stale test now fails safe: a waiter clears a lock only when kill -0 of the recorded pid fails, or when ps gives a non-empty start time that differs. ps runs with TZ=UTC0 and LC_ALL=C, so a holder and a waiter in different time zones write and compare the same text. A record that lacks its pid, start or token counts as no record and is never cleared, and take() treats an empty start as a failed write of the record. HUP now acts like INT and TERM. During the take and the release (defer_signals) a signal only marks the run, and release() clears held itself, so no signal stops the release halfway or releases twice. The record is read with builtins, and ps runs at most once a second, so a waiter forks about as often as the old helper. New tests (24 in all, about 35 s): HUP in every end path, a signal between the mkdir and the record (a slow ps shim), a signal during the release (a slow mv shim), a ps that prints nothing for the waiter and for the taker, a holder and a waiter in two time zones, records without a pid, start or token, at most three ps calls in 2 s, and the move back of a lock that another run takes during the clear (an mv shim). Each new test except the move back went red on the reviewed helper for the intended reason. tools/README.md gives the exact start= format, the fail-safe rule and a recovery procedure for an empty lock or a waiting old helper. ter-s4fg records the follow-up: one record format and liveness test for this lock and the Stryker lock, and the queue's pid-only liveness.
