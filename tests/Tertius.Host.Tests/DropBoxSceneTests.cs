using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The drop-box scene of the demo: a box falls onto a floor in a root frame with gravity. A script names the entities of a
/// scene by their scene ids, so the ids are part of what the scene offers.
/// </summary>
public sealed class DropBoxSceneTests : IDisposable
{
    private readonly World world = NewJoltWorld();

    public void Dispose() => world.Dispose();

    private SceneInstance Build() =>
        DemoScenes.All.Single(s => s.Name == "drop-box").Build(world, RenderResources.None);

    [Fact]
    public void TheSceneNamesItsGroundAsTheFrameThatHoldsTheFloorAndTheBox()
    {
        SceneInstance scene = Build();

        FrameHandle ground = world.GetOwnedFrame(scene["ground"]);
        world.GetFrame(scene["floor"]).Should().Be(ground);
        world.GetFrame(scene["box"]).Should().Be(ground);
    }

    [Fact]
    public void TheSceneNamesItsSunAsTheFrameThatTheLightComesFrom()
    {
        SceneInstance scene = Build();

        world.SunFrame.Should().Be(world.GetOwnedFrame(scene["sun"]));
    }
}
