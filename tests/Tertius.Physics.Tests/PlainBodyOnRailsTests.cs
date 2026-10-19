using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A plain kinematic body, the body of an entity that owns no frame, moves on rails as a kinematic hull does
/// (docs/plans/rails-components.md, "The flow per kind of entity"): physics aims it at the end of each motion intent and
/// puts it exactly there after the update, and the body state of the step carries the step record's pose and instant
/// velocity, which step 2 writes into the transform row. A hand-off carries the record's instant velocity. The world runs on
/// the real backend.
/// </summary>
public class PlainBodyOnRailsTests
{
    /// <summary>
    /// A circular orbit of 50 km around the asteroid, which moves at 632 m/s, too slow and too near for a numeric frame, so
    /// the body stays in the asteroid's own system. Its pull of 8 m/s² puts the instant velocity at the end of a step 0.067
    /// m/s past the chord of the step.
    /// </summary>
    private static readonly KeplerOrbit Orbit = KeplerOrbit.Circular(50_000, phase: 0.3, spinRate: 0.2);

    private static BodyDesc KinematicBall => Ball() with { Motion = BodyMotion.Kinematic };

    /// <summary>A plain kinematic ball on <see cref="Orbit"/> in <paramref name="space"/>, spawned where the orbit is at time 0.</summary>
    private static Entity OnOrbit(World world, FrameHandle space) => world.Spawn(new SpawnDesc
    {
        Frame = space,
        Pose = new DPose(Orbit.StateAt(0, Scenes.AsteroidMu).Position, Orbit.RotationAt(0, Scenes.AsteroidMu)),
        Body = KinematicBall,
        Components = [new KeplerMotion { Orbit = Orbit }],
    });

    [Fact]
    public void APlainKinematicBodyOnKeplerRailsStandsOnItsOrbitToTheLastBit()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity ball = OnOrbit(world, space);
        Tick(world, 2);

        for (int i = 0; i < 30; i++)
        {
            world.Tick();
            world.PoseIn(ball, space).Should().Be(new DPose(Orbit.StateAt(world.Time, Scenes.AsteroidMu).Position, Orbit.RotationAt(world.Time, Scenes.AsteroidMu)),
                "the body stands on its orbit at sync point {0}, rotation included", world.TickNumber);
        }
        world.Frames.Numeric(world.GetFrame(ball)).Should().Be(NumericMode.None, "the body stays in the asteroid's own system");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The velocity that gameplay reads for a plain body on rails is the instant velocity of its sample at the end of the
    /// step, narrowed to a float, not Jolt's chord of the step, which lies 0.067 m/s behind on this orbit.
    /// </summary>
    [Fact]
    public void APlainBodyOnRailsReadsTheSamplesInstantVelocity()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity ball = OnOrbit(world, space);
        Tick(world, 2);

        for (int i = 0; i < 30; i++)
        {
            world.Tick();
            Double3 velocity = Orbit.StateAt(world.Time, Scenes.AsteroidMu).Velocity;
            (world.VelocityIn(ball, space) - velocity).Length.Should().BeLessThan(1e-3,
                "the body reads its sample's instant velocity at sync point {0}", world.TickNumber);
        }
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Moves its body on a straight line along +X in <paramref name="space"/> that speeds up at <paramref name="acceleration"/>
    /// m/s², from <paramref name="start"/> at 2 m/s at time 0. In every tick it writes the full sample of the step that the
    /// tick's intents act in, in the frame that the body is in, through the frames' relation that the tick reads. The frames
    /// in these tests do not move relative to each other, so the relation converts the sample exactly.
    /// </summary>
    private sealed class Speeding(FrameHandle space, Double3 start, double acceleration) : IComponent
    {
        public Double3 At(double time) => start + new Double3(2 * time + 0.5 * acceleration * time * time, 0, 0);

        public Double3 VelocityAt(double time) => new(2 + acceleration * time, 0, 0);

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            double end = self.TimeAt(self.Tick + 2);
            double step = self.DeltaTime;
            DPose relation = self.Snapshot.Frames.Relative(space, self.Frame);
            DQuaternion turn = relation.Rotation;
            self.MoveOnRails(new MotionSample(
                DPose.Compose(relation, new DPose(At(end), DQuaternion.Identity)),
                turn.Rotate((At(end) - At(end - step)) / step),
                turn.Rotate(VelocityAt(end)),
                turn.Rotate(new Double3(acceleration, 0, 0)),
                Double3.Zero,
                Double3.Zero));
        }
    }

    /// <summary>
    /// A plain kinematic cube on rails that speeds up at 3 m/s² passes through the bubble of a turned room at rest. At the
    /// sync point of each hand-off it reads the instant velocity of its rails in the frame that it left, as at every other
    /// sync point: the hand-off carries the instant velocity of the step's sample, not Jolt's chord, which lies 0.025 m/s
    /// behind.
    /// </summary>
    [Fact]
    public void APlainBodyOnRailsThatHandsOffCarriesTheSamplesInstantVelocity()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        var turned = DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2) * DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2);
        FrameHandle room = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(new Double3(20, 0, 0), turned),
            AsFrame = new FrameSpec(FrameBounds.Sphere(5)),
        }));
        var rails = new Speeding(space, new Double3(5, 0, 0), acceleration: 3);
        Entity cube = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(rails.At(0), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Kinematic),
            Components = [rails],
        });
        Tick(world, 2);

        var handOffs = new List<(FrameHandle To, double Off)>();
        FrameHandle frame = world.GetFrame(cube);
        for (int i = 0; i < 240; i++)
        {
            world.Tick();
            if (world.GetFrame(cube) == frame)
                continue;
            frame = world.GetFrame(cube);
            handOffs.Add((frame, (world.VelocityIn(cube, space) - rails.VelocityAt(world.Time)).Length));
        }

        handOffs.Select(handOff => handOff.To).Should().Equal([room, space], "the cube passes through the room's bubble");
        handOffs.Should().AllSatisfy(handOff => handOff.Off.Should().BeLessThan(1e-4,
            "the hand-off into {0} carries the instant velocity of the step's sample", handOff.To));
        world.Errors.Should().BeEmpty();
    }
}
