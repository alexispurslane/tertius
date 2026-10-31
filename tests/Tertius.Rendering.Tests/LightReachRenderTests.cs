using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.ExposureScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// A light's reach under the automatic exposure (ARCHITECTURE.md, "Shading"): it ends at the cutoff of the highest
/// exposure that the frame can use or its adapt step can write, which the renderer derives from the exposure of the last
/// finished frame. So a lamp that the settled exposure cannot show leaves the light list, and the metering draw of a still
/// after a camera cut meters every lamp that its new exposure can show.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class LightReachRenderTests
{
    /// <summary>The Sun's irradiance at 1 AU, in W/m²: the solar constant.</summary>
    private const float SolarConstant = 1361;

    /// <summary>The radiant power of a household bulb of 800 lumens, in W.</summary>
    private const float HouseholdBulbPower = 2.5f;

    /// <summary>A film at 60 frames per second of snapshot time.</summary>
    private const double FrameSeconds = 1.0 / 60;

    /// <summary>A household bulb at <paramref name="position"/>.</summary>
    private static PointRenderLight Bulb(Vector3 position) =>
        Gpu.Lamp(
            position: position,
            color: Vector3.One,
            intensity: HouseholdBulbPower / (4 * MathF.PI),
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>
    /// A matte white square that fills the view <see cref="Ahead"/>, lit by the Sun behind the camera, which wants about
    /// −11 stops, with a household bulb 50 m behind the camera, whose reach is about 730 m at +10 stops.
    /// </summary>
    private static RenderSnapshot SunlitSquareWithABulbBehind(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: SolarConstant);
        AddGraySquare(renderer, snapshot);
        snapshot.Lights.Add(Bulb(new(0, 0, 50)));
        return snapshot;
    }

    /// <summary>The gray square lit by a dim star behind the camera, which wants about −8 stops.</summary>
    private static RenderSnapshot DimSunlitSquare(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 290);
        AddGraySquare(renderer, snapshot);
        return snapshot;
    }

    /// <summary>
    /// The gray square in the dark, 3 m from a lamp of 0.59 W/sr at the eye, which wants about +4 stops, 12 stops above
    /// <see cref="DimSunlitSquare"/>. At −8 stops the lamp's reach is 2.45 m, so it does not reach the square.
    /// </summary>
    private static RenderSnapshot LampLitSquare(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0);
        snapshot.Ambient.Intensity = 0;
        AddGraySquare(renderer, snapshot);
        snapshot.Lights.Add(
            Gpu.Lamp(position: Vector3.Zero, color: Vector3.One, intensity: 0.59f, chainDepth: TestChains.CamerasFrame)
        );
        return snapshot;
    }

    [Fact]
    public void UnderTheSunABulbThatCannotLightTheViewLeavesTheLightListOnceTheExposureSettles()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: 2,
            exposure: AutoExposure.Default
        );
        try
        {
            RenderSnapshot snapshot = SunlitSquareWithABulbBehind(renderer);
            Film(renderer: renderer, snapshot: snapshot, start: 0, frameSeconds: FrameSeconds, frames: 4);

            renderer.LastFrameStats.Lights.Should().Be(1, "only the Sun can show beside a sunlit square");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AStillAfterACutIntoALampLitRoomSettlesAsAFreshRendererDoes()
    {
        float fresh = SettledStops(AutoExposure.Default, LampLitSquare);
        VulkanRenderer renderer = Renderer(AutoExposure.Default);
        try
        {
            Film(
                renderer: renderer,
                snapshot: DimSunlitSquare(renderer),
                start: 0,
                frameSeconds: FrameSeconds,
                frames: 4
            );
            // At the tick of the frame before: a camera cut, which the metering draw settles in one endless step. Its
            // meter must see the lamp's light on the square, which the exposure before the cut cannot show.
            FilmFrame cut = Still(renderer: renderer, snapshot: LampLitSquare(renderer), time: 3 * FrameSeconds);

            cut.Stops.Should().BeApproximately(fresh, 1f / 16);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
