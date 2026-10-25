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
            graph.Add(FrameGraph.Stage.Lighting, new EmptyPass());

        Action addOneMore = () => graph.Add(FrameGraph.Stage.Lighting, new EmptyPass());

        addOneMore
            .Should()
            .Throw<InvalidOperationException>("the stamp after one more pass would fall past the end of the pool");
    }

    [Fact]
    public void TheOcclusionPassesRunAfterTheGBufferPassAndBeforeTheLightingPass()
    {
        using var graph = new FrameGraph();
        graph.Add(FrameGraph.Stage.Lighting, new NamedPass("lighting"));
        graph.Add(FrameGraph.Stage.AmbientOcclusion, new NamedPass("occlusion depth"));
        graph.Add(FrameGraph.Stage.AmbientOcclusion, new NamedPass("occlusion horizons"));
        graph.Add(FrameGraph.Stage.GBuffer, new NamedPass("G-buffer"));

        graph
            .Passes.Select(pass => pass.Name)
            .Should()
            .Equal(
                ["G-buffer", "occlusion depth", "occlusion horizons", "lighting"],
                "the occlusion passes read the finished G-buffer, and the lighting pass reads their occlusion"
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
