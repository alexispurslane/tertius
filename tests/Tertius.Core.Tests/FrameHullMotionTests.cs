using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The hull of a frame that has a parent moves the way that the frame's driver needs: physics moves it as a dynamic body,
/// the Kepler and Spin drivers move it on rails as a kinematic body, and the static driver keeps it still.
/// </summary>
public class FrameHullMotionTests
{
    [Theory]
    [InlineData(MotionDriver.Physics, BodyMotion.Dynamic)]
    [InlineData(MotionDriver.Kepler, BodyMotion.Kinematic)]
    [InlineData(MotionDriver.Spin, BodyMotion.Kinematic)]
    [InlineData(MotionDriver.Static, BodyMotion.Static)]
    public void EachDriverGivesTheHullTheMotionThatItNeeds(MotionDriver driver, BodyMotion motion)
    {
        BodyDesc.FrameHullMotion(driver).Should().Be(motion);
    }
}
