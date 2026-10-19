using System.Numerics;
using Tertius.Rendering;
using Xunit.Sdk;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The reference image check on real headless renders. The scene is a sunlit white box, 1 m wide, 4 m ahead of the
/// camera, against the sky. The reference images of these tests go to captures/reference-images/, not into the
/// repository.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ReferenceImageRenderTests
{
    private const int Width = 320, Height = 180;
    private static readonly Vector3 BoxCenter = new(0, 0, -4);

    private static string Folder
    {
        get
        {
            string folder = Path.Combine(Path.GetDirectoryName(Gpu.CapturePath("x"))!, "reference-images");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>Renders the box, moved by <paramref name="offset"/> from its place, at alpha 1.</summary>
    private static Capture RenderBox(string name, Vector3 offset = default)
    {
        VulkanRenderer renderer = Gpu.Headless(Width, Height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new Vector3(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.8f, 0.8f, 0.8f, 1), gloss: 0.5f));
            RenderSnapshot snapshot = Gpu.Snapshot(new Vector3(0.4f, 0.8f, 0.5f));
            snapshot.Instances.Add(Gpu.Instance(box, white, BoxCenter + offset, Vector3.One,
                Quaternion.CreateFromYawPitchRoll(0.6f, 0.3f, 0), radius: 0.87f, entity: 1));
            return Gpu.RenderAndCapture(renderer, snapshot, name);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASecondRenderOfASceneMatchesTheReferenceThatTheFirstApproved()
    {
        string reference = Path.Combine(Folder, "box-again.png");
        ReferenceImages.Verify(RenderBox("reference-box-first"), reference, approve: true);

        Action check = () => ReferenceImages.Verify(RenderBox("reference-box-second"), reference, approve: false);

        check.Should().NotThrow();
    }

    [Fact]
    public void AMovedBoxFailsWithAHeatMapThatLightsUpWhereTheBoxWas()
    {
        string reference = Path.Combine(Folder, "box-moved.png");
        ReferenceImages.Verify(RenderBox("reference-box-in-place"), reference, approve: true);

        Action check = () => ReferenceImages.Verify(RenderBox("reference-box-moved", offset: new Vector3(1.6f, 0, 0)), reference, approve: false);

        check.Should().Throw<FailException>();
        Capture heatMap = Capture.Load(Path.Combine(Folder, "box-moved.flip.png"));
        heatMap.MeanLuma(Gpu.Project(BoxCenter, Width, Height), 2)
            .Should().BeGreaterThan(0.5f, "the box moved 1.6 m to the right, clear of its old place, which became sky");
        heatMap.MeanLuma(new Vector2(20, 20), 2).Should().BeLessThan(0.02f, "the sky far from the box did not change");
    }
}
