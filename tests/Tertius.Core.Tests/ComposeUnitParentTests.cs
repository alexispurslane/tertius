using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// Ticket ter-swgm made <see cref="DPose.Compose"/> normalize the rotation of its parent, so that it moves the position of the
/// child by the same rotation that it composes into the child's rotation. That costs a division for every pose that the
/// render snapshot builds, and the snapshot never needs it, because its parent is a pose from the frame tree, which is unit
/// length by construction. <see cref="DPose.ComposeUnitParent"/> is the same composition for a parent whose rotation is unit
/// length. These tests compare it with <see cref="DPose.Compose"/>, and check that a build with contracts refuses a parent
/// that is not unit length.
/// </summary>
public class ComposeUnitParentTests
{
    /// <summary>A rotation that came out of a float quaternion, as Jolt gives it: not unit length. Its square is 1.7E-7 over 1.</summary>
    private static readonly DQuaternion Widened =
        DQuaternion.FromQuaternion(new(0.18257418f, 0.36514837f, 0.5477226f, 0.73029685f));

    /// <summary>The place of a ship in a low orbit round Earth, in Earth's axes, 6.77E6 m from the center.</summary>
    private static readonly Double3 InOrbit = new(4.2e6, -3.9e6, 3.6e6);

    /// <summary>The place of a child in its parent's axes, 5385 m from the parent's origin.</summary>
    private static readonly Double3 Offset = new(3000, 2000, -4000);

    public static TheoryData<string, DQuaternion> UnitRotations => new()
    {
        { "the identity", DQuaternion.Identity },
        { "a rotation about an axis", DQuaternion.FromAxisAngle(new(1, 2, 3), 0.9) },
        { "a rotation by half a turn", DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI) },
        { "a float rotation from Jolt, once normalized", Widened.Normalized() }
    };

    public static TheoryData<string, DQuaternion> RotationsThatAreNotUnitLength => new()
    {
        { "a float rotation from Jolt, not normalized", Widened },
        { "a rotation twice as long", Scaled(2) },
        { "a rotation half as long", Scaled(0.5) },
        { "a rotation just outside the tolerance", Scaled(1 + 5e-12) },
        { "the zero rotation", default },
        { "a rotation that is not a number", new(double.NaN, 0, 0, 1) }
    };

    /// <summary>A rotation about +Z by a quarter turn, scaled so that its length is <paramref name="length"/>.</summary>
    private static DQuaternion Scaled(double length) => new(0, 0, Math.Sqrt(0.5) * length, Math.Sqrt(0.5) * length);

    public static TheoryData<string, DQuaternion, DQuaternion> UnitParentsAndLocalRotations()
    {
        var data = new TheoryData<string, DQuaternion, DQuaternion>();
        foreach ((string parentName, DQuaternion parent) in new[]
                 {
                     ("the identity", DQuaternion.Identity),
                     ("a rotation about an axis", DQuaternion.FromAxisAngle(new(1, 2, 3), 0.9)),
                     ("a rotation by half a turn", DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI)),
                     ("a float rotation from Jolt, once normalized", Widened.Normalized())
                 })
        foreach ((string localName, DQuaternion local) in new[]
                 {
                     ("the identity", DQuaternion.Identity),
                     ("a rotation about another axis", DQuaternion.FromAxisAngle(new(-2, 1, 1), -1.3)),
                     ("a float rotation from Jolt, not normalized", Widened)
                 })
            data.Add($"a parent with {parentName} and a child with {localName}", parent, local);

        return data;
    }

    private static void ExpectSameComponents(DQuaternion actual, DQuaternion expected, double tolerance)
    {
        Math.Abs(actual.X - expected.X).Should().BeLessThan(tolerance, $"{actual} should be {expected}");
        Math.Abs(actual.Y - expected.Y).Should().BeLessThan(tolerance, $"{actual} should be {expected}");
        Math.Abs(actual.Z - expected.Z).Should().BeLessThan(tolerance, $"{actual} should be {expected}");
        Math.Abs(actual.W - expected.W).Should().BeLessThan(tolerance, $"{actual} should be {expected}");
    }

    [Theory]
    [MemberData(nameof(UnitParentsAndLocalRotations))]
    public void ItGivesTheSamePoseAsComposeForAParentOfUnitLength(string why, DQuaternion parentRotation,
        DQuaternion localRotation)
    {
        var parent = new DPose(InOrbit, parentRotation);
        var local = new DPose(Offset, localRotation);

        DPose composed = DPose.ComposeUnitParent(parent, local);

        DPose expected = DPose.Compose(parent, local);
        Double3.Distance(composed.Position, expected.Position).Should().BeLessThan(1e-9, why);
        ExpectSameComponents(composed.Rotation, expected.Rotation, 1e-12);
    }

    [Theory]
    [MemberData(nameof(UnitRotations))]
    public void ItMovesThePositionOfTheChildByTheRotationOfItsParent(string why, DQuaternion parentRotation)
    {
        var parent = new DPose(InOrbit, parentRotation);

        DPose composed = DPose.ComposeUnitParent(parent, new(Offset, DQuaternion.Identity));

        Double3.Distance(composed.Position, InOrbit + parentRotation.Rotate(Offset)).Should().BeLessThan(1e-9, why);
    }

    [Fact]
    public void TheRotationOfTheResultIsOfUnitLengthWhateverTheRotationOfTheChild()
    {
        // The snapshot builder passes the pose of an entity, which a program wrote or Jolt gave, and its rotation is not unit.
        // The result is a pose in the frame tree's sense again, so the next composition can rely on it.
        var parent = new DPose(InOrbit, DQuaternion.FromAxisAngle(new(1, 2, 3), 0.9));

        DPose composed = DPose.ComposeUnitParent(parent, new(Offset, Widened));

        (composed.Rotation.LengthSquared - 1).Should().BeApproximately(0, 1e-14);
    }

    [Fact]
    public void AParentWithinTheUnitToleranceMovesThePositionByAnErrorWithinTheTolerance()
    {
        // Decision: a parent whose square of the length is within DQuaternion.UnitTolerance of 1 counts as unit, and its rotation
        // is used as it is. So the position of the result differs from that of Compose by at most the length of the child's
        // position times the tolerance, here 5.4E-9 m.
        DQuaternion edge = Scaled(Math.Sqrt(1 + 5e-13));
        edge.IsUnit.Should().BeTrue("the rotation is inside the tolerance, which is the premise of this test");
        var parent = new DPose(InOrbit, edge);
        var local = new DPose(Offset, DQuaternion.Identity);

        DPose composed = DPose.ComposeUnitParent(parent, local);

        DPose expected = DPose.Compose(parent, local);
        Double3.Distance(composed.Position, expected.Position).Should()
            .BeLessThan(Offset.Length * DQuaternion.UnitTolerance);
    }

#if TERTIUS_CONTRACTS
    [Theory]
    [MemberData(nameof(RotationsThatAreNotUnitLength))]
    public void AParentWhoseRotationIsNotUnitLengthIsABug(string why, DQuaternion rotation)
    {
        var parent = new DPose(InOrbit, rotation);

        Action compose = () => DPose.ComposeUnitParent(parent, new(Offset, DQuaternion.Identity));

        compose.Should().Throw<ContractException>(why).WithMessage("Precondition*");
    }

    [Theory]
    [MemberData(nameof(UnitRotations))]
    public void AParentWhoseRotationIsUnitLengthIsAccepted(string why, DQuaternion rotation)
    {
        var parent = new DPose(InOrbit, rotation);

        Action compose = () => DPose.ComposeUnitParent(parent, new(Offset, Widened));

        compose.Should().NotThrow(why);
    }
#endif
}
