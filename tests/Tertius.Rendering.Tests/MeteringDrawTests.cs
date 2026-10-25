using static Tertius.Rendering.Tests.ExposureScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The metering draw (docs/plans/auto-exposure.md, "The metering draw (option B)"): a headless frame under
/// <see cref="AutoExposure"/> whose step is at least <see cref="VulkanRenderer.MeteringStepSeconds"/> is drawn twice, so
/// that the shown draw uses the exposure that the first draw metered on its own view. A film's frames draw once.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class MeteringDrawTests
{
    /// <summary>The ramp square ahead in sunlight, and the same square behind the camera, which the sun does not light.</summary>
    private static RenderSnapshot TwoSquares(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Sunlit();
        AddRampSquare(renderer: renderer, snapshot: snapshot, position: Ahead, rotation: FacingTheCamera);
        AddRampSquare(renderer: renderer, snapshot: snapshot, position: Behind, rotation: FacingBehind);
        return snapshot;
    }

    private static void Turn(RenderSnapshot snapshot) =>
        snapshot.Camera = snapshot.Camera with { Rotation = TurnedAround, PreviousRotation = TurnedAround };

    [Fact]
    public void TheFirstFrameShowsTheSettledExposure()
    {
        VulkanRenderer renderer = Renderer(AutoExposure.Default);
        try
        {
            RenderSnapshot snapshot = Sunlit();
            snapshot.Sun.Intensity = 0;
            AddRampSquare(renderer: renderer, snapshot: snapshot, position: Ahead, rotation: FacingTheCamera);
            // 226 frames at 15 frames per second, so that the last one has adapted for 15 s.
            FilmFrame[] film = Film(
                renderer: renderer,
                snapshot: snapshot,
                start: 0,
                frameSeconds: 1.0 / 15,
                frames: 226
            );

            film[^1].Stops.Should().BeGreaterThan(0, "the dark view wants more than the seed's 0 stops");
            film[0].Luma.Should().BeApproximately(film[^1].Luma, 2f / 255);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AHeadlessFrameAfterALongStepShowsTheExposureOfItsOwnView()
    {
        AutoExposure setting = AutoExposure.Default;
        float darkTarget = SettledStops(
            setting,
            renderer =>
            {
                RenderSnapshot snapshot = TwoSquares(renderer);
                Turn(snapshot);
                return snapshot;
            }
        );
        VulkanRenderer renderer = Renderer(setting);
        try
        {
            RenderSnapshot snapshot = TwoSquares(renderer);
            float bright = Still(renderer: renderer, snapshot: snapshot, time: 0).Stops;
            Turn(snapshot);
            float shown = Still(renderer: renderer, snapshot: snapshot, time: 2).Stops;

            float expected = ExposureMeter.Approach(
                stops: bright,
                target: darkTarget,
                intoDarkFactor: ExposureMeter.ApproachFactor(2, setting.IntoDarkSeconds),
                intoLightFactor: ExposureMeter.ApproachFactor(2, setting.IntoLightSeconds)
            );
            darkTarget.Should().BeGreaterThan(bright, "the view behind the camera is darker");
            shown.Should().BeApproximately(expected, 0.01f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AMeteringDrawAndItsShownDrawMoveTheExposureByTheirStepOnce()
    {
        VulkanRenderer renderer = Renderer(AutoExposure.Default);
        try
        {
            RenderSnapshot snapshot = TwoSquares(renderer);
            Still(renderer: renderer, snapshot: snapshot, time: 0);
            Turn(snapshot);
            // The second frame lies at the time of the first, so its step is 0 and it shows what the first left.
            FilmFrame[] after = Film(renderer: renderer, snapshot: snapshot, start: 2, frameSeconds: 0, frames: 2);

            after[1].Stops.Should().Be(after[0].Stops, "the shown draw moves the exposure by a step of 0");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFilmFrameDrawsOnce()
    {
        VulkanRenderer renderer = Renderer(AutoExposure.Default);
        try
        {
            RenderSnapshot snapshot = Sunlit();
            AddGraySquare(renderer, snapshot);
            double[] steps = [1.0 / 30, 1.0 / 30, 0.5, 0.5];
            var advances = new List<long>();
            renderer.Render(snapshot, 1f);
            long last = renderer.LastFrameStats.Exposure?.Frame ?? -1;
            foreach (double step in steps)
            {
                snapshot.Time += step;
                renderer.Render(snapshot, 1f);
                // With one frame in flight, each drawn frame reports the frame drawn before it.
                long reported = renderer.LastFrameStats.Exposure!.Value.Frame;
                advances.Add(reported - last);
                last = reported;
            }

            advances.Should().Equal([1, 1, 2, 2], "a step of 0.5 s draws a metering draw before the shown draw");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
