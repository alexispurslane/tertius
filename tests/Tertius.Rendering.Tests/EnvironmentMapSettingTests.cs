namespace Tertius.Rendering.Tests;

/// <summary>
/// The checks of an <see cref="EnvironmentMapSetting"/> and of <see cref="RendererOptions.EnvironmentMap"/>
/// (docs/plans/environment-map.md, "The setting").
/// </summary>
public sealed class EnvironmentMapSettingTests
{
    [Fact]
    public void TheZeroValueOfTheSettingIsNoEnvironmentMap() =>
        default(EnvironmentMapSetting).Value.Should().Be(new NoEnvironmentMap());

    [Fact]
    public void NoEnvironmentMapHasNoProblem() =>
        new EnvironmentMapSetting(new NoEnvironmentMap()).Problem.Should().BeNull();

    [Fact]
    public void TheRenderersDefaultIsAMapOf128TexelsPerFaceThatDrawsOneFacePerFrame() =>
        new RendererOptions()
            .EnvironmentMap.Value.Should()
            .Be(new SceneEnvironmentMap(FaceSize: 128, FacesPerFrame: 1), "the user chose 128 texels and one face");

    [Fact]
    public void TheDefaultMapHasNoProblem() =>
        new EnvironmentMapSetting(SceneEnvironmentMap.Default).Problem.Should().BeNull();

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(128)]
    [InlineData(1024)]
    public void AFaceSizeThatIsAPowerOfTwoFrom16To1024HasNoProblem(int faceSize) =>
        new EnvironmentMapSetting(SceneEnvironmentMap.Default with { FaceSize = faceSize }).Problem.Should().BeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(-16)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(48)]
    [InlineData(1023)]
    [InlineData(2048)]
    [InlineData(int.MinValue)]
    public void AFaceSizeThatIsNotAPowerOfTwoFrom16To1024IsAProblemThatNamesIt(int faceSize) =>
        new EnvironmentMapSetting(SceneEnvironmentMap.Default with { FaceSize = faceSize })
            .Problem.Should()
            .Be($"SceneEnvironmentMap.FaceSize must be a power of two from 16 to 1024, not {faceSize}.");

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void FacesPerFrameFrom1To6HaveNoProblem(int facesPerFrame) =>
        new EnvironmentMapSetting(SceneEnvironmentMap.Default with { FacesPerFrame = facesPerFrame })
            .Problem.Should()
            .BeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7)]
    public void FacesPerFrameOutside1To6AreAProblemThatNamesThem(int facesPerFrame) =>
        new EnvironmentMapSetting(SceneEnvironmentMap.Default with { FacesPerFrame = facesPerFrame })
            .Problem.Should()
            .Be($"SceneEnvironmentMap.FacesPerFrame must be 1 to 6, not {facesPerFrame}.");

    [Fact]
    public void AMapOfZerosIsAProblemBecauseARecordStructsZerosAreNotItsDefaults() =>
        new EnvironmentMapSetting(new SceneEnvironmentMap()).Problem.Should().NotBeNull();

    [Fact]
    public void TheRendererRefusesAMapSettingWithAProblemAndGivesTheProblem() =>
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = 64,
                        Height = 64,
                        EnvironmentMap = SceneEnvironmentMap.Default with { FaceSize = 48 },
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithParameterName("options")
            .WithMessage("SceneEnvironmentMap.FaceSize must be a power of two from 16 to 1024, not 48.*");

    /// <summary>The renderer with a valid map setting, on the GPU.</summary>
    [Collection(GpuCollection.Name)]
    public sealed class OnTheGpu
    {
        [Fact]
        public void TheRendererStartsWithTheDefaultMapAndReportsNoValidationMessage() =>
            Gpu.DisposeAndAssertClean(Gpu.Headless(width: 64, height: 64, environmentMap: SceneEnvironmentMap.Default));
    }
}
