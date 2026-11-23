---
id: tc-5azv
status: open
deps: []
links: []
created: 2026-11-23T21:14:09Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [tools, testing, mutation]
---
# tools/mutate.py counts the test host's wait for the GPU lock inside its 120 s timeout, and its table reads that timeout as a kill, so a mutant that never ran can be reported killed

Found by t0-y88b's listed mutants on 2026-11-23. tools/mutate.py runs a mutant's test classes under a timeout of 120 s (TEST_TIMEOUT). A Rendering test host first takes the GPU lock in its shared mode (tests/Tertius.Testing/GpuTestLock.cs), and the lock's queue is first come, first served: a host that arrives behind an exclusive ticket waits until that ticket's run ends. The timeout counts that wait. tools/README.md's table of results says a timeout 'counts as killed, but the table lists it apart', so a mutant whose tests never ran can be reported as a kill.

Evidence: t0-y88b's run of 16 Rendering shader mutants from 15:24 to 16:06 on the Mac (/private/tmp/t0-y88b/mutate, results.json and <id>.test.log). 13 of the 16 ended in timeout after 123 to 168 s: S01 to S06 and S09 to S15. Each of their test logs holds only 'Running tests from ...' and 'Waiting for the GPU lock in its shared mode (log /tmp/tertius-gpu-test-lock/...)', and no test result. Another lead's exclusive takes held the lock or a queue ticket for most of 14:40 to 16:07. The same mutants' baseline, run alone, took 70 to 83 s. Two earlier attempts stopped at a baseline for the same reason (baseline-1 and baseline-2 logs, 14:41 and 14:45). The mutants that got the lock in time gave real results: S07 and S16 killed, S08 survived.

What a fix must give: a mutant whose tests never ran is not a result and never counts as killed. Either the timeout starts when the test host holds the lock, or a timeout whose log shows a wait for the lock and no test result becomes its own result, such as 'lock-wait', that the table counts as not run. The README's result table and triage rule then say so. Do not fix it in t0-y88b.

