namespace Tertius.Rendering.Tests;

/// <summary>
/// The order of the stages of a frame graph, and the bound on its passes: each pass needs a query of the frame's
/// timestamp pool.
/// </summary>
public sealed class FrameGraphTests
{
    [Fact]
    public void AFrameGraphRefusesAPassBeyondTheMostThatTheGpuTimerCanTime()
    {
        using var graph = new FrameGraph();
        for (int pass = 0; pass < GpuTimer.MaxPasses; pass++)
            graph.Add(FrameGraph.Stage.DirectLight, new EmptyPass());

        Action addOneMore = () => graph.Add(FrameGraph.Stage.DirectLight, new EmptyPass());

        addOneMore
            .Should()
            .Throw<InvalidOperationException>("the stamp after one more pass would fall past the end of the pool");
    }

    [Fact]
    public void TheDeformationRecordsBeforeEveryPassThatDrawsAMesh()
    {
        using var graph = new FrameGraph();
        graph.Add(FrameGraph.Stage.GBuffer, new NamedPass("G-buffer"));
        graph.Add(FrameGraph.Stage.EnvironmentFaces, new NamedPass("environment faces"));
        graph.Add(FrameGraph.Stage.ShadowMaps, new NamedPass("shadow maps"));
        graph.Add(FrameGraph.Stage.Deformation, new NamedPass("deformation"));

        graph
            .Passes.Select(pass => pass.Name)
            .First()
            .Should()
            .Be("deformation", "the shadows, the faces and the G-buffer draw the copies that it writes");
    }

    [Fact]
    public void TheEnvironmentMapsStagesRecordAfterTheShadowMapsAndBeforeTheImpostorCaptures()
    {
        using var graph = new FrameGraph();
        graph.Add(FrameGraph.Stage.ImpostorCaptures, new NamedPass("impostor captures"));
        graph.Add(FrameGraph.Stage.EnvironmentPrefilter, new NamedPass("environment prefilter"));
        graph.Add(FrameGraph.Stage.EnvironmentFaces, new NamedPass("environment faces"));
        graph.Add(FrameGraph.Stage.ShadowMaps, new NamedPass("shadow maps"));

        graph
            .Passes.Select(pass => pass.Name)
            .Should()
            .Equal(
                ["shadow maps", "environment faces", "environment prefilter", "impostor captures"],
                "a face samples this frame's cascades, the prefilter reads this frame's faces, and a capture bakes the map's light"
            );
    }

    [Fact]
    public void TheCandidatesTheDirectPassAndTheAccumulationRunInTurnAfterTheSurfaceTest()
    {
        using var graph = new FrameGraph();
        graph.Add(FrameGraph.Stage.AccumulateDirect, new NamedPass("accumulate direct"));
        graph.Add(FrameGraph.Stage.DirectLight, new NamedPass("direct light"));
        graph.Add(FrameGraph.Stage.LightCandidates, new NamedPass("light candidates"));
        graph.Add(FrameGraph.Stage.SurfaceTest, new NamedPass("surface test"));

        graph
            .Passes.Select(pass => pass.Name)
            .Should()
            .Equal(
                ["surface test", "light candidates", "direct light", "accumulate direct"],
                "the candidates read the pass bits, the direct pass their reservoirs, and the accumulation its samples"
            );
    }

    [Fact]
    public void TheOcclusionPassesRunAfterTheDirectAccumulationAndBeforeTheComposite()
    {
        using var graph = new FrameGraph();
        graph.Add(FrameGraph.Stage.Composite, new NamedPass("composite"));
        graph.Add(FrameGraph.Stage.AmbientOcclusion, new NamedPass("occlusion depth"));
        graph.Add(FrameGraph.Stage.AmbientOcclusion, new NamedPass("occlusion sectors"));
        graph.Add(FrameGraph.Stage.AccumulateDirect, new NamedPass("accumulate direct"));
        graph.Add(FrameGraph.Stage.DirectLight, new NamedPass("direct light"));
        graph.Add(FrameGraph.Stage.GBuffer, new NamedPass("G-buffer"));

        graph
            .Passes.Select(pass => pass.Name)
            .Should()
            .Equal(
                ["G-buffer", "direct light", "accumulate direct", "occlusion depth", "occlusion sectors", "composite"],
                "the occlusion passes read the finished G-buffer and the direct history, and the composite reads their "
                    + "estimate target"
            );
    }

    [Fact]
    public void TheDepthPyramidFollowsTheGBufferAndTheReflectionsComeBetweenTheCompositeAndTheTransparentPass()
    {
        using var graph = new FrameGraph();
        graph.Add(FrameGraph.Stage.Transparent, new NamedPass("transparent"));
        graph.Add(FrameGraph.Stage.Reflections, new NamedPass("radiance pyramid"));
        graph.Add(FrameGraph.Stage.Composite, new NamedPass("composite"));
        graph.Add(FrameGraph.Stage.AmbientOcclusion, new NamedPass("occlusion depth"));
        graph.Add(FrameGraph.Stage.DirectLight, new NamedPass("direct light"));
        graph.Add(FrameGraph.Stage.DepthPyramid, new NamedPass("depth pyramid"));
        graph.Add(FrameGraph.Stage.GBuffer, new NamedPass("G-buffer"));

        graph
            .Passes.Select(pass => pass.Name)
            .Should()
            .Equal(
                [
                    "G-buffer",
                    "depth pyramid",
                    "direct light",
                    "occlusion depth",
                    "composite",
                    "radiance pyramid",
                    "transparent",
                ],
                "the depth pyramid describes the finished distance, and the reflections read the lit image before glass"
            );
    }

    [Fact]
    public void TheScatterRunsAfterTheCompositeAndBeforeTheReflections()
    {
        using var graph = new FrameGraph();
        graph.Add(FrameGraph.Stage.Transparent, new NamedPass("transparent"));
        graph.Add(FrameGraph.Stage.Reflections, new NamedPass("radiance pyramid"));
        graph.Add(FrameGraph.Stage.SkinScatter, new NamedPass("scatter rows"));
        graph.Add(FrameGraph.Stage.SkinScatter, new NamedPass("scatter columns"));
        graph.Add(FrameGraph.Stage.Composite, new NamedPass("composite"));
        graph.Add(FrameGraph.Stage.DirectLight, new NamedPass("direct light"));

        graph
            .Passes.Select(pass => pass.Name)
            .Should()
            .Equal(
                ["direct light", "composite", "scatter rows", "scatter columns", "radiance pyramid", "transparent"],
                "the scatter blurs the diffuse light that the composite wrote, and a mirror shows the scattered skin"
            );
    }

    private sealed class NamedPass(string name) : RenderPass
    {
        public override string Name => name;

        public override void Record(FrameContext context) { }
    }

    private sealed class EmptyPass : RenderPass
    {
        public override string Name => "empty";

        public override void Record(FrameContext context) { }
    }
}
