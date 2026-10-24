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
public sealed class PlainBodyOnRailsTests
{
    /// <summary>
    /// A circular orbit of 50 km around the asteroid, which moves at 632 m/s, too slow and too near for a numeric frame, so
    /// the body stays in the asteroid's own system. Its pull of 8 m/s² puts the instant velocity at the end of a step 0.067
    /// m/s past the chord of the step.
    /// </summary>
    private static readonly KeplerOrbit Orbit = KeplerOrbit.Circular(50_000, phase: 0.3, spinRate: 0.2);

    private static BodyDesc KinematicBall => Ball() with { Motion = BodyMotion.Kinematic };

    /// <summary>A plain kinematic ball on <see cref="Orbit"/> in <paramref name="space"/>, spawned where the orbit is at time 0.</summary>
    private static Entity OnOrbit(World world, FrameHandle space) =>
        world.Spawn(
            new()
            {
                Frame = space,
                Pose = new(Orbit.StateAt(0, Scenes.AsteroidMu).Position, Orbit.RotationAt(0, Scenes.AsteroidMu)),
                Body = KinematicBall,
                Components = [new KeplerMotion { Orbit = Orbit }],
            }
        );

    [Fact]
    public void APlainKinematicBodyOnKeplerRailsStandsOnItsOrbitToTheLastBit()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity ball = OnOrbit(world, space);
        Tick(world, 2);

        for (int i = 0; i < 30; i++)
        {
            world.Tick();
            world
                .PoseIn(ball, space)
                .Should()
                .Be(
                    new DPose(
                        Orbit.StateAt(world.Time, Scenes.AsteroidMu).Position,
                        Orbit.RotationAt(world.Time, Scenes.AsteroidMu)
                    ),
                    "the body stands on its orbit at sync point {0}, rotation included",
                    world.TickNumber
                );
        }

        world
            .Frames.Numeric(world.GetFrame(ball))
            .Should()
            .Be(NumericMode.None, "the body stays in the asteroid's own system");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The rails spawns of the test helpers put a plain kinematic body on its orbit with its first motion intent and no spawn
    /// velocity, which a kinematic body that owns no frame refuses (see <see cref="RailsSpawn"/>). So it stands on its
    /// orbit from the first sync point on.
    /// </summary>
    [Fact]
    public void ARailsSpawnPutsAPlainKinematicBodyOnItsKeplerOrbitFromItsFirstStep()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity ball = world.Spawn(
            new SpawnDesc { Frame = space, Body = KinematicBall }.OnKeplerOrbit(world, Orbit, firstMove: true)
        );

        for (int i = 0; i < 5; i++)
        {
            world.Tick();
            (world.PoseIn(ball, space).Position - Orbit.StateAt(world.Time, Scenes.AsteroidMu).Position)
                .Length.Should()
                .BeLessThan(1e-9, "the body stands on its orbit at sync point {0}", world.TickNumber);
        }

        world.Errors.Should().BeEmpty();
    }

    /// <summary>The spin of the rails spawns does the same for a plain kinematic body: it turns in place from its first step.</summary>
    [Fact]
    public void ARailsSpawnTurnsAPlainKinematicBodyInPlaceFromItsFirstStep()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Ground(world, 0);
        var spin = new FrameSpin(Tilt: DQuaternion.Identity, Rate: 0.5);
        Entity ball = world.Spawn(
            new SpawnDesc
            {
                Frame = space,
                Pose = At(3),
                Body = KinematicBall,
            }.Spinning(world, spin, firstMove: true)
        );

        for (int i = 0; i < 5; i++)
        {
            world.Tick();
            DPose pose = world.PoseIn(ball, space);
            pose.Position.Should()
                .Be(new Double3(3, 0, 0), "the body turns in place at sync point {0}", world.TickNumber);
            DQuaternion expected = spin.RotationAt(world.Time);
            Math.Abs((pose.Rotation.Conjugate() * expected).W)
                .Should()
                .BeGreaterThan(1 - 1e-12, "the body turns with the spin at sync point {0}", world.TickNumber);
        }

        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The velocity that gameplay reads for a plain body on rails is the instant velocity of its sample at the end of the
    /// step, narrowed to a float, not Jolt's chord of the step, which lies 0.067 m/s behind on this orbit.
    /// </summary>
    [Fact]
    public void APlainBodyOnRailsReadsTheSamplesInstantVelocity()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity ball = OnOrbit(world, space);
        Tick(world, 2);

        for (int i = 0; i < 30; i++)
        {
            world.Tick();
            Double3 velocity = Orbit.StateAt(world.Time, Scenes.AsteroidMu).Velocity;
            (world.VelocityIn(ball, space) - velocity)
                .Length.Should()
                .BeLessThan(1e-3, "the body reads its sample's instant velocity at sync point {0}", world.TickNumber);
        }

        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A plain kinematic ball that end poses move at 1,200 m/s through a frame without gravity gets a falling numeric frame
    /// as its physics home, which moves with it. It reads the velocity of its end poses in the frame that it is in, so the
    /// body state takes away the velocity that the home has after the step, and a read adds it back.
    /// </summary>
    [Fact]
    public void APlainBodyOnRailsInANumericFrameReadsTheVelocityOfItsMotionIntents()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Ground(world, 0);
        Entity ball = world.Spawn(
            KinematicDrift.Drifting(new() { Frame = space, Body = KinematicBall }, world, new(1_200, 0, 0))
        );
        Tick(world, 10);
        world
            .Frames.Numeric(world.PhysicsHomeOf(ball))
            .Should()
            .NotBe(NumericMode.None, "a body at 1,200 m/s runs in a numeric frame");

        for (int i = 0; i < 30; i++)
        {
            world.Tick();
            (world.VelocityIn(ball, space) - new Double3(1_200, 0, 0))
                .Length.Should()
                .BeLessThan(1e-3, "the ball reads the velocity of its end poses at sync point {0}", world.TickNumber);
        }

        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The ball of the test above flies through the bubble of a room at rest, 2 km away. It hands off from its numeric
    /// home into the room and out again, and at each hand-off it reads the velocity of its end poses: the hand-off takes
    /// away the instant velocity of the numeric home, as it does for every body, before it adds the motion of the home.
    /// </summary>
    [Fact]
    public void APlainBodyOnRailsThatHandsOffFromANumericFrameCarriesTheVelocityOfItsMotionIntents()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Ground(world, 0);
        FrameHandle room = world.GetOwnedFrame(
            world.Spawn(
                new()
                {
                    Frame = space,
                    Pose = At(2_000),
                    AsFrame = new(FrameBounds.Sphere(100)),
                }
            )
        );
        Entity ball = world.Spawn(
            KinematicDrift.Drifting(new() { Frame = space, Body = KinematicBall }, world, new(1_200, 0, 0))
        );

        var handOffs = new List<(FrameHandle To, bool FromNumeric, double Off)>();
        FrameHandle frame = world.GetFrame(ball);
        bool numeric = false;
        for (int i = 0; i < 240; i++)
        {
            world.Tick();
            if (world.GetFrame(ball) != frame)
            {
                frame = world.GetFrame(ball);
                handOffs.Add((frame, numeric, (world.VelocityIn(ball, space) - new Double3(1_200, 0, 0)).Length));
            }

            numeric = world.Frames.Numeric(world.PhysicsHomeOf(ball)) != NumericMode.None;
        }

        handOffs
            .Select(handOff => handOff.To)
            .Should()
            .Equal([room, space], "the ball passes through the room's bubble");
        handOffs
            .Should()
            .Contain(handOff => handOff.FromNumeric, "the ball hands off from a numeric home at least once");
        handOffs
            .Should()
            .AllSatisfy(handOff =>
                handOff
                    .Off.Should()
                    .BeLessThan(1e-3, "the hand-off into {0} carries the velocity of the end poses", handOff.To)
            );
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Moves its body on a straight line along +X in <paramref name="space"/> that speeds up at
    /// <paramref name="acceleration"/>
    /// m/s², from <paramref name="start"/> at 2 m/s at time 0, and turns it about Z, from 0.5 rad/s at time 0, faster by
    /// <paramref name="spinUp"/> rad/s². In every tick it writes the full sample of the step that the tick's intents act in,
    /// in the frame that the body is in, through the frames' relation that the tick reads. The frames in these tests do not
    /// move relative to each other, so the relation converts the sample exactly.
    /// </summary>
    private sealed class Speeding(FrameHandle space, Double3 start, double acceleration, double spinUp) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public Double3 At(double time) => start + new Double3(2 * time + 0.5 * acceleration * time * time, 0, 0);

        public Double3 VelocityAt(double time) => new(2 + acceleration * time, 0, 0);

        public DQuaternion RotationAt(double time) =>
            DQuaternion.FromAxisAngle(Double3.UnitZ, 0.5 * time + 0.5 * spinUp * time * time);

        public Double3 AngularVelocityAt(double time) => new(0, 0, 0.5 + spinUp * time);

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            double end = self.TimeAt(self.Tick + 2);
            double step = self.DeltaTime;
            DPose relation = self.Snapshot.Frames.Relative(space, self.Frame);
            DQuaternion turn = relation.Rotation;
            self.MoveOnRails(
                new(
                    Pose: DPose.Compose(relation, new(At(end), RotationAt(end))),
                    ChordVelocity: turn.Rotate((At(end) - At(end - step)) / step),
                    Velocity: turn.Rotate(VelocityAt(end)),
                    Acceleration: turn.Rotate(new Double3(acceleration, 0, 0)),
                    AngularVelocity: turn.Rotate(AngularVelocityAt(end)),
                    AngularAcceleration: turn.Rotate(new Double3(0, 0, spinUp))
                )
            );
        }
    }

    /// <summary>
    /// A plain kinematic cube of 1 m that <paramref name="rails"/> moves, spawned where the rails put it at time 0. With
    /// <paramref name="lopsided"/>, the cube sits 1 m along its X axis from the body's origin, so its centre of mass does too
    /// (see <see cref="CentreOfMass"/>).
    /// </summary>
    private static Entity CubeOn(World world, FrameHandle space, Speeding rails, bool lopsided = false) =>
        world.Spawn(
            new()
            {
                Frame = space,
                Pose = new(rails.At(0), rails.RotationAt(0)),
                Body = new(
                    Shape: lopsided
                        ? new CompoundShapeDesc([
                            new(new BoxShapeDesc(new(0.5f)), new(new(1, 0, 0), Quaternion.Identity)),
                        ])
                        : new BoxShapeDesc(new(0.5f)),
                    Motion: BodyMotion.Kinematic
                ),
                Components = [rails],
            }
        );

    /// <summary>The centre of mass of a cube of <see cref="CubeOn"/> in its body's axes.</summary>
    private static Double3 CentreOfMass(bool lopsided) => lopsided ? new(1, 0, 0) : Double3.Zero;

    /// <summary>
    /// The instant velocity of the centre of mass of a cube of <see cref="CubeOn"/> on <paramref name="rails"/> at
    /// <paramref name="time"/>: the velocity of its origin, plus the turn of the arm from the origin to the centre of mass.
    /// </summary>
    private static Double3 CentreVelocity(Speeding rails, double time, bool lopsided) =>
        rails.VelocityAt(time)
        + Double3.Cross(rails.AngularVelocityAt(time), rails.RotationAt(time).Rotate(CentreOfMass(lopsided)));

    /// <summary>
    /// The velocity that gameplay reads for a plain body is that of its centre of mass, as Jolt gives it for every body. So
    /// a plain body on rails whose centre of mass lies 1 m from its origin reads the instant velocity of its rails at the
    /// origin plus the turn of that arm, which is 0.5 m/s and more here.
    /// </summary>
    [Fact]
    public void APlainBodyOnRailsReadsTheInstantVelocityOfItsCentreOfMass()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        var rails = new Speeding(space: space, start: Double3.Zero, acceleration: 3, spinUp: 2);
        Entity cube = CubeOn(world: world, space: space, rails: rails, lopsided: true);
        Tick(world, 2);

        for (int i = 0; i < 30; i++)
        {
            world.Tick();
            (world.VelocityIn(cube, space) - CentreVelocity(rails, world.Time, lopsided: true))
                .Length.Should()
                .BeLessThan(
                    1e-4,
                    "the body reads the instant velocity of its centre of mass at sync point {0}",
                    world.TickNumber
                );
        }

        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The angular velocity that gameplay reads for a plain body on rails is the instant angular velocity of its sample at
    /// the end of the step, not Jolt's chord of the turn, which lies half a step of the spin-up of 2 rad/s², 0.017 rad/s,
    /// behind.
    /// </summary>
    [Fact]
    public void APlainBodyOnRailsReadsTheSamplesInstantAngularVelocity()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        var rails = new Speeding(space: space, start: Double3.Zero, acceleration: 0, spinUp: 2);
        Entity cube = CubeOn(world: world, space: space, rails: rails);
        Tick(world, 2);

        for (int i = 0; i < 30; i++)
        {
            world.Tick();
            (world.AngularVelocityIn(cube, space) - rails.AngularVelocityAt(world.Time))
                .Length.Should()
                .BeLessThan(
                    1e-4,
                    "the body reads its sample's instant angular velocity at sync point {0}",
                    world.TickNumber
                );
        }

        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A plain kinematic cube on rails that speeds up at 3 m/s² and spins up at 2 rad/s² passes through the bubble of a
    /// turned room at rest. At the sync point of each hand-off it reads the instant velocities of its rails in the frame
    /// that it left, as at every other sync point: the hand-off carries the instant velocities of the step's sample at the
    /// cube's centre of mass, not Jolt's chords, which lie 0.025 m/s and 0.017 rad/s behind. The body state of the step
    /// agrees with the hand-off: it is the cube's state in its new frame.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APlainBodyOnRailsThatHandsOffCarriesTheSamplesInstantVelocity(bool lopsided)
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        DQuaternion turned =
            DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2)
            * DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2);
        FrameHandle room = world.GetOwnedFrame(
            world.Spawn(
                new()
                {
                    Frame = space,
                    Pose = new(new(20, 0, 0), turned),
                    AsFrame = new(FrameBounds.Sphere(5)),
                }
            )
        );
        var rails = new Speeding(space: space, start: new(5, 0, 0), acceleration: 3, spinUp: 2);
        Entity cube = CubeOn(world: world, space: space, rails: rails, lopsided: lopsided);
        Tick(world, 2);

        var handOffs = new List<(FrameHandle To, double Off, double TurnOff)>();
        FrameHandle frame = world.GetFrame(cube);
        for (int i = 0; i < 240; i++)
        {
            world.Tick();
            if (world.GetFrame(cube) == frame)
                continue;
            frame = world.GetFrame(cube);
            handOffs.Add(
                (
                    frame,
                    (world.VelocityIn(cube, space) - CentreVelocity(rails, world.Time, lopsided)).Length,
                    (world.AngularVelocityIn(cube, space) - rails.AngularVelocityAt(world.Time)).Length
                )
            );
            HandOff handOff = faults.Outputs[^1].HandOffs.Single(h => h.Entity == cube);
            BodyState state = faults.Outputs[^1].Bodies.Single(b => b.Entity == cube);
            state.Frame.Should().Be(handOff.To);
            (state.Pose.Position - handOff.Pose.Position)
                .Length.Should()
                .BeLessThan(1e-9, "the body state is the cube's place in its new frame");
            Math.Abs(Dot(state.Pose.Rotation, handOff.Pose.Rotation))
                .Should()
                .BeGreaterThan(1 - 1e-6, "and its rotation there");
            (state.LinearVelocity - handOff.LinearVelocity)
                .Length()
                .Should()
                .BeLessThan(1e-5f, "and its velocity there");
        }

        handOffs
            .Select(handOff => handOff.To)
            .Should()
            .Equal([room, space], "the cube passes through the room's bubble");
        handOffs
            .Should()
            .AllSatisfy(handOff =>
            {
                handOff
                    .Off.Should()
                    .BeLessThan(
                        1e-4,
                        "the hand-off into {0} carries the instant velocity of the step's sample",
                        handOff.To
                    );
                handOff
                    .TurnOff.Should()
                    .BeLessThan(
                        1e-4,
                        "the hand-off into {0} carries the instant angular velocity of the step's sample",
                        handOff.To
                    );
            });
        world.Errors.Should().BeEmpty();
    }

    private static double Dot(DQuaternion a, DQuaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
}
