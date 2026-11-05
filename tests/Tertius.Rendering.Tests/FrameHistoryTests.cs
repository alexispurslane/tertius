using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The record of the last drawn frame and the rules that relate each draw to it (docs/plans/temporal-renderer.md, "The
/// frame history" and "Resets"): which draws reset every history, the draw index since the last reset, the parity of the
/// images that alternate, the previous camera relative to this draw's eye, and the commit after the submission, so that a
/// draw that throws changes nothing. No GPU needed.
/// </summary>
public sealed class FrameHistoryTests
{
    private static readonly Extent2D Output = new(1280, 720);

    /// <summary>A snapshot whose camera's frame is <paramref name="frameId"/>, with the render origin and the cut count given.</summary>
    private static RenderSnapshot Snapshot(long cuts = 1, Double3 origin = default, long frameId = 7)
    {
        var snapshot = new RenderSnapshot { CameraCuts = cuts, OriginInFrame = origin };
        snapshot.Chain.Add(new(FrameId: frameId, Rotation: Quaternion.Identity, PreviousRotation: Quaternion.Identity));
        return snapshot;
    }

    /// <summary>The camera that a batch would write for <paramref name="snapshot"/>, with its eye at <paramref name="offset"/> from the origin.</summary>
    private static DrawnCamera CameraOf(RenderSnapshot snapshot, Vector3 offset = default) =>
        new(
            EyeInFrame: snapshot.OriginInFrame + (Double3)offset,
            FrameId: snapshot.OriginFrameId,
            Rotation: Quaternion.Identity,
            TanHalfX: 1f,
            TanHalfY: 0.5625f,
            RootRotation: Quaternion.Identity
        );

    /// <summary>Begins a draw of <paramref name="snapshot"/>, writes its camera as the batch does, and commits it.</summary>
    private static FrameHistory.PendingFrame Draw(
        FrameHistory history,
        RenderSnapshot snapshot,
        Extent2D? output = null,
        Extent2D? renderSize = null
    )
    {
        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: snapshot,
            outputSize: output ?? Output,
            renderSize: renderSize ?? output ?? Output
        );
        pending.Write(CameraOf(snapshot));
        history.Drew(pending);
        return pending;
    }

    [Fact]
    public void TheFirstDrawResetsEveryHistory()
    {
        var history = new FrameHistory(StillRule.Window);

        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(),
            outputSize: Output,
            renderSize: Output
        );

        pending.Reset.Should().BeTrue();
        pending.DrawIndex.Should().Be(0);
    }

    [Fact]
    public void ADrawOfTheSameCutAfterADrawnFrameContinuesIt()
    {
        var history = new FrameHistory(StillRule.Window);
        Draw(history: history, snapshot: Snapshot());

        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(),
            outputSize: Output,
            renderSize: Output
        );

        pending.Reset.Should().BeFalse();
        pending.DrawIndex.Should().Be(1);
    }

    [Fact]
    public void ADrawWhoseSnapshotCountsAnotherCutResets()
    {
        var history = new FrameHistory(StillRule.Window);
        Draw(history: history, snapshot: Snapshot(cuts: 1));

        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(cuts: 2),
            outputSize: Output,
            renderSize: Output
        );

        pending.Reset.Should().BeTrue();
        pending.DrawIndex.Should().Be(0);
    }

    [Fact]
    public void ADrawAfterASkippedSnapshotThatHeldACutResets()
    {
        // The builder counted a cut in a snapshot that the renderer never drew, and the snapshot after it counts no new cut.
        var history = new FrameHistory(StillRule.Window);
        Draw(history: history, snapshot: Snapshot(cuts: 1));

        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(cuts: 2),
            outputSize: Output,
            renderSize: Output
        );

        pending
            .Reset.Should()
            .BeTrue("the count differs from the drawn frame's, though this snapshot cut nothing itself");
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1920, 720)]
    [InlineData(1280, 1080)]
    public void ADrawAtAnotherOutputSizeResets(int width, int height)
    {
        var history = new FrameHistory(StillRule.Window);
        Draw(history: history, snapshot: Snapshot());

        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(),
            outputSize: new((uint)width, (uint)height),
            renderSize: new((uint)width, (uint)height)
        );

        pending.Reset.Should().BeTrue("the output's width or height or both changed");
    }

    [Fact]
    public void ADrawAtAnotherRenderSizeDoesNotReset()
    {
        var history = new FrameHistory(StillRule.Window);
        Draw(history: history, snapshot: Snapshot(), renderSize: new(1280, 720));

        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(),
            outputSize: Output,
            renderSize: new(960, 540)
        );

        pending.Reset.Should().BeFalse();
        pending.PreviousRenderSize.Should().Be(new Extent2D(1280, 720));
    }

    [Fact]
    public void TheDrawIndexCountsTheDrawnFramesSinceTheLastReset()
    {
        var history = new FrameHistory(StillRule.Window);
        Draw(history: history, snapshot: Snapshot(cuts: 1));
        Draw(history: history, snapshot: Snapshot(cuts: 1));
        Draw(history: history, snapshot: Snapshot(cuts: 2));
        Draw(history: history, snapshot: Snapshot(cuts: 2));

        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(cuts: 2),
            outputSize: Output,
            renderSize: Output
        );

        pending.DrawIndex.Should().Be(2, "the third draw reset, and two draws followed it");
        pending.DrawnFrames.Should().Be(4);
    }

    [Fact]
    public void ADrawThatThrowsBeforeDrewDoesNotConsumeItsCut()
    {
        var history = new FrameHistory(StillRule.Window);
        Draw(history: history, snapshot: Snapshot(cuts: 1));
        history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(cuts: 2),
            outputSize: Output,
            renderSize: Output
        );

        FrameHistory.PendingFrame again = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(cuts: 2),
            outputSize: Output,
            renderSize: Output
        );

        again.Reset.Should().BeTrue("the draw that threw never committed the count of 2");
    }

    [Fact]
    public void ADrawThatThrowsBeforeDrewLeavesTheDrawIndexAndTheParityAsTheyWere()
    {
        var history = new FrameHistory(StillRule.Window);
        Draw(history: history, snapshot: Snapshot());
        FrameHistory.PendingFrame thrown = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(),
            outputSize: Output,
            renderSize: Output
        );
        thrown.Write(CameraOf(Snapshot()));
        int thrownParity = thrown.Parity;

        FrameHistory.PendingFrame again = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(),
            outputSize: Output,
            renderSize: Output
        );

        again.DrawIndex.Should().Be(1);
        again.DrawnFrames.Should().Be(1);
        again.Parity.Should().Be(thrownParity, "the next draw writes the images that the draw which threw wrote");
    }

    [Fact]
    public void ACommitOfADrawWhoseBatchWroteNoCameraThrowsAndCommitsNothing()
    {
        var history = new FrameHistory(StillRule.Window);
        Draw(history: history, snapshot: Snapshot(cuts: 1));
        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(cuts: 2),
            outputSize: Output,
            renderSize: Output
        );

        FluentActions.Invoking(() => history.Drew(pending)).Should().Throw<InvalidOperationException>();

        history.DrawnFrames.Should().Be(1);
        history
            .Begin(
                step: FrameHistoryTesting.FilmStep,
                draw: 0,
                snapshot: Snapshot(cuts: 2),
                outputSize: Output,
                renderSize: Output
            )
            .Reset.Should()
            .BeTrue("the commit that threw kept the count of 1 cut");
    }

    [Fact]
    public void TheCountOfDrawnFramesGrowsOnlyWithACommittedDraw()
    {
        var history = new FrameHistory(StillRule.Window);
        Draw(history: history, snapshot: Snapshot());
        Draw(history: history, snapshot: Snapshot());
        history
            .Begin(
                step: FrameHistoryTesting.FilmStep,
                draw: 0,
                snapshot: Snapshot(),
                outputSize: Output,
                renderSize: Output
            )
            .Write(CameraOf(Snapshot()));

        history.DrawnFrames.Should().Be(2, "the third draw threw before Drew");
    }

    [Fact]
    public void ACommittedDrawKeepsTheParityOfTheImagesThatItWrote()
    {
        var history = new FrameHistory(StillRule.Window);
        Draw(history: history, snapshot: Snapshot());

        FrameHistory.PendingFrame drawn = Draw(history: history, snapshot: Snapshot());

        drawn.Parity.Should().Be(1, "a read of the draw's images after its commit finds the ones that it wrote");
    }

    [Fact]
    public void ADrawThatThrowsBeforeDrewLeavesThePlacementHistoryAsItWas()
    {
        var history = new FrameHistory(StillRule.Window);
        FrameHistory.PendingFrame first = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(),
            outputSize: Output,
            renderSize: Output
        );
        first.Write(CameraOf(Snapshot()));
        first.Place(Placement(slot: 3));
        history.Drew(first);
        FrameHistory.PendingFrame thrown = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(),
            outputSize: Output,
            renderSize: Output
        );
        thrown.Place(Placement(slot: 5));

        FrameHistory.PendingFrame again = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(),
            outputSize: Output,
            renderSize: Output
        );

        again.PreviousPlacements.ToArray().Select(p => p.Key.Entity.Index).Should().Equal(3u);
    }

    [Fact]
    public void TheParityAlternatesWithEachDrawnFrame()
    {
        var history = new FrameHistory(StillRule.Window);
        int firstParity = Draw(history: history, snapshot: Snapshot()).Parity;

        FrameHistory.PendingFrame second = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(),
            outputSize: Output,
            renderSize: Output
        );

        second.Parity.Should().Be(1 - firstParity);
    }

    [Fact]
    public void ThePreviousEyeAfterAStepOfTenKilometersFarFromTheFramesOriginIsExactToAMillimeter()
    {
        var history = new FrameHistory(StillRule.Window);
        var start = new Double3(6.4e6, 0, 0);
        RenderSnapshot before = Snapshot(origin: start);
        FrameHistory.PendingFrame first = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: before,
            outputSize: Output,
            renderSize: Output
        );
        first.Write(CameraOf(before, offset: new(0.25f, 0, 0)));
        history.Drew(first);
        RenderSnapshot after = Snapshot(origin: start + new Double3(10_000.0004, 0, 0));
        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: after,
            outputSize: Output,
            renderSize: Output
        );

        PreviousView previous = pending.PreviousViewOf(CameraOf(after, offset: new(0.25f, 0, 0)));

        previous.Eye.X.Should().BeApproximately(-10_000.0004f, 1e-3f);
        previous.Eye.Y.Should().Be(0);
        previous.Eye.Z.Should().Be(0);
    }

    [Fact]
    public void AResetByTheOutputSizeKeepsThePreviousCamera()
    {
        var history = new FrameHistory(StillRule.Window);
        RenderSnapshot before = Snapshot();
        FrameHistory.PendingFrame first = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: before,
            outputSize: Output,
            renderSize: Output
        );
        first.Write(CameraOf(before, offset: new(0, 2, 0)));
        history.Drew(first);
        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(),
            outputSize: new(1920, 1080),
            renderSize: new(1920, 1080)
        );

        PreviousView previous = pending.PreviousViewOf(CameraOf(Snapshot()));

        previous.Eye.Should().Be(new Vector3(0, 2, 0), "the motion vectors stay exact across a reset");
    }

    [Fact]
    public void ADrawInAnotherFrameOfTheCameraContinuesNoPreviousCameraOrPlacement()
    {
        var history = new FrameHistory(StillRule.Window);
        FrameHistory.PendingFrame first = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(frameId: 7),
            outputSize: Output,
            renderSize: Output
        );
        first.Write(CameraOf(Snapshot(frameId: 7), offset: new(0, 2, 0)));
        first.Place(Placement(slot: 3));
        history.Drew(first);
        RenderSnapshot elsewhere = Snapshot(cuts: 2, frameId: 9);
        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: elsewhere,
            outputSize: Output,
            renderSize: Output
        );

        PreviousView previous = pending.PreviousViewOf(CameraOf(elsewhere));

        previous.Eye.Should().Be(Vector3.Zero, "a position in another frame's axes means nothing in this one's");
        pending.PreviousPlacements.Length.Should().Be(0);
    }

    [Fact]
    public void TheFirstDrawSeesItsOwnCameraAsThePreviousOne()
    {
        var history = new FrameHistory(StillRule.Window);
        RenderSnapshot snapshot = Snapshot();
        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: snapshot,
            outputSize: Output,
            renderSize: Output
        );
        DrawnCamera camera = CameraOf(snapshot) with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f) };

        PreviousView previous = pending.PreviousViewOf(camera);

        previous
            .Should()
            .Be(
                new PreviousView(
                    Eye: Vector3.Zero,
                    Rotation: camera.Rotation,
                    TanHalfX: camera.TanHalfX,
                    TanHalfY: camera.TanHalfY,
                    RootRotation: camera.RootRotation,
                    RenderSize: Output
                )
            );
    }

    [Fact]
    public void ThePreviousCameraIsTheDrawnFramesCamera()
    {
        var history = new FrameHistory(StillRule.Window);
        RenderSnapshot before = Snapshot();
        FrameHistory.PendingFrame first = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: before,
            outputSize: Output,
            renderSize: new(640, 360)
        );
        DrawnCamera drawn = CameraOf(before) with
        {
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f),
            TanHalfX = 2f,
            TanHalfY = 1f,
            RootRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.1f),
        };
        first.Write(drawn);
        history.Drew(first);
        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(),
            outputSize: Output,
            renderSize: Output
        );

        PreviousView previous = pending.PreviousViewOf(CameraOf(Snapshot()));

        previous
            .Should()
            .Be(
                new PreviousView(
                    Eye: Vector3.Zero,
                    Rotation: drawn.Rotation,
                    TanHalfX: 2f,
                    TanHalfY: 1f,
                    RootRotation: drawn.RootRotation,
                    RenderSize: new(640, 360)
                )
            );
    }

    [Fact]
    public void TheRecordKeepsItsInvariantsThroughDrawsResetsAndDrawsThatThrow()
    {
        var history = new FrameHistory(StillRule.Window);
        Draw(history: history, snapshot: Snapshot(cuts: 1));
        history
            .Begin(
                step: FrameHistoryTesting.FilmStep,
                draw: 0,
                snapshot: Snapshot(cuts: 2),
                outputSize: Output,
                renderSize: Output
            )
            .Place(Placement(slot: 1));
        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: Snapshot(cuts: 2),
            outputSize: Output,
            renderSize: Output
        );
        pending.Write(CameraOf(Snapshot(cuts: 2)));
        pending.Place(Placement(slot: 1));
        pending.Place(Placement(slot: 4));
        history.Drew(pending);

        history.Invoking(h => h.AssertInvariants()).Should().NotThrow();
    }

    private static KeptPlacement Placement(uint slot) =>
        new(
            Key: new(Entity: new(Index: slot, Generation: 1), Part: 0),
            Row0: Vector4.UnitX,
            Row1: Vector4.UnitY,
            Row2: Vector4.UnitZ,
            Mesh: 0,
            Copy: null
        );
}
