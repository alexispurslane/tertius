using System.Numerics;
using Tertius.ReflectionReference;

namespace Tertius.Rendering.Tests;

/// <summary>The history capture's motion, emission, clock and PNG schedule without renderer draws.</summary>
public sealed class ReflectionHistoryScheduleTests
{
    [Theory]
    [InlineData("original", "Original")]
    [InlineData("capped", "PreciseCapped")]
    [InlineData("uncapped", "PreciseUncapped")]
    public void TheCaptureCommandSelectsTheRequestedControlWithoutCreatingAnOutput(string control, string expected)
    {
        string path = Path.Combine(Path.GetTempPath(), $"tertius-history-parse-{Guid.NewGuid():N}");

        var request = HistoryRequest.Parse(["--history", "static", "rough", control, "--out", path]);

        request.Control.ToString().Should().Be(expected);
        request.WarmUpDraws.Should().Be(1024);
        request.Output.Should().Be(path);
        Directory.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void AnExplicitWarmUpChangesTheContinuousClockBeforeAnyCapture()
    {
        var request = HistoryRequest.Parse([
            "--history",
            "move",
            "smooth",
            "uncapped",
            "--warmup",
            "2048",
            "--out",
            "unused",
        ]);

        new HistorySequence(request.Event, request.WarmUpDraws, request.Camera).At(0).Tick.Should().Be(2049);
    }

    [Fact]
    public void TheLoadedRendererAndProbeSourcesMatchTheirCompiledIdentities()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tertius-history-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            Dictionary<string, string> identities = Program.RequireHistorySources(path);

            identities.Keys.Should().Contain(typeof(VulkanRenderer).Assembly.Location);
            identities.Keys.Should().Contain(typeof(HistorySequence).Assembly.Location);
            identities
                .Keys.Should()
                .Contain(file => file.EndsWith("reflection_sharing_prototype.glsl", StringComparison.Ordinal));
            identities.Keys.Should().Contain(file => file.EndsWith("Program.History.cs", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }

    [Theory]
    [InlineData(59, -1, -1)]
    [InlineData(60, -0.9666667f, -1)]
    [InlineData(89, 0, -0.03333333f)]
    [InlineData(119, 1, 0.9666667f)]
    [InlineData(120, 1, 1)]
    public void TheEmitterCrossesTwoMetersOverSixtyFramesWithTheCorrectPreviousPosition(
        int frame,
        float position,
        float previous
    )
    {
        var schedule = new HistorySequence(HistoryEvent.Move, 1024, HistoryCamera.Still);

        HistoryFrameState state = schedule.At(frame);

        state.EmitterX.Should().BeApproximately(position, 0.000001f);
        state.PreviousEmitterX.Should().BeApproximately(previous, 0.000001f);
        state.Emission.Should().Be(6800);
    }

    [Theory]
    [InlineData("Off", 59, 6800)]
    [InlineData("Off", 60, 0)]
    [InlineData("Off", 239, 0)]
    [InlineData("Dim", 59, 6800)]
    [InlineData("Dim", 60, 5440)]
    public void AnEmissionEventChangesOnlyEmissionAtFrameSixty(string change, int frame, float emission)
    {
        var schedule = new HistorySequence(Enum.Parse<HistoryEvent>(change), 1024, HistoryCamera.Still);

        HistoryFrameState state = schedule.At(frame);

        state.Emission.Should().Be(emission);
        state.EmitterX.Should().Be(-1);
        state.PreviousEmitterX.Should().Be(-1);
    }

    [Fact]
    public void TheClockContinuesFromWarmUpWithoutASecondInitialTick()
    {
        var schedule = new HistorySequence(HistoryEvent.Move, 1024, HistoryCamera.Still);

        HistoryFrameState lastWarmUp = schedule.At(-1);
        HistoryFrameState firstMeasured = schedule.At(0);

        lastWarmUp.Tick.Should().Be(1024);
        firstMeasured.Tick.Should().Be(1025);
        (firstMeasured.Time - lastWarmUp.Time).Should().BeApproximately(1.0 / 60, 1e-12);
        lastWarmUp.EmitterX.Should().Be(-1);
    }

    [Theory]
    [InlineData("Static", -1, false)]
    [InlineData("Static", 0, true)]
    [InlineData("Static", 1, false)]
    [InlineData("Static", 127, true)]
    [InlineData("Move", -1, false)]
    [InlineData("Move", 0, true)]
    [InlineData("Move", 239, true)]
    [InlineData("Off", 60, true)]
    public void StaticSavesOnlyTheFirstAndLastPngWhileDynamicSavesEveryMeasuredPng(string change, int frame, bool save)
    {
        var schedule = new HistorySequence(Enum.Parse<HistoryEvent>(change), 1024, HistoryCamera.Still);

        schedule.At(frame).SavePng.Should().Be(save);
        schedule.MeasuredFrames.Should().Be(change == "Static" ? 128 : 240);
    }

    [Fact]
    public void TheCaptureCommandSelectsFsrAndTheWalkingCamera()
    {
        var request = HistoryRequest.Parse([
            "--history",
            "static",
            "rough",
            "uncapped",
            "--out",
            "unused",
            "--upscaling",
            "fsr",
            "--camera",
            "walk",
        ]);

        request.Fsr.Should().BeTrue();
        request.Camera.Should().Be(HistoryCamera.Walk);
    }

    [Fact]
    public void ACaptureWithoutOptionsDrawsWithoutUpscalingFromAStillCamera()
    {
        var request = HistoryRequest.Parse(["--history", "static", "rough", "uncapped", "--out", "unused"]);

        request.Fsr.Should().BeFalse();
        request.Camera.Should().Be(HistoryCamera.Still);
    }

    [Theory]
    [InlineData("upscaler", "Upscaler")]
    [InlineData("none", "None")]
    [InlineData("always", "Always")]
    [InlineData("held", "Held")]
    public void TheJitterOptionSelectsTheDebugJitter(string value, string expected)
    {
        var request = HistoryRequest.Parse([
            "--history",
            "static",
            "rough",
            "uncapped",
            "--out",
            "unused",
            "--upscaling",
            "fsr",
            "--jitter",
            value,
        ]);

        request.Jitter.ToString().Should().Be(expected);
    }

    [Fact]
    public void ACaptureWithoutAJitterOptionKeepsTheRenderersJitterRule()
    {
        var request = HistoryRequest.Parse(["--history", "static", "rough", "uncapped", "--out", "unused"]);

        request.Jitter.Should().Be(DebugJitter.Upscaler);
    }

    [Fact]
    public void ThePngOptionAsksForEveryMeasuredFrame()
    {
        var request = HistoryRequest.Parse([
            "--history",
            "static",
            "smooth",
            "uncapped",
            "--out",
            "unused",
            "--png",
            "all",
        ]);

        request.EveryPng.Should().BeTrue();
    }

    [Fact]
    public void ACaptureWithoutAPngOptionSavesOnlyTheScheduledFrames()
    {
        var request = HistoryRequest.Parse(["--history", "static", "rough", "uncapped", "--out", "unused"]);

        request.EveryPng.Should().BeFalse();
    }

    [Fact]
    public void AStillCaptureThatAsksForEveryPngSavesEveryMeasuredFrame()
    {
        var request = HistoryRequest.Parse([
            "--history",
            "static",
            "smooth",
            "uncapped",
            "--out",
            "unused",
            "--png",
            "all",
        ]);

        Enumerable.Range(0, 128).Should().OnlyContain(frame => request.SavesPng(frame));
        request.SavesPng(-1).Should().BeFalse("a warm-up draw is not measured");
    }

    [Fact]
    public void AStillCaptureWithoutThePngOptionSavesOnlyItsFirstAndLastFrame()
    {
        var request = HistoryRequest.Parse(["--history", "static", "smooth", "uncapped", "--out", "unused"]);

        Enumerable.Range(-1, 129).Where(request.SavesPng).Should().Equal(0, 127);
    }

    [Fact]
    public void AWalkingCameraNeedsAStillEmitter()
    {
        Action parse = () =>
            HistoryRequest.Parse(["--history", "move", "rough", "uncapped", "--out", "unused", "--camera", "walk"]);

        parse.Should().Throw<ArgumentException>().WithMessage("*static event*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(59)]
    public void TheWalkingCameraStandsAtTheStillPoseBeforeFrameSixty(int frame)
    {
        HistoryFrameState state = new HistorySequence(HistoryEvent.Static, 1024, HistoryCamera.Walk).At(frame);

        state.Camera.Should().Be(new HistorySequence(HistoryEvent.Static, 1024, HistoryCamera.Still).At(frame).Camera);
        state.Camera.Position.Should().Be(new Vector3(0, 3, 6));
        state.Camera.YawDegrees.Should().Be(0);
    }

    [Fact]
    public void TheStillPoseIsTheComparisonScenesPitchedCamera()
    {
        HistoryPose still = new HistorySequence(HistoryEvent.Static, 1024, HistoryCamera.Still).At(0).Camera;

        still
            .Rotation.Should()
            .Be(
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 6),
                "the still captures must repeat the sealed ones"
            );
    }

    [Theory]
    [InlineData(60, 1 / 6f)]
    [InlineData(90, 31 / 6f)]
    [InlineData(119, 10)]
    [InlineData(239, 10)]
    public void TheWalkingCameraPansLeftAtTenDegreesPerSecondToTenDegrees(int frame, float yaw)
    {
        HistoryFrameState state = new HistorySequence(HistoryEvent.Static, 1024, HistoryCamera.Walk).At(frame);

        state.Camera.YawDegrees.Should().BeApproximately(yaw, 1e-5f);
        var forward = Vector3.Transform(-Vector3.UnitZ, state.Camera.Rotation);
        forward
            .X.Should()
            .BeLessThanOrEqualTo(0, "a positive yaw about +Y turns the view toward -x, the emitter's side");
    }

    [Fact]
    public void TheWalkingCameraHoldsItsPlaceDuringThePan()
    {
        var schedule = new HistorySequence(HistoryEvent.Static, 1024, HistoryCamera.Walk);

        schedule.At(119).Camera.Position.Should().Be(new Vector3(0, 3, 6));
    }

    [Fact]
    public void TheWalkingCameraWalksToItsRightAtOneMeterPerSecondAfterThePan()
    {
        var schedule = new HistorySequence(HistoryEvent.Static, 1024, HistoryCamera.Walk);

        Vector3 step = schedule.At(179).Camera.Position - schedule.At(119).Camera.Position;

        step.Length().Should().BeApproximately(1, 1e-5f, "sixty frames at 60 frames per second take one second");
        var right = Vector3.Transform(Vector3.UnitX, schedule.At(179).Camera.Rotation);
        Vector3.Dot(Vector3.Normalize(step), right).Should().BeApproximately(1, 1e-5f);
        step.Y.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(200)]
    public void EachFramesPreviousCameraIsTheLastFramesCamera(int frame)
    {
        var schedule = new HistorySequence(HistoryEvent.Static, 1024, HistoryCamera.Walk);

        schedule.At(frame).PreviousCamera.Should().Be(schedule.At(frame - 1).Camera);
    }

    [Fact]
    public void AWalkMeasuresTwoHundredFortyFramesAndSavesEveryPng()
    {
        var schedule = new HistorySequence(HistoryEvent.Static, 1024, HistoryCamera.Walk);

        schedule.MeasuredFrames.Should().Be(240);
        Enumerable.Range(0, 240).Should().OnlyContain(frame => schedule.At(frame).SavePng);
    }
}
