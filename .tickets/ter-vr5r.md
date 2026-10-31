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


## Notes

**2026-10-31T21:17:59Z**

Result: RequestAndWait cannot miss the cancellation. Only the thread pool's delays under load exceed the bound, so the fix is in the tests.

Interleavings. RequestAndWait writes the pending sink, then calls SemaphoreSlim.Wait(stop). Wait checks the token first. Then it registers a callback on the token, and the callback pulses the semaphore's monitor under the semaphore's lock. Inside that lock, Wait checks the token again before every Monitor.Wait. So each cancel moment ends the wait:
- Before the call or before the registration: the check throws, or the registration runs the callback at once.
- After the last check and before Monitor.Wait: the callback blocks on the lock until Monitor.Wait releases it, and then its pulse wakes the wait.
- During Monitor.Wait: the pulse wakes it, and the check after the wake throws.
The catch then takes the request back with a compare-exchange, unless the render thread took it first. No lost wake-up is possible, so the code needs no change.

Cause. The old tests started the request with Task.Run and closed the window with CancellationTokenSource(50 ms), and both run on the thread pool. A due timer runs inline on the timer dispatch only if it is first in its pass. Otherwise the timer queue puts it on the pool's normal queue. When the pool is starved, the 50 ms cancel waits there, but the 5 s timer of WaitAsync can fire inline in a later pass. That gives TimeoutException although the request waits correctly.

Reproduction (scratch test, not committed). 40 work items blocked the pool, and one more short timer was made after the closing. The old shape of ARequestStopsWaitingWhenTheWindowCloses failed with the ticket's TimeoutException after 5.4 s. The request started at 38 ms, and the cancel never ran. In the same starved pool, the old ARequestWaitsUntilTheRenderThreadHasHandedTheSinkItsPixels and ARequestIsTakenOnce failed in TakeWithin after 5 s. Their request sat in the local queue of the test thread, which polled without returning to the pool.

Fix. The new test helper BlockingCall runs the call on a thread of its own, which starts at once. WaitUntilBlocked returns once that thread is in a wait. The test closes the window on its own thread with Cancel(), which runs the semaphore's callback at once. End joins the thread within Bound. No pool thread or pool timer is on the measured path. Bound stays 5 s: it covers only the OS's scheduling of threads that can run, which takes milliseconds even at a load of 80. It exists so that a bug fails the test instead of hanging the run.

Tests changed, all with the same shape (Task.Run or a pool timer, plus a fixed bound):
- CaptureMailboxTests: ARequestWaitsUntilTheRenderThreadHasHandedTheSinkItsPixels, ARequestIsTakenOnce, ARequestStopsWaitingWhenTheWindowCloses, ARequestThatTheWindowCancelsBeforeItIsTakenIsTakenBack.
- WindowPlaybackTests: AWaitForALaterTickEndsWhenTheWindowCloses, ASpeedSoSlowThatATickLastsMonthsStillWaitsAndStops, ACaptureThatTheWindowClosesOnStopsTheRun.
Not changed: ACapturePublishesTheStateOfItsMomentForTheRenderThread and ACaptureHandsTheRenderThreadASinkThatWritesThePngOfItsNameIntoTheFolder already serve on a LongRunning thread and capture on the test thread. FreePlayTests.InputFromAnotherThreadIsRecordedAtTheTickBeforeWhichTheWorldGotIt uses Task.Run but has no fixed bound.

After the fix, all seven tests passed in the starved pool in at most 113 ms. Both classes passed 5 times in a row at a machine load of 71 to 88. Only tests changed, so ARCHITECTURE.md and the doc comments of CaptureMailbox need no change.
