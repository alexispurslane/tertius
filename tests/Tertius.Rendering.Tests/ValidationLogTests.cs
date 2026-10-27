namespace Tertius.Rendering.Tests;

/// <summary>
/// What the validation log leaves out: MoltenVK's report of blending on an integer target, which it makes for every
/// pipeline that draws into one, blending or not (<see cref="ValidationLog"/>).
/// </summary>
public sealed class ValidationLogTests
{
    [Fact]
    public void MoltenVksReportOfBlendingOnAnUnsignedIntegerTargetIsLeftOut() =>
        ValidationLog
            .IsFalseIntegerBlendReport(
                "VK_ERROR_FEATURE_NOT_PRESENT: Blending is enabled for attachment with format VK_FORMAT_R8G8B8A8_UINT, "
                    + "which does not support it."
            )
            .Should()
            .BeTrue();

    [Fact]
    public void AReportOfBlendingOnAFloatTargetIsKept() =>
        ValidationLog
            .IsFalseIntegerBlendReport(
                "VK_ERROR_FEATURE_NOT_PRESENT: Blending is enabled for attachment with format VK_FORMAT_R32G32B32A32_SFLOAT, "
                    + "which does not support it."
            )
            .Should()
            .BeFalse("every float target the renderer blends into must blend, so such a report is real");
}
