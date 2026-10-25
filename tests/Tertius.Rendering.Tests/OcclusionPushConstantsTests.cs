using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The push constants of the occlusion passes: the camera as occlusion.glsl reads it, and the setting
/// (docs/plans/ambient-occlusion.md, "Push constants and sets").
/// </summary>
public sealed class OcclusionPushConstantsTests
{
    private static readonly Quaternion Turned = Quaternion.CreateFromYawPitchRoll(yaw: 0.3f, pitch: -0.2f, roll: 0.1f);

    private static readonly CameraView Camera = new()
    {
        Rotation = Turned,
        Right = Vector3.Transform(Vector3.UnitX, Turned),
        Up = Vector3.Transform(Vector3.UnitY, Turned),
        Forward = Vector3.Transform(-Vector3.UnitZ, Turned),
        TanHalfX = 0.75f,
        TanHalfY = 0.42f,
    };

    [Fact]
    public void TheCameraAxesTravelAsUnitVectorsWithTheTangentsOfTheHalfFieldOfViewBesideThem()
    {
        var push = OcclusionPushConstants.For(
            camera: Camera,
            setting: GroundTruthOcclusion.Default,
            lastDenoise: false
        );

        push.CameraRight.Should().Be(new Vector4(Camera.Right, 0.75f));
        push.CameraUp.Should().Be(new Vector4(Camera.Up, 0.42f));
        push.CameraForward.Should().Be(new Vector4(Camera.Forward, 0), "the shaders read only its xyz");
    }

    [Fact]
    public void TheRadiusTheSlicesAndTheStepsComeFromTheSetting()
    {
        var setting = new GroundTruthOcclusion(RadiusMeters: 0.75f, Slices: 5, StepsPerSide: 7);

        var push = OcclusionPushConstants.For(camera: Camera, setting: setting, lastDenoise: false);

        push.RadiusMeters.Should().Be(0.75f);
        push.Slices.Should().Be(5u);
        push.StepsPerSide.Should().Be(7u);
    }

    [Theory]
    [InlineData(true, 1u)]
    [InlineData(false, 0u)]
    public void OnlyTheLastDenoiseDispatchIsMarked(bool lastDenoise, uint expected)
    {
        var push = OcclusionPushConstants.For(
            camera: Camera,
            setting: GroundTruthOcclusion.Default,
            lastDenoise: lastDenoise
        );

        push.LastDenoise.Should().Be(expected);
    }
}
