using System.Numerics;
using AwesomeAssertions;
using Tertius.Physics;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Ticket ter-swgm. The local rotation of every frame is a unit quaternion (see <see cref="FrameTree.AssertInvariants()"/>
/// ).
/// A caller can give the world a rotation that is not unit length: a spawn description, or a component that writes its
/// transform. The world normalizes it where it takes it, and does not refuse it. A rotation that physics reports is unit
/// length too. The tests run on the real world and the real Jolt backend.
/// </summary>
public sealed class UnitRotationWorldTests
{
    public static TheoryData<string, DQuaternion> RotationsThatAreNotUnitLength => new()
    {
        { "a rotation twice as long as a unit rotation", new(X: 0, Y: 0, Z: 0, W: 2) },
        {
            "a rotation that came out of a float quaternion",
            DQuaternion.FromQuaternion(new(x: 0.18257418f, y: 0.36514837f, z: 0.5477226f, w: 0.73029685f))
        },
        { "a short rotation", new(X: 0, Y: 0.1, Z: 0, W: 0.1) }
    };

    public static TheoryData<string, DQuaternion> RotationsThatAreUnitLength => new()
    {
        { "a rotation from an axis and an angle", DQuaternion.FromAxisAngle(new(1, 2, 3), 0.7) },
        // The square of its length is 2E-14 above 1, inside the tolerance, and dividing by its length would change its bits.
        {
            "a rotation a little long, inside the tolerance",
            new(X: 0, Y: 0, Z: Math.Sqrt(0.5) * (1 + 1e-14), W: Math.Sqrt(0.5) * (1 + 1e-14))
        }
    };

    private static TestWorld NewWorld() =>
        new(physics: new JoltPhysicsBackend(JoltBackendOptions.Unpaced),
            configure: options => options.DisposePhysics = true);

    /// <summary>How far the square of the length of <paramref name="rotation"/> is from 1.</summary>
    private static double Excess(DQuaternion rotation) => Math.Abs(rotation.LengthSquared - 1);

    private static void ExpectSameTurn(DQuaternion actual, DQuaternion given)
    {
        PoseChecks.AssertSameRotation(actual, given.Normalized(), 1e-12);
        Excess(actual).Should().BeLessThan(1e-12);
    }

    [Theory]
    [MemberData(nameof(RotationsThatAreNotUnitLength))]
    public void ASpawnedFrameWhoseRotationIsNotUnitLengthGetsTheUnitRotationOfTheSameTurn(string why, DQuaternion given)
    {
        using TestWorld test = NewWorld();

        Entity owner = test.World.Spawn(new()
        {
            Frame = test.Root,
            Pose = new(new(6.77e6, 0, 0), given),
            AsFrame = new(FrameBounds.Sphere(100))
        });

        FrameHandle frame = test.World.GetOwnedFrame(owner);
        ExpectSameTurn(test.World.Frames.LocalPose(frame).Rotation, given);
        ExpectSameTurn(test.World.PoseInOwnFrame(owner).Rotation, given);
        test.World.Frames.LocalPose(frame).Position.Should().Be(new Double3(6.77e6, 0, 0), why);
    }

    [Theory]
    [MemberData(nameof(RotationsThatAreNotUnitLength))]
    public void AnEntityFarInsideAFrameInASpawnedFrameIsWhereTheUnitRotationOfTheOuterFramePutsIt(string why,
        DQuaternion given)
    {
        using TestWorld test = NewWorld();
        FrameHandle outer = test.SpawnStaticFrame(new(Double3.Zero, given), radius: 1e8f);
        FrameHandle inner = test.World.SpawnStaticFrame(outer, TestWorld.At(6.77e6, 0, 0), radius: 1e4f);
        Entity inside = test.World.SpawnAt(inner, new(0, 0, 1000));

        test.World.Snapshot.TryGetPoseIn(inside, test.Root, out DPose inRoot).Should().BeTrue();

        // The inner frame is 6.77E6 m from the outer frame's origin, so a rotation of the outer frame that is not unit length
        // moves the entity by a large part of that distance.
        PoseChecks.AssertNear(inRoot.Position, given.Normalized().Rotate(new Double3(6.77e6, 0, 1000)), 1e-6);
        Excess(inRoot.Rotation).Should().BeLessThan(1e-12, why);
    }

    [Theory]
    [MemberData(nameof(RotationsThatAreUnitLength))]
    public void AFrameSpawnedWithAUnitRotationKeepsItBitForBit(string why, DQuaternion rotation)
    {
        using TestWorld test = NewWorld();

        FrameHandle frame = test.SpawnStaticFrame(new(new(10, 0, 0), rotation));

        test.World.Frames.LocalPose(frame).Rotation.Should().Be(rotation, why);
        test.World.PoseInOwnFrame(frame.Entity).Rotation.Should().Be(rotation, why);
    }

    [Theory]
    [MemberData(nameof(RotationsThatAreNotUnitLength))]
    public void ASpawnedEntityWhoseRotationIsNotUnitLengthGetsTheUnitRotationOfTheSameTurn(string why,
        DQuaternion given)
    {
        using TestWorld test = NewWorld();

        Entity entity = test.World.Spawn(new() { Frame = test.Root, Pose = new(new(1, 2, 3), given) });

        ExpectSameTurn(test.World.PoseInOwnFrame(entity).Rotation, given);
        test.World.PoseInOwnFrame(entity).Position.Should().Be(new Double3(1, 2, 3), why);
    }

    [Theory]
    [MemberData(nameof(RotationsThatAreNotUnitLength))]
    public void AStaticFrameWhoseOwnerWritesARotationThatIsNotUnitLengthGetsTheUnitRotationOfTheSameTurn(string why,
        DQuaternion given)
    {
        using TestWorld test = NewWorld();
        Entity owner = test.World.Spawn(new()
        {
            Frame = test.Root,
            Pose = new(new(6.77e6, 0, 0), DQuaternion.Identity),
            AsFrame = new(FrameBounds.Sphere(100)),
            Components =
            [
                Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
                    self.SetTransform(self.PoseInOwnFrame() with { Rotation = given }))
            ]
        });
        FrameHandle frame = test.World.GetOwnedFrame(owner);

        test.Run(2);

        ExpectSameTurn(test.World.Frames.LocalPose(frame).Rotation, given);
        ExpectSameTurn(test.World.PoseInOwnFrame(owner).Rotation, given);
        test.World.Frames.LocalPose(frame).Position.Should().Be(new Double3(6.77e6, 0, 0), why);
    }

    [Fact]
    public void AShipFrameThatPhysicsMovesKeepsAUnitRotationOnEveryTick()
    {
        using TestWorld test = NewWorld();
        Entity owner = test.World.Spawn(new()
        {
            Frame = test.Root,
            Pose = new(new(6.77e6, 0, 0), DQuaternion.Identity),
            LinearVelocity = new Vector3(0, 7700, 0),
            AngularVelocity = new(0.0011f, 0.0007f, 0.0013f),
            AsFrame = new(FrameBounds.Sphere(100)),
            Body = new(Shape: new BoxShapeDesc(new(10, 4, 30)), Mass: 1e5f, LinearDamping: 0, AngularDamping: 0)
        });
        FrameHandle ship = test.World.GetOwnedFrame(owner);

        double worst = 0;
        for (int tick = 0; tick < 120; tick++)
        {
            test.Run(1);
            worst = Math.Max(worst,
                Math.Max(Excess(test.World.Frames.LocalPose(ship).Rotation),
                    Excess(test.World.PoseInOwnFrame(owner).Rotation)));
        }

        worst.Should().BeLessThan(1e-12);
        test.World.PoseIn(owner, test.Root).Position.Y.Should()
            .BeGreaterThan(10_000, "the ship moved 2 s at 7,700 m/s along +Y");
    }
}
