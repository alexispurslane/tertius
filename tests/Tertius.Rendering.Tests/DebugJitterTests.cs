using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The debug switch TERTIUS_DEBUG_JITTER (ticket tiunr-4b2g, <see cref="DebugJitter"/>): which draws jitter their
/// projection, seen in the G-buffer's distance image of a still scene, which a jitter moves at every edge.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class DebugJitterTests
{
    private const int Width = 128;
    private const int Height = 72;
    private const string Switch = "TERTIUS_DEBUG_JITTER";

    private static readonly UpscalingSetting Fsr = new Fsr3Upscaling(new FixedRenderScale(1), 0);

    [Fact]
    public void WithoutUpscalingTheRenderersRuleDrawsTheSameDistanceInEveryDraw()
    {
        (float[] first, float[] second) = DistancesOfTwoDraws(null, new NoUpscaling());

        second.Should().Equal(first, "a draw without an upscale does not jitter");
    }

    [Fact]
    public void UnderFsrTheRenderersRuleJittersEveryDraw()
    {
        (float[] first, float[] second) = DistancesOfTwoDraws(null, Fsr);

        second.Should().NotEqual(first, "FSR's sequence moves the projection in each draw");
    }

    [Fact]
    public void TheAlwaysSettingJittersADrawWithoutUpscaling()
    {
        (float[] first, float[] second) = DistancesOfTwoDraws("always", new NoUpscaling());

        second.Should().NotEqual(first, "the setting takes FSR's sequence without FSR");
    }

    [Fact]
    public void TheNoneSettingDrawsUnderFsrWithoutJitter()
    {
        (float[] first, float[] second) = DistancesOfTwoDraws("none", Fsr);

        second.Should().Equal(first, "no draw jitters");
    }

    [Fact]
    public void AnUnknownSettingIsRefused()
    {
        string? previous = Environment.GetEnvironmentVariable(Switch);
        Environment.SetEnvironmentVariable(Switch, "sometimes");
        try
        {
            Action select = () => DebugJitter.Selected();

            select.Should().Throw<ArgumentException>().WithMessage("*TERTIUS_DEBUG_JITTER*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Switch, previous);
        }
    }

    /// <summary>The distance images of the third and the fourth draw of a still scene of a floor and a block.</summary>
    private static (float[] First, float[] Second) DistancesOfTwoDraws(string? value, UpscalingSetting upscaling)
    {
        string? previous = Environment.GetEnvironmentVariable(Switch);
        Environment.SetEnvironmentVariable(Switch, value);
        try
        {
            VulkanRenderer renderer = Gpu.Headless(
                width: Width,
                height: Height,
                framesInFlight: 1,
                cascadedLights: 0,
                warmUpFrames: 0,
                upscaling: upscaling
            );
            try
            {
                RenderSnapshot scene = ReflectionTesting.Gray(0.5f);
                scene.Instances.Add(
                    ReflectionTesting.Floor(
                        renderer,
                        new(
                            BaseColor: new(new Vector3(0.5f), 1),
                            Roughness: 0.5f,
                            Model: new StandardShading(Metallic: 0)
                        )
                    )
                );
                RenderInstance block = ReflectionTesting.Block(
                    box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: ReflectionTesting.Glow(renderer, new(4)),
                    center: new(0, 0, -5),
                    size: new(4, 1.5f, 1)
                );
                block.EntityIndex = 2;
                scene.Instances.Add(block);
                float[] first = [];
                for (int draw = 0; draw < 4; draw++)
                {
                    scene.Tick++;
                    scene.Time += 1.0 / 60;
                    renderer.Render(scene, 1);
                    if (draw == 2)
                        first = renderer.ReadDistance();
                }
                return (first, renderer.ReadDistance());
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(Switch, previous);
        }
    }
}
