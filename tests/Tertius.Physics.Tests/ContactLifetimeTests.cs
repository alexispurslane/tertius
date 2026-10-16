using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.WorldTestingGrowth;

namespace Tertius.Physics.Tests;

/// <summary>
/// Physics reports a contact once, when it starts. A contact that never ended is not reported again, whatever Jolt does
/// underneath. Jolt forgets the contacts of a body that falls asleep while the rest of its system keeps stepping, and the
/// contacts of a hull that becomes static. It reports them again when the body wakes or moves again. A growth of the
/// system forgets every contact too. A contact that really ends while a body sleeps or is static still ends, so it is
/// reported again when it starts again. The tests run the World on the real backend and count the contacts in the step
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

        /// <summary>True while Jolt simulates the body of <paramref name="entity"/> in the system of the ground.</summary>
        public bool IsAwake(Entity entity) => WorldTesting.IsAwake(Faults.Jolt, Frame, entity);

        /// <summary>
        /// A kinematic block that slides slowly, high above the far end of the floor, and touches nothing. It is always
        /// awake, so the system of the ground steps every tick, also while every other body sleeps.
        /// </summary>
        public void KeepSteppingTheSystem() => World.Spawn(new SpawnDesc
        {
            Frame = Frame,
            Pose = At(-40, 20),
            LinearVelocity = new Vector3(0, 0, 0.1f),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Kinematic),
        });

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

        /// <summary>Adds a lander, a frame with a hull that has the physics driver, 2 m above the floor.</summary>
        public FrameHandle Lander()
        {
            Entity owner = World.Spawn(new SpawnDesc
            {
                Frame = Frame,
                Pose = At(0, 2),
                Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
                AsFrame = new FrameSpec(FrameBounds.Sphere(1), MotionDriver.Physics),
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
    /// The reviewer's probe C1. A lander with the physics driver rests on the floor. The game parks the lander for a second
    /// and then gives it the physics driver again. Jolt drops the contact of two static bodies, but the lander never left
    /// the floor.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALanderThatIsParkedAndUnparkedOnTheFloorReportsNoNewContact(bool systemKeepsStepping)
    {
        using var ground = new Ground();
        if (systemKeepsStepping)
            ground.KeepSteppingTheSystem();
        FrameHandle lander = ground.Lander();
        Tick(ground.World, 120);
        ground.ContactsOf(lander.Entity, ground.Floor).Should().Be(1);

        ground.World.SetFrameDriver(lander, MotionDriver.Static);
        Tick(ground.World, 60);
        ground.World.SetFrameDriver(lander, MotionDriver.Physics);
        Tick(ground.World, 60);

        ground.ContactsOf(lander.Entity, ground.Floor).Should().Be(1, "the lander never left the floor");
        ground.World.Errors.Should().BeEmpty();
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
        ground.World.SetFrameDriver(lander, MotionDriver.Static);
        Tick(ground.World, 30);

        // A static frame moves where the game puts it.
        DPose parked = ground.World.Frames.LocalPose(lander);
        ground.World.Frames.SetLocalPose(lander, parked with { Position = parked.Position + new Double3(0, 3, 0) });
        Tick(ground.World, 30);
        ground.World.SetFrameDriver(lander, MotionDriver.Physics);
        Tick(ground.World, 120);

        ground.World.Frames.LocalPose(lander).Position.Y.Should().BeApproximately(2, 0.01, "the lander fell back onto the floor");
        ground.ContactsOf(lander.Entity, ground.Floor).Should().Be(2, "the lander left the floor and landed again");
        ground.World.Errors.Should().BeEmpty();
    }
}
