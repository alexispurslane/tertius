using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// When every tile's reflection share restarts at the ceiling (<see cref="ScreenTraceImages.SharesRestart"/>): at each
/// draw that resets every history, and not at a new render size. No GPU needed.
/// </summary>
public sealed class ReflectionShareRestartTests
{
    private static readonly Extent2D Output = new(1280, 720);

    [Fact]
    public void TheFirstDrawRestartsTheShares()
    {
        var history = new FrameHistory(StillRule.Window);

        ScreenTraceImages.SharesRestart(Begin(history, Snapshot(cuts: 1), Output)).Should().BeTrue();
    }

    [Fact]
    public void ADrawThatContinuesTheLastAtItsRenderSizeKeepsTheShares()
    {
        var history = new FrameHistory(StillRule.Window);
        Draw(history, Snapshot(cuts: 1), Output);

        ScreenTraceImages.SharesRestart(Begin(history, Snapshot(cuts: 1), Output)).Should().BeFalse();
    }

    [Fact]
    public void ACameraCutRestartsTheShares()
    {
        var history = new FrameHistory(StillRule.Window);
        Draw(history, Snapshot(cuts: 1), Output);

        ScreenTraceImages.SharesRestart(Begin(history, Snapshot(cuts: 2), Output)).Should().BeTrue();
    }

    [Fact]
    public void ANewRenderSizeKeepsTheSharesAsTheHistoriesDo()
    {
        var history = new FrameHistory(StillRule.Window);
        Draw(history, Snapshot(cuts: 1), Output);

        FrameHistory.PendingFrame pending = Begin(history, Snapshot(cuts: 1), new(1152, 648));

        pending.Reset.Should().BeFalse("the histories reproject across render sizes");
        ScreenTraceImages
            .SharesRestart(pending)
            .Should()
            .BeFalse(
                "the dynamic render scale changes the size often, and a restart would bring back the grain of a restart "
                    + "on every surface whose share had fallen"
            );
    }

    private static RenderSnapshot Snapshot(long cuts)
    {
        var snapshot = new RenderSnapshot { CameraCuts = cuts };
        snapshot.Chain.Add(new(FrameId: 7, Rotation: Quaternion.Identity, PreviousRotation: Quaternion.Identity));
        return snapshot;
    }

    private static FrameHistory.PendingFrame Begin(
        FrameHistory history,
        RenderSnapshot snapshot,
        Extent2D renderSize
    ) =>
        history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: snapshot,
            outputSize: Output,
            renderSize: renderSize
        );

    /// <summary>Begins a draw, writes its camera as the batch does, and commits it.</summary>
    private static void Draw(FrameHistory history, RenderSnapshot snapshot, Extent2D renderSize)
    {
        FrameHistory.PendingFrame pending = Begin(history, snapshot, renderSize);
        pending.Write(
            new(
                EyeInFrame: snapshot.OriginInFrame,
                FrameId: snapshot.OriginFrameId,
                Rotation: Quaternion.Identity,
                TanHalfX: 1f,
                TanHalfY: 0.5625f,
                RootRotation: Quaternion.Identity
            )
        );
        history.Drew(pending);
    }
}
