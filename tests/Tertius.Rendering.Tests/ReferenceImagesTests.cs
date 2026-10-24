using Xunit.Sdk;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The check of an image against its approved reference image, on hand-made images in a temporary folder. The check
/// fails when a pixel's FLIP value is above the tolerance, writes the actual image and a FLIP heat map beside the
/// reference when it fails, and makes the image the reference when approval is on.
/// </summary>
public sealed class ReferenceImagesTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("tertius-reference-images-");

    /// <summary>
    /// The folder of the reference does not exist until the check writes into it, as in a fresh checkout that has no
    /// reference image yet.
    /// </summary>
    private string ReferenceFolder => Path.Combine(folder.FullName, "ReferenceImages");

    private string Reference => Path.Combine(ReferenceFolder, "scene.png");
    private string Actual => Path.Combine(ReferenceFolder, "scene.actual.png");
    private string HeatMap => Path.Combine(ReferenceFolder, "scene.flip.png");

    public void Dispose() => folder.Delete(recursive: true);

    private Action Checking(Capture image, bool approve = false) =>
        () => ReferenceImages.Verify(actual: image, referencePath: Reference, approve: approve);

    /// <summary>A gray 64×64 image with a white 8×8 square at (40, 40): a change that anyone sees.</summary>
    private static Capture GrayWithSquare() =>
        TestImages.GrayWith(
            width: 64,
            height: 64,
            value: 128,
            changed: (x, y) => x is >= 40 and < 48 && y is >= 40 and < 48,
            changedValue: 255
        );

    [Fact]
    public void AnImageWithoutAReferenceFailsAndIsWrittenBesideTheMissingReferenceEvenWithoutItsFolder()
    {
        Directory.Exists(ReferenceFolder).Should().BeFalse("no image was approved or checked yet");
        Capture image = TestImages.Gray(16, 16, 128);

        Checking(image).Should().Throw<FailException>().WithMessage($"*{ReferenceImages.ApproveVariable}*");

        File.Exists(Reference).Should().BeFalse("only approval writes a reference");
        Capture.Load(Actual).Rgba.Should().Equal(image.Rgba);
    }

    [Fact]
    public void ApprovalMakesTheImageTheReferenceThatLaterChecksPass()
    {
        Capture image = TestImages.Gray(16, 16, 128);

        Checking(image, approve: true).Should().NotThrow();

        Capture.Load(Reference).Rgba.Should().Equal(image.Rgba);
        Checking(image).Should().NotThrow();
    }

    [Fact]
    public void ApprovalReplacesAnOldReferenceAndRemovesTheFailureFiles()
    {
        Checking(TestImages.Gray(64, 64, 128), approve: true).Should().NotThrow();
        Capture changed = GrayWithSquare();
        Checking(changed).Should().Throw<FailException>();

        Checking(changed, approve: true).Should().NotThrow();

        Capture.Load(Reference).Rgba.Should().Equal(changed.Rgba);
        File.Exists(Actual).Should().BeFalse();
        File.Exists(HeatMap).Should().BeFalse();
    }

    [Fact]
    public void AMatchingImageRemovesTheFailureFilesOfAnEarlierFailure()
    {
        Capture image = TestImages.Gray(64, 64, 128);
        Checking(image, approve: true).Should().NotThrow();
        Checking(GrayWithSquare()).Should().Throw<FailException>();

        Checking(image).Should().NotThrow();

        File.Exists(Actual).Should().BeFalse();
        File.Exists(HeatMap).Should().BeFalse();
    }

    [Fact]
    public void AVisibleChangeFailsAndWritesTheActualImageAndAHeatMapThatShowsWhere()
    {
        Checking(TestImages.Gray(64, 64, 128), approve: true).Should().NotThrow();
        Capture changed = GrayWithSquare();

        Checking(changed).Should().Throw<FailException>().WithMessage($"*{Reference}*");

        Capture.Load(Actual).Rgba.Should().Equal(changed.Rgba);
        var heatMap = Capture.Load(HeatMap);
        heatMap.Luma(44, 44).Should().BeGreaterThan(0.5f, "the square changed from gray to white");
        heatMap.Luma(10, 10).Should().BeLessThan(0.02f, "the far corner did not change");
    }

    [Fact]
    public void AChangeOfOneLevelInEveryPixelPasses()
    {
        // Gray 129 against gray 128 scores 0.030 in every pixel (L* 53.98 against 53.59): below the tolerance of 0.1,
        // though the whole image changed.
        Checking(TestImages.Gray(64, 64, 128), approve: true).Should().NotThrow();

        Checking(TestImages.Gray(64, 64, 129)).Should().NotThrow();

        File.Exists(Actual).Should().BeFalse();
    }

    [Fact]
    public void AUniformChangeJustBelowOneNoticeableDifferencePasses()
    {
        // Gray 125 against gray 128 scores 0.065 in every pixel (L* 52.41 against 53.59, a step of 1.2): below the
        // tolerance of 0.1.
        Checking(TestImages.Gray(64, 64, 128), approve: true).Should().NotThrow();

        Checking(TestImages.Gray(64, 64, 125)).Should().NotThrow();
    }

    [Fact]
    public void AUniformChangeJustAboveOneNoticeableDifferenceFailsInEveryPixel()
    {
        // Gray 118 against gray 128 scores 0.150 in every pixel (L* 49.64 against 53.59, a step of 3.9): above the
        // tolerance of 0.1.
        Checking(TestImages.Gray(64, 64, 128), approve: true).Should().NotThrow();

        Checking(TestImages.Gray(64, 64, 118))
            .Should()
            .Throw<FailException>()
            .WithMessage("4096 pixels differ visibly*");
    }

    [Fact]
    public void AVisibleChangeOfOnePixelFails()
    {
        // One white pixel on black scores far above 0.1 at that pixel, though the mean over the 4,096 pixels stays far below 0.1.
        Checking(TestImages.Gray(64, 64, 0), approve: true).Should().NotThrow();

        Checking(
                TestImages.GrayWith(
                    width: 64,
                    height: 64,
                    value: 0,
                    changed: (x, y) => x == 32 && y == 32,
                    changedValue: 255
                )
            )
            .Should()
            .Throw<FailException>();
    }

    [Fact]
    public void AReferenceOfAnotherSizeFailsNamesBothSizesAndRemovesTheHeatMapOfAnEarlierFailure()
    {
        Checking(TestImages.Gray(64, 64, 128), approve: true).Should().NotThrow();
        Checking(GrayWithSquare()).Should().Throw<FailException>();
        File.Exists(HeatMap).Should().BeTrue("the earlier failure compared two images of one size");
        Capture wider = TestImages.Gray(96, 64, 128);

        Checking(wider).Should().Throw<FailException>().WithMessage("*64×64*96×64*");

        Capture.Load(Actual).Rgba.Should().Equal(wider.Rgba);
        File.Exists(HeatMap)
            .Should()
            .BeFalse("FLIP compares only images of one size, so the old heat map would show a stale comparison");
    }
}
