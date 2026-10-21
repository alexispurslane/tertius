using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// WalkHeading: a person's view turns about the person's up toward the way that it walks, at a limited rate, while it
/// stands on a floor, and holds its heading otherwise, on the World and the real Jolt backend. The person is the capsule of
/// the demo scenes, whose center rests 0.9 m above the floor, with its eye 0.75 m above its center. In every scene the
/// frames' +Y is up. The heading is the view's -Z, the way that the person faces. It starts along -Z.
/// </summary>
public sealed class WalkHeadingTests : IDisposable
{
    private static readonly Vector3 Eye = new(0, 0.75f, 0);

    private readonly World world = NewWorld(out _);

    public void Dispose() => world.Dispose();

    private Entity Person(FrameHandle frame, Double3 position, Double3 velocity = default, params IComponent[] components) => world.Spawn(new SpawnDesc
    {
        Frame = frame,
        Pose = new DPose(position, DQuaternion.Identity),
        Body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70),
        LinearVelocity = velocity.ToVector3(),
        Render = new RenderData { ViewOffset = new Pose(Eye, Quaternion.Identity) },
        Components = [.. components, new WalkHeading()],
    });

    /// <summary>A root frame with 9.81 m/s² of gravity and a floor whose top is at 0.</summary>
    private FrameHandle OnTheGround()
    {
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        return ground;
    }

    /// <summary>The way that <paramref name="person"/> faces, the view's -Z, in <paramref name="frame"/>.</summary>
    private Double3 Facing(Entity person, FrameHandle frame) =>
        world.PoseIn(person, frame).Rotation.Rotate((Double3)Vector3.Transform(-Vector3.UnitZ, world.GetRender(person).ViewOffset.Rotation));

    /// <summary>The heading's angle about +Y from -Z, in radians, counterclockwise seen from above.</summary>
    private double Yaw(Entity person, FrameHandle frame)
    {
        Double3 facing = Facing(person, frame);
        return Math.Atan2(-facing.X, -facing.Z);
    }

    private static double Degrees(Double3 a, Double3 b) => Math.Acos(Math.Clamp(Double3.Dot(a.Normalized(), b.Normalized()), -1, 1)) * 180 / Math.PI;

    [Fact]
    public void ThePersonTurnsToFaceTheWayItWalks()
    {
        FrameHandle ground = OnTheGround();
        Entity person = Person(ground, new Double3(0, 0.95, 0), components: new SteadyWalk(new Vector3(2, 0, 0), ticks: 600));

        Tick(world, 120);

        Degrees(Facing(person, ground), Double3.UnitX).Should().BeLessThan(1);
    }

    /// <summary>
    /// The walk along +X starts a quarter turn from the heading along -Z. The heading turns the short way round, clockwise
    /// seen from above, by at most <see cref="WalkHeading.TurnRate"/> times a tick in each tick. So the quarter turn takes
    /// at least 0.39 s, 24 ticks.
    /// </summary>
    [Fact]
    public void ThePersonTurnsTheShortWayRoundAtALimitedRate()
    {
        FrameHandle ground = OnTheGround();
        Entity person = Person(ground, new Double3(0, 0.95, 0), components: new SteadyWalk(new Vector3(2, 0, 0), ticks: 600));

        List<double> yaws = [];
        for (int t = 0; t < 120; t++)
        {
            world.Tick();
            yaws.Add(Yaw(person, ground));
        }

        double step = WalkHeading.TurnRate / 60;
        yaws.Zip(yaws.Skip(1), (a, b) => b - a).Should().AllSatisfy(change => change.Should().BeInRange(-step - 1e-6, 1e-6, "the heading turns clockwise, a tick of the rate at most"));
        yaws.Take(23).Should().AllSatisfy(yaw => yaw.Should().BeGreaterThan(-Math.PI / 2 + 0.01, "a quarter turn takes 24 ticks at least"));
        yaws[^1].Should().BeApproximately(-Math.PI / 2, 0.02);
    }

    [Fact]
    public void ThePersonHoldsItsHeadingWhenItStopsWalking()
    {
        FrameHandle ground = OnTheGround();
        Entity person = Person(ground, new Double3(0, 0.95, 0), components: new SteadyWalk(new Vector3(2, 0, 2), ticks: 60));
        Tick(world, 63);
        Double3 stopped = Facing(person, ground);

        Tick(world, 120);

        Degrees(stopped, new Double3(1, 0, 1)).Should().BeLessThan(5, "the person faced its walk when it stopped");
        Degrees(Facing(person, ground), stopped).Should().BeLessThan(0.01, "the person holds its heading");
    }

    /// <summary>
    /// A walk slower than <see cref="WalkHeading.TurnSpeed"/> does not turn the person, so a belted person that the cabin
    /// shakes by a few centimeters keeps its heading.
    /// </summary>
    [Fact]
    public void ThePersonHoldsItsHeadingWhileItCreepsSlowerThanTheTurnSpeed()
    {
        FrameHandle ground = OnTheGround();
        Entity person = Person(ground, new Double3(0, 0.95, 0), components: new SteadyWalk(new Vector3(0.2f, 0, 0), ticks: 600));

        Tick(world, 120);

        world.PoseIn(person, ground).Position.X.Should().BeGreaterThan(0.3, "the person crept along +X");
        Degrees(Facing(person, ground), -Double3.UnitZ).Should().BeLessThan(0.01);
    }

    /// <summary>
    /// With no floor under it, the person drifts at 1 m/s along +X in a frame with no gravity. Nothing turns a floating
    /// person, so its heading holds.
    /// </summary>
    [Fact]
    public void ThePersonHoldsItsHeadingWhileItFloats()
    {
        FrameHandle space = Ground(world, 0);
        Entity person = Person(space, Double3.Zero, velocity: Double3.UnitX);

        Tick(world, 120);

        world.PoseIn(person, space).Position.X.Should().BeGreaterThan(1.5, "the person drifted along +X");
        Degrees(Facing(person, space), -Double3.UnitZ).Should().BeLessThan(0.01);
    }

    /// <summary>The heading keeps the view's eye: only the view's turn changes.</summary>
    [Fact]
    public void TheTurnKeepsTheEyeInItsPlace()
    {
        FrameHandle ground = OnTheGround();
        Entity person = Person(ground, new Double3(0, 0.95, 0), components: new SteadyWalk(new Vector3(2, 0, 0), ticks: 600));

        Tick(world, 60);

        world.GetRender(person).ViewOffset.Position.Should().Be(Eye);
    }
}

/// <summary>A test component that asks for one walk, <paramref name="velocity"/> in m/s in its entity's axes, for <paramref name="ticks"/> ticks, and then for no walk.</summary>
internal sealed class SteadyWalk(Vector3 velocity, int ticks) : IComponent
{
    private int left = ticks;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (left < 0)
            return;
        self.Body.MoveCharacter(left > 0 ? velocity : Vector3.Zero);
        left--;
    }
}
