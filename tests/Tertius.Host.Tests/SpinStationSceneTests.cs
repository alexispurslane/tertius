using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The light of the spin-station scene: a hub lamp on the station's spin axis inside the ring, which lights the floor
/// radially and turns with the station (docs/plans/local-light-shadows.md, decision 17's settlement).
/// </summary>
public sealed class SpinStationSceneTests : IDisposable
{
    private readonly World world = NewJoltWorld();

    public void Dispose() => world.Dispose();

    private SceneInstance Build() =>
        DemoScenes.All.Single(s => s.Name == "spin-station").Build(world, RenderResources.None);

    [Fact]
    public void TheHubLampSitsOnTheSpinAxis25MetersFromTheStationsCenter()
    {
        SceneInstance scene = Build();

        Double3 place = world.PoseIn(scene["hub-lamp"], world.GetOwnedFrame(scene["station"])).Position;

        place.Should().Be(new Double3(0, 25, 0), "the lamp sits where the old sun frame sat, inside the station");
    }

    [Fact]
    public void TheHubLampIsAPointLightThatCastsShadows()
    {
        SceneInstance scene = Build();

        world
            .GetRender(scene["hub-lamp"])
            .Light.Value.Should()
            .BeOfType<PointLightData>()
            .Which.CastsShadow.Should()
            .BeTrue("the atlas of a later part gives the lamp shadows without a change of the scene");
    }

    [Fact]
    public void TheHubLampSendsTheOldSunsIrradianceToTheFloor()
    {
        SceneInstance scene = Build();
        var lamp = (PointLightData)world.GetRender(scene["hub-lamp"]).Light.Value!;

        // The floor below the lamp's side of the ring lies 100 m from the axis, so sqrt(100² + 25²) m from the lamp.
        double distanceSquared = 100.0 * 100 + 25 * 25;
        double irradiance = lamp.Power / (4 * Math.PI * distanceSquared);

        irradiance.Should().BeApproximately(0.6, 0.01, "the floor keeps the 0.6 W/m² of the old dim sun");
    }
}
