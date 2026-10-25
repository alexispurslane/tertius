namespace Tertius.Rendering.Tests;

/// <summary>The bound on the passes of a frame graph: each pass needs a query of the frame's timestamp pool.</summary>
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

    private sealed class EmptyPass : RenderPass
    {
        public override string Name => "empty";

        public override void Record(FrameContext context) { }
    }
}
