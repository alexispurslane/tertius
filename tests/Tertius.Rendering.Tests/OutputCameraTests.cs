using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The camera's framing belongs to the output: a render size that changes only the samples leaves the field of view, the
/// places of world points on the output image and FSR's view constants as they are (ter-qt72).
/// </summary>
public sealed class OutputCameraTests(ITestOutputHelper output)
{
    private static readonly Extent2D Output = new(1920, 1080);

    [Theory]
    [InlineData(1904, 1072)]
    [InlineData(1912, 1072)]
    [InlineData(1872, 1048)]
    [InlineData(1280, 720)]
    public void TheHorizontalFieldOfViewIsTheOutputsAtEveryRenderSize(int width, int height)
    {
        PreparedScene scene = Draw(new((uint)width, (uint)height));

        float horizontalFov = 2 * MathF.Atan(scene.Camera.TanHalfX);
        float outputFov = 2 * MathF.Atan(MathF.Tan(Gpu.Fov / 2) * 1920 / 1080);
        output.WriteLine(
            $"{width} × {height}: horizontal fov {float.RadiansToDegrees(horizontalFov):F4}°, output's {float.RadiansToDegrees(outputFov):F4}°"
        );
        horizontalFov
            .Should()
            .BeApproximately(
                outputFov,
                1e-6f,
                "the render size {0} × {1} says only how many samples the picture gets",
                width,
                height
            );
    }

    [Theory]
    [InlineData(1872, 1048)]
    [InlineData(1904, 1072)]
    [InlineData(976, 544)]
    [InlineData(976, 552)]
    public void AWorldPointKeepsItsPlaceOnTheOutputAtARenderSizeOfAnotherShape(int width, int height)
    {
        var point = new Vector4(x: 3, y: 1, z: -5, w: 1);
        Vector2 exact = OutputPlace(point, new(1280, 720));

        Vector2 drawn = OutputPlace(point, new((uint)width, (uint)height));
        output.WriteLine(
            $"{width} × {height}: the point moves by ({drawn.X - exact.X:F3}, {drawn.Y - exact.Y:F3}) output px"
        );

        drawn
            .X.Should()
            .BeApproximately(
                exact.X,
                0.001f,
                "the projection takes its width from the output, not from {0} × {1}",
                width,
                height
            );
        drawn.Y.Should().BeApproximately(exact.Y, 0.001f);
    }

    [Fact]
    public void TheCamerasPixelCountsAreTheRenderSizes()
    {
        PreparedScene scene = Draw(new(1872, 1048));

        scene.Camera.Width.Should().Be(1872, "the render size says how many samples the picture gets");
        scene.Camera.Height.Should().Be(1048);
    }

    [Fact]
    public void FsrsViewConstantsDescribeTheOutputsCameraAtARenderSizeOfAnotherShape()
    {
        var render = new Extent2D(1872, 1048);
        PreparedScene scene = Draw(render);

        var constants = Fsr3Constants.Next(
            last: Fsr3Constants.Created(Output, Output),
            frame: new()
            {
                RenderSize = render,
                VerticalFov = scene.Camera.VerticalFov,
                Aspect = scene.Camera.Aspect,
                NearMeters = DepthRanges.NearPlane,
                Jitter = Vector2.Zero,
                FrameMilliseconds = 1000f / 60,
                Reset = true,
            },
            resetAccumulation: true
        );

        float tanHalfY = MathF.Tan(Gpu.Fov / 2);
        float tanHalfX = tanHalfY * 1920 / 1080;
        constants
            .TanHalfFov.Should()
            .BeApproximately(tanHalfX, 1e-6f, "FSR reads view positions of the output's camera");
        constants.DeviceToViewDepth2.Should().BeApproximately(tanHalfX, 1e-6f);
        constants.DeviceToViewDepth3.Should().BeApproximately(tanHalfY, 1e-6f);
    }

    /// <summary>Where <paramref name="point"/>, relative to the eye, lands on the output image, in output pixels from the top left.</summary>
    private static Vector2 OutputPlace(Vector4 point, Extent2D render)
    {
        PreparedScene scene = Draw(render);
        var clip = Vector4.Transform(point, scene.Constants.MotionViewProj);
        return new((clip.X / clip.W + 1) * 0.5f * Output.Width, (clip.Y / clip.W + 1) * 0.5f * Output.Height);
    }

    /// <summary>The first draw of a 1920 × 1080 output at <paramref name="render"/>, of the test camera at the origin.</summary>
    private static PreparedScene Draw(Extent2D render)
    {
        var history = new FrameHistory(StillRule.Window);
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        var scene = new PreparedScene();
        FrameHistory.PendingFrame frame = history.Begin(
            snapshot: snapshot,
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            outputSize: Output,
            renderSize: render
        );
        Batchers
            .Default()
            .Prepare(
                snapshot: snapshot,
                alpha: 1,
                highestStops: Batchers.HighestStops,
                meshes: [],
                materials: [],
                environment: default,
                region: DeformRegion.None,
                frame: frame,
                scene: scene
            );
        history.Drew(frame);
        return scene;
    }
}
