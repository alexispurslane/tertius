using System.Runtime.InteropServices;
using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo scenes of skinning and morph targets (docs/plans/skinning-and-morphs.md, "Part 5"): <c>rigs</c>, whose
/// bending cylinder, morphing box and twisting cylinder sway, and <c>rigs-crowd</c>, whose 100 cylinders of 20,000
/// vertices measure the cost. Each posed instance draws a deformed copy, the sway is the same on every run, and each
/// scene's checked-in script passes the checks of a run.
/// </summary>
public sealed class RigsSceneTests : IDisposable
{
    private readonly World world = NewJoltWorld();

    public void Dispose() => world.Dispose();

    private static SceneInstance Build(World world, IRenderResources resources, string name) =>
        DemoScenes.All.Single(s => s.Name == name).Build(world, resources);

    /// <summary>
    /// The snapshot that the sync point of the last tick of <paramref name="world"/> published. A second build here would
    /// make its previous rig poses those of the same tick.
    /// </summary>
    private static RenderSnapshot Published(World world) =>
        world.RenderSnapshots.AcquireLatest() ?? world.RenderSnapshots.ReadBuffer;

    /// <summary>The bits of every joint pose and weight of <paramref name="snapshot"/>, current and previous.</summary>
    private static (float[] Joints, float[] Weights) RigPoseBits(RenderSnapshot snapshot) =>
        (
            Joints: MemoryMarshal.Cast<Pose, float>(CollectionsMarshal.AsSpan(snapshot.JointPoses)).ToArray(),
            Weights: [.. snapshot.MorphWeights]
        );

    [Theory]
    [InlineData("rigs", 3)]
    [InlineData("rigs-crowd", 100)]
    public void RigsSceneDeformsEveryPosedInstance(string name, int posed)
    {
        using VulkanRenderer renderer = GpuTesting.LikeAScriptedRun(64, 36);
        Build(world, renderer, name);
        world.Tick();
        RenderSnapshot snapshot = Published(world);

        renderer.Render(snapshot, alpha: 1);

        snapshot.RigPoses.Should().HaveCount(posed, "each swaying entity of the scene is a posed instance");
        renderer.LastFrameStats.DeformedInstances.Should().Be(posed);
        renderer.LastFrameStats.UndeformedPosedInstances.Should().Be(0);
    }

    [Theory]
    [InlineData("rigs", 3)]
    [InlineData("rigs-crowd", 100)]
    public void RigsSceneGivesTheSameRigPosesOnEveryRun(string name, int posed)
    {
        using World other = NewJoltWorld();
        Build(world, new NumberedResources(), name);
        Build(other, new NumberedResources(), name);

        for (int i = 0; i < 90; i++)
        {
            world.Tick();
            other.Tick();
        }
        RenderSnapshot snapshot = Published(world);
        (float[] joints, float[] weights) = RigPoseBits(snapshot);
        (float[] otherJoints, float[] otherWeights) = RigPoseBits(Published(other));

        snapshot.RigPoses.Should().HaveCount(posed, "each swaying entity of the scene is a posed instance");

        otherJoints.Should().Equal(joints);
        otherWeights.Should().Equal(weights);
    }

    [Theory]
    [InlineData("rigs", 3)]
    [InlineData("rigs-crowd", 100)]
    public void EveryPosedInstanceOfARigsSceneChangesItsRigPoseFromTickToTick(string name, int posed)
    {
        Build(world, new NumberedResources(), name);
        for (int i = 0; i < 20; i++)
            world.Tick();

        RenderSnapshot snapshot = Published(world);

        snapshot.RigPoses.Should().HaveCount(posed, "each swaying entity of the scene is a posed instance");
        snapshot
            .RigPoses.Should()
            .AllSatisfy(entry =>
            {
                bool jointsMoved = !snapshot.JointsOf(entry).SequenceEqual(snapshot.PreviousJointsOf(entry));
                bool weightsMoved = !snapshot.WeightsOf(entry).SequenceEqual(snapshot.PreviousWeightsOf(entry));
                (jointsMoved || weightsMoved).Should().BeTrue("every rig of the scene sways");
            });
    }

    /// <summary>
    /// A run checks every step and resolves every name before its first tick: the scene and each camera. The run stops
    /// before its first tick here, so it changes nothing.
    /// </summary>
    [Theory]
    [InlineData("rigs.json")]
    [InlineData("rigs-crowd.json")]
    public void TheCheckedInScriptOfEachRigsScenePassesTheChecksOfARun(string file)
    {
        var script = Script.Load(Path.Combine(RepositoryRoot(), "scripts", file));
        SceneInstance scene = Build(world, RenderResources.None, script.Scene);

        RunResult result = ScriptRunner.Run(
            script: script,
            world: world,
            scene: scene,
            outputFolder: null,
            playback: new StopsAtOnce()
        );

        result.Stopped.Should().BeTrue();
        script.Scene.Should().Be(Path.GetFileNameWithoutExtension(file), "each rigs scene has its own script");
    }

    /// <summary>
    /// Resources that give each mesh, texture and material its own id, so that the snapshot holds the scene's instances
    /// and their rig poses. The none ids of <see cref="RenderResources.None"/> draw nothing.
    /// </summary>
    private sealed class NumberedResources : IRenderResources
    {
        private int next = 1;

        public MeshId CreateMesh(MeshData mesh) => new(next++);

        public TextureId CreateTexture(TextureData texture) => new(next++);

        public MaterialId CreateMaterial(MaterialDesc material) => new(next++);
    }

    /// <summary>A playback that stops the run before its first tick.</summary>
    private sealed class StopsAtOnce : IPlayback
    {
        public bool WaitForTick() => false;

        public bool Capture(World world, string name) => false;
    }
}
