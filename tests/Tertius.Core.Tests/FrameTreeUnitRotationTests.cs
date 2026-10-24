using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// Ticket ter-swgm. The local rotation of every frame is a unit quaternion. The pose tree composes poses and inverts them
/// on that assumption, so a frame whose rotation is a little longer or shorter than 1 moves everything below it, and
/// everything seen from it, by that much of the distance to the frame's parent. The tree checks it in
/// <see cref="FrameTree.AssertInvariants()"/>, which works in every build, and its setters refuse a rotation that
/// is not unit length in a build with contracts. The tolerance is 1E-12 in the square of the length.
/// </summary>
public class FrameTreeUnitRotationTests
{
    private static readonly DQuaternion Widened =
        DQuaternion.FromQuaternion(new(x: 0.18257418f, y: 0.36514837f, z: 0.5477226f, w: 0.73029685f));

    private static readonly Double3 InOrbit = new(4.2e6, -3.9e6, 3.6e6);

    public static TheoryData<string, DQuaternion> RotationsThatAreNotUnitLength => new()
    {
        { "a rotation 1E-7 too long", Scaled(1 + 1e-7) },
        { "a rotation 1E-7 too short", Scaled(1 - 1e-7) },
        { "a rotation that came out of a float quaternion", Widened },
        { "a rotation twice as long", Scaled(2) },
        { "a rotation just outside the tolerance", Scaled(1 + 5e-12) },
        { "the zero rotation", default },
        { "a rotation that is not a number", new(X: double.NaN, Y: 0, Z: 0, W: 1) }
    };

    public static TheoryData<string, DQuaternion> RotationsThatAreUnitLength => new()
    {
        { "the identity", DQuaternion.Identity },
        { "a rotation from an axis and an angle", DQuaternion.FromAxisAngle(new(1, 2, 3), 0.7) },
        { "a rotation that came out of a float quaternion and was normalized", Widened.Normalized() },
        { "a rotation a little inside the tolerance on the long side", Scaled(1 + 1e-14) },
        { "a rotation a little inside the tolerance on the short side", Scaled(1 - 1e-14) }
    };

    /// <summary>A rotation about +Z by a quarter turn, scaled so that its length is <paramref name="length"/>.</summary>
    private static DQuaternion Scaled(double length) => new(X: 0, Y: 0, Z: Math.Sqrt(0.5) * length, W: Math.Sqrt(0.5) * length);

    /// <summary>The root frame "earth" and the frame "ship" in it, at the place of a ship in a low orbit.</summary>
    private sealed class OrbitingShip
    {
        public readonly FrameTree Frames = new();
        public readonly FrameHandle Earth;
        public readonly FrameHandle Ship;

        public OrbitingShip()
        {
            Earth = Frames.Create(frameOwner: new(1, 1), parent: FrameHandle.Null, localPose: DPose.Identity, frameBounds: FrameBounds.Sphere(1e9f));
            Ship = Frames.Create(frameOwner: new(2, 1), parent: Earth, localPose: new(InOrbit, DQuaternion.FromAxisAngle(Double3.UnitX, 0.4)),
                frameBounds: FrameBounds.Sphere(100));
            Frames.Update();
        }

        /// <summary>
        /// Writes <paramref name="pose"/> into the tree's own storage, past the checks of the setters, as a bug in the tree would.
        /// </summary>
        public void Corrupt(FrameHandle frame, DPose pose)
        {
            ReadOnlySpan<DPose> locals = Frames.LocalPosesDense;
            Unsafe.Add(ref Unsafe.AsRef(in MemoryMarshal.GetReference(locals)), Frames.DenseIndex(frame)) = pose;
        }
    }

    [Fact]
    public void FramesWithUnitRotationsKeepTheInvariants()
    {
        var test = new OrbitingShip();

        Action check = test.Frames.AssertInvariants;

        check.Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(RotationsThatAreUnitLength))]
    public void AFrameWhoseLocalRotationIsUnitLengthKeepsTheInvariants(string why, DQuaternion rotation)
    {
        var test = new OrbitingShip();
        test.Corrupt(test.Ship, new(InOrbit, rotation));

        Action check = test.Frames.AssertInvariants;

        check.Should().NotThrow(why);
    }

    [Theory]
    [MemberData(nameof(RotationsThatAreNotUnitLength))]
    public void AFrameWhoseLocalRotationIsNotUnitLengthBreaksTheInvariants(string why, DQuaternion rotation)
    {
        var test = new OrbitingShip();
        test.Corrupt(test.Ship, new(InOrbit, rotation));

        Action check = test.Frames.AssertInvariants;

        check.Should().Throw<ContractException>(why).WithMessage("*Invariant*");
    }

    [Fact]
    public void ARootFrameWhoseLocalRotationIsNotUnitLengthBreaksTheInvariants()
    {
        var test = new OrbitingShip();
        test.Corrupt(test.Earth, new(Double3.Zero, Widened));

        Action check = test.Frames.AssertInvariants;

        check.Should().Throw<ContractException>().WithMessage("*Invariant*");
    }

    [Fact]
    public void ReparentingAFrameWithAUnitRotationKeepsTheInvariants()
    {
        // A move that keeps the world pose composes poses. Its result must be a unit rotation too.
        var test = new OrbitingShip();
        FrameHandle station = test.Frames.Create(frameOwner: new(3, 1), parent: test.Earth,
            localPose: new(InOrbit + new Double3(500, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 1.1)),
            frameBounds: FrameBounds.Sphere(300));
        test.Frames.Update();

        test.Frames.Reparent(test.Ship, station);
        test.Frames.Update();
        test.Frames.Destroy(station);
        test.Frames.Update();

        Action check = test.Frames.AssertInvariants;
        check.Should().NotThrow();
    }

#if TERTIUS_CONTRACTS
    [Theory]
    [MemberData(nameof(RotationsThatAreNotUnitLength))]
    public void SettingALocalPoseWhoseRotationIsNotUnitLengthIsABug(string why, DQuaternion rotation)
    {
        var test = new OrbitingShip();

        Action set = () => test.Frames.SetLocalPose(test.Ship, new(InOrbit, rotation));

        set.Should().Throw<ContractException>(why).WithMessage("Precondition*");
    }

    [Theory]
    [MemberData(nameof(RotationsThatAreNotUnitLength))]
    public void CreatingAFrameWhoseLocalRotationIsNotUnitLengthIsABug(string why, DQuaternion rotation)
    {
        var test = new OrbitingShip();

        Action create = () => test.Frames.Create(frameOwner: new(3, 1), parent: test.Earth, localPose: new(InOrbit, rotation), frameBounds: FrameBounds.Sphere(10));

        create.Should().Throw<ContractException>(why).WithMessage("Precondition*");
    }

    [Theory]
    [MemberData(nameof(RotationsThatAreUnitLength))]
    public void SettingALocalPoseWhoseRotationIsUnitLengthIsAccepted(string why, DQuaternion rotation)
    {
        var test = new OrbitingShip();

        test.Frames.SetLocalPose(test.Ship, new(InOrbit, rotation));

        test.Frames.LocalPose(test.Ship).Rotation.Should().Be(rotation, why);
    }
#endif
}
