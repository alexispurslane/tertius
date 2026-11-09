using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo's lamp panels are rectangle lights whose face is the panel's glowing face (ARCHITECTURE.md, "Rectangle
/// lights"): the orbit ship's cabin lamps under its roof, and the bay lamps on the station's bay walls.
/// </summary>
public sealed class LampPanelTests : IDisposable
{
    private readonly World world = HostTesting.NewJoltWorld();

    public void Dispose() => world.Dispose();

    /// <summary>The scene of <paramref name="name"/>, built without a renderer.</summary>
    private SceneInstance Build(string name) => DemoScenes.All.Single(s => s.Name == name).Build(world, RenderResources.None);

    [Fact]
    public void TheCabinLampsAreRectangleLightsOnTheLowerFacesOfTheirPanels()
    {
        SceneInstance scene = Build("orbit");

        foreach (int i in Enumerable.Range(0, 5))
        {
            RenderData render = world.GetRender(scene[$"cabin-lamp-{i}"]);
            RectLightData light = render.Light is RectLightData face ? face : default;

            (light.Width, light.Height).Should().Be((1.2f, 1.2f), "the light is the panel's face, 1.2 m square");
            light.Power.Should().Be(10, "a cabin lamp sends 10 W, about 3,000 lumens");
            light.CastsShadow.Should().BeTrue("the light casts shadows; its panel does not darken it");
            (light.Offset.Position - new Double3(0, -0.05, 0))
                .Length.Should()
                .BeLessThan(1e-6, "the face lies on the panel's lower side, 0.05 m below its middle");
            (Vector3.Transform(Vector3.UnitZ, light.Offset.Rotation) - new Vector3(0, -1, 0))
                .Length()
                .Should()
                .BeLessThan(1e-6f, "the face shines down into the cabin");
        }
    }

    [Fact]
    public void TheBayLampsAreRectangleLightsThatFaceIntoTheBay()
    {
        SceneInstance scene = Build("demo-surface");
        Vector3[] inward = [-Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY];

        foreach (int i in Enumerable.Range(0, inward.Length))
        {
            RectLightData light = world.GetRender(scene[$"bay-lamp-{i}"]).Light is RectLightData face ? face : default;

            (light.Width, light.Height).Should().Be((1.2f, 1.2f), "the light is the panel's face, 1.2 m square");
            light.Power.Should().Be(100, "a bay lamp sends a floodlight's 100 W, about 30,000 lumens");
            (light.Offset.Position - inward[i] * 0.05f)
                .Length()
                .Should()
                .BeLessThan(1e-6f, "the face lies on the panel's side toward the bay");
            (Vector3.Transform(Vector3.UnitZ, light.Offset.Rotation) - inward[i])
                .Length()
                .Should()
                .BeLessThan(1e-6f, "the face shines into the bay");
        }
    }
}
