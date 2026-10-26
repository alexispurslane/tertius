using System.Numerics;
using static Tertius.Rendering.Tests.ExposureScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The automatic exposure as the viewer sees it (docs/plans/auto-exposure.md, "GPU tests of the acceptance criteria"):
/// the exposure follows the brightness of the surfaces in view over film time, with its time constants, its trims, its
/// clamp and its compensation, and the sky does not move it. The views that settle use the gray square, whose pixels
/// share one luminance. The films use the ramp square, whose trimmed mean moves smoothly with the exposure.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class AutoExposureRenderTests
{
    /// <summary>The share of a stop within which two settled views count as one exposure: half a bin.</summary>
    private const float SettledTolerance = 1f / 16;

    /// <summary>A film at 15 frames per second of snapshot time.</summary>
    private const double FrameSeconds = 1.0 / 15;

    /// <summary>The settled exposure of the sunlit gray square that fills the view, with <paramref name="add"/> added.</summary>
    private static float SettledOnTheGraySquare(
        ExposureSetting exposure,
        Action<VulkanRenderer, RenderSnapshot>? add = null
    ) =>
        SettledStops(
            exposure,
            renderer =>
            {
                RenderSnapshot snapshot = Sunlit();
                AddGraySquare(renderer, snapshot);
                add?.Invoke(renderer, snapshot);
                return snapshot;
            }
        );

    /// <summary>The ramp square that fills the view, in sunlight, or with the sun out when <paramref name="sunOut"/>.</summary>
    private static RenderSnapshot RampView(VulkanRenderer renderer, bool sunOut)
    {
        RenderSnapshot snapshot = Sunlit();
        if (sunOut)
            Gpu.Star(snapshot: snapshot, irradiance: 0);
        AddRampSquare(renderer: renderer, snapshot: snapshot, position: Ahead, rotation: FacingTheCamera);
        return snapshot;
    }

    [Fact]
    public void TheSkyDoesNotMoveTheExposure()
    {
        // At 3 m the view is about 6.16 m by 3.46 m, so these squares cover a tenth and a half of it.
        float tenth = SettledOnTheGraySquare(
            AutoExposure.Default,
            (renderer, snapshot) =>
            {
                snapshot.Instances.Clear();
                AddGraySquare(renderer, snapshot, halfSize: 0.73f);
            }
        );
        float half = SettledOnTheGraySquare(
            AutoExposure.Default,
            (renderer, snapshot) =>
            {
                snapshot.Instances.Clear();
                AddGraySquare(renderer, snapshot, halfSize: 1.63f);
            }
        );

        tenth.Should().NotBe(0, "the square must move the exposure from its seed for the comparison to mean anything");
        tenth.Should().BeApproximately(half, SettledTolerance);
    }

    [Fact]
    public void ALampThatCoversLessThanTheBrightShareDoesNotMoveTheExposure()
    {
        float alone = SettledOnTheGraySquare(AutoExposure.Default);
        // A glowing patch of 1 m² just in front of the square covers 5 % of the view, at about 1,000 times its radiance.
        float withLamp = SettledOnTheGraySquare(
            AutoExposure.Default,
            (renderer, snapshot) =>
                AddGlowingSquare(
                    renderer: renderer,
                    snapshot: snapshot,
                    position: new(0, 0, -2.9f),
                    halfSize: 0.5f,
                    radiance: new(500)
                )
        );

        alone.Should().NotBe(0, "the square must move the exposure from its seed for the comparison to mean anything");
        withLamp.Should().BeApproximately(alone, SettledTolerance);
    }

    [Fact]
    public void ABlackPatchThatCoversLessThanTheDarkShareDoesNotMoveTheExposure()
    {
        float alone = SettledOnTheGraySquare(AutoExposure.Default);
        float withPatch = SettledOnTheGraySquare(
            AutoExposure.Default,
            (renderer, snapshot) =>
                AddGlowingSquare(
                    renderer: renderer,
                    snapshot: snapshot,
                    position: new(0, 0, -2.9f),
                    halfSize: 0.5f,
                    radiance: Vector3.Zero
                )
        );

        alone.Should().NotBe(0, "the square must move the exposure from its seed for the comparison to mean anything");
        withPatch.Should().BeApproximately(alone, SettledTolerance);
    }

    [Fact]
    public void ALargeSunlitObjectOverADarkGroundSettlesWithTheObjectNotClipped()
    {
        VulkanRenderer renderer = Renderer(AutoExposure.Default);
        try
        {
            // The first frame is a metering draw, so the image already shows the settled exposure.
            FilmFrame settled = Still(renderer: renderer, snapshot: BrightObjectOverDarkGround(renderer), time: 0);

            settled.Luma.Should().BeLessThan(250f / 255, "the square in the middle of the view keeps its detail");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void InTotalDarknessTheExposureStopsAtTheHighestStops()
    {
        float settled = SettledOnTheGraySquare(
            AutoExposure.Default,
            (_, snapshot) =>
            {
                Gpu.Star(snapshot: snapshot, irradiance: 0);
                snapshot.Ambient.Intensity = 0;
            }
        );

        settled.Should().Be(AutoExposure.Default.MaxStops);
    }

    [Fact]
    public void UnderLightTooBrightForTheClampTheExposureStopsAtTheLowestStops()
    {
        // The square then shows a radiance of about 16,000, which wants about −16 stops.
        float settled = SettledOnTheGraySquare(
            AutoExposure.Default,
            (_, snapshot) => Gpu.Star(snapshot: snapshot, irradiance: 1e5f)
        );

        settled.Should().Be(AutoExposure.Default.MinStops);
    }

    [Fact]
    public void TheCompensationMovesTheSettledExposureByItsStops()
    {
        float without = SettledOnTheGraySquare(AutoExposure.Default);
        float with = SettledOnTheGraySquare(AutoExposure.Default with { CompensationStops = 1 });

        with.Should().BeApproximately(without + 1, 1e-3f);
    }

    [Fact]
    public void AFixedExposureNeverMovesAndTimesNoExposurePass()
    {
        VulkanRenderer renderer = Renderer(new FixedExposure(1.25f));
        try
        {
            RenderSnapshot snapshot = RampView(renderer, sunOut: false);
            Still(renderer: renderer, snapshot: snapshot, time: 0);
            Gpu.Star(snapshot: snapshot, irradiance: 0);
            FilmFrame[] film = Film(
                renderer: renderer,
                snapshot: snapshot,
                start: FrameSeconds,
                frameSeconds: FrameSeconds,
                frames: 30
            );

            film.Select(frame => frame.Stops).Should().AllBeEquivalentTo(1.25f);
            if (renderer.TimesPasses)
                renderer
                    .LastFrameStats.GpuTimings!.Value.Passes.ToArray()
                    .Select(timing => timing.Pass)
                    .Should()
                    .NotContain("exposure");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ANewRendererAdaptsItsExposure()
    {
        var renderer = VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = Width,
                Height = Height,
                EnvironmentMap = new NoEnvironmentMap(),
            }
        );
        try
        {
            RenderSnapshot snapshot = Sunlit();
            AddGraySquare(renderer, snapshot);
            // With two frames in flight, the fourth drawn frame reports the second.
            for (int frame = 0; frame < 3; frame++)
                renderer.Render(snapshot, 1f);

            renderer.LastFrameStats.Exposure!.Value.Stops.Should().NotBe(0, "the sunlit square wants about −1.5 stops");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The films of seconds of snapshot time (tests/STYLE.md, "Long tests").</summary>
    [Collection(GpuCollection.Name)]
    public sealed class Films
    {
        /// <summary>The largest difference in stops between the readings and the approach's curve.</summary>
        private const double CurveTolerance = 0.01;

        /// <summary>The largest difference of the luma of two images that show one view at one settled exposure.</summary>
        private const float LumaTolerance = 2f / 255;

        /// <summary>
        /// Settles a new renderer on the ramp square in sunlight, or with the sun out when <paramref name="fromDark"/>,
        /// then turns the sun the other way and films <paramref name="frames"/> frames at <paramref name="frameSeconds"/>.
        /// </summary>
        private static (FilmFrame Settled, FilmFrame[] Film) SettleAndFilm(
            bool fromDark,
            double frameSeconds,
            int frames
        )
        {
            VulkanRenderer renderer = Renderer(AutoExposure.Default);
            try
            {
                RenderSnapshot snapshot = RampView(renderer, sunOut: fromDark);
                float sun = Gpu.StarIrradiance(Sunlit());
                FilmFrame settled = Still(renderer: renderer, snapshot: snapshot, time: 0);
                Gpu.Star(snapshot: snapshot, irradiance: fromDark ? sun : 0);
                FilmFrame[] film = Film(
                    renderer: renderer,
                    snapshot: snapshot,
                    start: frameSeconds,
                    frameSeconds: frameSeconds,
                    frames: frames
                );
                return (settled, film);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        /// <summary>The settled exposure and luma of the ramp square in sunlight, or with the sun out.</summary>
        private static FilmFrame Settled(bool sunOut)
        {
            VulkanRenderer renderer = Renderer(AutoExposure.Default);
            try
            {
                return Still(renderer: renderer, snapshot: RampView(renderer, sunOut), time: 0);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        /// <summary>The largest difference between each frame's exposure and the approach from its first frame's.</summary>
        private static double LargestDistanceFromTheCurve(FilmFrame[] film, double target, double timeConstant) =>
            film.Select(
                    (frame, index) =>
                        Math.Abs(
                            frame.Stops
                                - Approached(
                                    from: film[0].Stops,
                                    target: target,
                                    seconds: index * FrameSeconds,
                                    timeConstant: timeConstant
                                )
                        )
                )
                .Max();

        [Fact]
        public void AfterTheSunGoesOutTheImageBrightensOverSecondsOfFilmTime()
        {
            FilmFrame dark = Settled(sunOut: true);
            // 226 frames, so that the last one has adapted for 15 s.
            (FilmFrame sunlit, FilmFrame[] film) = SettleAndFilm(
                fromDark: false,
                frameSeconds: FrameSeconds,
                frames: 226
            );

            film[0].Luma.Should().BeLessThan(sunlit.Luma, "the first frame without the sun keeps the sunlit exposure");
            film.Select(frame => frame.Luma).Should().BeInAscendingOrder("the image brightens in every frame");
            LargestDistanceFromTheCurve(film, target: dark.Stops, timeConstant: 3).Should().BeLessThan(CurveTolerance);
            film[^1].Luma.Should().BeApproximately(sunlit.Luma, LumaTolerance, "both map the square to middle gray");
        }

        [Fact]
        public void BackInSunlightTheImageIsOverexposedAtFirstAndThenSettles()
        {
            FilmFrame sunlit = Settled(sunOut: false);
            // 76 frames, so that the last one has adapted for 5 s.
            (FilmFrame dark, FilmFrame[] film) = SettleAndFilm(fromDark: true, frameSeconds: FrameSeconds, frames: 76);

            film[0].Luma.Should().BeGreaterThan(sunlit.Luma + 40f / 255, "the first frame keeps the dark exposure");
            LargestDistanceFromTheCurve(film, target: sunlit.Stops, timeConstant: 1)
                .Should()
                .BeLessThan(CurveTolerance);
            film[^1].Luma.Should().BeApproximately(sunlit.Luma, LumaTolerance);
            dark.Stops.Should().BeGreaterThan(sunlit.Stops, "the dark view settled at a higher exposure");
        }

        [Fact]
        public void TheExposureRisesIntoTheDarkSlowerThanItFallsIntoTheLight()
        {
            // 16 frames, so that the last one has adapted for 1 s.
            (FilmFrame sunlit, FilmFrame[] rise) = SettleAndFilm(
                fromDark: false,
                frameSeconds: FrameSeconds,
                frames: 16
            );
            (FilmFrame dark, FilmFrame[] fall) = SettleAndFilm(fromDark: true, frameSeconds: FrameSeconds, frames: 16);

            double riseShare = (rise[^1].Stops - sunlit.Stops) / (dark.Stops - sunlit.Stops);
            double fallShare = (fall[^1].Stops - dark.Stops) / (sunlit.Stops - dark.Stops);

            riseShare.Should().BeGreaterThan(0, "the exposure rose");
            fallShare.Should().BeGreaterThan(riseShare);
        }

        [Fact]
        public void TheApproachReachesTheSameExposureAtFifteenAndAtSixtyFramesPerSecond()
        {
            // The last frame of each film has adapted for 3 s.
            (_, FilmFrame[] fifteen) = SettleAndFilm(fromDark: false, frameSeconds: 1.0 / 15, frames: 46);
            (_, FilmFrame[] sixty) = SettleAndFilm(fromDark: false, frameSeconds: 1.0 / 60, frames: 181);

            fifteen[^1]
                .Stops.Should()
                .NotBe(fifteen[0].Stops, "the exposure must move for the comparison to mean anything");
            fifteen[^1].Stops.Should().BeApproximately(sixty[^1].Stops, 0.01f);
        }

        [Fact]
        public void AFilmGivesTheSameExposuresOnEveryRun()
        {
            (_, FilmFrame[] first) = SettleAndFilm(fromDark: false, frameSeconds: FrameSeconds, frames: 60);
            (_, FilmFrame[] second) = SettleAndFilm(fromDark: false, frameSeconds: FrameSeconds, frames: 60);

            first[^1]
                .Stops.Should()
                .NotBe(first[0].Stops, "the exposure must move for the comparison to mean anything");
            second.Select(frame => frame.Stops).Should().Equal(first.Select(frame => frame.Stops));
        }

        [Fact]
        public void AViewWithNoSurfaceKeepsItsExposure()
        {
            VulkanRenderer renderer = Renderer(AutoExposure.Default);
            try
            {
                RenderSnapshot snapshot = RampView(renderer, sunOut: false);
                float settled = Still(renderer: renderer, snapshot: snapshot, time: 0).Stops;
                snapshot.Instances.Clear();
                FilmFrame[] sky = Film(
                    renderer: renderer,
                    snapshot: snapshot,
                    start: FrameSeconds,
                    frameSeconds: FrameSeconds,
                    frames: 75
                );

                settled
                    .Should()
                    .NotBe(0, "the square must move the exposure from its seed for the test to mean anything");
                sky.Select(frame => frame.Stops).Should().AllBeEquivalentTo(settled);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }
    }
}
