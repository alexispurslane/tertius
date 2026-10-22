using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Motion intents of entities without body or frame (docs/plans/rails-components.md, "Rails motion for every entity"). Such
/// an entity takes an end pose and a full sample. The step after the tick that wrote it applies it, and the sync point after
/// that step puts the entity's step record on its transform row at step 2: the record's pose, and its instant velocities
/// narrowed to floats. The entity holds still in a step without a motion intent, and a write of its transform in a tick wins
/// over the motion intent of the step beside that tick, and leaves the entity at rest. Every test reads the front snapshot
/// between ticks, so after the sync point of tick N it sees the record of step N.
/// </summary>
public class FramelessMotionIntentTests
{
    private const double EarthMu = 3.986004418e14;

    private static readonly DPose Start = new(new(1, 2, 3), DQuaternion.Identity);

    /// <summary>The end pose that the tests write: 3 m along +X from <see cref="Start"/>, turned 0.1 rad about +Y.</summary>
    private static readonly DPose End = new(new(4, 2, 3), DQuaternion.FromAxisAngle(Double3.UnitY, 0.1));

    /// <summary>A full sample whose instant velocities differ from its chord, so that a test can tell which one the row takes.</summary>
    private static readonly MotionSample Sample = new(End, ChordVelocity: new(2, 0, 0), Velocity: new(5, 0, 0),
        Acceleration: new(-1, 0, 0), AngularVelocity: new(0, 0, 0.5), AngularAcceleration: Double3.Zero);

    // ---- The record lands on the transform row -------------------------------------------------------------------

    [Theory]
    [InlineData("end pose", true)]
    [InlineData("full sample", true)]
    [InlineData("end pose", false)]
    [InlineData("full sample", false)]
    public void AnEntityWithoutBodyOrFrameFollowsItsMotionIntent(string kind, bool onJolt)
    {
        using TestWorld test = onJolt ? TestWorld.OnJolt(out _) : new();
        Entity entity = test.World.Spawn(new()
        {
            Frame = test.Root,
            Pose = Start,
            Components =
            [
                Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
                {
                    if (self.Tick == 0)
                        Write(self, kind);
                })
            ]
        });

        test.Run(1);

        test.World.PoseIn(entity, test.Root).Should()
            .Be(Start, "step 0 had no motion intent, and step 1 applies the one of tick 0");

        test.Run(1);

        double step = test.World.DeltaTime;
        (Double3 velocity, Double3 spin) = kind == "end pose"
            ? ((End.Position - Start.Position) / step, new Double3(0, 0.1 / step, 0))
            : (Sample.Velocity, Sample.AngularVelocity);
        test.World.PoseIn(entity, test.Root).Should()
            .Be(End.WithUnitRotation(), "step 1 put the entity exactly on its motion intent");
        (test.World.VelocityIn(entity, test.Root) - velocity).Length.Should().BeLessThan(1e-4,
            "the row takes the record's instant velocity, not its chord");
        (test.World.AngularVelocityIn(entity, test.Root) - spin).Length.Should().BeLessThan(1e-5);
    }

    [Fact]
    public void AnEntityWithoutBodyOrFrameHoldsStillInTheStepAfterItsLastMotionIntent()
    {
        using TestWorld test = TestWorld.OnJolt(out _);
        Entity entity = test.World.Spawn(new()
        {
            Frame = test.Root,
            Pose = Start,
            Components =
            [
                Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
                {
                    if (self.Tick == 0)
                        self.MoveOnRails(Sample);
                })
            ]
        });
        test.Run(2);
        test.World.VelocityIn(entity, test.Root).Should().NotBe(Double3.Zero, "step 1 moved it");

        test.Run(1);

        test.World.PoseIn(entity, test.Root).Should().Be(End, "nothing moved it in step 2");
        test.World.VelocityIn(entity, test.Root).Should()
            .Be(Double3.Zero, "it held still in step 2, which had no motion intent");
        test.World.AngularVelocityIn(entity, test.Root).Should().Be(Vector3.Zero);
    }

    /// <summary>
    /// Tick 1 writes the transform and the end pose of step 2. The write wins over the end pose of step 1 at the sync point of
    /// tick 1, and step 2 starts where the write put the entity.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATransformWriteWinsForOneTickOverAMotionIntent(bool ownsFrame)
    {
        using TestWorld test = TestWorld.OnJolt(out _);
        DPose written = TestWorld.At(10, 2, 3), next = TestWorld.At(12, 2, 3);
        Entity entity = test.World.Spawn(new()
        {
            Frame = test.Root,
            Pose = Start,
            AsFrame = ownsFrame ? new FrameSpec(FrameBounds.Sphere(10)) : null,
            Components =
            [
                Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
                {
                    if (self.Tick == 0)
                        self.MoveKinematic(End);
                    if (self.Tick == 1)
                    {
                        self.MoveKinematic(next);
                        self.SetTransform(written);
                    }
                })
            ]
        });

        test.Run(2);

        test.World.PoseIn(entity, test.Root).Should()
            .Be(written, "the write of tick 1 wins over the end pose of step 1");
        test.World.VelocityIn(entity, test.Root).Should().Be(Double3.Zero, "the write leaves the entity at rest");

        test.Run(1);

        test.World.PoseIn(entity, test.Root).Should().Be(next, "the end pose of step 2 puts it back on its motion");
        (test.World.VelocityIn(entity, test.Root) - (next.Position - written.Position) / test.World.DeltaTime).Length
            .Should().BeLessThan(1e-4, "step 2 started where the write put the entity");
    }

    /// <summary>
    /// The entity follows a full sample in every step, so its row holds the sample's velocities when tick 2 writes its
    /// transform.
    /// </summary>
    [Fact]
    public void ATransformWriteLeavesAnEntityWithoutABodyAtRest()
    {
        using TestWorld test = TestWorld.OnJolt(out _);
        Entity entity = test.World.Spawn(new()
        {
            Frame = test.Root,
            Pose = Start,
            Components =
            [
                Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
                {
                    self.MoveOnRails(Sample);
                    if (self.Tick == 2)
                        self.SetTransform(Start);
                })
            ]
        });
        test.Run(2);
        test.World.VelocityIn(entity, test.Root).Should().NotBe(Double3.Zero, "step 1 moved it");

        test.Run(1);

        test.World.PoseIn(entity, test.Root).Should().Be(Start);
        test.World.VelocityIn(entity, test.Root).Should().Be(Double3.Zero);
        test.World.AngularVelocityIn(entity, test.Root).Should().Be(Vector3.Zero);
    }

    // ---- The rails components on an entity without a frame -----------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AKeplerMotionMovesAnEntityWithoutBodyOrFrameOnItsOrbit(bool firstMove)
    {
        using TestWorld test = TestWorld.OnJolt(out _);
        var log = new List<string>();
        test.World.Log += log.Add;
        test.GiveRootPointGravity(EarthMu);
        KeplerOrbit orbit = KeplerOrbit.Circular(7e6, phase: 0.3);
        Entity entity =
            test.World.Spawn(new SpawnDesc { Frame = test.Root }.OnKeplerOrbit(test.World, orbit, firstMove));
        // Without a first motion intent the entity holds still in its first step, and its component's first sample acts in
        // the second.
        test.Run(firstMove ? 1 : 2);

        for (int tick = 0; tick < 20; tick++)
        {
            double time = test.World.Time;
            (Double3 place, Double3 velocity) = orbit.StateAt(time, EarthMu);
            DPose pose = test.World.PoseIn(entity, test.Root);
            pose.Position.Should().Be(place, $"the entity stands on its orbit at {time} s");
            pose.Rotation.Should().Be(orbit.RotationAt(time, EarthMu));
            // The row narrows the velocity of 7.5 km/s to a float, whose step there is 0.5 mm/s.
            (test.World.VelocityIn(entity, test.Root) - velocity).Length.Should().BeLessThan(1e-3,
                "the row takes the orbit's instant velocity in every step");
            test.Run(1);
        }

        log.Should().BeEmpty("the entity takes a full sample, so the component has nothing to report");
    }

    [Fact]
    public void ASpinMotionTurnsAnEntityWithoutAFrameInPlace()
    {
        using TestWorld test = TestWorld.OnJolt(out _);
        var spin = new FrameSpin(DQuaternion.FromAxisAngle(Double3.UnitX, 0.2), Rate: 0.5);
        var place = new Double3(5, 0, 0);
        Entity entity = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = new(place, DQuaternion.Identity) }
            .Spinning(test.World, spin, firstMove: true));

        for (int tick = 0; tick < 20; tick++)
        {
            test.Run(1);

            DPose pose = test.World.PoseIn(entity, test.Root);
            pose.Position.Should().Be(place, "a spin does not move its entity");
            DQuaternion expected = spin.RotationAt(test.World.Time);
            Math.Abs(Dot(pose.Rotation, expected)).Should()
                .BeApproximately(1, 1e-12, $"it turned by the spin at {test.World.Time} s");
            (test.World.AngularVelocityIn(entity, test.Root) - spin.AngularVelocity).Length.Should().BeLessThan(1e-6);
            test.World.VelocityIn(entity, test.Root).Should().Be(Double3.Zero);
        }
    }

    /// <summary>
    /// The root has no spawn velocity: its end poses alone carry it 2 m/s along +X and turn it 0.5 rad/s about +Y, from its
    /// first step on.
    /// </summary>
    [Fact]
    public void AnAttachedChildFollowsARootThatMotionIntentsMove()
    {
        using TestWorld test = TestWorld.OnJolt(out _);
        var drift = new Double3(2, 0, 0);
        var spin = new Double3(0, 0.5, 0);
        DPose spawn = TestWorld.At(5);
        Entity root = test.World.Spawn(new()
        {
            Frame = test.Root,
            Pose = spawn,
            FirstMove = new(new EndPose(Advanced(spawn, drift, spin, test.World.DeltaTime))),
            Components =
            [
                Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
                    self.MoveKinematic(Advanced(self.PoseAtNextStepStartIn(self.Frame), drift, spin, self.DeltaTime)))
            ]
        });
        Entity child = test.World.Spawn(new()
        {
            Frame = test.Root,
            AttachTo = root,
            AttachOffset = new(new(0, 0, 2), Quaternion.Identity)
        });

        test.Run(10);

        DPose rootPose = test.World.PoseIn(root, test.Root);
        (rootPose.Position - (spawn.Position + drift * (10 * (double)test.World.DeltaTime))).Length.Should().BeLessThan(
            1e-12,
            "the root's end poses carried it for ten steps");
        Double3 lever = rootPose.Rotation.Rotate(new Double3(0, 0, 2));
        lever.X.Should().BeGreaterThan(0.1, "the root turned, and the child's lever with it");
        (test.World.PoseIn(child, test.Root).Position - (rootPose.Position + lever)).Length.Should().BeLessThan(1e-6,
            "the child stands at its offset from the root");
        (test.World.VelocityIn(child, test.Root) - (drift + Double3.Cross(spin, lever))).Length.Should().BeLessThan(
            1e-5,
            "the child takes the root's velocity at its lever");
        (test.World.AngularVelocityIn(child, test.Root) - spin).Length.Should().BeLessThan(1e-5);
    }

    // ---- Determinism -----------------------------------------------------------------------------------------------

    [Fact]
    public void MotionIntentsGiveTheSameStateWithOneAndEightWorkers()
    {
        (ulong one, int movedOne, int framelessOne) = RunMotionIntents(maxDop: 1);
        (ulong eight, int movedEight, _) = RunMotionIntents(maxDop: 8);

        movedOne.Should().Be(framelessOne,
            "every entity without body or frame left its spawn pose by its motion intents");
        movedEight.Should().Be(movedOne);
        eight.Should().Be(one,
            "each entity reads and writes only its own intent and row, whatever the order of the chunks");
    }

    /// <summary>
    /// Runs 60 ticks of a world in which motion intents move every kind of entity that takes them, on
    /// <paramref name="maxDop"/> workers with chunks of 16 slots: entities without body or frame on Kepler orbits, on spins
    /// and on end poses, some with an attached child, plain kinematic bodies on end poses, and frames without a body on
    /// Kepler orbits. Returns the hash of the world (see <see cref="WorldHash"/>), how many entities without body or frame
    /// left their spawn pose, and how many there are.
    /// </summary>
    private static (ulong Hash, int Moved, int Frameless) RunMotionIntents(int maxDop)
    {
        using TestWorld test =
            TestWorld.OnJolt(out _, maxDegreeOfParallelism: maxDop, chunkSize: 16, debugChecks: false);
        test.GiveRootPointGravity(1e8);
        World world = test.World;
        var random = new Random(7);
        var frameless = new List<(Entity Entity, DPose Spawn)>();
        var kinematicBall = new BodyDesc(new SphereShapeDesc(0.5f), Motion: BodyMotion.Kinematic);
        for (int i = 0; i < 120; i++)
        {
            DPose at = TestWorld.At(random.NextDouble() * 400 - 200, 0, random.NextDouble() * 400 - 200);
            var place = new SpawnDesc { Frame = test.Root, Pose = at };
            var velocity = new Double3(random.NextDouble() * 4 - 2, 0, random.NextDouble() * 4 - 2);
            SpawnDesc desc = (i % 4) switch
            {
                0 => new SpawnDesc { Frame = test.Root }.OnKeplerOrbit(world,
                    KeplerOrbit.Circular(1000 + i * 10, phase: i * 0.7), firstMove: i % 8 == 0),
                1 => place.Spinning(world, new(DQuaternion.Identity, Rate: 0.1 + 0.01 * i), firstMove: i % 8 == 1),
                2 => KinematicDrift.Drifting(place, world, velocity),
                _ => KinematicDrift.Drifting(new() { Frame = test.Root, Pose = at, Body = kinematicBall }, world,
                    velocity)
            };
            Entity entity = world.Spawn(desc);
            if (i % 4 != 3)
                frameless.Add((entity, world.PoseIn(entity, test.Root)));
            if (i % 10 == 0)
                world.Spawn(new()
                    { Frame = test.Root, AttachTo = entity, AttachOffset = new(new(0, 0, 2), Quaternion.Identity) });
        }

        for (int i = 0; i < 2; i++)
            world.Spawn(new SpawnDesc { Frame = test.Root, AsFrame = new(FrameBounds.Sphere(20)) }
                .OnKeplerOrbit(world, KeplerOrbit.Circular(3000 + i * 500, phase: i)));

        world.Run(60);

        int moved = frameless.Count(f => world.PoseIn(f.Entity, test.Root) != f.Spawn);
        return (WorldHash.Of(world), moved, frameless.Count);
    }

    // ---- Helpers -------------------------------------------------------------------------------------------------

    private static double Dot(DQuaternion a, DQuaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

    /// <summary><paramref name="pose"/> moved by <paramref name="velocity"/> and turned by <paramref name="spin"/>, both in the frame's axes, for
    /// <paramref name="step"/> seconds.</summary>
    private static DPose Advanced(in DPose pose, Double3 velocity, Double3 spin, double step) =>
        new(pose.Position + velocity * step,
            DQuaternion.FromAxisAngle(spin / spin.Length, spin.Length * step) * pose.Rotation);

    private static void Write(Self self, string kind)
    {
        if (kind == "end pose")
            self.MoveKinematic(End);
        else
            self.MoveOnRails(Sample);
    }
}
