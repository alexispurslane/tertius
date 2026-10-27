using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// The rule that gives a frame its proper acceleration, on a tree built in the test (docs/plans/rails-components.md, "The
/// proper acceleration of a frame"): a frame's proper acceleration is its acceleration over the step minus the gravity
/// that acts at its place where the step starts, in the frame's own axes. The gravity at its place is its semantic
/// parent's field there minus the parent's own proper acceleration. The tests run step 2 of the sync point on the tree
/// (<see cref="FrameTree.TakeStepRecords"/>), in which every frame takes its step record, or holds still without one. The
/// scenes on the real World are in Tertius.Physics.Tests.
/// </summary>
public sealed class FrameProperAccelerationTests
{
    private const double EarthMu = 3.986004418e14;
    private const double OrbitRadius = 6.771e6;
    private const double OrbitSpeed = 7_672.6;
    private const double Dt = 1.0 / 60;

    // ---- The rotation terms of a turning parent (docs/plans/spin-gravity.md, "The proper acceleration of a frame") ------
    // The station turns in place in a root frame without a field about its +Y. A rate ω about +Y gives a place at (r, 0, 0)
    // the centrifugal acceleration (ω² r, 0, 0), a chord (u, 0, 0) the Coriolis acceleration (0, 0, 2 ω u), and an angular
    // acceleration α about +Y the Euler acceleration (0, 0, α r). The gravity at a frame's place in the station is the sum
    // of the three, so the frame feels it reversed.

    private const double StationRate = 0.31321;
    private static readonly GravityField Earth = GravityField.Point(EarthMu);

    private readonly FrameTree frames = new();
    private uint nextOwner = 1;

    private FrameHandle Frame(FrameHandle parent, DPose pose = default, GravityField field = default) =>
        frames.Create(
            frameOwner: new(nextOwner++, 1),
            parent: parent,
            localPose: pose.Rotation.LengthSquared == 0 ? new(pose.Position, DQuaternion.Identity) : pose,
            frameBounds: FrameBounds.Sphere(1e9f),
            field: field
        );

    [Fact]
    public void AFrameAtRestInAFrameThatHoldsStillInAFieldFeelsThatFieldOneSyncPointLater()
    {
        FrameHandle ground = Frame(FrameHandle.Null, field: GravityField.Uniform(new(0, -9.81f, 0)));
        FrameHandle cabin = Frame(ground, TestPose(0, 10, 0));
        FrameHandle crate = Frame(cabin, TestPose(1, 0, 0));

        frames.TakeStepRecords(stepIntents: [], step: Dt);

        frames
            .ProperAcceleration(cabin)
            .Should()
            .Be(new Vector3(0, 9.81f, 0), "the cabin holds still against the ground's gravity");
        frames
            .ProperAcceleration(crate)
            .Should()
            .Be(
                Vector3.Zero,
                "the rule read the cabin's value as the step found it, though the pass gave the cabin its new value before it reached the crate"
            );

        frames.TakeStepRecords(stepIntents: [], step: Dt);

        frames
            .ProperAcceleration(crate)
            .Should()
            .Be(
                new Vector3(0, 9.81f, 0),
                "the cabin has no field of its own, so the gravity at the crate's place is the cabin's proper acceleration reversed"
            );
    }

    [Fact]
    public void AFrameWhoseOwnersUpdateTheStepSkippedKeepsItsProperAcceleration()
    {
        FrameHandle ground = Frame(FrameHandle.Null, field: GravityField.Uniform(new(0, -9.81f, 0)));
        FrameHandle cabin = Frame(ground, TestPose(0, 10, 0));
        frames.TakeStepRecords(stepIntents: [], step: Dt);
        frames.SetGravity(ground, GravityField.Uniform(new(0, -3.7f, 0)));
        var owners = new StepOwners();
        owners.Skipped.Add(cabin);

        frames.TakeStepRecords(stepIntents: [], step: Dt, owners: owners);

        frames
            .ProperAcceleration(cabin)
            .Should()
            .Be(new Vector3(0, 9.81f, 0), "a skipped update changes nothing, as for the velocities");
        frames.AssertInvariants();
    }

    [Fact]
    public void AFrameThatHoldsStillInAFallingFrameIsInFreeFall()
    {
        FrameHandle earth = Frame(FrameHandle.Null, field: Earth);
        FrameHandle pod = FallingFrame(earth, new(OrbitRadius, 0, 0), new(0, 0, OrbitSpeed));
        FrameHandle cabin = Frame(pod);
        frames.Update();
        frames.BeginStep([], 1.0 / 60, lastStepFailed: false);

        frames.AdvanceNumericFrames(Dt);
        frames.TakeStepRecords(stepIntents: [], step: Dt);

        frames
            .ProperAcceleration(cabin)
            .Length()
            .Should()
            .BeLessThan(
                1e-6f,
                "the cabin moves with the falling frame, which accelerates by the gravity at its origin"
            );
    }

    [Fact]
    public void AFrameThatAFollowedRecordAcceleratesFeelsItsAccelerationLessTheGravityWhereTheStepStarted()
    {
        FrameHandle earth = Frame(FrameHandle.Null, field: Earth);
        var place = new Double3(OrbitRadius, 0, 0);
        FrameHandle ship = Frame(earth, new(place, DQuaternion.Identity));
        var chord = new Double3(0, 0, OrbitSpeed);
        var thrust = new Double3(0, 3, 0);
        Double3 end = place + chord * Dt;
        Double3 gravity = Earth.AtPrecise(place);
        var record = new MotionSample(
            Pose: new(end, DQuaternion.Identity),
            ChordVelocity: chord,
            Velocity: chord,
            Acceleration: gravity + thrust,
            AngularVelocity: Double3.Zero,
            AngularAcceleration: Double3.Zero
        );

        frames.TakeStepRecords(
            stepIntents: Moving(ship, new PhysicsIntent { Frame = earth, Move = new(record) }),
            step: Dt
        );

        Vector3 proper = frames.ProperAcceleration(ship);
        proper.X.Should().BeApproximately(0, 1e-9f);
        proper
            .Y.Should()
            .BeApproximately(
                3,
                1e-6f,
                "the thrust is the only acceleration that is not the gravity where the step started"
            );
        proper.Z.Should().BeApproximately(0, 1e-9f);
    }

    [Fact]
    public void AFrameTakesTheProperAccelerationThatPhysicsMeasuredInPlaceOfTheRule()
    {
        FrameHandle ground = Frame(FrameHandle.Null, field: GravityField.Uniform(new(0, -9.81f, 0)));
        FrameHandle ship = Frame(ground);
        frames.SetHullMotion(ship, BodyMotion.Dynamic);
        var jolt = new Vector3(0, 0, 120);
        var rest = new MotionSample(
            Pose: DPose.Identity,
            ChordVelocity: Double3.Zero,
            Velocity: Double3.Zero,
            Acceleration: Double3.Zero,
            AngularVelocity: Double3.Zero,
            AngularAcceleration: Double3.Zero
        );

        frames.TakeStepRecords(stepIntents: [], step: Dt, owners: new StepOwners().WithHullRecord(ship, rest, jolt));

        frames.ProperAcceleration(ship).Should().Be(jolt);
    }

    [Fact]
    public void AFrameAtRestInASpinningFrameIsPushedTowardTheAxisFromTheSyncPointAfterTheFramesFirstTurningStep()
    {
        // The rotation terms come from the station's turns of the step, which the tree fixed before it, so they do not wait
        // for the station's proper acceleration to move down a level.
        FrameHandle space = Frame(FrameHandle.Null);
        FrameHandle station = Frame(space);
        FrameHandle room = Frame(station, TestPose(100, 0, 0));

        RunStep(Moving(station, Spinning(1 * Dt)));

        Vector3 proper = frames.ProperAcceleration(room);
        proper.X.Should().BeApproximately((float)(-StationRate * StationRate * 100), 1e-5f, "ω² R toward the axis");
        proper.Y.Should().Be(0);
        proper.Z.Should().Be(0);
    }

    [Fact]
    public void AFrameThatMovesInASpinningFrameFeelsTheCoriolisTermOnTheChordOfItsRecord()
    {
        const double outward = 3;
        FrameHandle space = Frame(FrameHandle.Null);
        FrameHandle station = Frame(space);
        FrameHandle cart = Frame(station, TestPose(100, 0, 0));
        PhysicsIntent[] intents = Moving(station, Spinning(1 * Dt));
        intents[cart.Entity.Index] = new()
        {
            Frame = station,
            Move = new(new EndPose(TestPose(100 + outward * Dt, 0, 0))),
        };

        RunStep(intents);

        Vector3 proper = frames.ProperAcceleration(cart);
        proper
            .X.Should()
            .BeApproximately(
                (float)(-StationRate * StationRate * 100),
                1e-5f,
                "ω² r toward the axis, at the place where the step started"
            );
        proper.Y.Should().Be(0);
        proper
            .Z.Should()
            .BeApproximately(
                (float)(-2 * StationRate * outward),
                1e-6f,
                "the Coriolis term on the cart's chord, reversed"
            );
    }

    [Fact]
    public void AFrameAtRestInAFrameThatSpinsUpFeelsTheEulerTerm()
    {
        // The station spins up from rest at 0.5 rad/s² about its +Y. In its first step it is a new frame, whose step assumes
        // a steady turn, so the Euler term starts in the second: (0, 0, 0.5 · 100) at the room's place.
        const double spinUp = 0.5;
        FrameHandle space = Frame(FrameHandle.Null);
        FrameHandle station = Frame(space);
        FrameHandle room = Frame(station, TestPose(100, 0, 0));

        static MotionIntent SpinningUp(double stepEnd) =>
            new(
                new MotionSample(
                    Pose: new(Double3.Zero, DQuaternion.FromAxisAngle(Double3.UnitY, 0.5 * spinUp * stepEnd * stepEnd)),
                    ChordVelocity: Double3.Zero,
                    Velocity: Double3.Zero,
                    Acceleration: Double3.Zero,
                    AngularVelocity: new(0, spinUp * stepEnd, 0),
                    AngularAcceleration: new(0, spinUp, 0)
                )
            );

        RunStep(Moving(station, SpinningUp(1 * Dt)));
        frames.ProperAcceleration(room).Z.Should().Be(0, "the first step of a new frame assumes a steady turn");
        RunStep(Moving(station, SpinningUp(2 * Dt)));

        Vector3 proper = frames.ProperAcceleration(room);
        double rate = spinUp * 2 * Dt;
        proper
            .X.Should()
            .BeApproximately(
                (float)(-rate * rate * 100),
                1e-5f,
                "ω² R toward the axis, at the rate where the step ended"
            );
        proper.Z.Should().BeApproximately((float)(-spinUp * 100), 1e-4f, "the Euler term, reversed");
    }

    /// <summary>The spin sample of a station that turns at <see cref="StationRate"/> about its +Y, in the step that ends at
    /// <paramref name="stepEnd"/>.</summary>
    private static MotionIntent Spinning(double stepEnd) =>
        new(new FrameSpin(Tilt: DQuaternion.Identity, Rate: StationRate).Sample(Double3.Zero, stepEnd));

    /// <summary>One step: the tree updates, fixes the turns of the step that applies <paramref name="intents"/>, and takes the records of the step.</summary>
    private void RunStep(PhysicsIntent[] intents)
    {
        frames.Update();
        frames.BeginStep(intents, Dt, lastStepFailed: false);
        frames.TakeStepRecords(stepIntents: intents, step: Dt);
    }

    /// <summary>The intents of a step in which only the owner of <paramref name="frame"/> wrote one, its motion intent
    /// <paramref name="move"/>.</summary>
    private PhysicsIntent[] Moving(FrameHandle frame, MotionIntent move)
    {
        var intents = new PhysicsIntent[nextOwner];
        intents[frame.Entity.Index] = new() { Frame = frames.Parent(frame), Move = move };
        return intents;
    }

    /// <summary>The intents of a step in which only the owner of <paramref name="frame"/> wrote one: <paramref name="intent"/>
    /// .</summary>
    private static PhysicsIntent[] Moving(FrameHandle frame, in PhysicsIntent intent)
    {
        var intents = new PhysicsIntent[frame.Entity.Index + 1];
        intents[frame.Entity.Index] = intent;
        return intents;
    }

    private static DPose TestPose(double x, double y, double z) => new(new(x, y, z), DQuaternion.Identity);

    /// <summary>The falling frame that a numeric update gives one fast body of <paramref name="semantic"/>.</summary>
    private FrameHandle FallingFrame(FrameHandle semantic, Double3 position, Double3 velocity)
    {
        FrameHandle home = FrameHandle.Null;
        var body = new NumericBody
        {
            Entity = new(1000, 1),
            Semantic = semantic,
            Home = semantic,
            Position = position,
            HomePosition = position,
            Velocity = velocity,
            Radius = 1,
            Mass = 1,
        };
        frames.UpdateNumericFrames(
            bodies: [body],
            ticks: 1,
            tickRate: 10,
            changes: new(),
            applyMoves: moves => home = moves.Single().To
        );
        frames.IsNumeric(home).Should().BeTrue();
        return home;
    }
}
