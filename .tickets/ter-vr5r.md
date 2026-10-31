---
id: ter-vr5r
status: in_progress
deps: []
links: []
created: 2026-10-31T18:45:27Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [tests, host]
---
# CaptureMailboxTests.ARequestStopsWaitingWhenTheWindowCloses can time out under load

Found in the fix stage of ter-nwhv (2026-10-31). In a full Checked run of Tertius.slnx, with a machine load of about 28 on 10 cores from other builders, the test failed once: it expected an OperationCanceledException, but request.WaitAsync(Bound) threw a TimeoutException after Bound, 5 s (tests/Tertius.Host.Tests/WindowingTests.cs:388). The token cancels after 50 ms, so the request did not end within 5 s of start. A rerun of Tertius.Host.Tests alone passed (1,885 of 1,885). The branch did not touch Tertius.Host. Find whether RequestAndWait can miss the cancellation, or whether only the thread pool's delay under load exceeds the bound.

