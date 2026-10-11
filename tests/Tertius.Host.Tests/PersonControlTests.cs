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

    public void Dispose() => world.Dispose();

    private void Ticks(int count)
    {
        for (int i = 0; i < count; i++)
            world.Tick();
    }

    private void Post(InputKind kind, int code = 0, Vector2 delta = default) =>
        world.Post(Event.Create(Topics.Input, Entity.Null, new InputEvent { Kind = kind, Code = code, Delta = delta }));

    private void Press(Key key) => Post(InputKind.KeyDown, (int)key);

    private void MoveMouse(float right, float down) => Post(InputKind.MouseMove, delta: new Vector2(right, down));

    /// <summary>
    /// The velocity along the floor, in the room's axes, after the posted input has acted for a while. The part along
    /// <paramref name="up"/>, +Y unless given, is left out: it holds the step of gravity that pins the person to the floor.
    /// </summary>
    private Vector3 WalkingVelocity(Entity walker = default, Vector3? up = null)
    {
        // A post joins the routing at the end of the next tick, the person handles it in the tick after that, and physics
        // moves the person in the step of the third tick.
        Ticks(10);
        Vector3 velocity = world.Snapshot.VelocityOf(walker.IsNull ? person : walker), along = up ?? Vector3.UnitY;
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
        Post(InputKind.KeyUp, (int)key);

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
        world.Post(Event.Create(sideways, Entity.Null, new InputEvent { Kind = InputKind.MouseMove, Delta = new Vector2(-MathF.PI / 2 / PersonControl.TurnPerPixel, 0) }));
        world.Post(Event.Create(sideways, Entity.Null, new InputEvent { Kind = InputKind.KeyDown, Code = (int)Key.W }));

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
        double standing = world.Snapshot.PoseOf(person).Position.Y;
        Press(Key.Space);
        Ticks(3 + 24);

        // A jump at 4 m/s rises 0.82 m in 0.41 s, about 24 ticks. Physics adds each tick's gravity before the move, so
        // after 25 moves the person is 0.781 m up.
        (world.Snapshot.PoseOf(person).Position.Y - standing).Should().BeApproximately(0.781, 0.005);
    }

    [Fact]
    public void HoldingSpaceJumpsOnceAndItsReleaseDoesNotJumpAgain()
    {
        double standing = world.Snapshot.PoseOf(person).Position.Y;
        Press(Key.Space);
        // The jump lasts 0.82 s, 49 ticks.
        Ticks(3 + 60);
        double landed = world.Snapshot.PoseOf(person).Position.Y;
        Post(InputKind.KeyUp, (int)Key.Space);
        Ticks(3 + 10);

        landed.Should().BeApproximately(standing, 0.005);
        world.Snapshot.PoseOf(person).Position.Y.Should().BeApproximately(standing, 0.005);
    }
}
