using System.Numerics;
using Silk.NET.Input;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// PersonControl: the keys and the mouse on the input topic walk a character and turn its view. A person stands on a floor
/// in a room with 9.81 m/s² of gravity along -Y, and the tests post the input that the window would post in free play.
/// </summary>
public sealed class PersonControlTests : IDisposable
{
    private static readonly Vector3 Eye = new(0, 0.75f, 0);

    private readonly World world = NewJoltWorld();
    private readonly Entity person;

    public PersonControlTests()
    {
        FrameHandle room = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e4f), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        world.Spawn(new SpawnDesc
        {
            Frame = room,
            Pose = new DPose(new Double3(0, -0.5, 0), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), BodyMotion.Static),
        });
        person = world.Spawn(new SpawnDesc
        {
            Frame = room,
            Pose = new DPose(new Double3(0, 0.95, 0), DQuaternion.Identity),
            Body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70),
            Components = [new PersonControl(Eye)],
            Subscriptions = [Topics.Input],
        });
        // The person lands on the floor.
        Ticks(30);
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

    /// <summary>The velocity along the floor, in the room's axes, after the posted input has acted for a while.</summary>
    private Vector3 WalkingVelocity()
    {
        // A post joins the routing at the end of the next tick, the person handles it in the tick after that, and physics
        // moves the person in the step of the third tick.
        Ticks(10);
        Vector3 velocity = world.Snapshot.VelocityOf(person);
        return velocity with { Y = 0 };
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

    [Fact]
    public void ReleasingTheKeyStopsThePerson()
    {
        Press(Key.W);
        Ticks(20);
        Post(InputKind.KeyUp, (int)Key.W);

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

        // A jump at 4 m/s rises 0.82 m in 0.41 s, about 24 ticks.
        (world.Snapshot.PoseOf(person).Position.Y - standing).Should().BeInRange(0.7, 0.85);
    }
}
