namespace Tertius.Rendering.Tests;

/// <summary>
/// The frame graph: the order of its stages, and the bound on its passes, each of which needs a query of the frame's
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
    public void TheEnvironmentMapsStagesRecordAfterTheShadowMapsAndBeforeTheImpostorCaptures()
    {
        using var graph = new FrameGraph();
        graph.Add(FrameGraph.Stage.ImpostorCaptures, new EmptyPass("impostor captures"));
        graph.Add(FrameGraph.Stage.EnvironmentPrefilter, new EmptyPass("environment prefilter"));
        graph.Add(FrameGraph.Stage.EnvironmentFaces, new EmptyPass("environment faces"));
        graph.Add(FrameGraph.Stage.ShadowMaps, new EmptyPass("shadow maps"));

        graph
            .Passes.Select(pass => pass.Name)
            .Should()
            .Equal(
                ["shadow maps", "environment faces", "environment prefilter", "impostor captures"],
                "a face samples this frame's cascades, the prefilter reads this frame's faces, and a capture bakes the map's light"
            );
    }

    private sealed class EmptyPass(string name = "empty") : RenderPass
    {
        public override string Name => name;

        public override void Record(FrameContext context) { }
    }
}
