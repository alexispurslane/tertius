using System.Numerics;
using Tertius.Assets;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The scene <c>art-preview</c> (<see cref="DemoScenes.ArtPreview"/>): it places every Blue Danube model through one
/// <see cref="ModelStore"/>, so repeated placements share one renderer model, and the converted station keeps the window
/// emission and the gallery normal map of its overlay. The tests that need the converted art skip without it.
/// </summary>
public sealed class ArtPreviewTests : IDisposable
{
    private readonly World world = NewJoltWorld();
    private readonly RecordingResources resources = new();

    public void Dispose() => world.Dispose();

    private static ContentFolders ConvertedArt()
    {
        ContentFolders? content = ContentFolders.Find();
        Assert.SkipWhen(
            BlueDanubeArt.Missing(content).Count > 0,
            $"The Blue Danube art is not converted: run `{BlueDanubeArt.SetupCommand}`."
        );
        return content ?? throw new InvalidOperationException("Missing gives every model when no content root exists.");
    }

    private SceneInstance Build(ContentFolders content) => DemoScenes.ArtPreview(content).Build(world, resources);

    [Fact]
    public void BothShuttlesDrawOneModel()
    {
        SceneInstance scene = Build(ConvertedArt());

        ModelId shuttle = world.GetRender(scene["shuttle-a"]).Model;

        shuttle.IsNone.Should().BeFalse();
        world.GetRender(scene["shuttle-b"]).Model.Should().Be(shuttle);
    }

    [Fact]
    public void EveryStoolDrawsOneModel()
    {
        SceneInstance scene = Build(ConvertedArt());

        ModelId[] stools = [.. Enumerable.Range(0, 4).Select(i => world.GetRender(scene[$"stool-{i}"]).Model)];

        stools.Distinct().Should().ContainSingle().Which.IsNone.Should().BeFalse();
    }

    [Fact]
    public void EachModelIsUploadedOnce()
    {
        Build(ConvertedArt());

        resources.Models.Should().HaveCount(BlueDanubeArt.All.Count);
    }

    [Fact]
    public void APlacedShuttleHoldsItsWholeHullInItsBoundingRadius()
    {
        SceneInstance scene = Build(ConvertedArt());

        // The nose, the farthest point, lies 10.01 m forward of the origin and a little above it.
        world.GetRender(scene["shuttle-a"]).BoundingRadius.Should().BeInRange(10.01f, 10.5f);
    }

    [Fact]
    public void WithoutTheArtTheSceneKeepsItsViewsAndPlacesNoModel()
    {
        SceneInstance scene = Build(new ContentFolders(NewFolder()));

        scene.Views.Should().Contain(["station-view", "shuttle-front", "passenger-front"]);
        scene.EntityOf("shuttle-a").Should().BeNull();
        resources.Models.Should().BeEmpty();
    }

    [Fact]
    public void TheStationKeepsItsLitWindowsAndItsGalleryRelief()
    {
        var store = new ModelStore(resources, ConvertedArt());

        store.Load(BlueDanubeArt.Station).Ok.Should().NotBeNull();

        MaterialDesc[] materials = [.. resources.Materials.Values];
        materials.Should().Contain(m => m.Emissive == new Vector3(0.92f, 0.65f, 0.36f), "the habitat's cabin windows glow");
        materials
            .Should()
            .Contain(
                m => !m.NormalMap.IsNone && m.Model == new StandardShading(Metallic: 0.35f),
                "the habitat's gallery panels keep their normal map"
            );
    }
}
