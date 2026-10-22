using System.Runtime.Versioning;
using Tertius.Host.Scripting;
using Tertius.Testing;
using static Tertius.Host.Tests.HostTesting;
using static Tertius.Host.Tests.VideoTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The video sink on the real ffmpeg of the dev shell: what the video holds, and what happens when ffmpeg fails. A video
/// whose path is a folder makes the real ffmpeg fail as a full disk does: it reads the first frame, cannot open its output,
/// and exits with an error. See docs/plans/blue-danube-demo.md, change 1, "Failure handling".
/// </summary>
public sealed class FfmpegVideoSinkTests : IDisposable
{
    private const int Width = 8, Height = 6;

    private static readonly ScriptWindow Size = new(Width, Height);
    private readonly string folder = NewFolder();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    /// <summary>
    /// The RGBA pixels of frame <paramref name="number"/>: a pattern of its own in red, green and blue, and a fourth byte of
    /// each pixel that changes from pixel to pixel, as the renderer's undefined fourth byte may.
    /// </summary>
    private static byte[] Frame(int number, int width = Width, int height = Height)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int pixel = 0; pixel < width * height; pixel++)
        {
            rgba[pixel * 4] = (byte)(pixel * 7 + number * 40);
            rgba[pixel * 4 + 1] = (byte)(255 - pixel * 3 - number * 25);
            rgba[pixel * 4 + 2] = (byte)(pixel * number * 11);
            rgba[pixel * 4 + 3] = (byte)(pixel * 37 + 5);
        }

        return rgba;
    }

    private static void Take(FfmpegVideoSink sink, byte[] rgba, int width = Width, int height = Height) =>
        sink.Take(new(rgba, width, height));

    private string VideoPath(string name = "video.mkv") => Path.Combine(folder, name);

    [Fact]
    public void AVideoHoldsEveryFrameItTookInOrderPixelForPixel()
    {
        string path = VideoPath();
        using (FfmpegVideoSink sink = FfmpegVideoSink.Start(new(path, 30), Size))
        {
            for (int number = 0; number < 3; number++)
                Take(sink, Frame(number));
            sink.Finish();
        }

        List<byte[]> frames = DecodeFrames(path, Width, Height);

        frames.Should().HaveCount(3);
        for (int number = 0; number < 3; number++)
            frames[number].Should().Equal(Rgb(Frame(number)), "frame {0} is kept exactly, without its fourth bytes",
                number);
    }

    [Fact]
    public void AVideoPlaysAtItsFrameRate()
    {
        string path = VideoPath();
        using (FfmpegVideoSink sink = FfmpegVideoSink.Start(new(path, 24), Size))
        {
            Take(sink, Frame(0));
            sink.Finish();
        }

        FrameRate(path).Should().Be("24/1");
    }

    [Fact]
    public void AVideoReplacesAnOlderLongerVideoAtTheSamePath()
    {
        string path = VideoPath();
        using (FfmpegVideoSink longer = FfmpegVideoSink.Start(new(path, 30), Size))
        {
            for (int number = 0; number < 3; number++)
                Take(longer, Frame(number));
            longer.Finish();
        }

        using (FfmpegVideoSink shorter = FfmpegVideoSink.Start(new(path, 30), Size))
        {
            Take(shorter, Frame(5));
            shorter.Finish();
        }

        DecodeFrames(path, Width, Height).Should().ContainSingle().Which.Should().Equal(Rgb(Frame(5)));
    }

    [Fact]
    public void AVideoMakesTheFolderOfItsFile()
    {
        string path = Path.Combine(folder, "runs", "danube", "video.mkv");
        using (FfmpegVideoSink sink = FfmpegVideoSink.Start(new(path, 30), Size))
        {
            Take(sink, Frame(0));
            sink.Finish();
        }

        CountFrames(path).Should().Be(1);
    }

    [Fact]
    public void AVideoThatTookNoFrameLeavesNoFile()
    {
        string path = VideoPath();

        FfmpegVideoSink.Start(new(path, 30), Size).Dispose();

        File.Exists(path).Should().BeFalse("ffmpeg writes a video with no frame, and the sink deletes it");
    }

    [Fact]
    public void DisposingAVideoWithoutFinishingItEndsTheFileSoThatItPlays()
    {
        string path = VideoPath();

        using (FfmpegVideoSink sink = FfmpegVideoSink.Start(new(path, 30), Size))
        {
            Take(sink, Frame(0));
            Take(sink, Frame(1));
        }

        CountFrames(path).Should().Be(2, "a run that fails at a step keeps the frames before it");
    }

    [Fact]
    public void WhenFfmpegStopsDuringTheVideoTheNextFrameFailsWithItsExitCodeTheFramesWrittenAndItsLastErrorLines()
    {
        // A folder at the path of the video: ffmpeg reads the first frame, cannot open its output, and exits.
        string path = VideoPath("folder.mkv");
        Directory.CreateDirectory(path);
        using FfmpegVideoSink sink = FfmpegVideoSink.Start(new(path, 30), Size);
        int taken = 0;

        // The pipe holds some frames that ffmpeg never reads, so a later frame meets the closed pipe.
        Action film = () =>
        {
            for (; taken < 10_000; taken++)
                Take(sink, Frame(taken % 7));
        };

        VideoStreamException failure = film.Should().Throw<VideoStreamException>().Which;
        failure.ExitCode.Should().NotBe(0);
        failure.FramesWritten.Should().Be(taken, "the frames before the failed one went into the pipe");
        failure.ErrorLines.Should().Contain(line => line.Contains("Is a directory"));
        failure.Message.Should().Contain($"exit code {failure.ExitCode}").And.Contain("Is a directory").And
            .Contain(path);
    }

    [Fact]
    public void WhenFfmpegFailsAtTheEndFinishFailsWithItsExitCodeAndItsLastErrorLines()
    {
        string path = VideoPath("folder.mkv");
        Directory.CreateDirectory(path);
        using FfmpegVideoSink sink = FfmpegVideoSink.Start(new(path, 30), Size);
        // One small frame fits in the pipe, so its write succeeds whatever ffmpeg does with it.
        Take(sink, Frame(0));

        VideoStreamException failure = sink.Invoking(s => s.Finish()).Should().Throw<VideoStreamException>().Which;

        failure.ExitCode.Should().NotBe(0);
        failure.FramesWritten.Should().Be(1);
        failure.ErrorLines.Should().Contain(line => line.Contains("Is a directory"));
    }

    [Fact]
    // The stand-in is a shell script, which needs its execute permission.
    [UnsupportedOSPlatform("windows")]
    public void AFailureReportsTheLast20LinesOfFfmpegsErrorOutputOldestFirst()
    {
        // A stand-in for ffmpeg that writes 25 error lines and exits with code 3. The real ffmpeg, at -loglevel error,
        // writes only a few lines before it fails.
        string program = Path.Combine(folder, "noisy-ffmpeg");
        File.WriteAllText(program, "#!/bin/sh\nfor i in $(seq 1 25); do echo \"line $i\" >&2; done\nexit 3\n");
        File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using FfmpegVideoSink sink = FfmpegVideoSink.Start(new(VideoPath(), 30), Size, program);

        VideoStreamException failure = sink.Invoking(s => s.Finish()).Should().Throw<VideoStreamException>().Which;

        failure.ExitCode.Should().Be(3);
        failure.ErrorLines.Should().Equal(Enumerable.Range(6, 20).Select(i => $"line {i}"));
    }

    [Fact]
    public void TakingAFrameAllocatesNothing()
    {
        byte[] rgba = Frame(0);
        using FfmpegVideoSink sink = FfmpegVideoSink.Start(new(VideoPath(), 30), Size);
        // The first frames warm up the path of the write.
        for (int number = 0; number < 5; number++)
            Take(sink, rgba);

        long allocated = Allocations.Fewest(() =>
        {
            for (int number = 0; number < 200; number++)
                Take(sink, rgba);
        });
        sink.Finish();

        allocated.Should().Be(0,
            "an 18,000-frame run writes each frame into ffmpeg's input with no copy and no allocation");
    }

    [Fact]
    public void AFrameOfAnotherSizeThanTheVideoIsRefused()
    {
        using FfmpegVideoSink sink = FfmpegVideoSink.Start(new(VideoPath(), 30), Size);

        Action take = () => Take(sink, Frame(0, Width * 2, Height), Width * 2, Height);

        take.Should().Throw<ArgumentException>().WithMessage("*16x6*8x6*");
    }
}
