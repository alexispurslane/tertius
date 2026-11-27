using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using static Tertius.Rendering.Tests.ExposureScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Bloom stays out of the automatic exposure (ARCHITECTURE.md, "Bloom" and "The meter and the adapt step"; issue #2). One
/// moving film under the renderer's defaults, with its screen tracer, FSR at 2/3 and every history, is drawn three times:
/// with no bloom, with the default bloom and with an exaggerated bloom. The camera pans across a sunlit ramp wall with a
/// bright lamp panel on it, and halfway through the sun dims by 4 stops, so the meter sees the view and the light change.
/// Each film frame steps 1/30 s, below <see cref="FrameHistory.MeteringStepSeconds"/>, so it is one moving draw and not a
/// still. The tests compare the exposure that each frame's passes used, the exposure that its adapt step wrote, the
/// meter's histogram, the HDR images before bloom and the delayed readings of <see cref="RenderStats.Exposure"/>, frame
/// by frame, and the halo in the last frame. The three films are drawn once for the whole class.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class BloomExposureIsolationTests
{
    private const int FilmWidth = 320,
        FilmHeight = 180;

    /// <summary>The step of one film frame in s: two ticks at 60 Hz, below the still rule's 0.09 s.</summary>
    private const double FrameSeconds = 1.0 / 30;

    /// <summary>The film's frames: 2 s, the first a still at time 0.</summary>
    private const int Frames = 60;

    /// <summary>The film frame from which the sun gives a sixteenth of its irradiance.</summary>
    private const int DimFrame = 30;

    /// <summary>The frames in flight of the renderer's defaults, by which a reading lags the frame that it describes.</summary>
    private const int FramesInFlight = 2;

    /// <summary>The camera's turn about +Y at the first and at the last film frame, in radians: a pan to the right.</summary>
    private const float FirstYaw = 0.25f,
        LastYaw = -0.25f;

    /// <summary>The lamp panel's radiance, in W/(m²·sr) per channel: far above white at every exposure of the film.</summary>
    private const float LampRadiance = 4;

    /// <summary>Where the lamp panel stands, just in front of the wall, left of the view axis and above it.</summary>
    private static readonly Vector3 LampPosition = new(-0.9f, 0.35f, -2.95f);

    /// <summary>A bloom far stronger and wider than the default, with a threshold four times lower.</summary>
    private static readonly Bloom Exaggerated = new(Threshold: 0.25f, Knee: 1, Strength: 2, Levels: 7);

    /// <summary>The variable that names the folder of the films' report and turns the report on.</summary>
    private const string ReportVariable = "TERTIUS_BLOOM_ISOLATION_REPORT";

    private static readonly Lazy<Shot> Shots = new(() =>
        new(Off: Shoot(new NoBloom()), Default: Shoot(Bloom.Default), Strong: Shoot(Exaggerated))
    );

    // A second replay reads only the normal delayed stats. The diagnostic copies in Shoot wait for the GPU, so they
    // cannot by themselves prove the behavior with frames in flight. Two extra moving draws collect the film's tail.
    private static readonly Lazy<ExposureReading?[][]> Uninterrupted = new(() =>
        [Replay(new NoBloom()), Replay(Bloom.Default), Replay(Exaggerated)]
    );

    /// <summary>The three films of one moving shot: with no bloom, the default bloom and <see cref="Exaggerated"/>.</summary>
    private sealed record Shot(Film Off, Film Default, Film Strong)
    {
        public Film[] All => [Off, Default, Strong];
    }

    /// <summary>
    /// What one film recorded at each film frame. <paramref name="Used"/> is the exposure in stops that the frame's passes
    /// used and <paramref name="Next"/> the one that its adapt step wrote for the next frame, both from the exposure buffer
    /// after the frame. <paramref name="Readings"/> holds <see cref="RenderStats.Exposure"/> after each frame: the
    /// delayed report of an older frame, except for the captured last frame, which reports its own.
    /// <paramref name="Histograms"/> holds the meter's 256 counts of each frame. <paramref name="Hdr"/> and
    /// <paramref name="PostHdr"/> hold a digest of the bits of the render-size HDR image, which the meter reads, and of the
    /// upscaled HDR image, which bloom and the tone map read. <paramref name="Last"/> is the captured last frame.
    /// </summary>
    private sealed record Film(
        float[] Used,
        float[] Next,
        ExposureReading?[] Readings,
        uint[][] Histograms,
        string[] Hdr,
        string[] PostHdr,
        Capture Last
    );

    /// <summary>The renderer's defaults at the film's size, with validation and the given bloom.</summary>
    private static VulkanRenderer Renderer(BloomSetting bloom) =>
        VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = FilmWidth,
                Height = FilmHeight,
                Bloom = bloom,
            }
        );

    /// <summary>The sunlit ramp wall that fills the view at 3 m, and the lamp panel in front of it.</summary>
    private static RenderSnapshot Scene(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Sunlit();
        AddRampSquare(renderer: renderer, snapshot: snapshot, position: Ahead, rotation: FacingTheCamera);
        AddGlowingSquare(
            renderer: renderer,
            snapshot: snapshot,
            position: LampPosition,
            halfSize: 0.2f,
            radiance: new(LampRadiance)
        );
        return snapshot;
    }

    /// <summary>The camera's rotation at <paramref name="frame"/>: the pan from <see cref="FirstYaw"/> to <see cref="LastYaw"/>.</summary>
    private static Quaternion Pan(int frame) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, FirstYaw + (LastYaw - FirstYaw) * frame / (Frames - 1));

    /// <summary>A digest of the bits of <paramref name="values"/>: equal digests mean equal images, bit for bit.</summary>
    private static string Digest(float[] values) =>
        Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(values.AsSpan())));

    /// <summary>Sets the identical camera, time and light inputs of both replays, including the two tail draws.</summary>
    private static void Move(RenderSnapshot snapshot, float sun, int frame)
    {
        snapshot.Time = frame * FrameSeconds;
        snapshot.Camera = snapshot.Camera with
        {
            Rotation = Pan(frame),
            PreviousRotation = Pan(Math.Max(frame - 1, 0)),
        };
        if (frame == DimFrame)
            Gpu.Star(snapshot: snapshot, irradiance: sun / 16);
    }

    /// <summary>Draws without captures or diagnostic reads, including enough tail draws to report every film frame.</summary>
    private static ExposureReading?[] Replay(BloomSetting bloom)
    {
        VulkanRenderer renderer = Renderer(bloom);
        try
        {
            RenderSnapshot snapshot = Scene(renderer);
            float sun = Gpu.StarIrradiance(snapshot);
            var readings = new ExposureReading?[Frames + FramesInFlight];
            for (int frame = 0; frame < readings.Length; frame++)
            {
                Move(snapshot: snapshot, sun: sun, frame: frame);
                renderer.Render(snapshot, 1f);
                readings[frame] = renderer.LastFrameStats.Exposure;
            }

            return readings;
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>Draws the film under <paramref name="bloom"/> and records every frame.</summary>
    private static Film Shoot(BloomSetting bloom)
    {
        VulkanRenderer renderer = Renderer(bloom);
        try
        {
            RenderSnapshot snapshot = Scene(renderer);
            float sun = Gpu.StarIrradiance(snapshot);
            float[] used = new float[Frames];
            float[] next = new float[Frames];
            var readings = new ExposureReading?[Frames];
            uint[][] histograms = new uint[Frames][];
            string[] hdr = new string[Frames];
            string[] postHdr = new string[Frames];
            Capture? last = null;
            for (int frame = 0; frame < Frames; frame++)
            {
                Move(snapshot: snapshot, sun: sun, frame: frame);
                if (frame < Frames - 1)
                    renderer.Render(snapshot, 1f);
                else
                    last = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "bloom isolation film");

                readings[frame] = renderer.LastFrameStats.Exposure;
                (next[frame], used[frame]) = renderer.ReadExposureStops();
                histograms[frame] = renderer.ReadExposureHistogram();
                hdr[frame] = Digest(renderer.ReadHdr());
                postHdr[frame] = Digest(renderer.ReadPostHdr());
            }

            return new(
                Used: used,
                Next: next,
                Readings: readings,
                Histograms: histograms,
                Hdr: hdr,
                PostHdr: postHdr,
                Last: last!
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The mean rise of the luma of <paramref name="image"/> over <paramref name="unbloomed"/>'s, in codes of 255.</summary>
    private static double MeanLumaRise(Capture image, Capture unbloomed)
    {
        double sum = 0;
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
            sum += image.Luma(x, y) - unbloomed.Luma(x, y);
        return 255 * sum / (image.Width * image.Height);
    }

    [Fact]
    public void EveryFrameOfAMovingShotIsShadedWithTheSameExposureWhateverTheBloom()
    {
        Shot shot = Shots.Value;

        shot.Default.Used.Select(BitConverter.SingleToInt32Bits)
            .Should()
            .Equal(
                shot.Off.Used.Select(BitConverter.SingleToInt32Bits),
                "the default bloom must not change any applied exposure bit"
            );
        shot.Strong.Used.Select(BitConverter.SingleToInt32Bits)
            .Should()
            .Equal(
                shot.Off.Used.Select(BitConverter.SingleToInt32Bits),
                "the exaggerated bloom must not change any applied exposure bit"
            );
    }

    [Fact]
    public void EveryAdaptStepOfAMovingShotWritesTheSameExposureWhateverTheBloom()
    {
        Shot shot = Shots.Value;

        shot.Default.Next.Select(BitConverter.SingleToInt32Bits)
            .Should()
            .Equal(shot.Off.Next.Select(BitConverter.SingleToInt32Bits));
        shot.Strong.Next.Select(BitConverter.SingleToInt32Bits)
            .Should()
            .Equal(shot.Off.Next.Select(BitConverter.SingleToInt32Bits));
    }

    [Fact]
    public void EveryAppliedExposureIsIdenticalWithoutSynchronizingTheMovingDraws()
    {
        Film[] films = Shots.Value.All;
        ExposureReading?[][] uninterrupted = Uninterrupted.Value;
        for (int variant = 0; variant < films.Length; variant++)
        {
            Film film = films[variant];
            long firstShown = film.Readings[^1]!.Value.Frame - (Frames - 1);
            for (int frame = 0; frame < Frames; frame++)
            {
                ExposureReading reading = uninterrupted[variant][frame + FramesInFlight]!.Value;
                reading
                    .Frame.Should()
                    .Be(firstShown + frame, "match the drawn-frame number, not the current Render call");
                BitConverter
                    .SingleToInt32Bits(reading.Stops)
                    .Should()
                    .Be(
                        BitConverter.SingleToInt32Bits(films[0].Used[frame]),
                        "at moving frame {0}, bloom variant {1}",
                        frame,
                        variant
                    );
            }
        }
    }

    [Fact]
    public void TheMeterCountsTheSameHistogramInEveryFrameWhateverTheBloom()
    {
        Shot shot = Shots.Value;

        for (int frame = 0; frame < Frames; frame++)
        {
            shot.Default.Histograms[frame].Should().Equal(shot.Off.Histograms[frame], "at film frame {0}", frame);
            shot.Strong.Histograms[frame].Should().Equal(shot.Off.Histograms[frame], "at film frame {0}", frame);
        }
    }

    [Fact]
    public void TheMeteredHdrImageIsTheSameInEveryFrameWhateverTheBloom()
    {
        Shot shot = Shots.Value;

        shot.Default.Hdr.Should().Equal(shot.Off.Hdr, "every history that feeds the metered image ignores bloom");
        shot.Strong.Hdr.Should().Equal(shot.Off.Hdr);
    }

    [Fact]
    public void TheUpscaledHdrImageThatBloomReadsIsTheSameInEveryFrameWhateverTheBloom()
    {
        Shot shot = Shots.Value;

        shot.Default.PostHdr.Should()
            .Equal(shot.Off.PostHdr, "bloom only reads the upscaled image, and FSR's history too");
        shot.Strong.PostHdr.Should().Equal(shot.Off.PostHdr);
    }

    [Fact]
    public void TheDelayedExposureReadingsAreTheSameWhateverTheBloom()
    {
        Shot shot = Shots.Value;

        shot.Default.Readings.Should().Equal(shot.Off.Readings);
        shot.Strong.Readings.Should().Equal(shot.Off.Readings);
    }

    [Fact]
    public void EachDelayedReadingReportsTheExposureThatItsFrameUsedFramesInFlightFilmFramesEarlier()
    {
        Film film = Shots.Value.Default;
        // The still at film frame 0 draws its warm-up draws first, so the film's frame numbers start past them.
        long firstShown = film.Readings[^1]!.Value.Frame - (Frames - 1);

        for (int frame = FramesInFlight; frame < Frames - 1; frame++)
            film.Readings[frame]
                .Should()
                .Be(
                    new ExposureReading(
                        Frame: firstShown + frame - FramesInFlight,
                        Stops: film.Used[frame - FramesInFlight]
                    ),
                    "film frame {0} waited on the slot of the frame {1} draws before it",
                    frame,
                    FramesInFlight
                );
        film.Readings[^1]
            .Should()
            .Be(
                new ExposureReading(Frame: firstShown + Frames - 1, Stops: film.Used[^1]),
                "a capture reports its own frame"
            );
    }

    [Fact]
    public void ThePanMovesTheMeteredViewInEveryFrame()
    {
        Film film = Shots.Value.Default;

        for (int frame = 1; frame < Frames; frame++)
            film.Histograms[frame]
                .Should()
                .NotEqual(film.Histograms[frame - 1], "the camera turned at film frame {0}", frame);
    }

    [Fact]
    public void TheExposureRisesAfterTheSunDimsWhateverTheBloom()
    {
        foreach (Film film in Shots.Value.All)
        {
            film.Used[DimFrame]
                .Should()
                .BeApproximately(
                    film.Used[DimFrame - 1],
                    0.05f,
                    "the frame that sees the dim sun still uses the bright exposure"
                );
            film.Used[^1].Should().BeGreaterThan(film.Used[DimFrame] + 0.5f, "the meter sees the wall 4 stops darker");
            film.Used[DimFrame..]
                .Should()
                .BeInAscendingOrder("the exposure adapts toward the darker view in every frame");
        }
    }

    [Fact]
    public void TheHaloGrowsFromNoBloomToTheDefaultToTheExaggeratedBloom()
    {
        Shot shot = Shots.Value;

        double byDefault = MeanLumaRise(shot.Default.Last, shot.Off.Last);
        double byStrong = MeanLumaRise(shot.Strong.Last, shot.Off.Last);

        byDefault.Should().BeGreaterThan(5, "the default bloom puts a visible halo around the lamp");
        byStrong.Should().BeGreaterThan(2 * byDefault, "the exaggerated bloom spreads a far brighter halo");
    }

    [Fact]
    public void TheWallKeepsVisibleSurfaceContrastBelowTheLampWhateverTheBloom()
    {
        foreach (Film film in Shots.Value.All)
        {
            Capture image = film.Last;
            float bright = image.MeanLuma(new(40, 145), 3);
            float dark = image.MeanLuma(new(140, 145), 3);
            bright
                .Should()
                .BeGreaterThan(dark + 10f / 255, "the ramp below the lamp must retain visible surface contrast");
            bright.Should().BeLessThan(240f / 255, "the wall detail must remain below display white");
        }
    }

    [Fact]
    public void TheFilmsWriteTheirLastFramesAndTheirExposuresOnRequest()
    {
        string? folder = Environment.GetEnvironmentVariable(ReportVariable);
        Assert.SkipWhen(string.IsNullOrEmpty(folder), $"Set {ReportVariable} to a folder to write the films' report.");
        Shot shot = Shots.Value;
        Directory.CreateDirectory(folder);
        (string Name, Film Film)[] films = [("off", shot.Off), ("default", shot.Default), ("exaggerated", shot.Strong)];
        foreach ((string name, Film film) in films)
            film.Last.Save(Path.Combine(folder, $"last-frame-{name}.png"));
        var rows = new List<string>
        {
            "frame,time_s,used_off,used_default,used_exaggerated,histograms_equal,hdr_equal",
        };
        for (int frame = 0; frame < Frames; frame++)
        {
            bool histogramsEqual = films.All(other =>
                other.Film.Histograms[frame].SequenceEqual(shot.Off.Histograms[frame])
            );
            bool hdrEqual = films.All(other => other.Film.Hdr[frame] == shot.Off.Hdr[frame]);
            rows.Add(
                FormattableString.Invariant(
                    $"{frame},{frame * FrameSeconds:F4},{shot.Off.Used[frame]:R},{shot.Default.Used[frame]:R},{shot.Strong.Used[frame]:R},{histogramsEqual},{hdrEqual}"
                )
            );
        }

        File.WriteAllLines(Path.Combine(folder, "exposure-by-frame.csv"), rows);
        File.Exists(Path.Combine(folder, "exposure-by-frame.csv")).Should().BeTrue();
    }
}
