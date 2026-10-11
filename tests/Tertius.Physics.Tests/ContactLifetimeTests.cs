using System.Numerics;

namespace Tertius.Physics.Tests;

/// <summary>
/// Physics reports a contact once, when it starts. A contact that never ended is not reported again, whatever Jolt does
/// underneath. Jolt forgets the contacts of a body that falls asleep while the rest of its system keeps stepping, and the
/// contacts of a hull that becomes static. It reports them again when the body wakes or moves again. A growth of the
/// system forgets every contact too. A contact that really ends while a body sleeps or is static still ends, so it is
/// reported again when it starts again.
/// </summary>
public class ContactLifetimeTests
{
    /// <summary>True while Jolt simulates the body of <paramref name="entity"/> in the system of <paramref name="frame"/>.</summary>
    private static bool IsAwake(PhysicsHarness h, FrameHandle frame, Entity entity) =>
        h.Backend.BodiesIn(frame).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, entity)).Active;

    /// <summary>The number of contacts between <paramref name="a"/> and <paramref name="b"/> that physics reported so far.</summary>
    private static int ContactsOf(PhysicsHarness h, Entity a, Entity b) =>
        h.Contacts.Count(contact => (contact.A == a && contact.B == b) || (contact.A == b && contact.B == a));

    /// <summary>A ground frame with gravity of 9.81 m/s² and a floor whose top is at y = 0.</summary>
    private sealed class Ground : IDisposable
    {
        public Ground(JoltBackendOptions? options = null)
        {
            H = new PhysicsHarness(options);
            Frame = H.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static,
                GravityField.Uniform(new Vector3(0, -9.81f, 0)));
            Floor = H.AddBody(Frame, Shapes.Floor(50), Shapes.At(0, -0.5, 0));
        }

        public PhysicsHarness H { get; }
        public FrameHandle Frame { get; }
        public Entity Floor { get; }

        /// <summary>
        /// A kinematic block that slides slowly, high above the far end of the floor, and touches nothing. It is always
        /// awake, so the system of the ground steps every tick, also while every other body sleeps.
        /// </summary>
        public void KeepSteppingTheSystem() =>
            H.AddBody(Frame, new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Kinematic), Shapes.At(-40, 20, 0), velocity: new Vector3(0, 0, 0.1f));

        /// <summary>Adds a box of 1 m that rests on the floor, and steps until it is asleep.</summary>
        public Entity SleepingBox()
        {
            Entity box = H.AddBody(Frame, Shapes.Box(0.5f, mass: 10), Shapes.At(0, 0.5, 0));
            H.Step();
            H.StepUntil(() => !IsAwake(H, Frame, box), 180);
            IsAwake(H, Frame, box).Should().BeFalse("the box rests on the floor, so it falls asleep");
            ContactsOf(H, Floor, box).Should().Be(1, "the box touched the floor when it arrived");
            return box;
        }

        /// <summary>Changes the gravity of the ground a little, which wakes every body in it.</summary>
        public void ChangeGravity() => H.Frames.Gravity(Frame) = GravityField.Uniform(new Vector3(0, -9.82f, 0));

        public void Dispose() => H.Dispose();
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
        ground.H.Step(30);

        ground.ChangeGravity();
        ground.H.Step(5);
        IsAwake(ground.H, ground.Frame, box).Should().BeTrue("the new gravity woke the box");
        ground.H.Step(30);

        ContactsOf(ground.H, ground.Floor, box).Should().Be(1, "the box never left the floor");
    }

    [Fact]
    public void ABoxThatSleepsOnTheFloorAndWakesWhenABallHitsItReportsOnlyTheBall()
    {
        using var ground = new Ground();
        ground.KeepSteppingTheSystem();
        Entity box = ground.SleepingBox();
        ground.H.Step(30);

        Entity ball = ground.H.AddBody(ground.Frame, Shapes.Ball(0.25f, mass: 1), Shapes.At(0, 2, 0));
        ground.H.Step(60);

        ContactsOf(ground.H, ball, box).Should().Be(1, "the ball hit the box");
        ContactsOf(ground.H, ground.Floor, box).Should().Be(1, "the box never left the floor");
    }

    [Fact]
    public void ABoxThatSleepsOnTheFloorThroughAGrowthOfItsSystemReportsNoNewContactWhenItWakes()
    {
        using var ground = new Ground(new JoltBackendOptions { MaxBodiesPerFrame = 8 });
        ground.KeepSteppingTheSystem();
        Entity box = ground.SleepingBox();
        ground.H.Step(30);

        for (int i = 0; i < 8; i++)
            ground.H.AddBody(ground.Frame, Shapes.Box(0.5f, motion: BodyMotion.Static), Shapes.At(3 * i, -100, 0));
        ground.H.Step(30);
        ground.H.Backend.CapacityOf(ground.Frame).Should().Be(16, "the system grew");
        ground.ChangeGravity();
        ground.H.Step(5);
        IsAwake(ground.H, ground.Frame, box).Should().BeTrue("the new gravity woke the box");
        ground.H.Step(30);

        ContactsOf(ground.H, ground.Floor, box).Should().Be(1, "the box never left the floor");
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
        FrameHandle lander = ground.H.AddFrameWithBody(ground.Frame, Shapes.At(0, 2, 0), FrameBounds.Sphere(1), MotionDriver.Physics, Shapes.Box(2, mass: 1000));
        ground.H.Step(120);
        ContactsOf(ground.H, lander.Entity, ground.Floor).Should().Be(1);

        ground.H.Frames.SetDriver(lander, MotionDriver.Static);
        ground.H.Step(60);
        ground.H.Frames.SetDriver(lander, MotionDriver.Physics);
        ground.H.Step(60);

        ContactsOf(ground.H, lander.Entity, ground.Floor).Should().Be(1, "the lander never left the floor");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABoxThatTheGameLiftsOffTheFloorWhileItSleepsReportsTheFloorAgainWhenItLands(bool systemKeepsStepping)
    {
        using var ground = new Ground();
        if (systemKeepsStepping)
            ground.KeepSteppingTheSystem();
        Entity box = ground.SleepingBox();
        ground.H.Step(30);

        ground.H.Intent(box).Flags |= IntentFlags.Teleport;
        ground.H.Intent(box).TeleportPose = Shapes.At(0, 2.5, 0);
        ground.H.Step();
        ground.H.Step(120);

        ground.H[box].Pose.Position.Y.Should().BeApproximately(0.5, 0.01, "the box fell back onto the floor");
        ContactsOf(ground.H, ground.Floor, box).Should().Be(2, "the box left the floor and landed again");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AParkedLanderThatTheGameLiftsOffTheFloorReportsTheFloorAgainWhenItLands(bool systemKeepsStepping)
    {
        using var ground = new Ground();
        if (systemKeepsStepping)
            ground.KeepSteppingTheSystem();
        FrameHandle lander = ground.H.AddFrameWithBody(ground.Frame, Shapes.At(0, 2, 0), FrameBounds.Sphere(1), MotionDriver.Physics, Shapes.Box(2, mass: 1000));
        ground.H.Step(120);
        ContactsOf(ground.H, lander.Entity, ground.Floor).Should().Be(1);
        ground.H.Frames.SetDriver(lander, MotionDriver.Static);
        ground.H.Step(30);

        // A static frame moves where the game puts it.
        DPose parked = ground.H.Frames.LocalPose(lander);
        ground.H.Frames.SetLocalPose(lander, parked with { Position = parked.Position + new Double3(0, 3, 0) });
        ground.H.Step(30);
        ground.H.Frames.SetDriver(lander, MotionDriver.Physics);
        ground.H.Step(120);

        ground.H.Frames.LocalPose(lander).Position.Y.Should().BeApproximately(2, 0.01, "the lander fell back onto the floor");
        ContactsOf(ground.H, lander.Entity, ground.Floor).Should().Be(2, "the lander left the floor and landed again");
    }
}
