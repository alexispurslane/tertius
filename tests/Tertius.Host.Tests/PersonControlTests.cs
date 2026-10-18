using System.Numerics;
using Silk.NET.Input;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// PersonControl: the keys and the mouse on the input topic walk a character and turn its view, while the character's
/// view is on. A person stands on a floor in a room with 9.81 m/s² of gravity along -Y, its view is the world's camera, and
/// the tests post the input that the window would post in free play.
/// </summary>
public sealed class PersonControlTests : IDisposable
{
    private static readonly Vector3 Eye = new(0, 0.75f, 0);

    private readonly World world = NewJoltWorld();
    private readonly Entity person;

    public PersonControlTests()
    {
        person = StandOnAFloor(Vector3.UnitY);
        world.Camera = person;
    }

    /// <summary>
    /// Makes the room's gravity pull along -<paramref name="up"/>, puts a floor under the origin across that direction, and
    /// stands a person on it with the identity rotation. Physics then turns the person's +Y to <paramref name="up"/>.
    /// </summary>
    private Entity StandOnAFloor(Vector3 up)
    {
        FrameHandle room = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e4f), MotionDriver.Static, GravityField.Uniform(-9.81f * up)));
        world.Spawn(new SpawnDesc
        {
            Frame = room,
            Pose = new DPose(new Double3(-0.5 * up.X, -0.5 * up.Y, -0.5 * up.Z), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(50) - 49.5f * Vector3.Abs(up)), BodyMotion.Static),
        });
        Entity standing = world.Spawn(new SpawnDesc
        {
            Frame = room,
            Pose = new DPose(new Double3(0.95 * up.X, 0.95 * up.Y, 0.95 * up.Z), DQuaternion.Identity),
            Body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70),
            Components = [new PersonControl(Eye)],
            Subscriptions = [Topics.Input],
        });
        // The person lands on the floor.
        Ticks(30);
        return standing;
    }

    /// <summary>
    /// Floats a person at the origin of a second room, which has no gravity, and makes its view the world's camera. The
    /// person keeps the up that it spawned with, +Y.
    /// </summary>
    private Entity FloatInSpace() => FloatInSpace(DQuaternion.Identity);

    /// <summary>
    /// Floats a person turned by <paramref name="rotation"/> at the origin of a second room, which has no gravity, and
    /// makes its view the world's camera. The person keeps the up that it spawned with, its own +Y.
    /// </summary>
    private Entity FloatInSpace(DQuaternion rotation)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e4f), MotionDriver.Static));
        Entity floating = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(Double3.Zero, rotation),
            Body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: Mass),
            Components = [new PersonControl(Eye)],
            Subscriptions = [Topics.Input],
        });
        world.Camera = floating;
        Ticks(3);
        return floating;
    }

    private const float Mass = 70;

    /// <summary>The acceleration of the thrusters, in m/s².</summary>
    private const float Thrust = PersonControl.ThrustForce / Mass;

    /// <summary>The velocity that the posted input adds to <paramref name="floating"/> in half a second, once it acts.</summary>
    private Vector3 GainInHalfASecond(Entity floating)
    {
        Ticks(10);
        Vector3 early = world.Snapshot.VelocityInOwnFrame(floating);
        Ticks(30);
        return world.Snapshot.VelocityInOwnFrame(floating) - early;
    }

    public void Dispose() => world.Dispose();

    private void Ticks(int count)
    {
        for (int i = 0; i < count; i++)
            world.Tick();
    }

    private void Post(InputEvent e) => world.Post(Event.Create(Topics.Input, Entity.Null, e));

    private void Press(Key key) => Post(InputEvent.KeyDown((int)key));

    private void MoveMouse(float right, float down) => Post(InputEvent.MouseMove(new Vector2(right, down)));

    /// <summary>
    /// The velocity along the floor, in the room's axes, after the posted input has acted for a while. The part along
    /// <paramref name="up"/>, +Y unless given, is left out: it holds the step of gravity that pins the person to the floor.
    /// </summary>
    private Vector3 WalkingVelocity(Entity walker = default, Vector3? up = null)
    {
        // A post joins the routing at the end of the next tick, the person handles it in the tick after that, and physics
        // moves the person in the step of the third tick.
        Ticks(10);
        Vector3 velocity = world.Snapshot.VelocityInOwnFrame(walker.IsNull ? person : walker), along = up ?? Vector3.UnitY;
        return velocity - Vector3.Dot(velocity, along) * along;
    }

    private Quaternion View => world.Snapshot.RenderOf(person).ViewOffset.Rotation;

    private static void ExpectNear(Vector3 actual, Vector3 expected, float tolerance) =>
        Vector3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    [Theory]
    [InlineData(Key.W, 0, -1)]
    [InlineData(Key.S, 0, 1)]
    [InlineData(Key.A, -1, 0)]
    [InlineData(Key.D, 1, 0)]
    public void EachOfWASDWalksThePersonAtWalkingSpeedAlongItsOwnDirection(Key key, float x, float z)
    {
        Press(key);

        // The person faces -Z, as the entity's own axes do: W walks forward, S back, A left and D right.
        ExpectNear(WalkingVelocity(), new Vector3(x, 0, z) * PersonControl.WalkSpeed, 0.05f);
    }

    [Fact]
    public void TwoKeysTogetherWalkDiagonallyNoFasterThanOne()
    {
        Press(Key.W);
        Press(Key.D);

        ExpectNear(WalkingVelocity(), Vector3.Normalize(new Vector3(1, 0, -1)) * PersonControl.WalkSpeed, 0.05f);
    }

    [Theory]
    [InlineData(Key.W)]
    [InlineData(Key.S)]
    [InlineData(Key.A)]
    [InlineData(Key.D)]
    public void ReleasingTheKeyStopsThePerson(Key key)
    {
        Press(key);
        Ticks(20);
        Post(InputEvent.KeyUp((int)key));

        ExpectNear(WalkingVelocity(), Vector3.Zero, 0.05f);
    }

    [Fact]
    public void MovingTheMouseRightTurnsTheViewAndTheWalkToTheRight()
    {
        // A quarter turn, at the turn per pixel of the mouse.
        MoveMouse(MathF.PI / 2 / PersonControl.TurnPerPixel, 0);
        Ticks(3);
        Press(Key.W);

        ExpectNear(Vector3.Transform(-Vector3.UnitZ, View), Vector3.UnitX, 1e-4f);
        ExpectNear(WalkingVelocity(), Vector3.UnitX * PersonControl.WalkSpeed, 0.05f);
    }

    [Fact]
    public void AfterATurnMovingTheMouseDownTiltsTheViewDownwardAlongTheNewHeading()
    {
        MoveMouse(MathF.PI / 2 / PersonControl.TurnPerPixel, 0);
        MoveMouse(0, 0.8f / PersonControl.TurnPerPixel);
        Ticks(3);

        // The tilt is about the turned view's own side, not about the room's +X, which would roll the view.
        ExpectNear(Vector3.Transform(-Vector3.UnitZ, View), new Vector3(MathF.Cos(0.8f), -MathF.Sin(0.8f), 0), 1e-4f);
    }

    [Fact]
    public void WhereGravityPullsSidewaysTheTurnedViewWalksThePersonAlongItsFloor()
    {
        // In a second room, gravity pulls along -X, so physics turns the person's +Y to +X: a quarter turn about -Z.
        Entity sideways = StandOnAFloor(Vector3.UnitX);
        world.Camera = sideways;
        world.Post(Event.Create(sideways, Entity.Null, InputEvent.MouseMove(new Vector2(-MathF.PI / 2 / PersonControl.TurnPerPixel, 0))));
        world.Post(Event.Create(sideways, Entity.Null, InputEvent.KeyDown((int)Key.W)));

        // The view's quarter turn left takes forward from -Z to -X in the person's axes, and the body's turn takes -X to
        // +Y in the room's: along the floor, which lies across +X.
        ExpectNear(WalkingVelocity(sideways, Vector3.UnitX), Vector3.UnitY * PersonControl.WalkSpeed, 0.05f);
    }

    [Fact]
    public void LookingDownDoesNotSlowTheWalkOrTipItIntoTheFloor()
    {
        MoveMouse(0, 0.8f / PersonControl.TurnPerPixel);
        Ticks(3);
        Press(Key.W);

        Vector3.Transform(-Vector3.UnitZ, View).Y.Should().BeApproximately(-MathF.Sin(0.8f), 1e-4f);
        ExpectNear(WalkingVelocity(), -Vector3.UnitZ * PersonControl.WalkSpeed, 0.05f);
    }

    [Fact]
    public void TheViewNeverTipsPastStraightDownOrStraightUp()
    {
        MoveMouse(0, 100_000);
        Ticks(3);
        float down = Vector3.Transform(-Vector3.UnitZ, View).Y;
        MoveMouse(0, -200_000);
        Ticks(3);
        float up = Vector3.Transform(-Vector3.UnitZ, View).Y;

        // Straight up or down, the turn of the view about the vertical would have no meaning.
        down.Should().BeInRange(-0.99999f, -0.9999f);
        up.Should().BeInRange(0.9999f, 0.99999f);
    }

    [Fact]
    public void WhileAnotherViewIsOnThePersonIgnoresTheKeysAndTheMouse()
    {
        world.Camera = world.Spawn(new SpawnDesc { Frame = world.Snapshot.FrameOf(person), Render = new RenderData() });
        MoveMouse(300, 200);
        Press(Key.W);

        ExpectNear(WalkingVelocity(), Vector3.Zero, 0.05f);
        View.Should().Be(Quaternion.Identity);
    }

    [Fact]
    public void WhenItsViewGoesOffThePersonStopsAndLetsGoOfTheKeysThatItHeld()
    {
        Press(Key.W);
        Ticks(10);
        Entity other = world.Spawn(new SpawnDesc { Frame = world.Snapshot.FrameOf(person), Render = new RenderData() });
        world.Camera = other;
        Vector3 away = WalkingVelocity();
        world.Camera = person;

        // W is still down at the keyboard, but the window posts no second down, so the person waits for a new press.
        ExpectNear(away, Vector3.Zero, 0.05f);
        ExpectNear(WalkingVelocity(), Vector3.Zero, 0.05f);
        // Every key was let go: a new press of D walks right alone.
        Press(Key.D);
        ExpectNear(WalkingVelocity(), Vector3.UnitX * PersonControl.WalkSpeed, 0.05f);
    }

    [Fact]
    public void TheViewLooksOutFromTheEye()
    {
        Ticks(3);

        world.Snapshot.RenderOf(person).ViewOffset.Position.Should().Be(Eye);
    }

    [Fact]
    public void SpaceMakesAStandingPersonJump()
    {
        double standing = world.Snapshot.PoseInOwnFrame(person).Position.Y;
        // Space goes up at once, so that its thrust does not add to the jump.
        Press(Key.Space);
        Post(InputEvent.KeyUp((int)Key.Space));
        Ticks(3 + 24);

        // A jump at 4 m/s rises 0.82 m in 0.41 s, about 24 ticks. The jump speed is an instant velocity and physics moves
        // the person by the chord of each step, so after 25 moves it is 4 m/s · t - g t² / 2 = 0.815 m up, t = 25/60 s.
        (world.Snapshot.PoseInOwnFrame(person).Position.Y - standing).Should().BeApproximately(0.815, 0.005);
    }

    [Fact]
    public void ASpacePressThatComesAfterAnotherPressAndReleaseInTheSameTickStillMakesOneJump()
    {
        double standing = world.Snapshot.PoseInOwnFrame(person).Position.Y;
        // The person handles all four events in one tick, in the order of the posts. The press that comes second must not
        // cancel the first one. Space goes up at the end, so that its thrust does not add to the jump.
        Press(Key.Space);
        Post(InputEvent.KeyUp((int)Key.Space));
        Press(Key.Space);
        Post(InputEvent.KeyUp((int)Key.Space));
        Ticks(3 + 24);

        // The plain jump of an instant 4 m/s, after 25 chords (see SpaceMakesAStandingPersonJump).
        (world.Snapshot.PoseInOwnFrame(person).Position.Y - standing).Should().BeApproximately(0.815, 0.005);
    }

    [Fact]
    public void HoldingSpaceAfterAJumpThrustsThePersonHigher()
    {
        double standing = world.Snapshot.PoseInOwnFrame(person).Position.Y;
        Press(Key.Space);
        Ticks(3 + 24);

        // The plain jump of an instant 4 m/s is 0.815 m up after its 25 chords. Physics jumps the person in the step of the third tick and
        // reports it in the air from the step of the fourth. The person reads that report in the fifth tick and thrusts, so
        // the thrust acts from the step of the sixth tick on: in the last 22 of the 25 moves. Physics adds each tick's
        // thrust to the velocity before the move, so the thrust lifts the person by 1.5 m/s² · Δt² · (1 + 2 + ... + 22),
        // 0.105 m. One tick more or less of thrust changes that by 0.01 m.
        double lift = Thrust * world.DeltaTime * world.DeltaTime * (22 * 23 / 2);
        (world.Snapshot.PoseInOwnFrame(person).Position.Y - standing).Should().BeApproximately(0.815 + lift, 0.005);
    }

    [Fact]
    public void APersonThatLetsGoOfWInTheAirStopsInTheFirstStepInWhichItStands()
    {
        Press(Key.W);
        Ticks(20);
        Press(Key.Space);
        Post(InputEvent.KeyUp((int)Key.Space));
        Ticks(10);
        Post(InputEvent.KeyUp((int)Key.W));
        world.Snapshot.IsStanding(person).Should().BeFalse("the person jumped");
        world.Snapshot.VelocityInOwnFrame(person).Z.Should().BeLessThan(-PersonControl.WalkSpeed, "the person keeps its walk in the air, and W thrusts it on");
        int ticks = 0;
        while (!world.Snapshot.IsStanding(person) && ticks++ < 120)
            Ticks(1);

        // Physics keeps the last walk that the person asked for until the person stands again. In the air the person
        // asks for the walk of the keys that it holds now, and it let go of W, so no walk is left over when it lands.
        world.Snapshot.IsStanding(person).Should().BeTrue();
        Vector3 velocity = world.Snapshot.VelocityInOwnFrame(person);
        new Vector2(velocity.X, velocity.Z).Length().Should().BeLessThan(0.05f);
    }

    [Fact]
    public void APersonThatHoldsWThroughAJumpWalksOnAtWalkingSpeedInEveryStepAfterItLands()
    {
        Press(Key.W);
        Ticks(20);
        Press(Key.Space);
        Post(InputEvent.KeyUp((int)Key.Space));
        Ticks(10);
        world.Snapshot.IsStanding(person).Should().BeFalse("the person jumped");
        int ticks = 0;
        while (!world.Snapshot.IsStanding(person) && ticks++ < 120)
            Ticks(1);
        world.Snapshot.IsStanding(person).Should().BeTrue();

        // In the air the person asks physics for the walk of the keys that it holds now, so physics walks it on in the
        // first step in which it stands. The person reads that it stands a tick late, so the thrust of its last two
        // ticks in the air adds 0.025 m/s to each of its first two steps on the floor.
        for (int step = 0; step < 10; step++)
        {
            Vector3 velocity = world.Snapshot.VelocityInOwnFrame(person);
            Vector3.Distance(velocity with { Y = 0 }, -Vector3.UnitZ * PersonControl.WalkSpeed)
                .Should().BeLessThan(0.05f, $"{velocity} is a walk at 3 m/s in step {step} after the person stands");
            Ticks(1);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ASpacePressPostedOneOrTwoTicksBeforeThePersonStandsAgainJumpsAgain(int ticksBefore)
    {
        double standing = world.Snapshot.PoseInOwnFrame(person).Position.Y;
        Press(Key.Space);
        Post(InputEvent.KeyUp((int)Key.Space));
        // Physics jumps the person in the step of the third tick. The person touches the floor in the step of the 51st
        // tick, after 49 chords of its instant 4 m/s, and physics stands it again from the step of the 52nd. The test
        // posts the second press ticksBefore ticks before that tick. Space goes up at once, so that its thrust does not
        // change the jump.
        Ticks(52 - 1 - ticksBefore);
        Press(Key.Space);
        Post(InputEvent.KeyUp((int)Key.Space));
        Ticks(ticksBefore);
        world.Snapshot.IsStanding(person).Should().BeFalse("the person is still in the air");
        Ticks(1);
        world.Snapshot.IsStanding(person).Should().BeTrue("the person stands again in the step of the 52nd tick");
        Ticks(24);

        // The person handles the second press while it reads that it is in the air, and asks physics for a jump all
        // the same. Physics jumps the person in the first or the second step in which it stands, so 24 ticks later the
        // person is near the top of a second jump, 0.814 m up: 0.815 m after 24 chords, 0.813 m after 23.
        (world.Snapshot.PoseInOwnFrame(person).Position.Y - standing).Should().BeApproximately(0.814, 0.005);
    }

    [Fact]
    public void PressingETellsEveryEntityWithinReachThatThePersonUsesIt()
    {
        FrameHandle room = world.Snapshot.FrameOf(person);
        Double3 at = world.Snapshot.PoseInOwnFrame(person).Position;
        Probe near = new(EventMask.Of<Use>()), far = new(EventMask.Of<Use>());
        world.Spawn(new SpawnDesc { Frame = room, Pose = new DPose(at + new Double3(PersonControl.Reach - 0.1, 0, 0), DQuaternion.Identity), Components = [near] });
        world.Spawn(new SpawnDesc { Frame = room, Pose = new DPose(at + new Double3(0, 0, PersonControl.Reach + 0.1), DQuaternion.Identity), Components = [far] });
        Ticks(1);
        Press(Key.E);
        Ticks(10);

        near.Seen.Should().ContainSingle("one press is one use");
        near.Seen[0].Event.As<Use>().User.Should().Be(person);
        far.Seen.Should().BeEmpty();
    }

    [Fact]
    public void AnEPressThatComesAfterAnotherPressAndReleaseInTheSameTickStillMakesOneUse()
    {
        FrameHandle room = world.Snapshot.FrameOf(person);
        Probe near = new(EventMask.Of<Use>());
        world.Spawn(new SpawnDesc { Frame = room, Pose = world.Snapshot.PoseInOwnFrame(person), Components = [near] });
        Ticks(1);
        // The person handles all three events in one tick, in the order of the posts. The press that comes second must
        // not cancel the first one, and the two presses make one use between them.
        Press(Key.E);
        Post(InputEvent.KeyUp((int)Key.E));
        Press(Key.E);
        Ticks(10);

        near.Seen.Should().ContainSingle("two presses in one tick are one use");
        near.Seen[0].Event.As<Use>().User.Should().Be(person);
    }

    [Fact]
    public void WhileAnotherViewIsOnPressingETellsNoOne()
    {
        Probe near = new(EventMask.Of<Use>());
        world.Spawn(new SpawnDesc { Frame = world.Snapshot.FrameOf(person), Pose = world.Snapshot.PoseInOwnFrame(person), Components = [near] });
        world.Camera = world.Spawn(new SpawnDesc { Frame = world.Snapshot.FrameOf(person), Render = new RenderData() });
        Ticks(1);
        Press(Key.E);
        Ticks(10);

        near.Seen.Should().BeEmpty();
    }

    [Fact]
    public void WeightlessHoldingWThrustsThePersonAlongItsView()
    {
        Entity floating = FloatInSpace();
        // Half a radian up from -Z.
        MoveMouse(0, -0.5f / PersonControl.TurnPerPixel);
        Press(Key.W);

        ExpectNear(GainInHalfASecond(floating), new Vector3(0, MathF.Sin(0.5f), -MathF.Cos(0.5f)) * Thrust * 0.5f, 1e-3f);
    }

    [Theory]
    [InlineData(Key.S, 0, 1)]
    [InlineData(Key.A, -1, 0)]
    [InlineData(Key.D, 1, 0)]
    public void WeightlessSADThrustBackLeftAndRightOfTheView(Key key, float x, float z)
    {
        Entity floating = FloatInSpace();
        Press(key);

        ExpectNear(GainInHalfASecond(floating), new Vector3(x, 0, z) * Thrust * 0.5f, 1e-3f);
    }

    [Theory]
    [InlineData(Key.Space, 1)]
    [InlineData(Key.C, -1)]
    public void WeightlessSpaceAndCThrustAlongThePersonsUpWhereverItLooks(Key key, float along)
    {
        Entity floating = FloatInSpace();
        MoveMouse(0.6f / PersonControl.TurnPerPixel, 0.8f / PersonControl.TurnPerPixel);
        Press(key);

        ExpectNear(GainInHalfASecond(floating), Vector3.UnitY * along * Thrust * 0.5f, 1e-3f);
    }

    [Fact]
    public void WeightlessTwoKeysTogetherThrustNoHarderThanOne()
    {
        Entity floating = FloatInSpace();
        Press(Key.W);
        Press(Key.Space);

        ExpectNear(GainInHalfASecond(floating), Vector3.Normalize(new Vector3(0, 1, -1)) * Thrust * 0.5f, 1e-3f);
    }

    [Fact]
    public void WeightlessKeysThatNearlyCancelThrustNearlyNotAtAll()
    {
        Entity floating = FloatInSpace();
        // The view stops just short of straight up, so W thrusts almost along the person's up, and C against it.
        MoveMouse(0, -100_000);
        Press(Key.W);
        Press(Key.C);

        // W and C together leave a hundredth of one key's thrust: 0.0075 m/s in half a second, where one key adds 0.75 m/s.
        GainInHalfASecond(floating).Length().Should().BeLessThan(0.02f);
    }

    [Fact]
    public void WeightlessSpaceThrustsATurnedPersonAlongItsOwnUp()
    {
        // A quarter turn about +Z takes the person's +Y to the room's -X.
        Entity floating = FloatInSpace(DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2));
        MoveMouse(0.6f / PersonControl.TurnPerPixel, 0);
        Press(Key.Space);

        ExpectNear(GainInHalfASecond(floating), -Vector3.UnitX * Thrust * 0.5f, 1e-3f);
    }

    [Fact]
    public void WeightlessWThrustsATurnedPersonAlongItsTurnedView()
    {
        Entity floating = FloatInSpace(DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2));
        MoveMouse(0.6f / PersonControl.TurnPerPixel, 0);
        Press(Key.W);

        // The view's turn of 0.6 rad to the right takes forward from -Z to (sin 0.6, 0, -cos 0.6) in the person's axes.
        // The body's quarter turn about +Z then takes the person's +X to the room's +Y, and keeps -Z where it is.
        ExpectNear(GainInHalfASecond(floating), new Vector3(0, MathF.Sin(0.6f), -MathF.Cos(0.6f)) * Thrust * 0.5f, 1e-3f);
    }

    [Theory]
    [InlineData(Key.Space, 1)]
    [InlineData(Key.C, -1)]
    public void WeightlessWhenItsViewGoesOffThePersonLetsGoOfItsThrusters(Key key, float along)
    {
        Entity floating = FloatInSpace();
        Press(key);
        Ticks(10);
        world.Camera = world.Spawn(new SpawnDesc { Frame = world.Snapshot.FrameOf(floating), Render = new RenderData() });
        // The person lets go in its next tick. A force that it added in the tick before can still act in that tick's step.
        Ticks(3);
        Vector3 drift = world.Snapshot.VelocityInOwnFrame(floating);
        Ticks(60);

        (drift.Y * along).Should().BeGreaterThan(0.1f, "the person thrust while its view was on");
        ExpectNear(world.Snapshot.VelocityInOwnFrame(floating), drift, 1e-5f);
    }

    [Fact]
    public void WeightlessThePersonKeepsDriftingAfterTheKeysAreLetGo()
    {
        Entity floating = FloatInSpace();
        Press(Key.D);
        Ticks(30);
        Post(InputEvent.KeyUp((int)Key.D));
        Ticks(5);
        Vector3 drift = world.Snapshot.VelocityInOwnFrame(floating);
        Ticks(60);

        drift.X.Should().BeGreaterThan(0.5f);
        ExpectNear(world.Snapshot.VelocityInOwnFrame(floating), drift, 1e-5f);
    }

    [Theory]
    [InlineData(Key.W, 0, 0, -1)]
    [InlineData(Key.Space, 0, 1, 0)]
    [InlineData(Key.C, 0, -1, 0)]
    public void InTheAirUnderGravityTheKeysThrustThePersonOnTopOfGravity(Key key, float x, float y, float z)
    {
        Press(Key.Space);
        // The person handles the press in the second tick. Space goes up right after, so that its thrust adds only to its
        // own case. Physics drops the jump of a second press of Space, because the person is in the air by then.
        Ticks(2);
        Post(InputEvent.KeyUp((int)Key.Space));
        Press(key);
        // Physics jumps the person in the step of the third tick and reports it in the air from the step of the fourth.
        // The person reads that report in the fifth tick, so the thrust acts from the step of the sixth tick on. With C the
        // jump lasts about 42 ticks, so the person is in the air for all 35 samples.
        Ticks(3);
        Vector3 last = world.Snapshot.VelocityInOwnFrame(person);
        Vector3 acceleration = new Vector3(0, -9.81f, 0) + new Vector3(x, y, z) * Thrust;
        for (int sample = 0; sample < 35; sample++)
        {
            Ticks(1);
            Vector3 velocity = world.Snapshot.VelocityInOwnFrame(person);

            // The person falls freely, so it is weightless, and its thrusters push it: W along the view, Space and C along
            // its up.
            Vector3.Distance((velocity - last) / world.DeltaTime, acceleration).Should().BeLessThan(0.05f, $"at sample {sample}");
            last = velocity;
        }
    }

    [Fact]
    public void HoldingSpaceJumpsOnceAndItsReleaseDoesNotJumpAgain()
    {
        double standing = world.Snapshot.PoseInOwnFrame(person).Position.Y, top = standing;
        Press(Key.Space);
        for (int i = 0; i < 3 + 60; i++)
        {
            Ticks(1);
            top = Math.Max(top, world.Snapshot.PoseInOwnFrame(person).Position.Y);
        }
        double landed = world.Snapshot.PoseInOwnFrame(person).Position.Y;
        Post(InputEvent.KeyUp((int)Key.Space));
        Ticks(3 + 10);

        // Physics jumps the person in the step of the third tick, and held Space thrusts it up from the step of the
        // sixth on. The jump speed is an instant velocity, and physics moves the person by the chord of each step. So the
        // jump peaks 0.934 m up, where a plain jump peaks 0.815 m up, and the person touches the floor in the step of the
        // 59th tick, where a plain jump touches it in the step of the 51st.
        (top - standing).Should().BeApproximately(0.934, 0.005);
        landed.Should().BeApproximately(standing, 0.005);
        world.Snapshot.PoseInOwnFrame(person).Position.Y.Should().BeApproximately(standing, 0.005);
    }
}
