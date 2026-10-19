using Xunit.Sdk;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The approval variable of snapshot tests: the check reads TERTIUS_APPROVE_SNAPSHOTS from the environment, and only
/// the value 1 makes the image the reference. The tests write the name out instead of using
/// <see cref="ReferenceImages.ApproveVariable"/>, because ARCHITECTURE.md documents the name.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ReferenceImageApprovalVariableTests : IDisposable
{
    private const string Variable = "TERTIUS_APPROVE_SNAPSHOTS";

    private readonly string? valueBefore = Environment.GetEnvironmentVariable(Variable);
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("tertius-approval-variable-");

    private string Reference => Path.Combine(folder.FullName, "scene.png");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Variable, valueBefore);
        folder.Delete(recursive: true);
    }

    [Fact]
    public void TheVariableSetTo1MakesTheImageTheReference()
    {
        Environment.SetEnvironmentVariable(Variable, "1");
        Capture image = TestImages.Gray(16, 16, 128);

        Action check = () => ReferenceImages.Verify(image, Reference);

        check.Should().NotThrow();
        Capture.Load(Reference).Rgba.Should().Equal(image.Rgba);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public void WithoutTheVariableSetTo1TheCheckWritesNoReference(string? value)
    {
        Environment.SetEnvironmentVariable(Variable, value);

        Action check = () => ReferenceImages.Verify(TestImages.Gray(16, 16, 128), Reference);

        check.Should().Throw<FailException>().WithMessage("No approved reference image*");
        File.Exists(Reference).Should().BeFalse();
    }
}
