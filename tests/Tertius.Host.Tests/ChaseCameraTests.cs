using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// ChaseCamera: a camera entity attached to a person keeps a framing behind and above the person's eye, as the person
/// walks, turns, climbs a stair, stands with its back to a wall and walks into a ship's frame, on the World and the real
/// Jolt backend. The person is the capsule of the demo scenes, 1.8 m tall and 0.3 m in radius, whose center rests 0.9 m
/// above the floor, with its eye 0.75 m above its center. In every scene the frames' +Y is up. The framing of every test is
/// 3.5 m behind the eye, 0.8 m above it, and a look at the point 2 m ahead of it.
/// </summary>
public sealed class ChaseCameraTests : IDisposable
{
    private const double Behind = 3.5, Above = 0.8, Ahead = 2;

    private static readonly Vector3 Eye = new(0, 0.75f, 0);

    /// <summary>The half extents of the person's box: the capsule's height and width.</summary>
    private static readonly Vector3 Half = new(0.3f, 0.9f, 0.3f);

    private readonly World world = NewWorld(out _);

    public void Dispose() => world.Dispose();

    private Entity Person(FrameHandle frame, Double3 position, params IComponent[] components) => world.Spawn(new SpawnDesc
    {
        Frame = frame,
        Pose = new DPose(position, DQuaternion.Identity),
        Body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70),
        Render = new RenderData { ViewOffset = new Pose(Eye, Quaternion.Identity) },
        Components = components,
    });

    /// <summary>A chase camera on <paramref name="person"/>, which starts at the framing behind the eye along <paramref name="behind"/>.</summary>
    private Entity Camera(Entity person, Vector3 behind) => world.Spawn(new SpawnDesc
    {
        AttachTo = person,
        AttachOffset = new Pose(Eye + new Vector3(0, (float)Above, 0) + behind * (float)Behind, Quaternion.Identity),
        Render = new RenderData(),
        Components = [new ChaseCamera(Behind, Above, Ahead, Half, world.VerticalFov)],
    });

    /// <summary>Where the person's eye is in <paramref name="frame"/>.</summary>
    private Double3 EyeOf(Entity person, FrameHandle frame) => world.PoseIn(person, frame).TransformPoint((Double3)Eye);

    /// <summary>The angle between the camera's view, along its -Z, and the way from the camera to the person's eye, in degrees.</summary>
    private double AngleToEye(Entity camera, Entity person, FrameHandle frame)
    {
        DPose view = world.PoseIn(camera, frame);
        return Degrees(view.Rotation.Rotate(-Double3.UnitZ), EyeOf(person, frame) - view.Position);
    }

    private static double Degrees(Double3 a, Double3 b) => Math.Acos(Math.Clamp(Double3.Dot(a.Normalized(), b.Normalized()), -1, 1)) * 180 / Math.PI;

    /// <summary>A root frame with 9.81 m/s² of gravity and a floor whose top is at 0.</summary>
    private FrameHandle OnTheGround()
    {
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        return ground;
    }

    /// <summary>
    /// A person that stands still at the origin, with its camera behind it along +Z: the camera rests 3.5 m behind the eye
    /// and 0.8 m above it, at (0, 2.45, 3.5), and looks along -Z. A look at (0, 1.65, -2), 2 m ahead of the eye, would put
    /// the near bottom edge of the person's box 98 percent of the half picture below its middle, so the camera tilts down
    /// until that edge lies at <see cref="ChaseCamera.FrameMargin"/>, 85 percent.
    /// </summary>
    [Fact]
    public void TheCameraRestsBehindAndAboveTheEyeOfAPersonThatStands()
    {
        FrameHandle ground = OnTheGround();
        Entity person = Person(ground, new Double3(0, 0.95, 0));
        Entity camera = Camera(person, Vector3.UnitZ);

        Tick(world, 120);

        DPose view = world.PoseIn(camera, ground);
        Double3.Distance(view.Position, new Double3(0, 2.45, 3.5)).Should().BeLessThan(0.01);
        view.Rotation.Rotate(-Double3.UnitZ).X.Should().BeApproximately(0, 1e-3);
        Double3 nearFoot = view.Rotation.Conjugate().Rotate(world.PoseIn(person, ground).TransformPoint(new Double3(0.3, -0.9, 0.3)) - view.Position);
        (nearFoot.Y / -nearFoot.Z / Math.Tan(world.VerticalFov / 2)).Should().BeApproximately(-ChaseCamera.FrameMargin, 0.01);
        Degrees(view.Rotation.Rotate(Double3.UnitY), Double3.UnitY).Should().BeLessThan(15, "the view's up leans only by its look down");
    }

    /// <summary>
    /// The person walks an L, 6 m along +X and 6 m along -Z, and then 6 m along -X, while its camera starts behind it along
    /// +Z. In every tick the person's eye stays within 15 degrees of the middle of the view, and between 1 m and 5 m from
    /// the camera. A hard attach does that too, so the camera also swings behind the person after each turn: 1 m before the
    /// end of each leg, the camera lies behind the eye along the leg, within 10 degrees.
    /// </summary>
    [Fact]
    public void TheCameraKeepsTheWalkingPersonInItsFramingAndSwingsBehindItAfterEachTurn()
    {
        FrameHandle ground = OnTheGround();
        Waypoint[] path = [new(ground, new Double3(6, 0.9, 0)), new(ground, new Double3(6, 0.9, -6)), new(ground, new Double3(0, 0.9, -6))];
        Double3[] legs = [Double3.UnitX, -Double3.UnitZ, -Double3.UnitX];
        Entity person = Person(ground, new Double3(0, 0.95, 0), new Walker(path));
        Entity camera = Camera(person, Vector3.UnitZ);
        var behind = new List<double>();

        for (int tick = 0; tick < 480; tick++)
        {
            world.Tick();
            AngleToEye(camera, person, ground).Should().BeLessThan(15, $"at tick {world.TickNumber}");
            Double3 back = world.PoseIn(camera, ground).Position - EyeOf(person, ground);
            back.Length.Should().BeInRange(1, 5, $"at tick {world.TickNumber}");
            // The leg that the person walks now ends at the waypoint with the number of the legs measured so far.
            Double3 toEnd = path[Math.Min(behind.Count, legs.Length - 1)].Position - world.PoseIn(person, ground).Position;
            if (behind.Count < legs.Length && (toEnd with { Y = 0 }).Length < 1)
                behind.Add(Degrees(back with { Y = 0 }, -legs[behind.Count]));
        }

        behind.Should().HaveCount(3).And.OnlyContain(angle => angle < 10);
    }

    /// <summary>
    /// The person walks up a stair 0.3 m high. The camera rises after it over some ticks, where a hard attach would rise
    /// with the person at once: three ticks after the person stands on the stair, the camera has risen less than half of
    /// the stair, and a second later it is at its framing above the eye again, within 2 cm. The camera reads the person one
    /// step late, so in the tick of the step itself it moves with the person once; the test does not look at that tick.
    /// </summary>
    [Fact]
    public void TheCameraRisesAfterThePersonOverSomeTicksWhenThePersonClimbsAStair()
    {
        FrameHandle ground = OnTheGround();
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(8, 0.15), Body = new BodyDesc(new BoxShapeDesc(new Vector3(5, 0.15f, 5)), BodyMotion.Static) });
        Entity person = Person(ground, new Double3(0, 0.95, 0), new Walker([new Waypoint(ground, new Double3(7, 1.2, 0))]));
        Entity camera = Camera(person, -Vector3.UnitX);
        Tick(world, 30);
        double before = world.PoseIn(camera, ground).Position.Y;

        TickUntil(world, () => world.PoseIn(person, ground).Position.Y > 1.15, 300).Should().BeLessThan(300, "the person climbs the stair");
        Tick(world, 3);
        double rise = world.PoseIn(camera, ground).Position.Y - before;
        Tick(world, 60);

        rise.Should().BeLessThan(0.15);
        world.PoseIn(camera, ground).Position.Y.Should().BeApproximately(EyeOf(person, ground).Y + Above, 0.02);
    }

    /// <summary>
    /// The person stands with its back 1 m from a wall, and the camera's framing lies 3.5 m behind it, beyond the wall: from
    /// the third tick on, the camera stays at least 0.15 m in front of the wall, more than 0.5 m behind the eye, and with
    /// the eye within 20 degrees of the middle of its view. So close, the person's box is too tall for the picture, and the
    /// camera keeps its top in the picture, which puts the eye about 17 degrees above the middle. The camera's arm starts
    /// in the person's capsule, which it passes through.
    /// </summary>
    [Fact]
    public void TheCameraStopsShortOfAWallBehindThePerson()
    {
        FrameHandle ground = OnTheGround();
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 2, 1.25), Body = new BodyDesc(new BoxShapeDesc(new Vector3(5, 2, 0.25f)), BodyMotion.Static) });
        Entity person = Person(ground, new Double3(0, 0.95, 0));
        Entity camera = Camera(person, Vector3.UnitZ);
        Tick(world, 2);

        for (int tick = 0; tick < 60; tick++)
        {
            world.Tick();
            Double3 at = world.PoseIn(camera, ground).Position;
            at.Z.Should().BeLessThan(0.85, $"at tick {world.TickNumber}");
            Double3.Distance(at, EyeOf(person, ground)).Should().BeGreaterThan(0.5, $"at tick {world.TickNumber}");
            AngleToEye(camera, person, ground).Should().BeLessThan(20, $"at tick {world.TickNumber}");
        }
    }

    /// <summary>
    /// The person stands with its back 1 m from a wall, which then goes: the camera moves back out to its framing 3.5 m
    /// behind the eye over some ticks, by less than 0.1 m in any tick, where it would jump 2.7 m in one tick if the arm grew
    /// back at once, and it reaches the framing within 3 s.
    /// </summary>
    [Fact]
    public void TheCameraMovesBackOutSmoothlyWhenTheWallBehindThePersonGoes()
    {
        FrameHandle ground = OnTheGround();
        Entity wall = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 2, 1.25), Body = new BodyDesc(new BoxShapeDesc(new Vector3(5, 2, 0.25f)), BodyMotion.Static) });
        Entity person = Person(ground, new Double3(0, 0.95, 0));
        Entity camera = Camera(person, Vector3.UnitZ);
        Tick(world, 60);
        world.Destroy(wall);
        Double3 last = world.PoseIn(camera, ground).Position;

        for (int tick = 0; tick < 180; tick++)
        {
            world.Tick();
            Double3 at = world.PoseIn(camera, ground).Position;
            Double3.Distance(at, last).Should().BeLessThan(0.1, $"at tick {world.TickNumber}");
            last = at;
        }

        Double3.Distance(last, EyeOf(person, ground) + new Double3(0, Above, Behind)).Should().BeLessThan(0.05);
    }

    /// <summary>
    /// The person walks from 16 m off the side of a parked shuttle to the place 3 m outside its hatch, and enters the
    /// shuttle's frame on the way. The camera, behind the person along +X, goes into the shuttle's frame with it, and its
    /// place on the ground moves smoothly through the hand-off: by less than 0.1 m in any tick, where the person walks
    /// 0.05 m a tick, and with the eye within 15 degrees of the middle of the view.
    /// </summary>
    [Fact]
    public void TheCameraFollowsThePersonIntoAShipsFrameSmoothly()
    {
        FrameHandle ground = OnTheGround();
        Entity shuttle = Shuttle.Park(world, ground, new DPose(new Double3(0, Shuttle.Hull.Y, 0), DQuaternion.Identity));
        FrameHandle cabin = world.GetOwnedFrame(shuttle);
        Entity person = Person(ground, new Double3(16, 0.95, Shuttle.Hatch.Center.Z), new Walker([new Waypoint(cabin, Shuttle.OutsideHatch)]));
        Entity camera = Camera(person, Vector3.UnitX);
        world.Tick();
        Double3 last = world.PoseIn(camera, ground).Position;

        for (int tick = 0; tick < 300; tick++)
        {
            world.Tick();
            Double3 at = world.PoseIn(camera, ground).Position;
            Double3.Distance(at, last).Should().BeLessThan(0.1, $"at tick {world.TickNumber}");
            AngleToEye(camera, person, ground).Should().BeLessThan(15, $"at tick {world.TickNumber}");
            last = at;
        }

        world.GetFrame(person).Should().Be(cabin, "the person walked into the shuttle's frame");
        world.GetFrame(camera).Should().Be(cabin);
    }
}
