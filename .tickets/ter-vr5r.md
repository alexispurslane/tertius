---
id: ter-vr5r
status: closed
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

**2026-10-31T21:04:44Z**

Workflow run 6b4ec457-963f-4b56-a9c4-27982bf09108 (version 869b0b07), branch ter-vr5r-mailbox-cancel-bound from 858eecea. Decide first whether RequestAndWait can miss the cancellation; if it can, a deterministic red test and a fix that makes missed wake-ups impossible; if not, a test whose bound measures the code and not the thread pool.

**2026-10-31T21:17:59Z**

Result: RequestAndWait cannot miss the cancellation. Only the thread pool's delays under load exceed the bound, so the fix is in the tests.

Interleavings. RequestAndWait writes the pending sink, then calls SemaphoreSlim.Wait(stop). Wait checks the token first. Then it registers a callback on the token, and the callback pulses the semaphore's monitor under the semaphore's lock. Inside that lock, Wait checks the token again before every Monitor.Wait. So each cancel moment ends the wait:
- Before the call or before the registration: the check throws, or the registration runs the callback at once.
- After the last check and before Monitor.Wait: the callback blocks on the lock until Monitor.Wait releases it, and then its pulse wakes the wait.
- During Monitor.Wait: the pulse wakes it, and the check after the wake throws.
The catch then takes the request back with a compare-exchange, unless the render thread took it first. If the render thread took it first, it still draws the sink and calls Done (WindowHost.cs:315), which leaves one permit that no thread waits for. That is harmless: the cancel stops the run (WindowPlayback.Capture returns false), and a window's mailbox serves no request after the window closes. So the code takes no measure for a reused mailbox, because no caller reuses one. No lost wake-up is possible, so the code needs no change.

Cause. The old tests started the request with Task.Run and closed the window with CancellationTokenSource(50 ms), and both run on the thread pool. A due timer runs inline on the timer dispatch only if it is first in its pass. Otherwise the timer queue puts it on the pool's normal queue. When the pool is starved, the 50 ms cancel waits there, but the 5 s timer of WaitAsync can fire inline in a later pass. That gives TimeoutException although the request waits correctly.

Reproduction (scratch test, not committed). 40 work items blocked the pool, and one more short timer was made after the closing. The old shape of ARequestStopsWaitingWhenTheWindowCloses failed with the ticket's TimeoutException after 5.4 s. The request started at 38 ms, and the cancel never ran. In the same starved pool, the old ARequestWaitsUntilTheRenderThreadHasHandedTheSinkItsPixels and ARequestIsTakenOnce failed in TakeWithin after 5 s. Their request sat in the local queue of the test thread, which polled without returning to the pool.

Fix. The new test helper BlockingCall runs the call on a thread of its own, which starts at once. WaitUntilBlocked returns once that thread is in a wait. The test closes the window on its own thread with Cancel(), which runs the semaphore's callback at once. End joins the thread within Bound. No pool thread or pool timer is on the measured path. Bound stays 5 s: it covers only the OS's scheduling of threads that can run, which takes milliseconds even at a load of 80. It exists so that a bug fails the test instead of hanging the run.

Tests changed, all with the same shape (Task.Run or a pool timer, plus a fixed bound):
- CaptureMailboxTests: ARequestWaitsUntilTheRenderThreadHasHandedTheSinkItsPixels, ARequestIsTakenOnce, ARequestStopsWaitingWhenTheWindowCloses, ARequestThatTheWindowCancelsBeforeItIsTakenIsTakenBack.
- WindowPlaybackTests: AWaitForALaterTickEndsWhenTheWindowCloses, ASpeedSoSlowThatATickLastsMonthsStillWaitsAndStops, ACaptureThatTheWindowClosesOnStopsTheRun.
- WindowPlaybackTests, in the fix stage: ACapturePublishesTheStateOfItsMomentForTheRenderThread and ACaptureHandsTheRenderThreadASinkThatWritesThePngOfItsNameIntoTheFolder. Their render thread (ServeOneCapture) now runs on BlockingCall too, so one helper owns every test thread and its bound.
Not changed: FreePlayTests.InputFromAnotherThreadIsRecordedAtTheTickBeforeWhichTheWorldGotIt uses Task.Run but has no fixed bound. CaptureMailboxTests.ARequestWithNoSinkIsRefused has a CancellationTokenSource(1 s) pool timer, but the request throws before it waits, so the timer only guards against a hang and a late timer cannot fail the test.

After the fix, all seven tests passed in the starved pool in at most 113 ms. Both classes passed 5 times in a row at a machine load of 71 to 88. Only tests changed, so ARCHITECTURE.md and the doc comments of CaptureMailbox need no change.

**2026-10-31T21:42:28Z**

Fix stage (review of e8705933). Each finding and its resolution:
- Parallel mechanism (ServeOneCapture beside BlockingCall). Fixed. ServeOneCapture now returns BlockingCall.Start of its body, and ACapturePublishesTheStateOfItsMomentForTheRenderThread and ACaptureHandsTheRenderThreadASinkThatWritesThePngOfItsNameIntoTheFolder are synchronous and call renderThread.End(). No test in Tertius.Host.Tests joins another thread with WaitAsync any more.
- Bound on a consumer test class. Fixed. Bound moved to BlockingCall.Bound, with its reason. TakeWithin and both test classes read it there, and the doc comments no longer point at each other.
- WaitUntilBlocked comment. Fixed. It now says that any wait counts, so a test that acts after it must also hold if it acts before that wait.
- Summaries over 50 words. Fixed. The BlockingCall summary has 43 words, Bound 50, WaitUntilBlocked 43, ServeOneCapture 42.
- Interleaving with a sink that the render thread took first. Fixed in the note above: one unused permit stays, which is harmless because no caller reuses a mailbox after the window closes. No code change.
- Gap: ARequestWithNoSinkIsRefused. Added to the "Not changed" list above.
- Gap: the reproduction could not be rerun. Its code is below. It is not committed, because it measures the pool and not the code. Put it in tests/Tertius.Host.Tests as a file of its own, with a Sink that ignores Take.

    [Fact]
    public async Task TheOldShapeTimesOutInAStarvedPool()
    {
        // Not disposed: blocked items that start late still wait on it.
        var release = new ManualResetEventSlim();
        for (int i = 0; i < 40; i++)
            ThreadPool.UnsafeQueueUserWorkItem(_ => release.Wait(), null);
        try
        {
            var mailbox = new CaptureMailbox();
            using var closing = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            // A second timer due in the same pass: only the first due timer runs inline on the timer thread.
            using var other = new Timer(callback: _ => { }, state: null, dueTime: 50, period: Timeout.Infinite);
            var request = Task.Run(() => mailbox.RequestAndWait(new Sink(), closing.Token), TestContext.Current.CancellationToken);
            await FluentActions
                .Awaiting(() => request.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
                .Should()
                .ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            release.Set();
        }
    }

Rerun in the fix stage at a machine load of about 118 on 10 cores, in Checked: it failed twice with "Expected a <System.OperationCanceledException> to be thrown, but found <System.TimeoutException>", after 5,327 ms and 5,278 ms. A second scratch test made the same starved pool and called all nine changed tests in turn: they passed in 557 ms together.

Only tests changed in this stage too, so ARCHITECTURE.md (which describes CaptureMailbox near line 3039) and the doc comments of CaptureMailbox need no change.

**2026-10-31T22:51:12Z**

Orchestrator's pass before the merge. Coverage: no src line changed (tests and the ticket only), so changed-line coverage has nothing to measure. Hand mutations in the ticket worktree, each built and run against its test class (Tertius.Host.Tests): m1 handedOver.Wait(stop) -> stop.ThrowIfCancellationRequested(); handedOver.Wait(CancellationToken.None): killed (CaptureMailboxTests); m2 remove the CompareExchange take-back: killed; m3 Math.Min(-late, 1) -> -late: killed (WindowPlaybackTests); m4 Capture passes CancellationToken.None: killed; m5 Capture's catch filtered out: killed; m7 WaitForTick's loop ignores stop: killed. m6 stop.WaitHandle.WaitOne(slice) -> Thread.Sleep(slice) survives, as the reviewer predicted: the slice of at most one second ends inside the 5 s bound, so the only effect is up to one second more latency, which no bound can see without becoming sensitive to the OS scheduler. Recorded as an accepted survivor.
