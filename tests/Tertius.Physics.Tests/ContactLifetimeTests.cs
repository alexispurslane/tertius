using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.WorldTestingGrowth;

namespace Tertius.Physics.Tests;

/// <summary>
/// Physics reports a contact once, when it starts. A contact that never ended is not reported again, whatever Jolt does
/// underneath. Jolt forgets the contacts of a body that falls asleep while the rest of its system keeps stepping, and it
/// reports them again when the body wakes. A growth of the system forgets every contact too. A contact that really ends
/// while a body sleeps or is static still ends, so it is reported again when it starts again. A hull that changes its
/// motion ends each contact that its new motion cannot make, such as a touch of a static body when it parks, and keeps
/// each one that its new motion can make (rule 6 of docs/plans/complete-ghosts.md). The tests run the World on the real backend and count the contacts in the step
/// outputs that Jolt really sent, which <see cref="FaultInjectingBackend"/> records.
/// </summary>
public class ContactLifetimeTests
{
    /// <summary>A cube of 1 m with no damping and no bounce, so that it touches the floor once when it lands.</summary>
    private static BodyDesc BoxDesc(float mass) =>
        new(new BoxShapeDesc(new Vector3(0.5f)), Mass: mass, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A ball with no damping and no bounce.</summary>
    private static BodyDesc BallDesc(float radius, float mass) =>
        new(new SphereShapeDesc(radius), Mass: mass, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A ground frame with gravity of 9.81 m/s² and a floor whose top is at y = 0, in a World on the real backend.</summary>
    private sealed class Ground : IDisposable
    {
        public Ground(int? maxBodiesPerFrame = null)
        {
            World = maxBodiesPerFrame is { } limit
                ? NewWorldWithFaultsAndSmallSystems(limit, out FaultInjectingBackend small)
                : NewWorldWithFaults(out small);
            Faults = small;
            Frame = WorldTesting.Ground(World, 9.81f);
            Floor = WorldTesting.Floor(World, Frame);
        }

        public World World { get; }
        public FaultInjectingBackend Faults { get; }
        public FrameHandle Frame { get; }
        public Entity Floor { get; }

        /// <summary>The number of contacts between <paramref name="a"/> and <paramref name="b"/> that Jolt reported so far.</summary>
        public int ContactsOf(Entity a, Entity b) => Faults.Outputs.SelectMany(output => output.Contacts)
            .Count(contact => (contact.A == a && contact.B == b) || (contact.A == b && contact.B == a));

        /// <summary>The number of ends of contacts between <paramref name="a"/> and <paramref name="b"/> that Jolt reported so far.</summary>
        public int EndsOf(Entity a, Entity b) => Faults.Outputs.SelectMany(output => output.ContactsEnded)
            .Count(ended => (ended.A == a && ended.B == b) || (ended.A == b && ended.B == a));

        /// <summary>True while Jolt simulates the body of <paramref name="entity"/> in the system of the ground.</summary>
        public bool IsAwake(Entity entity) => WorldTesting.IsAwake(Faults.Jolt, Frame, entity);

        /// <summary>
        /// A kinematic block that slides slowly, high above the far end of the floor, and touches nothing. It is always
        /// awake, so the system of the ground steps every tick, also while every other body sleeps.
        /// </summary>
        public void KeepSteppingTheSystem() => World.Spawn(KinematicDrift.Drifting(new SpawnDesc
        {
            Frame = Frame,
            Pose = At(-40, 20),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Kinematic),
        }, World, new Double3(0, 0, 0.1)));

        /// <summary>Adds a box of 1 m that rests on the floor, and ticks until it is asleep.</summary>
        public Entity SleepingBox(params IComponent[] components)
        {
            Entity box = World.Spawn(new SpawnDesc { Frame = Frame, Pose = At(0, 0.5), Body = BoxDesc(mass: 10), Components = components });
            World.Tick();
            TickUntil(World, () => !IsAwake(box), 180);
            IsAwake(box).Should().BeFalse("the box rests on the floor, so it falls asleep");
            ContactsOf(Floor, box).Should().Be(1, "the box touched the floor when it arrived");
            return box;
        }

        /// <summary>Adds a lander, a frame with a dynamic hull, 2 m above the floor.</summary>
        public FrameHandle Lander()
        {
            Entity owner = World.Spawn(new SpawnDesc
            {
                Frame = Frame,
                Pose = At(0, 2),
                Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
                AsFrame = new FrameSpec(FrameBounds.Sphere(1)),
            });
            return World.GetOwnedFrame(owner);
        }

        /// <summary>Changes the gravity of the ground a little, which wakes every body in it.</summary>
        public void ChangeGravity() => World.SetFrameGravity(Frame, GravityField.Uniform(new Vector3(0, -9.82f, 0)));

        public void Dispose() => World.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABoxThatSleepsOnTheFloorAndWakesThereReportsNoNewContact(bool systemKeepsStepping)
    {
        using var ground = new Ground();
        if (systemKeepsStepping)
            ground.KeepSteppingTheSystem();
        Entity box = ground.SleepingBox();
        Tick(ground.World, 30);

        ground.ChangeGravity();
        Tick(ground.World, 5);
        ground.IsAwake(box).Should().BeTrue("the new gravity woke the box");
        Tick(ground.World, 30);

        ground.ContactsOf(ground.Floor, box).Should().Be(1, "the box never left the floor");
        ground.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABoxThatSleepsOnTheFloorAndWakesWhenABallHitsItReportsOnlyTheBall()
    {
        using var ground = new Ground();
        ground.KeepSteppingTheSystem();
        Entity box = ground.SleepingBox();
        Tick(ground.World, 30);

        Entity ball = ground.World.Spawn(new SpawnDesc { Frame = ground.Frame, Pose = At(0, 2), Body = BallDesc(0.25f, mass: 1) });
        Tick(ground.World, 60);

        ground.ContactsOf(ball, box).Should().Be(1, "the ball hit the box");
        ground.ContactsOf(ground.Floor, box).Should().Be(1, "the box never left the floor");
        ground.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABoxThatSleepsOnTheFloorThroughAGrowthOfItsSystemReportsNoNewContactWhenItWakes()
    {
        using var ground = new Ground(maxBodiesPerFrame: 8);
        ground.KeepSteppingTheSystem();
        Entity box = ground.SleepingBox();
        Tick(ground.World, 30);

        for (int i = 0; i < 8; i++)
            ground.World.Spawn(new SpawnDesc
            {
                Frame = ground.Frame,
                Pose = At(3 * i, -100),
                Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static),
            });
        Tick(ground.World, 30);
        ground.Faults.Jolt.CapacityOf(ground.Frame).Should().Be(16, "the system grew");
        ground.ChangeGravity();
        Tick(ground.World, 5);
        ground.IsAwake(box).Should().BeTrue("the new gravity woke the box");
        Tick(ground.World, 30);

        ground.ContactsOf(ground.Floor, box).Should().Be(1, "the box never left the floor");
        ground.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A lander with a dynamic hull rests on the floor. The game parks the lander for a second and then unparks it, each
    /// with a change of the hull's motion in a tick. A static body touches no static body, so the parked lander's touch of
    /// the floor ends at the park, and the lander touches the floor again when it is unparked: every pair of a body follows
    /// its new motion at once (rule 6 of docs/plans/complete-ghosts.md, which the user decided on 2026-10-17). Jolt does
    /// not simulate the parked lander or the floor, so it never drops their pair on its own, and the backend ends it at
    /// the change. Before ter-7ul9 the touch went on through the park, as the reviewer's probe C1 of the contact ledger
    /// asked, because the ledger counted a pair of two bodies that Jolt does not simulate as held.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALanderThatIsParkedOnTheFloorStopsTouchingItAndTouchesItAgainWhenItIsUnparked(bool systemKeepsStepping)
    {
        using var ground = new Ground();
        if (systemKeepsStepping)
            ground.KeepSteppingTheSystem();
        FrameHandle lander = ground.Lander();
        Tick(ground.World, 120);
        ground.ContactsOf(lander.Entity, ground.Floor).Should().Be(1);

        ground.World.SetMotionNextTick(lander.Entity, BodyMotion.Static);
        Tick(ground.World, 60);
        ground.EndsOf(lander.Entity, ground.Floor).Should().Be(1, "a parked lander is a static body, which touches no static body");
        ground.World.SetMotionNextTick(lander.Entity, BodyMotion.Dynamic);
        Tick(ground.World, 60);

        ground.ContactsOf(lander.Entity, ground.Floor).Should().Be(2, "the unparked lander touches the floor again");
        ground.EndsOf(lander.Entity, ground.Floor).Should().Be(1);
        ground.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A lander with a dynamic hull rests on a platform with a static hull, which stands on the floor. In one tick, the game
    /// parks the lander and makes the platform dynamic. The lander and the platform touch under both motions, a static body
    /// on a dynamic one, so their touch goes on, whichever of the two the backend follows first: physics judges each pair
    /// after every hull of the step took its new motion. The platform starts to touch the floor. The lander's bubble does not
    /// fit in the platform's, so the lander never enters the platform's frame.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoHullsThatTouchKeepTheirTouchWhenOneParksAndTheOtherStartsToMoveInOneStep(bool landerFirst)
    {
        using var ground = new Ground();
        Entity Spawn(double y, BodyMotion motion, float bubble) => ground.World.Spawn(new SpawnDesc
        {
            Frame = ground.Frame,
            Pose = At(0, y),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), motion, Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(bubble)),
        });
        Entity lander = default, platform = default;
        if (landerFirst)
            lander = Spawn(6, BodyMotion.Dynamic, 4);
        platform = Spawn(2, BodyMotion.Static, 3.5f);
        if (!landerFirst)
            lander = Spawn(6, BodyMotion.Dynamic, 4);
        Tick(ground.World, 60);
        ground.ContactsOf(lander, platform).Should().Be(1, "the lander rests on the platform");

        ground.World.SetMotionNextTick(lander, BodyMotion.Static);
        ground.World.SetMotionNextTick(platform, BodyMotion.Dynamic);
        Tick(ground.World, 30);

        ground.World.GetFrame(lander).Should().Be(ground.Frame);
        ground.ContactsOf(lander, platform).Should().Be(1, "a static body touches a dynamic one");
        ground.EndsOf(lander, platform).Should().Be(0);
        ground.ContactsOf(platform, ground.Floor).Should().Be(1, "the dynamic platform rests on the floor");
        ground.Faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        ground.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// With no gravity, a ship with a dynamic hull hangs at rest 1 cm above a static floor. That is within Jolt's
    /// speculative contact distance of 2 cm, so Jolt finds the pair, but the two never touch, so the pair never starts. The
    /// ship parks, and the pair that the new motion cannot make is no contact that ends: the ship hears nothing of the
    /// floor.
    /// </summary>
    [Fact]
    public void AShipThatParksJustAboveAFloorThatItNeverTouchedHearsNothingOfTheFloor()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        Entity floor = WorldTesting.Floor(world, space);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(0, 2.01),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(4)),
        });
        Tick(world, 3);
        faults.Jolt.PendingPairCount(space).Should().BeGreaterThan(0, "Jolt found the hull and the floor within its speculative contact distance");

        world.SetMotionNextTick(owner, BodyMotion.Static);
        Tick(world, 5);

        faults.Outputs.SelectMany(output => output.Contacts).Should().NotContain(contact => contact.A == floor || contact.B == floor);
        faults.Outputs.SelectMany(output => output.ContactsEnded).Should().NotContain(ended => ended.A == floor || ended.B == floor);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABoxThatTheGameLiftsOffTheFloorWhileItSleepsReportsTheFloorAgainWhenItLands(bool systemKeepsStepping)
    {
        using var ground = new Ground();
        if (systemKeepsStepping)
            ground.KeepSteppingTheSystem();
        var pilot = new Pilot();
        Entity box = ground.SleepingBox(pilot);
        Tick(ground.World, 30);

        // The pilot writes the teleport in this tick, and Jolt applies it in the step of the next one.
        pilot.Once = body => body.Teleport(At(0, 2.5));
        ground.World.Tick();
        Tick(ground.World, 120);

        ground.World.PoseInOwnFrame(box).Position.Y.Should().BeApproximately(0.5, 0.01, "the box fell back onto the floor");
        ground.ContactsOf(ground.Floor, box).Should().Be(2, "the box left the floor and landed again");
        ground.World.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AParkedLanderThatTheGameLiftsOffTheFloorReportsTheFloorAgainWhenItLands(bool systemKeepsStepping)
    {
        using var ground = new Ground();
        if (systemKeepsStepping)
            ground.KeepSteppingTheSystem();
        FrameHandle lander = ground.Lander();
        Tick(ground.World, 120);
        ground.ContactsOf(lander.Entity, ground.Floor).Should().Be(1);
        ground.World.SetMotionNextTick(lander.Entity, BodyMotion.Static);
        Tick(ground.World, 30);

        // A static frame moves where the game puts it.
        DPose parked = ground.World.Frames.LocalPose(lander);
        ground.World.Frames.SetLocalPose(lander, parked with { Position = parked.Position + new Double3(0, 3, 0) });
        Tick(ground.World, 30);
        ground.World.SetMotionNextTick(lander.Entity, BodyMotion.Dynamic);
        Tick(ground.World, 120);

        ground.World.Frames.LocalPose(lander).Position.Y.Should().BeApproximately(2, 0.01, "the lander fell back onto the floor");
        ground.ContactsOf(lander.Entity, ground.Floor).Should().Be(2, "the lander left the floor and landed again");
        ground.World.Errors.Should().BeEmpty();
    }
}
