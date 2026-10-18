using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The sample table: the motion samples that the owners of frames write with <see cref="Self.MoveOnRails"/>, what a sample
/// must hold, how the World moves its three buffers on, and how it converts them. A sample that tick N writes is the
/// applied sample of step N + 1, and the followed sample at the sync point of tick N + 1. The tests read the buffers between
/// ticks: then the applied buffer holds what the next step applies, and the followed buffer what the frames followed at the
/// last sync point. Also the public reads that a rails component needs: <see cref="Self.TimeAt"/>,
/// <see cref="Self.OwnedFrame"/> and <see cref="FrameTree.TryGetAppliedSample"/>. Most tests read the table itself;
/// RailsFollowTests (Physics.Tests) checks how frames follow the samples.
/// </summary>
public class MotionSampleTableTests
{
    // ---- The buffers --------------------------------------------------------------------------------------------

    [Fact]
    public void ASampleThatATickWritesIsTheAppliedSampleOfTheNextStepAndTheFollowedSampleAtTheSyncPointAfterThatStep()
    {
        using var test = TestWorld.OnJolt(out _);
        FrameHandle frame = SpawnFrame(test, components: WritesTheTickNumber());

        test.Run(1);

        Applied(test, frame).Should().Be(SampleOfTick(0), "tick 0 wrote it, and step 1 applies it");

        test.Run(1);

        Followed(test, frame).Should().Be(SampleOfTick(0), "step 1 applied it, so the frame follows it at the sync point of tick 1");
        Applied(test, frame).Should().Be(SampleOfTick(1));
    }

    [Fact]
    public void AFrameWhoseComponentWritesNoSampleInATickHasNoAppliedSampleInTheNextStep()
    {
        using var test = new TestWorld();
        FrameHandle frame = SpawnFrame(test, components: Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                self.MoveOnRails(SampleOfTick(0));
        }));
        test.Run(1);
        Applied(test, frame).Should().NotBeNull("tick 0 wrote a sample");

        test.Run(1);

        Applied(test, frame).Should().BeNull("tick 1 wrote none");
        Followed(test, frame).Should().Be(SampleOfTick(0));
    }

    [Fact]
    public void TheLastMoveOnRailsOfATickWins()
    {
        using var test = new TestWorld();
        FrameHandle frame = SpawnFrame(test, components: Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            self.MoveOnRails(SampleOfTick(7));
            self.MoveOnRails(SampleOfTick(8));
        }));

        test.Run(1);

        Applied(test, frame).Should().Be(SampleOfTick(8));
    }

    [Fact]
    public void TheSampleBuffersMoveOnWithoutAPhysicsBackend()
    {
        using var test = new TestWorld();
        FrameHandle frame = SpawnFrame(test, components: WritesTheTickNumber());

        test.Run(2);

        Followed(test, frame).Should().Be(SampleOfTick(0));
        Applied(test, frame).Should().Be(SampleOfTick(1));
    }

    [Fact]
    public void AFrameThatTheTreeMadeRoomForCanWriteASample()
    {
        using var test = new TestWorld();
        // The tree starts with rows for 64 frames, so it grows for these.
        for (int i = 0; i < 100; i++)
            SpawnFrame(test);
        FrameHandle frame = SpawnFrame(test, components: WritesTheTickNumber());

        test.Run(2);

        test.World.Errors.Should().BeEmpty();
        Followed(test, frame).Should().Be(SampleOfTick(0));
        Applied(test, frame).Should().Be(SampleOfTick(1));
    }

    [Fact]
    public void ANewFrameThatReusesTheIndexOfADeadFrameHasNoSample()
    {
        using var test = new TestWorld();
        FrameHandle dead = SpawnFrame(test, components: WritesTheTickNumber());
        test.Run(2);
        test.World.Destroy(dead.Entity);

        FrameHandle reused = SpawnFrame(test);

        reused.Index.Should().Be(dead.Index, "the tree reuses the index of the dead frame");
        Applied(test, reused).Should().BeNull();
        Followed(test, reused).Should().BeNull();
    }

    [Fact]
    public void ASampleRelativeToADyingFrameConvertsIntoTheFramesNewParentInTheAppliedAndTheFollowedBuffer()
    {
        using var test = new TestWorld();
        const double mu = 4e14;
        test.GiveRootPointGravity(mu);
        // The parent goes round the root on a Kepler orbit and spins about a tilted axis, so it is away from the root's origin,
        // turned, moving and spinning, and the conversion moves, turns and adds the parent's motion.
        KeplerOrbit orbit = KeplerOrbit.Circular(7e6, phase: 0.8, spinRate: 0.02) with
        {
            AxialTilt = DQuaternion.FromAxisAngle(Double3.UnitZ, 0.5),
            SpinPhaseAtEpoch = 1.1,
        };
        FrameHandle parent = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e5f)),
        }.OnKeplerOrbit(test.World, orbit)));
        var sample = new MotionSample(new DPose(new Double3(30, -40, 50), DQuaternion.FromAxisAngle(Double3.UnitX, 0.3)),
            ChordVelocity: new Double3(1, 2, 3), Velocity: new Double3(1.5, 2.5, 3.5), Acceleration: new Double3(-0.1, 0.2, 0.3),
            AngularVelocity: new Double3(0.01, 0.02, 0.03), AngularAcceleration: new Double3(0.001, -0.002, 0.003),
            ProperAcceleration: new Vector3(0, 9.81f, 0));
        FrameHandle child = SpawnFrame(test, parent, components: Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.MoveOnRails(sample)));
        // After two ticks the parent stands on its rails: it followed the first sample of its KeplerMotion at the second
        // sync point.
        test.Run(2);
        FrameTree frames = test.World.Frames;
        DPose parentPose = frames.LocalPose(parent);
        Double3 parentVelocity = frames.LinearVelocity(parent);
        Double3 parentSpin = frames.AngularVelocity(parent);
        DQuaternion turn = parentPose.Rotation;
        parentPose.Position.Length.Should().BeGreaterThan(6e6, "the parent is on its orbit, far from the root's origin");
        Distance(turn, DQuaternion.Identity).Should().BeGreaterThan(0.1, "the parent is turned");
        parentVelocity.Length.Should().BeGreaterThan(1e3, "the parent moves on its orbit");
        parentSpin.Length.Should().BeGreaterThan(1e-3, "the parent spins");
        DPose composed = DPose.Compose(parentPose, sample.Pose);
        // A point that moves with the child's sample, at the sample's place at the end of the step.
        Double3 carried = parentVelocity + Double3.Cross(parentSpin, turn.Rotate(sample.Pose.Position));

        test.World.Destroy(parent.Entity);

        foreach ((string buffer, MotionSamples samples) in new[] { ("applied", frames.Samples.Applied), ("followed", frames.Samples.Followed) })
        {
            samples.TryGet(child, out MotionSample converted, out FrameHandle relativeTo).Should().BeTrue(buffer);
            relativeTo.Should().Be(test.Root, buffer);
            Distance(converted.Pose.Position, composed.Position).Should().BeLessThan(1e-6, $"the {buffer} place");
            Distance(converted.Pose.Rotation, composed.Rotation).Should().BeLessThan(1e-12, $"the {buffer} rotation");
            Distance(converted.ChordVelocity, turn.Rotate(sample.ChordVelocity) + carried).Should().BeLessThan(1e-9, $"the {buffer} chord");
            Distance(converted.Velocity, turn.Rotate(sample.Velocity) + carried).Should().BeLessThan(1e-9, $"the {buffer} velocity");
            Distance(converted.Acceleration, turn.Rotate(sample.Acceleration)).Should().BeLessThan(1e-12, $"the {buffer} acceleration");
            Distance(converted.AngularVelocity, turn.Rotate(sample.AngularVelocity) + parentSpin).Should().BeLessThan(1e-12, $"the {buffer} angular velocity");
            Distance(converted.AngularAcceleration, turn.Rotate(sample.AngularAcceleration)).Should().BeLessThan(1e-12, $"the {buffer} angular acceleration");
            converted.ProperAcceleration.Should().Be(sample.ProperAcceleration, "the proper acceleration is in the frame's own axes");
        }
    }

    [Fact]
    public void EachStepsContextCarriesTheSamplesThatTheStepApplies()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        FrameHandle frame = SpawnFrame(test, components: WritesTheTickNumber());

        test.Run(3);

        faults.Samples.Where(record => record.Frame == frame).Should().Equal(
            new SampleRecord(1, frame, test.Root, SampleOfTick(0)),
            new SampleRecord(2, frame, test.Root, SampleOfTick(1)));
    }

    // ---- Who can write a sample ---------------------------------------------------------------------------------

    [Fact]
    public void MoveOnRailsThrowsForAnEntityThatOwnsNoFrame()
    {
        using var test = new TestWorld();
        Entity plain = test.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.MoveOnRails(SampleOfTick(0))));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain(plain.ToString()).And.Contain(nameof(InvalidOperationException))
            .And.Contain("owns no frame");
    }

    // ---- What a sample must hold --------------------------------------------------------------------------------

    /// <summary>
    /// A sample is gameplay input, so <see cref="Self.MoveOnRails"/> checks it in every build, as a public API checks its
    /// arguments: a field that is not a finite number would reach the frame tree and Jolt.
    /// </summary>
    [Theory]
    [InlineData("place")]
    [InlineData("rotation")]
    [InlineData("chord velocity")]
    [InlineData("velocity")]
    [InlineData("acceleration")]
    [InlineData("angular velocity")]
    [InlineData("angular acceleration")]
    [InlineData("proper acceleration")]
    public void MoveOnRailsThrowsForASampleWithAFieldThatIsNotFinite(string field)
    {
        using var test = new TestWorld();
        MotionSample sample = SampleNotFiniteIn(field);
        FrameHandle frame = SpawnFrame(test, components: Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.MoveOnRails(sample)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain(frame.Entity.ToString()).And.Contain(nameof(ArgumentException));
        Applied(test, frame).Should().BeNull("the World keeps no sample that it refused");
    }

    [Fact]
    public void MoveOnRailsThrowsForASampleWhoseRotationIsZero()
    {
        using var test = new TestWorld();
        // The default sample has the rotation (0, 0, 0, 0), which no normalization can turn into a rotation.
        FrameHandle frame = SpawnFrame(test, components: Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.MoveOnRails(default)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain(frame.Entity.ToString()).And.Contain(nameof(ArgumentException));
        Applied(test, frame).Should().BeNull("the World keeps no sample that it refused");
    }

    /// <summary>
    /// The local pose of a frame must have a unit rotation (see <see cref="FrameTree"/>), so the World makes the rotation of
    /// a sample unit length, as it does for a write of a transform.
    /// </summary>
    [Fact]
    public void AFrameFollowsASampleWhoseRotationIsNotUnitLengthWithThatRotationMadeUnitLength()
    {
        using var test = new TestWorld();
        DQuaternion turn = DQuaternion.FromAxisAngle(Double3.UnitY, 0.5);
        var twiceAsLong = new DQuaternion(2 * turn.X, 2 * turn.Y, 2 * turn.Z, 2 * turn.W);
        MotionSample sample = SampleOfTick(3) with { Pose = new DPose(new Double3(3, 0, 0), twiceAsLong) };
        FrameHandle frame = SpawnFrame(test, components: Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.MoveOnRails(sample)));

        test.Run(2);

        test.World.Errors.Should().BeEmpty();
        Distance(test.World.Frames.LocalPose(frame).Rotation, turn).Should().BeLessThan(1e-15);
        Distance(Applied(test, frame)!.Value.Pose.Rotation, turn).Should().BeLessThan(1e-15, "physics gets the same unit rotation");
    }

    // ---- What Self tells a rails component ----------------------------------------------------------------------

    [Fact]
    public void SelfTimeAtOfTheCurrentTickEqualsWorldTimeBitForBit()
    {
        using var test = new TestWorld(configure: options => options.StartTime = FarStartTime);
        var times = new List<(double TimeAt, double Time)>();
        test.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => times.Add((self.TimeAt(self.Tick), self.Time))));

        test.Run(5);

        times.Should().HaveCount(5);
        times.Should().OnlyContain(t => BitConverter.DoubleToInt64Bits(t.TimeAt) == BitConverter.DoubleToInt64Bits(t.Time));
    }

    [Fact]
    public void SelfTimeAtTwoTicksOnEqualsWorldTimeTwoSyncPointsLater()
    {
        using var test = new TestWorld(configure: options => options.StartTime = FarStartTime);
        double stepEnd = 0, sumOfSteps = 0;
        test.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick != 2)
                return;
            stepEnd = self.TimeAt(self.Tick + 2);
            sumOfSteps = self.Time + 2.0 * self.DeltaTime;
        }));

        test.Run(4);

        BitConverter.DoubleToInt64Bits(stepEnd).Should().Be(BitConverter.DoubleToInt64Bits(test.World.Time),
            "the sync points of ticks 2 and 3 came after tick 2 read it");
        sumOfSteps.Should().NotBe(stepEnd, "at this start time a sum of steps rounds to another double, which the test must tell apart");
    }

    [Fact]
    public void SelfOwnedFrameIsTheFrameThatTheEntityOwnsAndNullForAnEntityThatOwnsNone()
    {
        using var test = new TestWorld();
        FrameHandle seenByOwner = FrameHandle.Null;
        FrameHandle seenByPlain = test.Root;
        FrameHandle frame = SpawnFrame(test, components: Lambda.OnTick((self, scoped ref events, scoped ref outbox) => seenByOwner = self.OwnedFrame));
        test.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => seenByPlain = self.OwnedFrame));

        test.Run(1);

        seenByOwner.Should().Be(frame);
        seenByPlain.Should().Be(FrameHandle.Null);
    }

    /// <summary>
    /// A mod's rails component can do what <see cref="SpinMotion"/> does with public reads only (docs/plans/rails-components.md,
    /// "The rails components"). This turntable holds its frame still at the place where the frame stands when the step of its
    /// sample starts. After a swap from a moving motion, that place is where the last sample of the old motion leaves the
    /// frame, so the frame stops there for good.
    /// </summary>
    [Fact]
    public void AModsTurntableThatUsesOnlyPublicReadsStopsItsFrameWhereTheMotionBeforeItLeavesTheFrame()
    {
        using var test = new TestWorld();
        var velocity = new Double3(10, 0, 0);
        Lambda flight = Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            self.MoveOnRails(StillAt(velocity * self.TimeAt(self.Tick + 2)) with { ChordVelocity = velocity, Velocity = velocity }));
        Lambda turntable = Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            FrameTree frames = self.Snapshot.Frames;
            Double3 place = frames.TryGetAppliedSample(self.OwnedFrame, out MotionSample current)
                ? current.Pose.Position
                : frames.LocalPose(self.OwnedFrame).Position;
            self.MoveOnRails(StillAt(place));
        });
        FrameHandle frame = SpawnFrame(test, components: flight);
        test.Run(10);

        test.World.RemoveComponent(frame.Entity, flight);
        test.World.AddComponent(frame.Entity, turntable);
        test.Run(1);
        Double3 stop = test.World.Frames.LocalPose(frame).Position;
        stop.Should().Be(velocity * test.World.Time, "the flight's last sample moves the frame in the step of the swap");

        for (int tick = 0; tick < 3; tick++)
        {
            test.Run(1);

            test.World.Frames.LocalPose(frame).Position.Should().Be(stop, "the turntable keeps the place where the flight left the frame");
        }
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    /// <summary>
    /// A start time at which <c>self.Time + 2 * self.DeltaTime</c> in tick 2 rounds to another double than the World's
    /// time two ticks later, at 60 ticks per second.
    /// </summary>
    private const double FarStartTime = 1e9 + 0.1;

    /// <summary>A sample whose place is the tick number along X, so that a test can tell which tick wrote it.</summary>
    private static MotionSample SampleOfTick(long tick) =>
        new(new DPose(new Double3(tick, 0, 0), DQuaternion.Identity), Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Vector3.Zero);

    /// <summary>A sample of a frame that stands still at <paramref name="place"/>, unturned.</summary>
    private static MotionSample StillAt(Double3 place) =>
        new(new DPose(place, DQuaternion.Identity), Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Vector3.Zero);

    /// <summary>
    /// <see cref="SampleOfTick"/> of tick 0 with a value that is not a finite number in <paramref name="field"/>. The fields
    /// take it in different components, so that each component of a vector is checked.
    /// </summary>
    private static MotionSample SampleNotFiniteIn(string field)
    {
        MotionSample sample = SampleOfTick(0);
        return field switch
        {
            "place" => sample with { Pose = new DPose(new Double3(0, 0, double.NaN), DQuaternion.Identity) },
            "rotation" => sample with { Pose = new DPose(Double3.Zero, new DQuaternion(0, 0, 0, double.PositiveInfinity)) },
            "chord velocity" => sample with { ChordVelocity = new Double3(0, double.NaN, 0) },
            "velocity" => sample with { Velocity = new Double3(double.NegativeInfinity, 0, 0) },
            "acceleration" => sample with { Acceleration = new Double3(0, 0, double.NaN) },
            "angular velocity" => sample with { AngularVelocity = new Double3(0, double.PositiveInfinity, 0) },
            "angular acceleration" => sample with { AngularAcceleration = new Double3(double.NaN, 0, 0) },
            "proper acceleration" => sample with { ProperAcceleration = new Vector3(0, 0, float.NaN) },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "no such field"),
        };
    }

    /// <summary>Writes <see cref="SampleOfTick"/> of the running tick, in every tick.</summary>
    private static Lambda WritesTheTickNumber() =>
        Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.MoveOnRails(SampleOfTick(self.Tick)));

    /// <summary>Spawns a frame without a body at the origin of <paramref name="parent"/>, or of the root, and returns it.</summary>
    private static FrameHandle SpawnFrame(TestWorld test, FrameHandle parent = default, params IComponent[] components) =>
        test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = parent.IsNull ? test.Root : parent,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
            Components = components,
        }));

    private static double Distance(Double3 a, Double3 b) => (a - b).Length;

    private static double Distance(DQuaternion a, DQuaternion b) =>
        Math.Max(Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)), Math.Max(Math.Abs(a.Z - b.Z), Math.Abs(a.W - b.W)));

    private static MotionSample? Applied(TestWorld test, FrameHandle frame) =>
        test.World.Frames.Samples.Applied.TryGet(frame, out MotionSample sample) ? sample : null;

    private static MotionSample? Followed(TestWorld test, FrameHandle frame) =>
        test.World.Frames.Samples.Followed.TryGet(frame, out MotionSample sample) ? sample : null;
}
