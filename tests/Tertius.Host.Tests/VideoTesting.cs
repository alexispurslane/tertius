using System.Diagnostics;
using System.Globalization;
using System.Text;
using StbImageSharp;

namespace Tertius.Host.Tests;

/// <summary>Reads the videos that the tests write, with ffmpeg and ffprobe from the dev shell.</summary>
public static class VideoTesting
{
    /// <summary>
    /// Every frame of the video, decoded to 8-bit RGB: width × height × 3 bytes per frame, rows from the top. Throws if
    /// ffmpeg cannot read the video.
    /// </summary>
    public static List<byte[]> DecodeFrames(string path, int width, int height)
    {
        byte[] all = RunTool("ffmpeg",
            ["-hide_banner", "-loglevel", "error", "-i", path, "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1"]);
        int frameBytes = width * height * 3;
        all.Length.Should().Be(all.Length / frameBytes * frameBytes, "the decoded video holds whole frames of {0}x{1}",
            width, height);
        var frames = new List<byte[]>();
        for (int start = 0; start < all.Length; start += frameBytes)
            frames.Add(all[start..(start + frameBytes)]);
        return frames;
    }

    /// <summary>How many frames ffprobe decodes from the video's first video stream.</summary>
    public static int CountFrames(string path) =>
        int.Parse(Probe(path, "stream=nb_read_frames", count: true), CultureInfo.InvariantCulture);

    /// <summary>The frame rate of the video's first video stream as ffprobe gives it, such as <c>30/1</c>.</summary>
    public static string FrameRate(string path) => Probe(path, "stream=r_frame_rate", count: false);

    /// <summary>The red, green and blue bytes of each pixel of RGBA pixels, in order: what the video keeps of them.</summary>
    public static byte[] Rgb(byte[] rgba)
    {
        byte[] rgb = new byte[rgba.Length / 4 * 3];
        for (int pixel = 0; pixel < rgba.Length / 4; pixel++)
        {
            rgb[pixel * 3] = rgba[pixel * 4];
            rgb[pixel * 3 + 1] = rgba[pixel * 4 + 1];
            rgb[pixel * 3 + 2] = rgba[pixel * 4 + 2];
        }

        return rgb;
    }

    /// <summary>The pixels of a decoded PNG as RGB: what a video frame of the same capture must hold.</summary>
    public static byte[] Rgb(string pngPath) =>
        Rgb(ImageResult.FromMemory(File.ReadAllBytes(pngPath), ColorComponents.RedGreenBlueAlpha).Data);

    private static string Probe(string path, string entries, bool count)
    {
        List<string> arguments = ["-v", "error", "-select_streams", "v:0", "-show_entries", entries, "-of", "csv=p=0"];
        if (count)
            arguments.Insert(0, "-count_frames");
        arguments.Add(path);
        return Encoding.ASCII.GetString(RunTool("ffprobe", arguments)).Trim();
    }

    /// <summary>Runs a tool of the dev shell and returns its standard output. Throws if it exits with a code other than 0.</summary>
    private static byte[] RunTool(string tool, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(tool)
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> errors = process.StandardError.ReadToEndAsync();
        using var output = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(output);
        process.WaitForExit();
        process.ExitCode.Should().Be(0, "{0} must read the video: {1}", tool, errors.Result);
        return output.ToArray();
    }
}
