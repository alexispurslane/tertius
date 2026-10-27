using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The point of a body that its linear velocity belongs to, on the World and the real Jolt backend
/// (docs/plans/real-physics-in-tests.md, "The velocity point"): every linear velocity that crosses the physics seam is
/// the velocity of the body's origin, the point that its pose names, also for a spinning body whose center of mass lies
/// off its origin.
/// <para>
/// Each off-center body here is a box of 1 m whose center sits 3 m along +X from the body's origin, so its center of mass
/// is there, and it spins at 0.5 rad/s about +Y. Its origin then moves 1.5 m/s faster or slower than its center of mass,
/// 25 mm in one step at 60 Hz. Nothing damps it and no gravity pulls it, so its center of mass moves on a straight line,
/// and its origin circles the center of mass at 3 m. Over one step the origin curves off the line of its velocity by
/// half of its centripetal acceleration times the step squared, ω² r dt² / 2 = 0.1 mm. The tolerance of 0.2 mm covers
/// that and the float rounding of Jolt's velocities, and lies far below the 25 mm of a velocity of the center of mass.
/// </para>
/// </summary>
public sealed class OriginVelocityTests
{
    private const double Spin = 0.5;

    /// <summary>How far, in m, the origin of an off-center body may end a step from where its velocity points.</summary>
    private const double Curve = 2e-4;

    /// <summary>Sets the velocity of its entity's body, and with <paramref name="angular"/> its angular velocity, on tick
    /// <paramref name="onTick"/>, or on every tick when <paramref name="onTick"/> is null.</summary>
    private sealed class SetsVelocity(Double3 velocity, Vector3? angular, long? onTick) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (onTick is { } tick && self.Tick != tick)
                return;
            self.Body.SetVelocity(velocity);
            if (angular is { } spin)
                self.Body.SetAngularVelocity(spin);
        }
    }

    /// <summary>
    /// Turns its kinematic entity about its origin, which stays where it is: in each tick it writes, as an end pose, the
    /// rotation by <see cref="Spin"/> about +Y since the time 0 at the end of the next step.
    /// </summary>
    private sealed class TurnsInPlace : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            self.MoveKinematic(
                new(Double3.Zero, DQuaternion.FromAxisAngle(Double3.UnitY, Spin * self.TimeAt(self.Tick + 2)))
            );
    }

    /// <summary>A body of 10 kg whose center of mass sits 3 m along +X from its origin: a box of 1 m centered there.</summary>
    private static BodyDesc OffCenterBody(BodyMotion motion = BodyMotion.Dynamic) =>
        new(
            Shape: new CompoundShapeDesc([new(new BoxShapeDesc(new(0.5f)), new(new(3, 0, 0), Quaternion.Identity))]),
            Motion: motion,
            Mass: 10,
            LinearDamping: 0,
            AngularDamping: 0
        );

    private static FrameHandle Space(World world) => world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));

    private static Vector3 SpinAboutY => new(0, (float)Spin, 0);

    [Fact]
    public void ASpinningBodyWhoseCenterOfMassSitsOffItsOriginReportsTheVelocityOfItsOrigin()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Space(world);
        Entity body = world.Spawn(
            new()
            {
                Frame = space,
                Pose = DPose.Identity,
                AngularVelocity = SpinAboutY,
                Body = OffCenterBody(),
            }
        );
        Tick(world, 3);
        Double3 before = world.PoseIn(body, space).Position;
        Double3 velocity = world.VelocityIn(body, space);

        world.Tick();

        Double3 moved = world.PoseIn(body, space).Position - before;
        Double3
            .Distance(moved, velocity * world.DeltaTime)
            .Should()
            .BeLessThan(Curve, "the origin moves on by the velocity that the World read for it");
    }

    [Fact]
    public void ABodyCreatedWithAVelocityOfItsOriginMovesItsOriginAtThatVelocityOnTheFirstTickWhileItSpins()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Space(world);
        var velocity = new Double3(2, 0, 0);
        Entity body = world.Spawn(
            new()
            {
                Frame = space,
                Pose = DPose.Identity,
                LinearVelocity = velocity,
                AngularVelocity = SpinAboutY,
                Body = OffCenterBody(),
            }
        );

        int ticks = TickUntil(world, () => world.PoseIn(body, space).Position != Double3.Zero, limit: 10);

        ticks.Should().BeLessThan(10, "the body moves in the first step after its spawn");
        Double3
            .Distance(world.PoseIn(body, space).Position, velocity * world.DeltaTime)
            .Should()
            .BeLessThan(Curve, "the spawn velocity is the velocity of the origin, which starts at the spawn pose");
    }

    [Fact]
    public void ABodyWhoseVelocityAndSpinTheGameSetsMovesItsOriginAtThatVelocityInTheStepThatAppliesThem()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Space(world);
        var velocity = new Double3(2, 0, 0);
        Entity body = world.Spawn(
            new()
            {
                Frame = space,
                Pose = DPose.Identity,
                Body = OffCenterBody(),
                Components = [new SetsVelocity(velocity, SpinAboutY, onTick: 3)],
            }
        );

        int ticks = TickUntil(world, () => world.PoseIn(body, space).Position != Double3.Zero, limit: 10);

        ticks.Should().BeLessThan(10, "the body moves in the step that applies the velocity that the game set");
        Double3
            .Distance(world.PoseIn(body, space).Position, velocity * world.DeltaTime)
            .Should()
            .BeLessThan(
                Curve,
                "the set velocity is the velocity of the origin with the spin that the same intent sets, not the spin before it"
            );
    }

    [Fact]
    public void ASpinningBodyWhoseVelocityTheGameSetsOnEveryTickMovesItsOriginAtThatVelocity()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Space(world);
        var velocity = new Double3(2, 0, 0);
        Entity body = world.Spawn(
            new()
            {
                Frame = space,
                Pose = DPose.Identity,
                AngularVelocity = SpinAboutY,
                Body = OffCenterBody(),
                Components = [new SetsVelocity(velocity, angular: null, onTick: null)],
            }
        );
        Tick(world, 5);
        Double3 before = world.PoseIn(body, space).Position;

        world.Tick();

        Double3 moved = world.PoseIn(body, space).Position - before;
        Double3
            .Distance(moved, velocity * world.DeltaTime)
            .Should()
            .BeLessThan(
                Curve,
                "each step starts with the origin at the set velocity, under the spin that the body has"
            );
    }

    [Fact]
    public void ASpinningBodyThatHandsOffIntoABubbleReportsTheVelocityOfItsOriginThere()
    {
        // The bubble is a frame without a body, at rest, so a velocity relative to it is the velocity relative to space.
        using World world = NewWorld(backend: out _);
        FrameHandle space = Space(world);
        Entity bay = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(15),
                AsFrame = new(FrameBounds.Sphere(5)),
            }
        );
        FrameHandle bubble = world.GetOwnedFrame(bay);
        Entity body = world.Spawn(
            new()
            {
                Frame = space,
                Pose = DPose.Identity,
                LinearVelocity = new(3, 0, 0),
                AngularVelocity = SpinAboutY,
                Body = OffCenterBody(),
            }
        );
        int ticks = TickUntil(world, () => world.GetFrame(body) == bubble, limit: 600);
        ticks.Should().BeLessThan(600, "the body flies into the bubble");
        Double3 before = world.PoseIn(body, bubble).Position;
        Double3 velocity = world.VelocityIn(body, bubble);

        world.Tick();

        Double3 moved = world.PoseIn(body, bubble).Position - before;
        Double3
            .Distance(moved, velocity * world.DeltaTime)
            .Should()
            .BeLessThan(Curve, "the hand-off carries the velocity of the origin into the bubble");
    }

    [Fact]
    public void AKinematicBodyThatAMotionIntentTurnsAboutItsOriginReportsItsOriginAtRest()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Space(world);
        Entity body = world.Spawn(
            new()
            {
                Frame = space,
                Pose = DPose.Identity,
                Body = OffCenterBody(BodyMotion.Kinematic),
                Components = [new TurnsInPlace()],
            }
        );

        Tick(world, 5);

        world.AngularVelocityIn(body, space).Y.Should().BeApproximately((float)Spin, 1e-6f, "the end poses turn it");
        world
            .VelocityIn(body, space)
            .Length.Should()
            .BeLessThan(1e-6, "the end poses hold its origin still, while its center of mass circles at 1.5 m/s");
    }

    [Fact]
    public void ASpinningBodyWhoseCenterOfMassIsItsOriginReportsJoltsVelocityBitForBit()
    {
        // Below 1 m/s, so that no numeric frame takes the body over and the World's velocity is the row's alone.
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = Space(world);
        Entity cube = world.Spawn(
            new()
            {
                Frame = space,
                Pose = DPose.Identity,
                LinearVelocity = new(0.6, 0.2, -0.3),
                AngularVelocity = new(0.3f, (float)Spin, -0.2f),
                Body = new(Shape: new BoxShapeDesc(new(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0),
            }
        );

        Tick(world, 3);

        world.GetFrame(cube).Should().Be(space);
        Vector3 jolts = backend
            .BodiesIn(space)
            .Single(facts =>
                BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, cube)
            )
            .LinearVelocity;
        world
            .VelocityIn(cube, space)
            .Should()
            .Be(
                (Double3)jolts,
                "a body whose center of mass is its origin takes no conversion, so not even a rounding"
            );
    }
}
