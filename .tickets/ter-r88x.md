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

**2026-11-04T20:22:46Z**

2026-11-04: built on branch ter-r88x-gpu-lock-holder. tools/gpu-lock.sh writes the holder's record (pid, start time from ps, token, label) into the file holder in the lock right after its mkdir, through a temporary file and a rename. The release removes the lock only when the record's token is the run's own. Otherwise it removes nothing and writes one line into the run's log that ends with "Discard this run's timing." Every removal renames the lock to a name of the remover's own first, so that no run sees a lock that lost its record halfway. Only the oldest waiter of the queue clears a stale lock, and only after the rename does it compare the renamed record with the record that it read. A lock without a record is never cleared. INT and TERM only mark the run until the record is written, so no signal leaves an empty lock. A failed write of the record removes the new lock and exits with 1. TERTIUS_GPU_LOCK and TERTIUS_GPU_QUEUE give the test paths. tools/test_gpu_lock.py (16 tests, about 10 s, folders under /private/tmp) went red on the old logic for the right reasons and is green. tools/README.md, the FSR timing test's command (it used mkdir and rmdir by hand) and docs/plans/temporal-renderer.md now name the helper. No tool on main needs a take/release form. Two limits stay: a SIGKILL between mkdir and the record leaves an empty lock for a person to remove, and an old helper that is the oldest waiter on a stale lock blocks the clear until it ends.

**2026-11-04T20:40:10Z**

2026-11-04: the review's fix stage. The stale test now fails safe: a waiter clears a lock only when kill -0 of the recorded pid fails, or when ps gives a non-empty start time that differs. ps runs with TZ=UTC0 and LC_ALL=C, so a holder and a waiter in different time zones write and compare the same text. A record that lacks its pid, start or token counts as no record and is never cleared, and take() treats an empty start as a failed write of the record. HUP now acts like INT and TERM. During the take and the release (defer_signals) a signal only marks the run, and release() clears held itself, so no signal stops the release halfway or releases twice. The record is read with builtins, and ps runs at most once a second, so a waiter forks about as often as the old helper. New tests (24 in all, about 35 s): HUP in every end path, a signal between the mkdir and the record (a slow ps shim), a signal during the release (a slow mv shim), a ps that prints nothing for the waiter and for the taker, a holder and a waiter in two time zones, records without a pid, start or token, at most three ps calls in 2 s, and the move back of a lock that another run takes during the clear (an mv shim). Each new test except the move back went red on the reviewed helper for the intended reason. tools/README.md gives the exact start= format, the fail-safe rule and a recovery procedure for an empty lock or a waiting old helper. ter-s4fg records the follow-up: one record format and liveness test for this lock and the Stryker lock, and the queue's pid-only liveness.

**2026-11-04T21:12:05Z**

2026-11-04: hardening in the ticket worktree, against base 93e46dd7f and implementation 2656cff9a. No engine code changed. No GPU run or .NET test suite ran, and no test or mutant touched the real GPU lock or its queue. Each test used its own TERTIUS_GPU_LOCK and TERTIUS_GPU_QUEUE under /private/tmp. The baseline passed all 24 tests in 35.037 s.

Changed-line coverage of tools/gpu-lock.sh: 68 of 69 changed non-comment code lines had a Bash line trace (98.55%, before and after hardening). The count excludes 23 function declarations and control delimiters. BASH_ENV enabled set -x only for the real helper, with PS4='+${BASH_SOURCE}:${LINENO}: ' and BASH_XTRACEFD directed to a separate file per helper under a fresh /private/tmp folder. The only untraced code line is 81, `done 2> /dev/null < "$1/holder"`: Bash attributes the loop and its input redirection to the read on line 74 rather than the closing line; the record-field and missing-record tests execute this loop, so no additional behavior test is needed. These are line-trace counts, not branch coverage.

Four new tests protect the oldest live waiter, a newline in the log path that would split the holder record, a failed rename that publishes the record, and an incomplete live record that becomes complete during a stale rename. The release signal test now covers INT, TERM and HUP, rather than TERM alone. The new tests failed on their matching mutants before the unchanged helper passed. The existing take signal test and the expanded release signal test both kill the corresponding signal mutants.

The scripted runner applied each mutant to a copy of the original helper bytes, checked Bash syntax, ran the suite with a 180 s timeout, recorded failed test names, and restored the exact original bytes in finally, also on INT, TERM or HUP. It killed the subprocess group on a timeout. Each run ended with a byte-hash check and `git diff --exit-code -- tools/gpu-lock.sh`. The first 24 mutants ran the complete original suite; M06 and M23 ran the complete strengthened suite again. M24, the isolated publication-failure mutant M25, and the signal rechecks used the named regression tests. M11 first reached the full-suite 180 s limit because it rejects every valid take; the empty-ps test then killed it in 0.25 s without a timeout. The list, runner, traces and raw logs were temporary evidence outside the repository.

| id | line | change | result | killed by | answer |
|---|---:|---|---|---|---|
| M01 | 143 | Invert release ownership: `[ "$record_token" = "$token" ]` -> `[ "$record_token" != "$token" ]` | killed | `TheIncidentsOf20261104.test_a_run_whose_lock_was_removed_and_taken_again_leaves_the_new_holders_lock`, `TheIncidentsOf20261104.test_a_stray_rmdir_after_a_failed_mkdir_leaves_the_holders_lock` | - |
| M02 | 143 | Release foreign and absent locks: `[ "$record_token" = "$token" ]` -> `true` | killed | `TheIncidentsOf20261104.test_a_run_whose_lock_was_removed_and_taken_again_leaves_the_new_holders_lock`, `TheRelease.test_a_run_whose_lock_another_run_holds_leaves_that_lock_with_its_record_and_writes_so_into_its_log` | - |
| M03 | 120 | Invert holder liveness: `if kill -0 "$record_pid"` -> `if ! kill -0 "$record_pid"` | killed | `TheRelease.test_a_waiter_that_a_signal_ends_leaves_the_holders_lock`, `TheStaleLock.test_two_waiters_on_a_stale_lock_take_it_one_after_the_other` | - |
| M04 | 125 | Treat empty ps as stale: `[ -n "$now" ] && ` -> `(delete)` | killed | `TheStaleLock.test_a_live_holder_is_not_cleared_when_ps_gives_the_waiter_nothing` | - |
| M05 | 125 | Invert start comparison: `[ "$now" != "$record_start" ]` -> `[ "$now" = "$record_start" ]` | killed | `TheRelease.test_a_waiter_that_a_signal_ends_leaves_the_holders_lock`, `TheStaleLock.test_a_live_holder_in_another_time_zone_is_not_cleared` | - |
| M06 | 159 | Ignore oldest waiter: `[ "$(oldest_ticket)" = "$ticket" ]` -> `true` | killed | `TheStaleLock.test_only_the_oldest_live_waiter_can_clear_and_take_a_stale_lock` | New regression test: red on this mutant, green on the helper. |
| M07 | 129 | Skip token check after rename: `[ "$record_token" = "$stale" ]` -> `true` | killed | `TheStaleLock.test_a_lock_that_another_run_takes_during_the_clear_goes_back_with_its_record` | - |
| M08 | 132 | Do not restore replaced lock: `mv "$gone" "$lock"` -> `:` | killed | `TheStaleLock.test_a_lock_that_another_run_takes_during_the_clear_goes_back_with_its_record` | - |
| M09 | 140 | Release twice through EXIT: `held=0` -> `:` | killed | `TheIncidentsOf20261104.test_a_stray_rmdir_after_a_failed_mkdir_leaves_the_holders_lock`, `TheRelease.test_a_run_whose_lock_vanished_writes_so_into_its_log` | - |
| M10 | 121 | Remove ps rate limit: `[ "$checked" = "$EPOCHSECONDS" ] && return` -> `:` | killed | `TheStaleLock.test_a_waiter_asks_ps_about_a_live_holder_at_most_once_a_second` | - |
| M11 | 103 | Invert empty-start refusal: `[ -z "$start" ]` -> `[ -n "$start" ]` | killed | `TheRecord.test_a_run_whose_ps_gives_no_start_time_removes_the_lock_and_does_not_run_its_command` | Full suite first timed out at 180 s; focused empty-ps test killed it in 0.25 s. |
| M12 | 153 | Omit discard warning: `echo ` -> `: ` | killed | `TheRelease.test_a_run_whose_lock_another_run_holds_leaves_that_lock_with_its_record_and_writes_so_into_its_log`, `TheRelease.test_a_run_whose_lock_vanished_writes_so_into_its_log` | - |
| M13 | 47 | Exit during take on INT: `trap 'signal=130' INT` -> `trap 'exit 130' INT` | killed | `TheRelease.test_a_signal_between_the_mkdir_and_the_record_ends_the_run_without_a_lock`, `TheRelease.test_a_signal_during_the_release_does_not_stop_it_halfway` | - |
| M14 | 48 | Exit during take on TERM: `trap 'signal=143' TERM` -> `trap 'exit 143' TERM` | killed | `TheRelease.test_a_signal_between_the_mkdir_and_the_record_ends_the_run_without_a_lock`, `TheRelease.test_a_signal_during_the_release_does_not_stop_it_halfway` | - |
| M15 | 49 | Exit during take on HUP: `trap 'signal=129' HUP` -> `trap 'exit 129' HUP` | killed | `TheRelease.test_a_signal_between_the_mkdir_and_the_record_ends_the_run_without_a_lock`, `TheRelease.test_a_signal_during_the_release_does_not_stop_it_halfway` | - |
| M16 | 139 | Do not defer release signals: `defer_signals` -> `:` | killed | `TheRelease.test_a_signal_during_the_release_does_not_stop_it_halfway` | - |
| M17 | 82 | Accept missing recorded start: `[ -n "$record_start" ]` -> `true` | killed | `TheStaleLock.test_a_record_that_lacks_a_field_is_never_cleared` | - |
| M18 | 82 | Accept missing recorded token: `[ -n "$record_token" ]` -> `true` | killed | `TheStaleLock.test_a_record_that_lacks_a_field_is_never_cleared` | - |
| M19 | 82 | Accept missing recorded pid: `[ -n "$record_pid" ]` -> `true` | killed | `TheStaleLock.test_a_record_that_lacks_a_field_is_never_cleared` | - |
| M20 | 107 | Leave failed take lock behind: `rmdir "$lock"` -> `:` | killed | `TheRecord.test_a_run_that_cannot_write_its_record_removes_the_lock_and_does_not_run_its_command`, `TheRecord.test_a_run_whose_ps_gives_no_start_time_removes_the_lock_and_does_not_run_its_command` | - |
| M21 | 104 | Do not write holder token: `token=%s` -> `wrong=%s` | killed | `TheRecord.test_two_runs_get_different_tokens`, `TheStaleLock.test_two_waiters_on_a_stale_lock_take_it_one_after_the_other` | - |
| M22 | 128 | Continue after failed stale rename: `\|\| return` -> `\|\| :` | survived | - | Judgment call: after a failed atomic rename, the missing renamed directory makes the later read and move-back fail without changing the lock; testing the redundant stderr is not worth coupling a test to those unsuccessful operations. |
| M23 | 42 | Allow newline to split record label: `label=${label//$'\n'/ }` -> `:` | killed | `TheRecord.test_a_newline_in_the_log_path_cannot_split_the_holders_record` | New regression test: red on this mutant, green on the helper. |
| M24 | 119 | Clear incomplete records: `read_record "$lock" \|\| return` -> `read_record "$lock" \|\| :` | killed | `TheStaleLock.test_an_incomplete_live_record_cannot_be_cleared_when_it_becomes_complete_during_the_rename` | New regression test: red on this mutant, green on the helper. |
| M25 | 105 | Ignore failed record publication: `! mv "$lock/holder.new" "$lock/holder"` -> `! { mv "$lock/holder.new" "$lock/holder"; true; }` | killed | `TheRecord.test_a_failed_record_rename_removes_the_lock_without_running_the_command` | New regression test: red on this mutant, green on the helper. |

25 rows: killed 24, timeout 0 after replay, survived 1, no-coverage 0, invalid 0. The unadjusted mutation score is 24/25 = 96%; M22 remains a documented judgment call, not an equivalent mutant.

Final traced run: `nix develop --command python3 -I tools/test_gpu_lock.py -v` with the tracing environment described above: 28 tests passed in 40.070 s. The production helper remains byte-for-byte identical to 2656cff9a.
