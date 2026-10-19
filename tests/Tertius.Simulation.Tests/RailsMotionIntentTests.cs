using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Full samples as motion intents: the motion samples that the owners of frames write with <see cref="Self.MoveOnRails"/>
/// into their intents (<see cref="PhysicsIntent.Move"/>), what a sample must hold, how the World swaps and converts them with
/// the rest of the intent, and how a frame follows them (docs/plans/rails-components.md, "Rails motion for every entity"). A
/// sample that tick N writes is the motion intent of step N + 1, and the frame stands on it at the sync point of tick N + 1.
/// The tests read the next step's intent between ticks. Also the public reads that a rails component needs:
/// <see cref="Self.TimeAt"/>, <see cref="Self.OwnedFrame"/> and <see cref="Self.PoseAtNextStepStartIn"/>. RailsFollowTests
/// (Physics.Tests) checks how frames follow the samples in physics.
/// </summary>
public class RailsMotionIntentTests
{
    // ---- The life of a full sample ------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFullSampleWrittenInATickMovesItsFrameInTheNextStep(bool withHull)
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        FrameHandle frame = SpawnFrame(test, body: withHull ? KinematicHull : null, components: WritesTheTickNumber());

        test.Run(1);

        NextSample(test, frame).Should().Be(SampleOfTick(0), "tick 0 wrote it, and step 1 applies it");

        test.Run(1);

        faults.Intents.Should().Contain(record => record.Entity == frame.Entity && record.Tick == 1 && record.Intent.Move == new MotionIntent(SampleOfTick(0)),
            "step 1 gets the sample in the owner's intent");
        test.World.Frames.LocalPose(frame).Should().Be(SampleOfTick(0).Pose, "step 1 applied it, so the frame stands on it at the sync point of tick 1");
        test.World.Frames.Followed(frame).Should().Be(FollowKind.Rails);
        NextSample(test, frame).Should().Be(SampleOfTick(1));
    }

    [Fact]
    public void AFrameWhoseComponentWritesNoSampleInATickHasNoMotionIntentInTheNextStep()
    {
        using var test = new TestWorld();
        FrameHandle frame = SpawnFrame(test, components: Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                self.MoveOnRails(SampleOfTick(0));
        }));
        test.Run(1);
        NextSample(test, frame).Should().NotBeNull("tick 0 wrote a sample");

        test.Run(1);

        NextMove(test, frame).HasValue.Should().BeFalse("tick 1 wrote none");
        test.World.Frames.LocalPose(frame).Should().Be(SampleOfTick(0).Pose);
    }

    /// <summary>
    /// One field holds the motion intent of a step, so the last write of a tick wins whatever its case. Until every writer
    /// takes the rules of one guard (docs/plans/rails-components.md, stage 6 of "Order of the work"), a dynamic body takes an
    /// end pose and a steer target, and the owner of a root frame with a body an end pose and a full sample.
    /// </summary>
    [Theory]
    [InlineData("end pose", "steer target")]
    [InlineData("steer target", "end pose")]
    [InlineData("end pose", "full sample")]
    [InlineData("full sample", "end pose")]
    [InlineData("full sample", "full sample")]
    public void TheLastMotionIntentOfATickWinsWhateverItsCase(string first, string last)
    {
        using var test = new TestWorld();
        bool steers = first == "steer target" || last == "steer target";
        Entity entity = test.World.Spawn(new SpawnDesc
        {
            Frame = steers ? test.Root : FrameHandle.Null,
            Body = steers ? TestWorld.Ball : new BodyDesc(new SphereShapeDesc(0.5f), Motion: BodyMotion.Static),
            AsFrame = steers ? null : new FrameSpec(FrameBounds.Sphere(10)),
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                Write(self, first, 1);
                Write(self, last, 2);
            })],
        });

        test.Run(1);

        test.World.Errors.Should().BeEmpty();
        test.World.Entities.NextStepIntent(entity).Move.Should().Be(Case(last, 2));
    }

    [Fact]
    public void WithoutAPhysicsBackendAFrameWithoutABodyFollowsItsMotionIntents()
    {
        using var test = new TestWorld();
        FrameHandle frame = SpawnFrame(test, components: WritesTheTickNumber());

        for (int tick = 1; tick <= 3; tick++)
        {
            test.Run(1);

            NextSample(test, frame).Should().Be(SampleOfTick(tick - 1), "the swap keeps the motion intents without a backend");
            if (tick > 1)
                test.World.Frames.LocalPose(frame).Should().Be(SampleOfTick(tick - 2).Pose, "the sync point applies them to a frame without a body");
        }
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
        test.World.Frames.LocalPose(frame).Should().Be(SampleOfTick(0).Pose);
        NextSample(test, frame).Should().Be(SampleOfTick(1));
    }

    [Fact]
    public void ANewFrameThatReusesTheSlotOfADeadFrameHasNoMotionIntent()
    {
        using var test = new TestWorld();
        FrameHandle dead = SpawnFrame(test, components: WritesTheTickNumber());
        test.Run(2);
        test.World.Destroy(dead.Entity);

        FrameHandle reused = SpawnFrame(test);

        reused.Index.Should().Be(dead.Index, "the tree reuses the index of the dead frame");
        NextMove(test, reused).HasValue.Should().BeFalse();
        test.World.Frames.Followed(reused).Should().Be(FollowKind.Static);
    }

    [Fact]
    public void AMotionIntentRelativeToADyingFrameConvertsIntoItsEntitysNewFrame()
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
            AngularVelocity: new Double3(0.01, 0.02, 0.03), AngularAcceleration: new Double3(0.001, -0.002, 0.003));
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

        PhysicsIntent intent = test.World.Entities.NextStepIntent(child.Entity);
        intent.Frame.Should().Be(test.Root);
        intent.Move.TryGetValue(out MotionSample converted).Should().BeTrue("a full sample stays a full sample");
        Distance(converted.Pose.Position, composed.Position).Should().BeLessThan(1e-6, "the place");
        Distance(converted.Pose.Rotation, composed.Rotation).Should().BeLessThan(1e-12, "the rotation");
        Distance(converted.ChordVelocity, turn.Rotate(sample.ChordVelocity) + carried).Should().BeLessThan(1e-9, "the chord");
        Distance(converted.Velocity, turn.Rotate(sample.Velocity) + carried).Should().BeLessThan(1e-9, "the velocity");
        // A known gap, not the right value: the right value also adds the dying parent's own acceleration on its orbit, which
        // the frame tree no longer holds after step 2. FrameTree.AccelerationRelative adds no acceleration of a semantic frame
        // on its path yet, so this pins only the turn of the sample's acceleration.
        Distance(converted.Acceleration, turn.Rotate(sample.Acceleration)).Should().BeLessThan(1e-12, "the acceleration");
        Distance(converted.AngularVelocity, turn.Rotate(sample.AngularVelocity) + parentSpin).Should().BeLessThan(1e-12, "the angular velocity");
        Distance(converted.AngularAcceleration, turn.Rotate(sample.AngularAcceleration)).Should().BeLessThan(1e-12, "the angular acceleration");
    }

    [Fact]
    public void EachStepGetsTheMotionIntentsThatItAppliesInItsIntents()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        FrameHandle frame = SpawnFrame(test, components: WritesTheTickNumber());

        test.Run(3);

        faults.Moves.Where(record => record.Entity == frame.Entity).Should().Equal(
            new MoveRecord(1, frame.Entity, test.Root, new MotionIntent(SampleOfTick(0))),
            new MoveRecord(2, frame.Entity, test.Root, new MotionIntent(SampleOfTick(1))));
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
    public void MoveOnRailsThrowsForASampleWithAFieldThatIsNotFinite(string field)
    {
        using var test = new TestWorld();
        MotionSample sample = SampleNotFiniteIn(field);
        FrameHandle frame = SpawnFrame(test, components: Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.MoveOnRails(sample)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain(frame.Entity.ToString()).And.Contain(nameof(ArgumentException));
        NextMove(test, frame).HasValue.Should().BeFalse("the World keeps no sample that it refused");
    }

    [Fact]
    public void MoveOnRailsThrowsForASampleWhoseRotationIsZero()
    {
        using var test = new TestWorld();
        // The default sample has the rotation (0, 0, 0, 0), which no normalization can turn into a rotation.
        FrameHandle frame = SpawnFrame(test, components: Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.MoveOnRails(default)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain(frame.Entity.ToString()).And.Contain(nameof(ArgumentException));
        NextMove(test, frame).HasValue.Should().BeFalse("the World keeps no sample that it refused");
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
        Distance(NextSample(test, frame)!.Value.Pose.Rotation, turn).Should().BeLessThan(1e-15, "physics gets the same unit rotation");
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

    [Fact]
    public void ThePoseAtTheNextStepStartIsTheEndOfTheRunningStepsMotionIntentInTheFrameThatTheReadNames()
    {
        using var test = new TestWorld();
        var turn = DQuaternion.FromAxisAngle(Double3.UnitZ, 0.5);
        FrameHandle parent = test.SpawnStaticFrame(new DPose(new Double3(100, 0, 0), turn));
        MotionSample sample = SampleOfTick(7);
        DPose inParent = default, inRoot = default;
        SpawnFrame(test, parent, components: Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                self.MoveOnRails(sample);
            else if (self.Tick == 1)
            {
                inParent = self.PoseAtNextStepStartIn(self.Frame);
                inRoot = self.PoseAtNextStepStartIn(test.Root);
            }
        }));

        test.Run(2);

        inParent.Should().Be(sample.Pose, "step 1 ends on the sample of tick 0, where step 2 starts");
        Distance(inRoot.Position, DPose.Compose(new DPose(new Double3(100, 0, 0), turn), sample.Pose).Position).Should().BeLessThan(1e-12);
    }

    [Fact]
    public void WithoutAMotionIntentThePoseAtTheNextStepStartIsTheRunningStepsTeleportOrThePoseNow()
    {
        using var test = TestWorld.OnJolt(out _);
        var seen = new List<DPose>();
        test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(3),
            Body = TestWorld.Ball,
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                seen.Add(self.PoseAtNextStepStartIn(self.Frame));
                if (self.Tick == 0)
                    self.Body.Teleport(TestWorld.At(40));
                else if (self.Tick == 2)
                    self.Body.SteerTo(TestWorld.At(90));
            })],
        });

        test.Run(4);

        seen[0].Should().Be(TestWorld.At(3), "no step applies an intent of the ball, so it starts the next step where it stands");
        seen[1].Should().Be(TestWorld.At(40), "the running step teleports the ball");
        seen[2].Position.Should().Be(new Double3(40, 0, 0), "the ball stands at the teleport's pose now, in no gravity");
        seen[3].Position.Should().Be(new Double3(40, 0, 0), "a steer target gives no exact end, so the read takes the pose now");
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
            self.MoveOnRails(StillAt(self.PoseAtNextStepStartIn(self.Frame).Position)));
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
        new(new DPose(new Double3(tick, 0, 0), DQuaternion.Identity), Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero);

    /// <summary>A sample of a frame that stands still at <paramref name="place"/>, unturned.</summary>
    private static MotionSample StillAt(Double3 place) =>
        new(new DPose(place, DQuaternion.Identity), Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero);

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
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "no such field"),
        };
    }

    /// <summary>Writes <see cref="SampleOfTick"/> of the running tick, in every tick.</summary>
    private static Lambda WritesTheTickNumber() =>
        Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.MoveOnRails(SampleOfTick(self.Tick)));

    /// <summary>A kinematic box of 1 m, the hull of a frame on rails.</summary>
    private static readonly BodyDesc KinematicHull = new(new BoxShapeDesc(new Vector3(0.5f)), Motion: BodyMotion.Kinematic);

    /// <summary>
    /// Spawns a frame at the origin of <paramref name="parent"/>, or of the root, with <paramref name="body"/> as its hull or
    /// without a body, and returns it.
    /// </summary>
    private static FrameHandle SpawnFrame(TestWorld test, FrameHandle parent = default, BodyDesc? body = null, params IComponent[] components) =>
        test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = parent.IsNull ? test.Root : parent,
            Body = body,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
            Components = components,
        }));

    /// <summary>Writes the case <paramref name="kind"/> of <see cref="Case"/> through its writer.</summary>
    private static void Write(Self self, string kind, double x)
    {
        switch (Case(kind, x))
        {
            case EndPose end:
                self.Body.MoveKinematic(end.Pose);
                break;
            case SteerTarget steer:
                self.Body.SteerTo(steer.Pose);
                break;
            case MotionSample sample:
                self.MoveOnRails(sample);
                break;
        }
    }

    /// <summary>A motion intent of the case <paramref name="kind"/> at <paramref name="x"/> m along X.</summary>
    private static MotionIntent Case(string kind, double x) => kind switch
    {
        "end pose" => new MotionIntent(new EndPose(TestWorld.At(x))),
        "steer target" => new MotionIntent(new SteerTarget(TestWorld.At(x))),
        _ => new MotionIntent(SampleOfTick((long)x)),
    };

    private static double Distance(Double3 a, Double3 b) => (a - b).Length;

    private static double Distance(DQuaternion a, DQuaternion b) =>
        Math.Max(Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)), Math.Max(Math.Abs(a.Z - b.Z), Math.Abs(a.W - b.W)));

    /// <summary>The motion intent that the next step applies to the owner of <paramref name="frame"/>, between two ticks.</summary>
    private static MotionIntent NextMove(TestWorld test, FrameHandle frame) => test.World.Entities.NextStepIntent(frame.Entity).Move;

    /// <summary>The full sample that the next step applies to the owner of <paramref name="frame"/>, or null.</summary>
    private static MotionSample? NextSample(TestWorld test, FrameHandle frame) =>
        NextMove(test, frame).TryGetValue(out MotionSample sample) ? sample : null;
}
