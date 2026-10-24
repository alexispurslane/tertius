using System.Diagnostics.CodeAnalysis;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Snapshot tests: a test renders a scene headlessly and compares the image with its approved reference image through
/// FLIP. A reference image of a test lives at ReferenceImages/&lt;name&gt;.png in this project.
/// <list type="bullet">
/// <item>The check fails when any pixel's FLIP value is above the tolerance. Each test can name its own tolerance. The
/// default, 0.1, is the FLIP value of a CIELAB lightness step of about 2.2 in a flat area, about one just noticeable
/// difference. So a change of one level in every pixel passes, and one changed pixel that anyone can see fails. The
/// rule looks at each pixel, not at the mean FLIP value over the image, because a small object that disappears hardly
/// moves the mean.</item>
/// <item>When the check fails, it writes the actual image as &lt;name&gt;.actual.png and the FLIP heat map as
/// &lt;name&gt;.flip.png beside the reference. A passing check removes them.</item>
/// <item>With the environment variable <see cref="ApproveVariable"/> set to 1, the check makes the image the reference
/// instead, and passes. Approve only after looking at the image: a snapshot proves only that nothing changed.</item>
/// </list>
/// </summary>
internal static class ReferenceImages
{
    public const string ApproveVariable = "TERTIUS_APPROVE_SNAPSHOTS";

    /// <summary>The largest FLIP value that a pixel may have and still match its reference.</summary>
    public const float DefaultTolerance = 0.1f;

    /// <summary>The folder of the approved reference images, in the source tree, so that an approval lands in git.</summary>
    public static string Folder =>
        Path.Combine(path1: Gpu.RepositoryRoot, path2: "tests", path3: "Tertius.Rendering.Tests",
            path4: "ReferenceImages");

    /// <summary>Whether this run approves the images that its snapshot tests render.</summary>
    private static bool Approving => Environment.GetEnvironmentVariable(ApproveVariable) == "1";

    /// <summary>
    /// Renders <paramref name="snapshot"/> headlessly (see <see cref="Gpu.RenderAndCapture"/>) and checks the image
    /// against the reference image <paramref name="name"/> in <see cref="Folder"/>, approving it when
    /// <see cref="ApproveVariable"/> is 1.
    /// </summary>
    public static void Verify(VulkanRenderer renderer, RenderSnapshot snapshot, string name,
        float tolerance = DefaultTolerance) =>
        Verify(Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name),
            Path.Combine(Folder, name + ".png"), tolerance);

    /// <summary>
    /// Checks <paramref name="actual"/> against the reference image at <paramref name="referencePath"/>, or makes it the
    /// reference when <see cref="ApproveVariable"/> is 1.
    /// </summary>
    public static void Verify(Capture actual, string referencePath, float tolerance = DefaultTolerance) =>
        Verify(actual: actual, referencePath: referencePath, approve: Approving, tolerance: tolerance);

    /// <summary>
    /// Checks <paramref name="actual"/> against the reference image at <paramref name="referencePath"/>, or makes it the
    /// reference when <paramref name="approve"/> is true. Fails the test through <see cref="Assert.Fail(string)"/>.
    /// </summary>
    public static void Verify(Capture actual, string referencePath, bool approve, float tolerance = DefaultTolerance)
    {
        string actualPath = Path.ChangeExtension(referencePath, ".actual.png");
        string heatMapPath = Path.ChangeExtension(referencePath, ".flip.png");

        // Approval and failure both write beside the reference, and its folder does not exist in a checkout that has
        // no reference image yet.
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(referencePath))!);

        if (approve)
        {
            actual.Save(referencePath);
            File.Delete(actualPath);
            File.Delete(heatMapPath);
            TestContext.Current.TestOutputHelper?.WriteLine($"Approved the reference image {referencePath}.");
            return;
        }

        if (!File.Exists(referencePath))
            Fail(actual: actual, actualPath: actualPath, heatMapPath: heatMapPath, map: null,
                message:
                $"No approved reference image at {referencePath}. The actual image is at {actualPath}. Open it, and if it " +
                $"shows the scene as its test describes it, approve it: run the test again with {ApproveVariable}=1.");

        Capture reference = Capture.Load(referencePath);
        if (reference.Width != actual.Width || reference.Height != actual.Height)
            Fail(actual: actual, actualPath: actualPath, heatMapPath: heatMapPath, map: null,
                message:
                $"The reference image {referencePath} is {reference.Width}×{reference.Height} pixels, but the actual image is " +
                $"{actual.Width}×{actual.Height}. The actual image is at {actualPath}. If the new size is intended, approve " +
                $"it with {ApproveVariable}=1.");

        FlipMap map = Flip.Compare(reference, actual);
        int visible = map.CountAbove(tolerance);
        if (visible == 0)
        {
            File.Delete(actualPath);
            File.Delete(heatMapPath);
            return;
        }

        Fail(actual: actual, actualPath: actualPath, heatMapPath: heatMapPath, map: map,
            message:
            $"{visible} pixels differ visibly from the reference image {referencePath}: their FLIP value is above " +
            $"{tolerance} (the largest is {map.Max:F3}, the mean over the image {map.Mean:F5}). The actual image is at " +
            $"{actualPath} and the FLIP heat map at {heatMapPath}. Open all three. If the change is intended, approve it " +
            $"with {ApproveVariable}=1.");
    }

    /// <summary>Writes the actual image, and the heat map when there is one, then fails the test.</summary>
    [DoesNotReturn]
    private static void Fail(Capture actual, string actualPath, string heatMapPath, FlipMap? map, string message)
    {
        actual.Save(actualPath);
        if (map is null)
            File.Delete(heatMapPath);
        else
            map.HeatMap().Save(heatMapPath);
        Assert.Fail(message);
    }
}
