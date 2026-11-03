using System.Reflection;
using System.Text.Json;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The golden values of FSR 3.1.4's host calculations, which tools/fsr3-golden/ prints from AMD's own host code
/// (tools/README.md, "FSR 3.1 golden values"). The file is the tool's output, embedded unchanged.
/// </summary>
internal sealed record Fsr3Golden(
    string Source,
    Fsr3Golden.PhaseCountCase[] PhaseCounts,
    Fsr3Golden.OffsetCase[] Offsets,
    Fsr3Golden.Layout ConstantsLayout,
    Fsr3Golden.Sequence[] Sequences
)
{
    private static readonly Lazy<Fsr3Golden> Loaded = new(() =>
    {
        using Stream stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream("Tertius.Rendering.Tests.Fsr3Golden.json")
            ?? throw new FileNotFoundException("Fsr3Golden.json is not embedded.");
        return JsonSerializer.Deserialize<Fsr3Golden>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Fsr3Golden.json is empty.");
    });

    public static Fsr3Golden Values => Loaded.Value;

    internal sealed record PhaseCountCase(int RenderWidth, int OutputWidth, int PhaseCount);

    internal sealed record OffsetCase(int Index, int PhaseCount, float X, float Y);

    internal sealed record Layout(int Size, int JitterOffset, int TanHalfFov, int DeltaPreExposure);

    internal sealed record Resource(string Name, string Format, uint Width, uint Height, uint MipCount);

    internal sealed record Job(string Kind, string Label, string? Target, float[]? Color, uint X, uint Y);

    internal sealed record Frame(
        uint RenderWidth,
        uint RenderHeight,
        float JitterX,
        float JitterY,
        float FrameMilliseconds,
        float PreExposure,
        float VerticalFov,
        bool Reset,
        uint[] ReactiveWords,
        Dictionary<string, float> Constants,
        uint[] SpdWords,
        Job[] Jobs
    );

    internal sealed record Sequence(
        string Name,
        uint MaxRenderWidth,
        uint MaxRenderHeight,
        uint OutputWidth,
        uint OutputHeight,
        float NearMeters,
        Resource[] Resources,
        Resource[] SharedResources,
        Frame[] Frames
    );
}
