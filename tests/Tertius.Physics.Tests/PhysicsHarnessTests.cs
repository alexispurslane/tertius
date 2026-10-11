using System.Numerics;

namespace Tertius.Physics.Tests;

/// <summary>
/// The harness stands in for the sync point, so the physics tests are only as strict as the harness. These tests pin
/// down how it treats a hand-off that a test refuses on purpose.
/// </summary>
public class PhysicsHarnessTests
{
    [Fact]
    public void AnEntityWhoseHandOffATestRefusesKeepsTheStateItHadBeforeTheStep()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        // A carrier bubble 1 km from the origin of space, and a fighter that flies into it.
        FrameHandle carrier = h.AddFrame(space, Shapes.At(1000, 0, 0), FrameBounds.Sphere(200), MotionDriver.Static);
        FrameHandle fighter = h.AddFrameWithBody(space, Shapes.At(1260, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics,
            Shapes.Box(2, mass: 1000), velocity: new Vector3(-60, 0, 0));
        h.RefusesHandOff = handOff => handOff.Entity == fighter.Entity;

        BodyState stateBefore = default;
        DPose poseBefore = default;
        Double3 velocityBefore = default;
        h.StepUntil(() =>
        {
            if (h.RefusedHandOffs.Count > 0)
                return true;
            stateBefore = h[fighter.Entity];
            poseBefore = h.Frames.LocalPose(fighter);
            velocityBefore = h.Frames.LinearVelocity(fighter);
            return false;
        }, 120);

        h.RefusedHandOffs.Should().ContainSingle().Which.To.Should().Be(carrier);
        h[fighter.Entity].Should().Be(stateBefore, "the harness skips the body state that came with the refused hand-off");
        h.Frames.Parent(fighter).Should().Be(space);
        h.Frames.LocalPose(fighter).Should().Be(poseBefore, "the harness skips the frame state that came with the refused hand-off");
        h.Frames.LinearVelocity(fighter).Should().Be(velocityBefore);
    }
}
