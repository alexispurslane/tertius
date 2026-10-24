using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The scene demo-meshes of the demo: a cylinder, a torus and a ring on a floor, each with a ball that drops onto it. The
/// script demo-meshes captures each ball at rest, so each must come to rest on its solid, where the solid's mesh is.
/// </summary>
public sealed class DemoMeshesSceneTests : IDisposable
{
    private readonly World world = NewJoltWorld();
    private readonly SceneInstance scene;

    public DemoMeshesSceneTests()
    {
        scene = DemoScenes.All.Single(s => s.Name == "demo-meshes").Build(world, RenderResources.None);
        // Each ball drops 0.5 m, which takes 0.32 s, and settles well within 3 s.
        for (int i = 0; i < 180; i++)
            world.Tick();
    }

    public void Dispose() => world.Dispose();

    private Double3 RestingCenter(string ball)
    {
        FrameHandle ground = world.GetOwnedFrame(scene["ground"]);
        world.VelocityIn(scene[ball], ground).Length.Should().BeLessThan(0.01, $"{ball} has come to rest");
        return world.PoseIn(scene[ball], ground).Position;
    }

    /// <summary>A ball rests on a facet within 1 mm of where the mesh puts it (see RevolvedSolidShapeTests).</summary>
    private static void ExpectAt(Double3 actual, Double3 expected, string because) =>
        (actual - expected)
            .Length.Should()
            .BeLessThan(1e-3, $"{because}: it is at {actual}, and the mesh puts it at {expected}");

    [Fact]
    public void TheCylindersBallRestsOnItsTopCapWhereItLanded() =>
        // The cylinder stands on the floor, so its top is twice its half height up.
        ExpectAt(RestingCenter("cylinder-ball"), new(-4.7, 1.5 + 0.25, 0), "the ball rests on the cap");

    [Fact]
    public void TheTorussBallRestsOnTheFlatTopOfItsTube()
    {
        // The torus lies on the flat bottom of its tube, r cos(π / 24) below the tube's center, so the flat top is twice that up.
        double top = 2 * 0.5 * Math.Cos(Math.PI / 24);
        double middle = 1.6 * Math.Cos(Math.PI / 64);
        ExpectAt(
            RestingCenter("torus-ball"),
            new(middle * Math.Cos(Math.PI / 64), top + 0.25, -middle * Math.Sin(Math.PI / 64)),
            "the ball rests on the tube"
        );
    }

    [Fact]
    public void TheRingsBallRestsInsideItOnTheMiddleOfItsLowestFacet()
    {
        // The ring stands on the middle of an outer facet, 3.25 cos(π / 256) below its axis, and the lowest inner facet is
        // 3 cos(π / 256) below the axis.
        double facet = Math.Cos(Math.PI / 256);
        ExpectAt(
            RestingCenter("ring-ball"),
            new(6, 3.25 * facet - 3 * facet + 0.25, 0),
            "the ball rests on the ring's floor"
        );
    }
}
