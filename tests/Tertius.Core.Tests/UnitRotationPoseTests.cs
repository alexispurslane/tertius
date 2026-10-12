using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// Ticket ter-swgm. Jolt keeps rotations as float quaternions. Widened to double, such a rotation is off from length 1 by up
/// to 2E-7 in the square of its length. <see cref="DQuaternion.Rotate"/> assumes a unit quaternion, so a pose with such a
/// rotation and a position of millions of meters used to move a point by about a meter, and not the same way in
/// <see cref="DPose.Compose"/> as in <see cref="DPose.Inverse"/>. These tests give poses and trees the rotation that Jolt
/// gives, and the position of a ship in a low orbit, and expect the answers of the rotation normalized in double.
/// </summary>
public class UnitRotationPoseTests
{
    /// <summary>
    /// A rotation that came out of a float quaternion, as Jolt gives it. The float quaternion is (1, 2, 3, 4) / sqrt(30), with
    /// its last component one step of a float too long, so the square of its length is 1.7E-7 above 1.
    /// </summary>
    private static readonly DQuaternion Widened = DQuaternion.FromQuaternion(new Quaternion(0.18257418f, 0.36514837f, 0.5477226f, 0.73029685f));

    /// <summary>The place of a ship in a low orbit round Earth, in Earth's axes, 6.77E6 m from the center.</summary>
    private static readonly Double3 InOrbit = new(4.2e6, -3.9e6, 3.6e6);

    private static void ExpectNear(Double3 actual, Double3 expected, double metres) =>
        Double3.Distance(actual, expected).Should().BeLessThan(metres, $"{actual} should be within {metres} m of {expected}");

    private static void ExpectSameComponents(DQuaternion actual, DQuaternion expected, double tolerance)
    {
        Math.Abs(actual.X - expected.X).Should().BeLessThan(tolerance, $"{actual} should be {expected}");
        Math.Abs(actual.Y - expected.Y).Should().BeLessThan(tolerance, $"{actual} should be {expected}");
        Math.Abs(actual.Z - expected.Z).Should().BeLessThan(tolerance, $"{actual} should be {expected}");
        Math.Abs(actual.W - expected.W).Should().BeLessThan(tolerance, $"{actual} should be {expected}");
    }

    [Fact]
    public void TheTestRotationIsAsFarFromUnitLengthAsAFloatRotationFromJoltCan()
    {
        // The squares of the lengths that Jolt gave in a run lay between -7.3E-8 and +2.1E-7.
        (Widened.LengthSquared - 1).Should().BeInRange(1e-7, 2.1e-7);
    }

    [Fact]
    public void ComposeMovesAPositionByTheNormalizedRotationOfItsParent()
    {
        var parent = new DPose(InOrbit, Widened);
        var local = new DPose(new Double3(3000, 2000, -4000), DQuaternion.Identity);

        DPose composed = DPose.Compose(parent, local);

        DPose expected = DPose.Compose(parent with { Rotation = Widened.Normalized() }, local);
        ExpectNear(composed.Position, expected.Position, 1e-9);
        ExpectSameComponents(composed.Rotation, expected.Rotation, 1e-12);
    }

    [Fact]
    public void ComposeGivesARotationOfUnitLength()
    {
        DPose composed = DPose.Compose(new DPose(InOrbit, Widened), new DPose(Double3.Zero, Widened));

        (composed.Rotation.LengthSquared - 1).Should().BeApproximately(0, 1e-14);
    }

    [Fact]
    public void InverseAgreesWithTheInverseOfTheNormalizedRotation()
    {
        var pose = new DPose(InOrbit, Widened);

        DPose inverse = pose.Inverse();

        DPose expected = (pose with { Rotation = Widened.Normalized() }).Inverse();
        ExpectNear(inverse.Position, expected.Position, 1e-6);
        ExpectSameComponents(inverse.Rotation, expected.Rotation, 1e-12);
    }

    [Fact]
    public void AnInverseHasARotationOfUnitLength()
    {
        DPose inverse = new DPose(InOrbit, Widened).Inverse();

        (inverse.Rotation.LengthSquared - 1).Should().BeApproximately(0, 1e-14);
    }

    [Fact]
    public void ThePoseComposedAfterItsInverseIsTheIdentity()
    {
        var pose = new DPose(InOrbit, Widened);

        DPose composed = DPose.Compose(pose, pose.Inverse());

        ExpectNear(composed.Position, Double3.Zero, 1e-6);
        ExpectSameComponents(composed.Rotation, DQuaternion.Identity, 1e-12);
    }

    [Fact]
    public void TheInverseComposedAfterThePoseIsTheIdentity()
    {
        var pose = new DPose(InOrbit, Widened);

        DPose composed = DPose.Compose(pose.Inverse(), pose);

        ExpectNear(composed.Position, Double3.Zero, 1e-6);
        ExpectSameComponents(composed.Rotation, DQuaternion.Identity, 1e-12);
    }

    [Fact]
    public void TheInverseOfTheInverseIsThePose()
    {
        var pose = new DPose(InOrbit, Widened);

        DPose again = pose.Inverse().Inverse();

        ExpectNear(again.Position, pose.Position, 1e-6);
        ExpectSameComponents(again.Rotation, Widened.Normalized(), 1e-12);
    }

    [Fact]
    public void AFloatPoseBecomesADoublePoseWhoseRotationIsOfUnitLength()
    {
        var pose = new Pose(new Vector3(1, 2, 3), new Quaternion(0.18257418f, 0.36514837f, 0.5477226f, 0.73029685f));

        DPose widened = pose.ToDPose();

        (widened.Rotation.LengthSquared - 1).Should().BeApproximately(0, 1e-14);
        widened.Position.Should().Be(new Double3(1, 2, 3));
        ExpectSameComponents(widened.Rotation, Widened.Normalized(), 1e-12);
    }

    // ---- The pose tree --------------------------------------------------------------------------------------------

    [Fact]
    public void ComputeRelativeToPutsTheParentOfTheReferenceWhereTheNormalizedRotationPutsIt()
    {
        // Earth, the ship in it with the rotation that Jolt gives, and a crate in Earth 30 m from the ship.
        var tree = new PoseTree<DPose>();
        PoseNode earth = tree.Add(PoseNode.Null, DPose.Identity);
        PoseNode ship = tree.Add(earth, new DPose(InOrbit, Widened));
        Double3 crateFromShip = new(30, 20, 10);
        PoseNode crate = tree.Add(earth, new DPose(InOrbit + crateFromShip, DQuaternion.Identity));
        tree.UpdateWorld();
        var relative = new DPose[tree.Count];

        tree.ComputeRelativeTo(ship, relative);

        DQuaternion toShip = Widened.Normalized().Conjugate();
        ExpectNear(relative[tree.DenseIndex(earth)].Position, toShip.Rotate(-InOrbit), 1e-6);
        ExpectNear(relative[tree.DenseIndex(crate)].Position, toShip.Rotate(crateFromShip), 1e-6);
    }

    [Fact]
    public void RelativeMovesADescendantOfANodeWhoseRotationIsNotUnitByTheNormalizedRotation()
    {
        var tree = new PoseTree<DPose>();
        PoseNode root = tree.Add(PoseNode.Null, DPose.Identity);
        PoseNode turned = tree.Add(root, new DPose(Double3.Zero, Widened));
        PoseNode far = tree.Add(turned, new DPose(InOrbit, DQuaternion.Identity));
        tree.UpdateWorld();

        DPose inRoot = tree.Relative(far, root);

        Double3 expected = Widened.Normalized().Rotate(InOrbit);
        ExpectNear(inRoot.Position, expected, 1e-6);
        ExpectNear(tree.World(far).Position, expected, 1e-6);
    }
}
