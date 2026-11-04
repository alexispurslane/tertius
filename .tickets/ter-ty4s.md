---
id: ter-ty4s
status: closed
deps: []
links: [ter-q58r]
created: 2026-11-04T17:48:58Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [tools, performance]
---
# The GPU lock: a run releases only the lock that it holds, and says when its lock vanished

Found in ter-q58r on 2026-11-04. During the acceptance rounds of ter-q58r, the builder saw /tmp/tertius-gpu-measure.lock disappear while one of its runs of tools/gpu-lock.sh held it. Which process removed it is unknown. Several sessions take the lock by hand with mkdir and rmdir (for example the briefs of ter-9nrc), so a session that removes a lock it did not make is the likely cause.

Two consequences follow. First, a second timed run can take the lock and share the GPU with the run that still believes it holds the lock, so both timings and both GPU clocks are wrong, and nothing records it. Second, gpu-lock.sh's EXIT trap runs rmdir on the lock path whenever held=1, so if another run took the lock after it vanished, gpu-lock.sh removes that other run's lock in turn.

Make the lock record its owner: the taker writes its process id and start time into the lock directory, and the release removes the directory only when the record is its own. At the release, gpu-lock.sh writes a line into the run's log when its lock vanished or belongs to someone else, so that the run's timing can be discarded. Document the rule for takers by hand in tools/README.md, 'The GPU lock'.


## Notes

**2026-11-04T19:05:31Z**

2026-11-04: duplicate of ter-r88x, which holds the cause of the incident. Its finding about the EXIT trap of tools/gpu-lock.sh is now a note in ter-r88x.
