using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The meshes that the back-face pass draws in the demo's scripted scenes (docs/plans/back-face-depth-layer.md, decision
/// 5). The pass draws both faces of an open mesh, so an open mesh with a one-sided material loses no back face, but the
/// demo has none today. Entities that a scene spawns after its build are not counted.
/// </summary>
public sealed class ScriptedSceneClosureTests
{
    /// <summary>
    /// Each entity that the build of <paramref name="scene"/> draws with a one-sided material of the opaque passes, by its
    /// scene id, and whether its mesh is closed.
    /// </summary>
    private static IEnumerable<(string Entity, bool Closed)> OneSidedOpaqueMeshes(Scene scene)
    {
        var resources = new RecordingResources();
        using World world = NewJoltWorld();
        SceneInstance instance = scene.Build(world, resources);
        var entities = new List<Entity>();
        world.CopyLiveEntities(entities);
        var drawn = new List<(string Entity, bool Closed)>();
        foreach (Entity entity in entities)
        {
            RenderData render = world.GetRender(entity);
            if (render.Mesh.IsNone || (render.Flags & RenderFlags.Visible) == 0)
                continue;
            // An entity of no material draws with the default material: opaque and one-sided.
            bool oneSidedOpaque =
                render.Material.IsNone
                || resources.Materials[render.Material] is { Alpha: not GlassAlpha } material
                    && !MaterialStore.DrawsBothSides(material.Model);
            if (!oneSidedOpaque)
                continue;
            // The flag that the batcher reads, so that the census counts what the pass draws.
            bool closed = StagedMesh.Of(resources.Meshes[render.Mesh]).Closed;
            drawn.Add(($"{scene.Name}: {instance.IdOf(entity)}", closed));
        }

        return drawn;
    }

    [Fact]
    public void NoScriptedSceneDrawsAnOpenMeshWithAOneSidedOpaqueMaterial()
    {
        (string Entity, bool Closed)[] drawn =
        [
            .. Directory
                .GetFiles(Path.Combine(RepositoryRoot(), "scripts"), "*.json")
                .Select(path => Script.Load(path).Scene)
                .Distinct()
                .Order(StringComparer.Ordinal)
                .SelectMany(name => OneSidedOpaqueMeshes(DemoScenes.All.Single(scene => scene.Name == name))),
        ];

        drawn.Should().HaveCountGreaterThan(500, "the scripted scenes draw hundreds of one-sided opaque entities");
        drawn
            .Where(entity => !entity.Closed)
            .Should()
            .BeEmpty("the stage 2 census of the back-face layer found no such mesh (stage-2/meshes.txt)");
    }
}
