using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The debug switch TERTIUS_DEBUG_REFLECTED_PLACE (ticket tiunr-wk29, <see cref="DebugReflectedPlace"/>): whether the
/// specular signal's reflected place competes with the surface place, seen through the accumulation trace of the real
/// screen tracer and accumulator on the plate and the glowing block of <see cref="AccumulationTraceTests"/>.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class DebugReflectedPlaceTests
{
    private const int Width = 128;
    private const int Height = 72;
    private const string Switch = "TERTIUS_DEBUG_REFLECTED_PLACE";
    private const string Agreement = "TERTIUS_REFLECTION_HISTORY_AGREEMENT";

    // Layer 0 of the trace and its flags (TemporalSignal.AccumulationTrace).
    private const int Lengths = 0;
    private const int HasSample = 1 << 8;
    private const int ReflectedTried = 1 << 5;

    /// <summary>A dark dielectric plate of roughness 0.2, the native captures' rough plate.</summary>
    private static readonly MaterialDesc Plate = new(
        BaseColor: new(new Vector3(0.06f), 1),
        Roughness: 0.2f,
        Reflectance: 0.5f,
        Model: new StandardShading(Metallic: 0)
    );

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("on")]
    public void UnsetEmptyAndOnKeepTheReflectedPlace(string? value)
    {
        DebugReflectedPlace selected = WithSwitch(value, () => DebugReflectedPlace.Selected());

        selected.Should().Be(DebugReflectedPlace.On);
    }

    [Fact]
    public void OffSelectsNoReflectedPlace()
    {
        DebugReflectedPlace selected = WithSwitch("off", () => DebugReflectedPlace.Selected());

        selected.Should().Be(DebugReflectedPlace.Off);
    }

    [Fact]
    public void AnUnknownSettingIsRefused()
    {
        Action select = () => WithSwitch("sometimes", () => DebugReflectedPlace.Selected());

        select.Should().Throw<ArgumentException>().WithMessage("*TERTIUS_DEBUG_REFLECTED_PLACE*");
    }

    [Fact]
    public void WithTheReflectedPlaceOffNoSampledPixelTriesIt()
    {
        float[] lengths = WithSwitch("off", () => TraceLengthsAfterJitteredStill(16));

        int[] sampled = [.. Enumerable.Range(0, Width * Height).Where(pixel => HasFlag(lengths, pixel, HasSample))];
        sampled.Length.Should().BeGreaterThan(Width * Height / 4);
        sampled.Should().OnlyContain(pixel => !HasFlag(lengths, pixel, ReflectedTried), "the reflected place never competes");
    }

    private static bool HasFlag(float[] layer, int pixel, int flag) => ((int)layer[4 * pixel + 3] & flag) != 0;

    /// <summary>Layer 0 of the accumulation trace after <paramref name="frames"/> jittered still draws under FSR.</summary>
    private static float[] TraceLengthsAfterJitteredStill(int frames)
    {
        string? previous = Environment.GetEnvironmentVariable(Agreement);
        Environment.SetEnvironmentVariable(Agreement, "control-trace");
        try
        {
            VulkanRenderer renderer = Gpu.Headless(
                width: Width,
                height: Height,
                framesInFlight: 1,
                cascadedLights: 0,
                screenTrace: ScreenTrace.Default,
                warmUpFrames: 0,
                upscaling: new Fsr3Upscaling(new FixedRenderScale(1), 0)
            );
            try
            {
                RenderSnapshot scene = ReflectionTesting.Gray(0.5f);
                scene.Instances.Add(ReflectionTesting.Floor(renderer, Plate));
                RenderInstance emitter = ReflectionTesting.Block(
                    box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: ReflectionTesting.Glow(renderer, new(4)),
                    center: new(0, 0, -5),
                    size: new(4, 1.5f, 1)
                );
                emitter.EntityIndex = 2;
                scene.Instances.Add(emitter);
                for (int frame = 0; frame < frames; frame++)
                {
                    scene.Tick++;
                    scene.Time += 1.0 / 60;
                    renderer.Render(scene, 1);
                }
                return renderer.ReadImageLayer(
                    renderer.ScreenTraceSignals.Specular.AccumulationTrace(0)
                        ?? throw new InvalidOperationException("The accumulation trace is off."),
                    Lengths
                );
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(Agreement, previous);
        }
    }

    private static T WithSwitch<T>(string? value, Func<T> action)
    {
        string? previous = Environment.GetEnvironmentVariable(Switch);
        Environment.SetEnvironmentVariable(Switch, value);
        try
        {
            return action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Switch, previous);
        }
    }
}
