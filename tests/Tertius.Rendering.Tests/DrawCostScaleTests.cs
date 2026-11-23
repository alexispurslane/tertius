using System.Numerics;
using Silk.NET.Vulkan;
using static Tertius.Rendering.Tests.RenderScaleTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The render scale of the two cost kinds of a draw (ter-bcwr): the catch-up draws after a reset, whose young histories
/// trace an own ray at nearly every pixel, and the ordinary draws. Each kind has its own fitted cost model and its own
/// request, so that a reset after the first dips at once to the catch-up scale and returns at once to the ordinary
/// scale. No GPU needed.
/// </summary>
public sealed class DrawCostScaleTests
{
    // Per unit of render area at the render size, in ms: an ordinary draw, and a catch-up draw, whose own rays cost twice
    // as much. With 3 ms of fixed work, the budget of 15 ms wants a scale of about 0.775 for an ordinary draw and about
    // 0.548 for a catch-up draw.
    private const double OrdinaryCost = 20;
    private const double CatchUpCost = 40;

    [Fact]
    public void ACatchUpDrawsMeasurementDoesNotMoveTheOrdinaryRequest()
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        float scale = controller.Next(frame: 0, cost: DrawCost.CatchUp);

        controller.Measure(Timings(frame: 0, scale: scale, renderCost: CatchUpCost));

        controller.RequestedScaleFor(DrawCost.Ordinary).Should().Be(1, "only the catch-up model saw the dear draw");
        controller.AssertInvariants();
    }

    [Fact]
    public void ACatchUpDrawsMeasurementMovesTheCatchUpRequestTowardItsBudget()
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        float scale = controller.Next(frame: 0, cost: DrawCost.CatchUp);

        controller.Measure(Timings(frame: 0, scale: scale, renderCost: CatchUpCost));

        double wanted = Math.Sqrt(12 / CatchUpCost);
        controller
            .RequestedScaleFor(DrawCost.CatchUp)
            .Should()
            .BeApproximately((float)(1 + 0.25 * (wanted - 1)), 1e-6f, "a shrink moves a quarter of the way");
    }

    [Fact]
    public void AnOrdinaryDrawsMeasurementDoesNotMoveTheCatchUpRequest()
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        float dear = controller.Next(frame: 0, cost: DrawCost.CatchUp);
        controller.Measure(Timings(frame: 0, scale: dear, renderCost: CatchUpCost));
        float catchUp = controller.RequestedScaleFor(DrawCost.CatchUp);
        float ordinary = controller.Next(frame: 1, cost: DrawCost.Ordinary);

        controller.Measure(Timings(frame: 1, scale: ordinary, renderCost: 100));

        controller.RequestedScaleFor(DrawCost.CatchUp).Should().Be(catchUp);
        controller.AssertInvariants();
    }

    [Fact]
    public void BeforeAnyCatchUpDrawWasMeasuredACatchUpDrawAsksForTheOrdinaryScale()
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        for (long frame = 0; frame < 30; frame++)
        {
            float scale = controller.Next(frame: frame, cost: DrawCost.Ordinary);
            controller.Measure(Timings(frame: frame, scale: scale, renderCost: OrdinaryCost));
        }

        controller
            .RequestedScaleFor(DrawCost.CatchUp)
            .Should()
            .Be(controller.RequestedScaleFor(DrawCost.Ordinary), "the first reset of a run has no catch-up model yet");
    }

    [Fact]
    public void TheFirstCatchUpMeasurementMovesTheCatchUpRequestFromTheOrdinaryScale()
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        for (long frame = 0; frame < 30; frame++)
        {
            float scale = controller.Next(frame: frame, cost: DrawCost.Ordinary);
            controller.Measure(Timings(frame: frame, scale: scale, renderCost: OrdinaryCost));
        }
        float ordinary = controller.RequestedScaleFor(DrawCost.Ordinary);
        float dear = controller.Next(frame: 30, cost: DrawCost.CatchUp);

        controller.Measure(Timings(frame: 30, scale: dear, renderCost: CatchUpCost));

        double wanted = Math.Sqrt(12 / CatchUpCost);
        controller
            .RequestedScaleFor(DrawCost.CatchUp)
            .Should()
            .BeApproximately(
                (float)(ordinary + 0.25 * (wanted - ordinary)),
                1e-6f,
                "the catch-up draws of the first reset asked for the ordinary scale, so their request moves on from it"
            );
    }

    [Fact]
    public void AMeasurementThatArrivesAfterTheKindChangedGoesToTheKindOfItsOwnDraw()
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        for (long frame = 0; frame < 8; frame++)
            controller.Next(frame: frame, cost: DrawCost.CatchUp);
        for (long frame = 8; frame < 11; frame++)
            controller.Next(frame: frame, cost: DrawCost.Ordinary);

        controller.Measure(Timings(frame: 7, scale: 1, renderCost: CatchUpCost));

        controller.RequestedScaleFor(DrawCost.Ordinary).Should().Be(1, "draw 7 was a catch-up draw");
        controller.RequestedScaleFor(DrawCost.CatchUp).Should().BeLessThan(1);
        controller.AssertInvariants();
    }

    [Fact]
    public void ALaterResetAsksForTheFittedCatchUpScaleInItsFirstDraw()
    {
        Run run = RunWithACut();

        run.CatchUpBeforeCut.Should()
            .BeLessThan(run.OrdinaryBeforeCut - 0.1f, "the start's catch-up draws fitted the catch-up model");
        run.Scales[run.Cut].Should().BeApproximately(run.CatchUpBeforeCut, 1e-6f);
    }

    [Fact]
    public void ALaterResetAsksForTheOrdinaryScaleInTheDrawAfterTheCatchUpLength()
    {
        Run run = RunWithACut();

        run.Scales[run.Cut + FrameHistory.CatchUpDraws]
            .Should()
            .BeApproximately(run.OrdinaryBeforeCut, 1e-6f, "the catch-up draws never moved the ordinary request");
    }

    /// <summary>
    /// The scale of each draw of a run, the index of the cut's first draw, and the two requests just before the cut.
    /// </summary>
    private readonly record struct Run(List<float> Scales, int Cut, float OrdinaryBeforeCut, float CatchUpBeforeCut);

    /// <summary>
    /// A run as the renderer draws it: 80 draws after the start, a camera cut, then 20 draws. The history derives each
    /// draw's kind, the controller requests that kind's scale, and each draw's timings arrive 2 draws late. A draw costs
    /// <see cref="CatchUpCost"/> while its draw index since the reset is under the catch-up length, as the plan's own
    /// rays do, and <see cref="OrdinaryCost"/> after.
    /// </summary>
    private static Run RunWithACut()
    {
        const int cut = 80;
        const int late = 2;
        var history = new FrameHistory(StillRule.Window);
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        var output = new Extent2D(1920, 1080);
        var scales = new List<float>();
        var inFlight = new Queue<GpuTimings>();
        (float Ordinary, float CatchUp) beforeCut = default;
        for (int frame = 0; frame < cut + 20; frame++)
        {
            if (frame == cut)
                beforeCut = (
                    controller.RequestedScaleFor(DrawCost.Ordinary),
                    controller.RequestedScaleFor(DrawCost.CatchUp)
                );
            RenderSnapshot snapshot = Snapshot(cuts: frame < cut ? 1 : 2);
            DrawCost cost = history.CostOf(
                snapshot: snapshot,
                step: FrameHistoryTesting.FilmStep,
                draw: 0,
                outputSize: output
            );
            float scale = controller.RequestedScaleFor(cost);
            FrameHistory.PendingFrame pending = history.Begin(
                snapshot: snapshot,
                step: FrameHistoryTesting.FilmStep,
                draw: 0,
                outputSize: output,
                renderSize: output
            );
            pending.Write(
                new(
                    EyeInFrame: default,
                    FrameId: snapshot.OriginFrameId,
                    Rotation: Quaternion.Identity,
                    TanHalfX: 1,
                    TanHalfY: 0.5625f,
                    RootRotation: Quaternion.Identity
                )
            );
            double renderCost = pending.DrawIndex < 8 ? CatchUpCost : OrdinaryCost;
            controller.Drew(frame: frame, realizedScale: scale, cost: pending.Cost);
            history.Drew(pending);
            scales.Add(scale);
            inFlight.Enqueue(Timings(frame: frame, scale: scale, renderCost: renderCost));
            if (inFlight.Count > late)
                controller.Measure(inFlight.Dequeue());
            controller.AssertInvariants();
        }
        return new(Scales: scales, Cut: cut, OrdinaryBeforeCut: beforeCut.Ordinary, CatchUpBeforeCut: beforeCut.CatchUp);
    }

    private static RenderSnapshot Snapshot(long cuts)
    {
        var snapshot = new RenderSnapshot { CameraCuts = cuts };
        snapshot.Chain.Add(new(FrameId: 7, Rotation: Quaternion.Identity, PreviousRotation: Quaternion.Identity));
        return snapshot;
    }
}
