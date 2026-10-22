using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// Gait: a drawn body attached to a person turns as the person's view turns about the person's up, and rises and falls a
/// little with each step while the person walks on a floor, on the World and the real Jolt backend. The person is the
/// capsule of the demo scenes, whose center rests 0.9 m above the floor, with its eye 0.75 m above its center. In every
/// scene the frames' +Y is up. The drawn body's center rests at the person's center. Its lift is how far its center lies
/// above that, along the person's up, in meters.
/// </summary>
public sealed class GaitTests : IDisposable
{
    private static readonly Vector3 Eye = new(0, 0.75f, 0);

    private readonly World world = NewWorld(out _);

    public void Dispose() => world.Dispose();

    private Entity Person(FrameHandle frame, Double3 position, Quaternion view, Double3 velocity = default,
        params IComponent[] components) => world.Spawn(new()
    {
        Frame = frame,
        Pose = new(position, DQuaternion.Identity),
        Body = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70),
        LinearVelocity = velocity.ToVector3(),
        Render = new RenderData { ViewOffset = new(Eye, view) },
        Components = components
    });

    private Entity DrawnBody(Entity person) => world.Spawn(new()
    {
        AttachTo = person,
        Render = new RenderData(),
        Components = [new Gait(Vector3.Zero)]
    });

    /// <summary>A root frame with 9.81 m/s² of gravity and a floor whose top is at 0.</summary>
    private FrameHandle OnTheGround()
    {
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        return ground;
    }

    /// <summary>The drawn body's pose relative to the person, in the person's axes.</summary>
    private DPose Relative(Entity body, Entity person, FrameHandle frame)
    {
        DPose of = world.PoseIn(person, frame), drawn = world.PoseIn(body, frame);
        DQuaternion back = of.Rotation.Conjugate();
        return new(back.Rotate(drawn.Position - of.Position), back * drawn.Rotation);
    }

    private double Lift(Entity body, Entity person, FrameHandle frame) => Relative(body, person, frame).Position.Y;

    /// <summary>The lifts of each tick for <paramref name="ticks"/> ticks.</summary>
    private List<double> Lifts(Entity body, Entity person, FrameHandle frame, int ticks)
    {
        List<double> lifts = [];
        for (int t = 0; t < ticks; t++)
        {
            world.Tick();
            lifts.Add(Lift(body, person, frame));
        }

        return lifts;
    }

    /// <summary>How many times the lifts rise past half of <see cref="Gait.StepRise"/>: once for each step.</summary>
    private static int Steps(List<double> lifts) =>
        lifts.Zip(lifts.Skip(1), (a, b) => a < Gait.StepRise / 2 && b >= Gait.StepRise / 2).Count(rises => rises);

    private static double Degrees(Double3 a, Double3 b) =>
        Math.Acos(Math.Clamp(Double3.Dot(a.Normalized(), b.Normalized()), -1, 1)) * 180 / Math.PI;

    /// <summary>
    /// The person's view looks a quarter turn to the left, along -X, and 30 degrees down. The body faces along -X too,
    /// upright: it takes the view's turn about the person's up, and not its tilt.
    /// </summary>
    [Fact]
    public void TheBodyFacesTheWayOfThePersonsViewAndStaysUpright()
    {
        FrameHandle ground = OnTheGround();
        Quaternion view = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) *
                          Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 6);
        Entity person = Person(ground, new(0, 0.95, 0), view);
        Entity body = DrawnBody(person);

        Tick(world, 10);

        DQuaternion turn = Relative(body, person, ground).Rotation;
        Degrees(turn.Rotate(-Double3.UnitZ), -Double3.UnitX).Should().BeLessThan(0.01);
        Degrees(turn.Rotate(Double3.UnitY), Double3.UnitY).Should().BeLessThan(0.01);
    }

    /// <summary>
    /// The person walks 2 m/s along -Z on a floor for 3 s, 6 m. The body rises by up to <see cref="Gait.StepRise"/> and
    /// comes back down once for each <see cref="Gait.StepLength"/> of the walk: 8 steps in 6 m.
    /// </summary>
    [Fact]
    public void TheBodyRisesAndFallsOnceForEachStepOfTheWalk()
    {
        FrameHandle ground = OnTheGround();
        Entity person = Person(ground, new(0, 0.95, 0), Quaternion.Identity,
            components: new SteadyWalk(new(0, 0, -2), ticks: 600));
        Entity body = DrawnBody(person);
        Tick(world, 30);

        List<double> lifts = Lifts(body, person, ground, 180);

        Steps(lifts).Should().BeInRange(7, 9);
        lifts.Max().Should().BeInRange(0.9 * Gait.StepRise, Gait.StepRise + 1e-6);
        lifts.Min().Should().BeInRange(-1e-6, 0.002, "the feet come down between the steps");
    }

    /// <summary>
    /// The phase of the steps follows the distance walked, not the time: a walk of 3.6 m takes as many steps at 1 m/s as
    /// at 2 m/s, about 5.
    /// </summary>
    [Fact]
    public void TheStepsFollowTheDistanceWalkedNotTheTime()
    {
        FrameHandle ground = OnTheGround();
        Entity slow = Person(ground, new(0, 0.95, 0), Quaternion.Identity,
            components: new SteadyWalk(new(0, 0, -1), ticks: 216));
        Entity fast = Person(ground, new(5, 0.95, 0), Quaternion.Identity,
            components: new SteadyWalk(new(0, 0, -2), ticks: 108));
        Entity slowBody = DrawnBody(slow), fastBody = DrawnBody(fast);

        List<double> slowLifts = [], fastLifts = [];
        for (int t = 0; t < 300; t++)
        {
            world.Tick();
            slowLifts.Add(Lift(slowBody, slow, ground));
            fastLifts.Add(Lift(fastBody, fast, ground));
        }

        world.PoseIn(slow, ground).Position.Z.Should().BeApproximately(world.PoseIn(fast, ground).Position.Z, 0.15,
            "both walked the same distance");
        Steps(fastLifts).Should().BeInRange(4, 6);
        Steps(slowLifts).Should().Be(Steps(fastLifts));
    }

    [Fact]
    public void TheBodyDoesNotBobWhileThePersonStandsStill()
    {
        FrameHandle ground = OnTheGround();
        Entity person = Person(ground, new(0, 0.95, 0), Quaternion.Identity);
        Entity body = DrawnBody(person);

        Lifts(body, person, ground, 120).Should().AllSatisfy(lift => Math.Abs(lift).Should().BeLessThan(1e-6));
    }

    /// <summary>The person drifts at 2 m/s along -Z with no floor under it, in a frame with no gravity, so it takes no steps.</summary>
    [Fact]
    public void TheBodyDoesNotBobWhileThePersonFloats()
    {
        FrameHandle space = Ground(world, 0);
        Entity person = Person(space, Double3.Zero, Quaternion.Identity, velocity: new(0, 0, -2));
        Entity body = DrawnBody(person);

        Lifts(body, person, space, 120).Should().AllSatisfy(lift => Math.Abs(lift).Should().BeLessThan(1e-6));
    }

    /// <summary>After the walk stops, the bob dies away within half a second, wherever in its step the person stopped.</summary>
    [Fact]
    public void TheBobDiesAwayWhenThePersonStops()
    {
        FrameHandle ground = OnTheGround();
        Entity person = Person(ground, new(0, 0.95, 0), Quaternion.Identity,
            components: new SteadyWalk(new(0, 0, -2), ticks: 100));
        Entity body = DrawnBody(person);
        List<double> walking = Lifts(body, person, ground, 100);

        List<double> stopped = Lifts(body, person, ground, 60);

        walking.Max().Should().BeGreaterThan(0.9 * Gait.StepRise, "the body bobbed while the person walked");
        stopped.Skip(30).Should().AllSatisfy(lift => Math.Abs(lift).Should().BeLessThan(0.001));
    }
}
