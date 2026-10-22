using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The chord rule of a conversion between two frames (docs/plans/spin-gravity.md, "The two conversion rules"): a chord is the
/// change of a body's coordinates over a step divided by the step, so it converts by the frame's poses at both ends of the
/// step. The frame of the tests turns by a quarter turn about +Z over a step of 1 s, so that every place and chord is a small
/// vector that can be checked by hand. Its axes turn by +90° about +Z, so the coordinates of a direction that is fixed in
/// the parent turn by −90° in the frame: (x, y, z) becomes (y, −x, z).
/// </summary>
public class StepPosesTests
{
    private static readonly DQuaternion Quarter = DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2);

    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance = 1e-12) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    [Fact]
    public void TheChordOfABodyAtRestInTheParentConvertsToNoChordThere()
    {
        // The body rests at (1, 0, 0) in the parent. In the frame it is at (1, 0, 0) where the step starts and at (0, -1, 0)
        // where it ends, so its chord there is (-1, -1, 0). The instant rule at the end of the step would give
        // R c + ω × (R x) = (1, -1, 0) + (0, π/2, 0), which is not zero.
        var poses = new StepPoses(DPose.Identity, new(Double3.Zero, Quarter));

        ExpectNear(poses.ChordToParent(new(0, -1, 0), new(-1, -1, 0), 1), Double3.Zero);
    }

    [Fact]
    public void TheChordOfABodyConvertsToTheParentByTheFramesMoveAndTurnOverTheStep()
    {
        // The frame moves from (10, 0, 0) to (10, 2, 0) and turns by a quarter turn. The body moves from (11, 0, 0) to
        // (11, 3, 0) in the parent, a chord of (0, 3, 0). In the frame it goes from (1, 0, 0) to the coordinates of (1, 1, 0)
        // turned by -90°, (1, -1, 0): a chord of (0, -1, 0).
        var poses = new StepPoses(new(new(10, 0, 0), DQuaternion.Identity), new(new(10, 2, 0), Quarter));

        ExpectNear(poses.ChordToParent(new(1, -1, 0), new(0, -1, 0), 1), new(0, 3, 0));
    }

    [Fact]
    public void TheChordOfABodyInTheParentConvertsIntoTheFrameByTheFramesMoveAndTurnOverTheStep()
    {
        // The body and the frame of the test before, the other way: the body ends at (11, 3, 0) in the parent with the
        // chord (0, 3, 0), and its chord in the frame is (0, -1, 0).
        var poses = new StepPoses(new(new(10, 0, 0), DQuaternion.Identity), new(new(10, 2, 0), Quarter));

        ExpectNear(poses.ChordFromParent(new(11, 3, 0), new(0, 3, 0), 1), new(0, -1, 0));
    }

    [Fact]
    public void TheAngularChordOfABodyThatDoesNotTurnInTheParentConvertsToNoTurnThere()
    {
        // In the frame the body turns by -90° about +Z over the step, so Jolt's angular velocity there is (0, 0, -π/2).
        var poses = new StepPoses(DPose.Identity, new(Double3.Zero, Quarter));

        ExpectNear(poses.AngularChordToParent(new(0, 0, -Math.PI / 2), 1), Double3.Zero);
    }

    [Fact]
    public void TheAngularChordOfABodyThatTurnsAboutTheFramesAxisConvertsIntoTheFrameLessTheFramesTurn()
    {
        // The body turns by 0.5 rad about +Z in the parent over the step, and the frame by a quarter turn, so in the frame
        // the body turns by 0.5 - π/2 rad about +Z.
        var poses = new StepPoses(DPose.Identity, new(Double3.Zero, Quarter));

        ExpectNear(poses.AngularChordFromParent(new(0, 0, 0.5), 1), new(0, 0, 0.5 - Math.PI / 2));
    }

    [Fact]
    public void AnAngularChordAcrossTheTurnConvertsBackToItself()
    {
        // The body turns about +X while the frame turns about +Z, so the rotations do not commute, and only the rule with
        // the rotations at both ends of the step gives back the chord that it started from.
        var poses = new StepPoses(new(Double3.Zero, DQuaternion.FromAxisAngle(Double3.UnitY, 0.3)),
            new(Double3.Zero, Quarter * DQuaternion.FromAxisAngle(Double3.UnitY, 0.3)));
        var chord = new Double3(0.7, 0, 0);

        ExpectNear(poses.AngularChordFromParent(poses.AngularChordToParent(chord, 1), 1), chord);
    }
}
