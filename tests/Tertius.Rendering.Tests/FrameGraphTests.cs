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
    public void TheOcclusionPassesRunAfterTheDirectPassAndBeforeTheAmbientPass()
    {
        using var graph = new FrameGraph();
        graph.Add(FrameGraph.Stage.AmbientLight, new NamedPass("ambient light"));
        graph.Add(FrameGraph.Stage.AmbientOcclusion, new NamedPass("occlusion depth"));
        graph.Add(FrameGraph.Stage.AmbientOcclusion, new NamedPass("occlusion sectors"));
        graph.Add(FrameGraph.Stage.DirectLight, new NamedPass("direct light"));
        graph.Add(FrameGraph.Stage.GBuffer, new NamedPass("G-buffer"));

        graph
            .Passes.Select(pass => pass.Name)
            .Should()
            .Equal(
                ["G-buffer", "direct light", "occlusion depth", "occlusion sectors", "ambient light"],
                "the occlusion passes read the finished G-buffer and the direct image, and the ambient pass reads their "
                    + "estimate target"
            );
    }

    [Fact]
    public void TheDepthPyramidFollowsTheGBufferAndTheReflectionsComeBetweenTheAmbientAndTheTransparentPass()
    {
        using var graph = new FrameGraph();
        graph.Add(FrameGraph.Stage.Transparent, new NamedPass("transparent"));
        graph.Add(FrameGraph.Stage.Reflections, new NamedPass("radiance pyramid"));
        graph.Add(FrameGraph.Stage.AmbientLight, new NamedPass("ambient light"));
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
                    "ambient light",
                    "radiance pyramid",
                    "transparent",
                ],
                "the depth pyramid describes the finished distance, and the reflections read the lit image before glass"
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
