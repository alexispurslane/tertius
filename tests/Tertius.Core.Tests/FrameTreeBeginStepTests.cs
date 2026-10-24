using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// How the frame tree fixes the turns of every frame before a step (<see cref="FrameTree.BeginStep"/>,
/// docs/plans/spin-gravity.md, "V1, the turn of the step in flight", "V2, the turn of the step that ended" and "V3, the
/// angular acceleration of a frame's last record"), on a tree built in the test. Each step of a test is the work of one
/// tick around a step: the tree updates, fixes the turns of the step from the intents of the step, and then takes the
/// step records of the step at step 2 of the sync point (see <see cref="RunStep"/>).
/// <para>
/// The station of the tests spins at 0.31321 rad/s about its own +Y, the rate that gives 9.81 m/s² at a radius of 100 m,
/// as the station of the spike on the branch spike-spin-gravity. A frame that turns by ω over a step of dt seconds has a
/// turn by −dt |ω| about ω: the coordinates of a direction that is fixed in inertial space turn backward in its axes.
/// </para>
/// </summary>
public class FrameTreeBeginStepTests
{
    private const double Step = 1.0 / 60;
    private const double StationRate = 0.31321;
    private const double EarthMu = 3.986004418e14;

    /// <summary>The radius of the facing orbit of the tests, at which a circular orbit turns at about 1.1e-3 rad/s.</summary>
    private const double OrbitRadius = 6.9e6;

    /// <summary>The angular acceleration of the frames that spin up in the tests, in rad/s².</summary>
    private const double SpinUpRate = 0.5;

    private static readonly FrameSpin StationSpin = new(Tilt: DQuaternion.Identity, Rate: StationRate);

    private static readonly KeplerOrbit FacingOrbit = KeplerOrbit.Circular(OrbitRadius) with
    {
        Attitude = KeplerAttitude.FacesParent
    };

    private static FrameTurns NoTurns => new(FrameTurn.None, FrameTurn.None);

    private readonly FrameTree frames = new();
    private readonly FrameHandle space;
    private uint nextOwner = 2;

    public FrameTreeBeginStepTests()
    {
        space = frames.Create(frameOwner: new(1, 1), parent: FrameHandle.Null, localPose: DPose.Identity, frameBounds: FrameBounds.Sphere(1e9f));
    }

    /// <summary>A new frame of a new owner in <paramref name="parent"/> at <paramref name="pose"/>, with a hull of
    /// <paramref name="hull"/> or none.</summary>
    private FrameHandle Frame(FrameHandle parent, DPose pose, BodyMotion? hull = null)
    {
        FrameHandle frame = frames.Create(frameOwner: new(nextOwner++, 1), parent: parent, localPose: pose, frameBounds: FrameBounds.Sphere(1e8f));
        frames.SetHullMotion(frame, hull);
        return frame;
    }

    /// <summary>The intents of a step in which the owner of each frame of <paramref name="moves"/> wrote its motion intent, relative to the frame's parent.</summary>
    private PhysicsIntent[] Intents(params (FrameHandle Frame, MotionIntent Move)[] moves)
    {
        var intents = new PhysicsIntent[nextOwner];
        foreach ((FrameHandle frame, MotionIntent move) in moves)
            intents[frame.Entity.Index] = new() { Frame = frames.Parent(frame), Move = move };
        return intents;
    }

    /// <summary>
    /// One step: the tree updates, fixes the turns of a step of <see cref="Step"/> seconds that applies
    /// <paramref name="intents"/>, and takes the step records of that step with what <paramref name="owners"/> says.
    /// </summary>
    private void RunStep(PhysicsIntent[] intents, bool lastStepFailed = false, StepOwners? owners = null)
    {
        frames.Update();
        frames.BeginStep(intents, Step, lastStepFailed);
        frames.TakeStepRecords(stepIntents: intents, step: Step, owners: owners);
    }

    /// <summary>The time at the end of step <paramref name="k"/>, the first step being 1, from a start at time 0.</summary>
    private static double End(int k) => k * Step;

    /// <summary>The station's spin sample of the step that ends at <paramref name="stepEnd"/>.</summary>
    private static MotionIntent Spin(double stepEnd, FrameSpin? spin = null) =>
        new((spin ?? StationSpin).Sample(Double3.Zero, stepEnd, Step));

    /// <summary>
    /// The full sample of the step that ends at <paramref name="stepEnd"/> of a frame that spins up about its parent's +Y from
    /// rest at time 0, at <see cref="SpinUpRate"/>: it has turned by half the rate times the square of the time, and turns at
    /// the rate times the time.
    /// </summary>
    private static MotionIntent SpinningUp(double stepEnd) => new(new MotionSample(
        Pose: new(Double3.Zero, DQuaternion.FromAxisAngle(Double3.UnitY, 0.5 * SpinUpRate * stepEnd * stepEnd)),
        ChordVelocity: Double3.Zero, Velocity: Double3.Zero, Acceleration: Double3.Zero, AngularVelocity: new(0, SpinUpRate * stepEnd, 0), AngularAcceleration: new(0, SpinUpRate, 0)));

    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    private static void ExpectNear(FrameTurn actual, FrameTurn expected, double tolerance)
    {
        DQuaternion q = actual.Turn, e = expected.Turn;
        new[] { q.X - e.X, q.Y - e.Y, q.Z - e.Z, q.W - e.W }.Max(Math.Abs).Should()
            .BeLessThan(tolerance, $"the turn {q} is not {e}");
        ExpectNear(actual.AngularVelocity, expected.AngularVelocity, tolerance);
    }

    // ---- V1: the turn of the step in flight --------------------------------------------------------------------

    [Fact]
    public void AFrameThatDoesNotTurnGetsExactlyTheIdentityTurnAndIsNoTurningFrame()
    {
        // A room that holds still, a cart in it on end poses that move it without a turn, and a ship in the room with a
        // dynamic hull that does not turn.
        FrameHandle room = Frame(space, new(new(5, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitX, 0.3)));
        FrameHandle cart = Frame(room, DPose.Identity);
        FrameHandle ship = Frame(room, new(new(0, 2, 0), DQuaternion.FromAxisAngle(Double3.UnitZ, 1.1)),
            BodyMotion.Dynamic);

        for (int k = 1; k <= 3; k++)
            RunStep(Intents((cart, new(new EndPose(new(new(k, 0, 0), DQuaternion.Identity))))));

        foreach (FrameHandle frame in new[] { space, room, cart, ship })
        {
            frames.Turns(frame).Should().Be(NoTurns, "no level of the chain of {0} turns", frame);
            frames.Turns(frame).Turns.Should().BeFalse();
        }
    }

    [Fact]
    public void AStationOnASpinTurnsItsAxesByMinusItsRateTimesTheStepAboutItsPoleAndTurnsAtItsRate()
    {
        FrameHandle station = Frame(space, DPose.Identity);

        for (int k = 1; k <= 3; k++)
            RunStep(Intents((station, Spin(End(k)))));

        var steady = new FrameTurn(DQuaternion.FromAxisAngle(Double3.UnitY, -StationRate * Step),
            new(0, StationRate, 0));
        ExpectNear(frames.StepTurn(station), steady, 1e-15);
        ExpectNear(frames.LastTurn(station), steady, 1e-15);
        frames.Turns(station).Turns.Should().BeTrue();
    }

    [Fact]
    public void AStationInAFacingOrbitGetsTheCrossTermOfItsSpinAndTheOrbitsTurnAsItsAngularAcceleration()
    {
        // The orbit frame turns at n about its own X, the orbit normal, and the station spins at its rate about its own Y,
        // which is at right angles to X. In the station's axes, the part n of its inertial angular velocity turns with the
        // station's spin, so the station's inertial angular velocity changes at n times the station's rate: about 3.4e-4
        // rad/s². Over one step the mean is smaller by (rate dt)² / 24, about 1e-6 of it.
        FrameHandle orbit = Frame(space,
            new(FacingOrbit.StateAt(0, EarthMu).Position, FacingOrbit.RotationAt(0, EarthMu)));
        FrameHandle station = Frame(orbit, DPose.Identity);

        for (int k = 1; k <= 3; k++)
            RunStep(Intents((orbit, new(FacingOrbit.Sample(EarthMu, End(k), Step))), (station, Spin(End(k)))));

        double n = Math.Sqrt(EarthMu / (OrbitRadius * OrbitRadius * OrbitRadius));
        double crossTerm = n * StationRate;
        crossTerm.Should().BeApproximately(3.45e-4, 1e-6);
        frames.Turns(station).AngularAcceleration(Step).Length.Should().BeApproximately(crossTerm, crossTerm * 1e-5);
        frames.StepTurn(station).AngularVelocity.Length.Should()
            .BeApproximately(Math.Sqrt(n * n + StationRate * StationRate), 1e-12);
    }

    [Fact]
    public void AFrameThatHoldsStillInATurningFrameTurnsWithItsParent()
    {
        FrameHandle station = Frame(space, DPose.Identity);
        FrameHandle room = Frame(station, new(new(0, 0, -100), DQuaternion.FromAxisAngle(Double3.UnitX, -Math.PI / 2)));

        for (int k = 1; k <= 3; k++)
            RunStep(Intents((station, Spin(End(k)))));

        // A turn by -90° about X carries +Z to +Y, so the room's +Z is the station's +Y, and the room turns at the station's
        // rate about its own +Z.
        var steady = new FrameTurn(DQuaternion.FromAxisAngle(Double3.UnitZ, -StationRate * Step),
            new(0, 0, StationRate));
        ExpectNear(frames.StepTurn(room), steady, 1e-15);
    }

    [Fact]
    public void AFrameInANumericFrameTurnsWithItsSemanticParent()
    {
        // A ship with a dynamic hull flies at 60 m/s through the station, so the numeric update puts it in a falling frame.
        // The falling frame does not turn and has the station's axes, so the ship, which does not turn in it, turns as the
        // station does.
        FrameHandle station = Frame(space, DPose.Identity);
        FrameHandle ship = Frame(station, new(new(1_000, 0, 0), DQuaternion.Identity), BodyMotion.Dynamic);
        frames.SetTreeLinearVelocity(ship, new(60, 0, 0));
        var hull = new NumericBody
        {
            Entity = ship.Entity, Semantic = station, Home = station, Position = new(1_000, 0, 0),
            HomePosition = new(1_000, 0, 0), Velocity = new(60, 0, 0), Radius = 2, Mass = 1, Character = true,
            OwnedFrame = ship
        };
        frames.UpdateNumericFrames(bodies: [hull], dt: 0.1, step: Step, changes: new(), applyMoves: _ => { });
        frames.IsNumeric(frames.TreeParent(ship)).Should().BeTrue();

        frames.Update();
        frames.BeginStep(Intents((station, Spin(End(1)))), Step, lastStepFailed: false);

        var steady = new FrameTurn(DQuaternion.FromAxisAngle(Double3.UnitY, -StationRate * Step),
            new(0, StationRate, 0));
        ExpectNear(frames.StepTurn(ship), steady, 1e-15);
        frames.Turns(frames.TreeParent(ship)).Should()
            .Be(frames.Turns(station), "a numeric frame reads its semantic frame's turns");
    }

    [Fact]
    public void BeginStepChoosesTheLevelOfAHullByItsMotionInTheStepNotByItsStoredMotion()
    {
        // The stored motion of the ship's hull is dynamic, with a spin in its row, but the intent of the step makes it
        // kinematic and turns it by 0.01 rad about Z to an end pose. The step's turn is the end pose's, not a prediction of
        // the dynamic hull's spin.
        FrameHandle ship = Frame(space, DPose.Identity, BodyMotion.Dynamic);
        frames.SetAngularVelocity(ship, new(0, 0.25f, 0));
        PhysicsIntent[] intents = Intents((ship,
            new(new EndPose(new(Double3.Zero, DQuaternion.FromAxisAngle(Double3.UnitZ, 0.01))))));
        intents[ship.Entity.Index].Motion = BodyMotion.Kinematic;
        intents[ship.Entity.Index].Flags = IntentFlags.SetMotion;

        frames.Update();
        frames.BeginStep(intents, Step, lastStepFailed: false);

        ExpectNear(frames.StepTurn(ship), new(DQuaternion.FromAxisAngle(Double3.UnitZ, -0.01), new(0, 0, 0.01 / Step)),
            1e-13);
    }

    [Fact]
    public void ADynamicHullTurnsItsFrameByTheAngularVelocityOfItsLastRecordPlusItsAngularAccelerationTimesTheStep()
    {
        // Fork F3: the rate of the step in flight is the row's angular velocity plus the record's angular acceleration times
        // the step, 0.25 + 0.125 / 60 rad/s about Y.
        FrameHandle ship = Frame(space, DPose.Identity, BodyMotion.Dynamic);
        var record = new MotionSample(Pose: DPose.Identity, ChordVelocity: Double3.Zero, Velocity: Double3.Zero, Acceleration: Double3.Zero, AngularVelocity: new(0, 0.25, 0),
            AngularAcceleration: new(0, 0.125, 0));
        frames.Update();
        frames.TakeStepRecords(stepIntents: [], step: Step, owners: new StepOwners().WithHullRecord(ship, record));

        frames.Update();
        frames.BeginStep([], Step, lastStepFailed: false);

        double rate = 0.25 + 0.125 * Step;
        ExpectNear(frames.StepTurn(ship), new(DQuaternion.FromAxisAngle(Double3.UnitY, -rate * Step), new(0, rate, 0)),
            1e-15);
    }

    [Fact]
    public void ADynamicHullThatDoesNotTurnLeavesItsFrameWithoutATurn()
    {
        FrameHandle ship = Frame(space, new(Double3.Zero, DQuaternion.FromAxisAngle(new(1, 2, 3), 0.7)),
            BodyMotion.Dynamic);
        frames.Update();
        frames.TakeStepRecords(stepIntents: [], step: Step, owners: new StepOwners().WithHullRecord(ship, new(Pose: frames.TreeLocalPose(ship),
            ChordVelocity: new(1, 0, 0), Velocity: new(1, 0, 0), Acceleration: Double3.Zero, AngularVelocity: Double3.Zero, AngularAcceleration: Double3.Zero)));

        frames.Update();
        frames.BeginStep([], Step, lastStepFailed: false);

        frames.Turns(ship).Should()
            .Be(NoTurns, "a hull that sleeps or does not turn keeps its contents' results to the bit");
    }

    // ---- V2: the turn of the step that ended, and the frames without a last turn --------------------------------

    [Fact]
    public void TheLastTurnOfAFrameIsTheTurnOfTheStepBefore()
    {
        FrameHandle station = Frame(space, DPose.Identity);

        for (int k = 1; k <= 3; k++)
            RunStep(Intents((station, SpinningUp(End(k)))));

        // The frame turns at the rate times the time at the end of each step.
        ExpectNear(frames.StepTurn(station).AngularVelocity, new(0, SpinUpRate * End(3), 0), 1e-15);
        ExpectNear(frames.LastTurn(station).AngularVelocity, new(0, SpinUpRate * End(2), 0), 1e-15);
        ExpectNear(frames.Turns(station).AngularAcceleration(Step), new(0, SpinUpRate, 0), 1e-12);
    }

    [Fact]
    public void ANewFrameReadsNoTurnBeforeItsFirstStepAlsoInTheSlotOfADestroyedFrameThatTurned()
    {
        FrameHandle station = Frame(space, DPose.Identity);
        for (int k = 1; k <= 2; k++)
            RunStep(Intents((station, SpinningUp(End(k)))));
        frames.Turns(station).Turns.Should().BeTrue();
        frames.AngularAcceleration(station).Should().NotBe(Double3.Zero);

        frames.Destroy(station);
        FrameHandle room = Frame(space, DPose.Identity);

        room.Index.Should().Be(station.Index, "the tree reuses the slot of the destroyed frame");
        frames.Turns(room).Should().Be(NoTurns);
        frames.AngularAcceleration(room).Should().Be(Double3.Zero);
    }

    [Fact]
    public void ANewFrameWhoseFirstStepCarriesASpinSampleTurnsSteadilyFromItsFirstStepAndThenLeavesTheList()
    {
        FrameHandle station = Frame(space, DPose.Identity);

        RunStep(Intents((station, SpinningUp(End(1)))));

        frames.StepTurn(station).IsIdentity.Should().BeFalse();
        frames.LastTurn(station).Should().Be(frames.StepTurn(station),
            "a new frame has no last turn, so its first step assumes a steady turn");

        FrameTurn first = frames.StepTurn(station);
        RunStep(Intents((station, SpinningUp(End(2)))));
        frames.LastTurn(station).Should().Be(first, "the frame left the list at its first step");
    }

    [Fact]
    public void ANewFrameWithoutAMotionIntentHoldsStillInItsFirstStepAndSpinsUpInItsSecond()
    {
        FrameHandle station = Frame(space, DPose.Identity);

        RunStep(Intents());
        frames.Turns(station).Should().Be(NoTurns);

        RunStep(Intents((station, Spin(End(2)))));
        frames.LastTurn(station).Should().Be(FrameTurn.None);
        // The station's axes turn at once from rest to its full rate, over the step that ends at End(2): from the rotation of
        // its spawn, the identity, to the spin's rotation there.
        ExpectNear(frames.StepTurn(station),
            new(DQuaternion.FromAxisAngle(Double3.UnitY, -StationRate * End(2)), new(0, StationRate, 0)), 1e-15);
        ExpectNear(frames.Turns(station).AngularAcceleration(Step), new(0, StationRate / Step, 0), 1e-12);
    }

    [Fact]
    public void ATeleportThatTurnsAFrameInATurningParentGivesItAndTheFramesBelowItNoFalseAngularAcceleration()
    {
        // The station spins in a facing orbit, and a room holds still in it. In the fourth step the station teleports by a
        // quarter turn about its pole, on a spin a quarter turn ahead. Its axes jump against inertial space, so the turn of
        // the step before is in axes that no longer exist: read against it, the part of the orbit's turn in the station's
        // angular velocity would jump by a quarter turn, about 0.09 rad/s² over the step.
        FrameHandle orbit = Frame(space,
            new(FacingOrbit.StateAt(0, EarthMu).Position, FacingOrbit.RotationAt(0, EarthMu)));
        FrameHandle station = Frame(orbit, DPose.Identity);
        FrameHandle room = Frame(station, new(new(0, 0, -100), DQuaternion.Identity));
        for (int k = 1; k <= 3; k++)
            RunStep(Intents((orbit, new(FacingOrbit.Sample(EarthMu, End(k), Step))), (station, Spin(End(k)))));

        FrameSpin ahead = StationSpin with { PhaseAtEpoch = Math.PI / 2 };
        PhysicsIntent[] intents = Intents((orbit, new(FacingOrbit.Sample(EarthMu, End(4), Step))),
            (station, Spin(End(4), ahead)));
        intents[station.Entity.Index].Flags = IntentFlags.Teleport;
        intents[station.Entity.Index].TeleportPose = new(Double3.Zero, ahead.RotationAt(End(3)));
        RunStep(intents);

        frames.Turns(station).AngularAcceleration(Step).Should()
            .Be(Double3.Zero, "the station takes its new turn as its last turn");
        frames.Turns(room).AngularAcceleration(Step).Should()
            .Be(Double3.Zero, "the room's axes jumped with the station's");
        frames.Turns(station).Turns.Should().BeTrue();
        ExpectNear(frames.StepTurn(station).AngularVelocity, frames.StepTurn(room).AngularVelocity, 1e-15);
    }

    [Fact]
    public void AFrameThatTeleportsStartsItsTurnAtTheTeleport()
    {
        // The station teleports by a quarter turn about its pole, on a spin a quarter turn ahead, so over the step it turns
        // only by its rate times the step from the teleport, not by the quarter turn of the jump.
        FrameHandle station = Frame(space, DPose.Identity);
        RunStep(Intents((station, Spin(End(1)))));

        FrameSpin ahead = StationSpin with { PhaseAtEpoch = Math.PI / 2 };
        PhysicsIntent[] intents = Intents((station, Spin(End(2), ahead)));
        intents[station.Entity.Index].Flags = IntentFlags.Teleport;
        intents[station.Entity.Index].TeleportPose = new(Double3.Zero, ahead.RotationAt(End(1)));
        RunStep(intents);

        var steady = new FrameTurn(DQuaternion.FromAxisAngle(Double3.UnitY, -StationRate * Step),
            new(0, StationRate, 0));
        ExpectNear(frames.StepTurn(station), steady, 1e-15);
    }

    [Fact]
    public void AFrameWithoutAHullOrAMotionIntentTakesNoTeleportAndKeepsItsLastTurn()
    {
        // Such a frame takes a teleport only as the start of its motion intent's record, so without one it holds still where
        // it stands, turning with the station that spins up around it.
        FrameHandle station = Frame(space, DPose.Identity);
        FrameHandle room = Frame(station, new(new(0, 0, -100), DQuaternion.Identity));
        for (int k = 1; k <= 3; k++)
            RunStep(Intents((station, SpinningUp(End(k)))));

        PhysicsIntent[] intents = Intents((station, SpinningUp(End(4))));
        intents[room.Entity.Index].Frame = station;
        intents[room.Entity.Index].Flags = IntentFlags.Teleport;
        intents[room.Entity.Index].TeleportPose = new(new(0, 0, -100), DQuaternion.FromAxisAngle(Double3.UnitX, 1));
        RunStep(intents);

        ExpectNear(frames.Turns(room).AngularAcceleration(Step), new(0, SpinUpRate, 0), 1e-12);
    }

    [Fact]
    public void ATeleportThatDoesNotTurnAFrameKeepsItsLastTurn()
    {
        FrameHandle station = Frame(space, DPose.Identity);
        for (int k = 1; k <= 3; k++)
            RunStep(Intents((station, SpinningUp(End(k)))));

        PhysicsIntent[] intents = Intents((station, SpinningUp(End(4))));
        intents[station.Entity.Index].Flags = IntentFlags.Teleport;
        intents[station.Entity.Index].TeleportPose = new(new(0, 7, 0), frames.TreeLocalPose(station).Rotation);
        RunStep(intents);

        ExpectNear(frames.Turns(station).AngularAcceleration(Step), new(0, SpinUpRate, 0), 1e-12);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AWriteOfTheOwnersTransformThatTurnsAFrameGivesItsNextStepNoLastTurn(bool turns)
    {
        FrameHandle station = Frame(space, DPose.Identity);
        for (int k = 1; k <= 2; k++)
            RunStep(Intents((station, SpinningUp(End(k)))));

        var owners = new StepOwners();
        DQuaternion rotation = DQuaternion.FromAxisAngle(Double3.UnitY, 0.5 * SpinUpRate * End(3) * End(3));
        owners.Writes[station] = new(new(0, 3, 0),
            turns ? DQuaternion.FromAxisAngle(Double3.UnitX, 0.2) * rotation : rotation);
        RunStep(Intents((station, SpinningUp(End(3)))), owners: owners);
        RunStep(Intents((station, SpinningUp(End(4)))));

        if (turns)
            frames.LastTurn(station).Should().Be(frames.StepTurn(station),
                "the write turned the station's axes against inertial space");
        else
            ExpectNear(frames.Turns(station).AngularAcceleration(Step), new(0, SpinUpRate, 0), 1e-12);
    }

    [Fact]
    public void AfterAFailedStepEveryFrameTakesItsNewTurnAsItsLastTurn()
    {
        FrameHandle station = Frame(space, DPose.Identity);
        FrameHandle room = Frame(station, new(new(0, 0, -100), DQuaternion.Identity));
        for (int k = 1; k <= 3; k++)
            RunStep(Intents((station, SpinningUp(End(k)))));

        RunStep(Intents((station, SpinningUp(End(4)))), lastStepFailed: true);

        frames.LastTurn(station).Should().Be(frames.StepTurn(station));
        frames.LastTurn(room).Should().Be(frames.StepTurn(room));

        RunStep(Intents((station, SpinningUp(End(5)))));
        ExpectNear(frames.Turns(station).AngularAcceleration(Step), new(0, SpinUpRate, 0), 1e-12);
    }

    // ---- V3: the angular acceleration of a frame's last record -----------------------------------------------

    [Fact]
    public void AFrameTakesTheAngularAccelerationOfItsStepRecordAndNoneWhenItHoldsStill()
    {
        FrameHandle station = Frame(space, DPose.Identity);
        FrameHandle ship = Frame(space, DPose.Identity, BodyMotion.Dynamic);
        var hullRecord = new MotionSample(Pose: DPose.Identity, ChordVelocity: Double3.Zero, Velocity: Double3.Zero, Acceleration: Double3.Zero, AngularVelocity: new(0, 0.25, 0),
            AngularAcceleration: new(0.5, 0, 0));

        RunStep(Intents((station, SpinningUp(End(1)))), owners: new StepOwners().WithHullRecord(ship, hullRecord));

        frames.AngularAcceleration(station).Should().Be(new Double3(0, SpinUpRate, 0));
        frames.AngularAcceleration(ship).Should().Be(new Double3(0.5, 0, 0));

        RunStep(Intents());

        frames.AngularAcceleration(station).Should().Be(Double3.Zero, "a frame without a record holds still");
        frames.AngularAcceleration(ship).Should().Be(Double3.Zero);
    }

    [Fact]
    public void AFrameWhoseUpdateTheStepSkippedKeepsItsAngularAcceleration()
    {
        FrameHandle ship = Frame(space, DPose.Identity, BodyMotion.Dynamic);
        var hullRecord = new MotionSample(Pose: DPose.Identity, ChordVelocity: Double3.Zero, Velocity: Double3.Zero, Acceleration: Double3.Zero, AngularVelocity: new(0, 0.25, 0),
            AngularAcceleration: new(0.5, 0, 0));
        RunStep(Intents(), owners: new StepOwners().WithHullRecord(ship, hullRecord));

        frames.Update();
        frames.TakeStepRecords(stepIntents: [], step: Step, stepFailed: true);

        frames.AngularAcceleration(ship).Should().Be(new Double3(0.5, 0, 0));
    }

    [Fact]
    public void AMoveToAnotherParentConvertsTheAngularAccelerationOfAFrameByTheRuleOfARelativeMotion()
    {
        // The station turns at 1 rad/s about Z and spins up at 0.5 rad/s² about Z, in the axes of space. A room in it turns at
        // 1 rad/s about X and spins up at 2 rad/s² about Y relative to the station. In space, the room spins up at
        // R α_b + α + ω × (R ω_b) = (0, 2, 0) + (0, 0, 0.5) + (0, 0, 1) × (1, 0, 0) = (0, 3, 0.5) rad/s².
        FrameHandle station = Frame(space, DPose.Identity);
        FrameHandle room = Frame(station, new(new(10, 0, 0), DQuaternion.Identity));
        PhysicsIntent[] intents = Intents(
            (station,
                new(new MotionSample(Pose: DPose.Identity, ChordVelocity: Double3.Zero, Velocity: Double3.Zero, Acceleration: Double3.Zero, AngularVelocity: new(0, 0, 1),
                    AngularAcceleration: new(0, 0, 0.5)))),
            (room, new(new MotionSample(Pose: new(new(10, 0, 0), DQuaternion.Identity), ChordVelocity: Double3.Zero, Velocity: Double3.Zero,
                Acceleration: Double3.Zero,
                AngularVelocity: new(1, 0, 0), AngularAcceleration: new(0, 2, 0)))));
        RunStep(intents);

        frames.Reparent(room, space);

        ExpectNear(frames.AngularAcceleration(room), new(0, 3, 0.5), 1e-12);
    }

    [Fact]
    public void AMoveToASiblingFrameConvertsTheAngularAccelerationDownTheOtherSideToo()
    {
        // The station and the room are as in the move to space above, and the ring, a sibling of the station, is turned by a
        // quarter turn about Z, turns at 2 rad/s about Y and spins up at 1 rad/s² about X, in the axes of space. In space the
        // room turns at (1, 0, 1) rad/s and spins up at (0, 3, 0.5) rad/s². In the ring's axes it spins up at
        // R⁻¹ (α' − α − ω × Ω') = R⁻¹ ((0, 3, 0.5) − (1, 0, 0) − (0, 2, 0) × (1, 0, 1)) = R⁻¹ (−3, 3, 2.5), and R⁻¹ carries
        // (x, y, z) to (y, −x, z): (3, 3, 2.5) rad/s².
        FrameHandle station = Frame(space, DPose.Identity);
        FrameHandle room = Frame(station, new(new(10, 0, 0), DQuaternion.Identity));
        DQuaternion quarter = DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2);
        FrameHandle ring = Frame(space, new(new(0, 50, 0), quarter));
        PhysicsIntent[] intents = Intents(
            (station,
                new(new MotionSample(Pose: DPose.Identity, ChordVelocity: Double3.Zero, Velocity: Double3.Zero, Acceleration: Double3.Zero, AngularVelocity: new(0, 0, 1),
                    AngularAcceleration: new(0, 0, 0.5)))),
            (room, new(new MotionSample(Pose: new(new(10, 0, 0), DQuaternion.Identity), ChordVelocity: Double3.Zero, Velocity: Double3.Zero,
                Acceleration: Double3.Zero,
                AngularVelocity: new(1, 0, 0), AngularAcceleration: new(0, 2, 0)))),
            (ring, new(new MotionSample(Pose: new(new(0, 50, 0), quarter), ChordVelocity: Double3.Zero, Velocity: Double3.Zero, Acceleration: Double3.Zero,
                AngularVelocity: new(0, 2, 0), AngularAcceleration: new(1, 0, 0)))));
        RunStep(intents);

        frames.Reparent(room, ring);

        ExpectNear(frames.AngularAcceleration(room), new(3, 3, 2.5), 1e-12);
    }

    [Fact]
    public void AMoveUpTwoLevelsComposesTheAngularAccelerationsOfBothLevels()
    {
        // The ring is turned by a quarter turn about Z in space, turns at 2 rad/s about Y and spins up at 1 rad/s² about X, in
        // the axes of space. The station rests in the ring's axes, turns at 1 rad/s about Z and spins up at 0.5 rad/s² about Z,
        // in the ring's axes, and the room turns at 1 rad/s about X and spins up at 2 rad/s² about Y in the station's. The ring
        // carries (x, y, z) to (−y, x, z). In the ring's axes the room spins up at ω_s × ω_b + α_b + α_s = (0, 3, 0.5) and
        // turns at (1, 0, 1). In space that is the ring's turn of (−3, 0, 0.5), plus ω_r × (0, 1, 1) = (2, 0, 0) for the
        // ring's spin, plus the ring's own (1, 0, 0): (0, 0, 0.5) rad/s².
        DQuaternion quarter = DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2);
        FrameHandle ring = Frame(space, new(new(0, 50, 0), quarter));
        FrameHandle station = Frame(ring, DPose.Identity);
        FrameHandle room = Frame(station, new(new(10, 0, 0), DQuaternion.Identity));
        PhysicsIntent[] intents = Intents(
            (ring, new(new MotionSample(Pose: new(new(0, 50, 0), quarter), ChordVelocity: Double3.Zero, Velocity: Double3.Zero, Acceleration: Double3.Zero,
                AngularVelocity: new(0, 2, 0), AngularAcceleration: new(1, 0, 0)))),
            (station,
                new(new MotionSample(Pose: DPose.Identity, ChordVelocity: Double3.Zero, Velocity: Double3.Zero, Acceleration: Double3.Zero, AngularVelocity: new(0, 0, 1),
                    AngularAcceleration: new(0, 0, 0.5)))),
            (room, new(new MotionSample(Pose: new(new(10, 0, 0), DQuaternion.Identity), ChordVelocity: Double3.Zero, Velocity: Double3.Zero,
                Acceleration: Double3.Zero,
                AngularVelocity: new(1, 0, 0), AngularAcceleration: new(0, 2, 0)))));
        RunStep(intents);

        frames.Reparent(room, space);

        ExpectNear(frames.AngularAcceleration(room), new(0, 0, 0.5), 1e-12);
    }

    // ---- The origin of a falling frame -----------------------------------------------------------------------

    [Fact]
    public void TheOriginOfAFallingFrameInATurningFrameKeepsToTheInertialLineOfAFreeBodyAtItsPlace()
    {
        // A point at rest in inertial space 10 km from the station's axis, at (10 km, 0, 0) in the station's axes now. The
        // station turns by w = rate times the step in each step, so in its axes the point was at 10 km (cos w, 0, −sin w) a
        // step ago, and is at 10 km (cos w, 0, sin w) a step from now. It moves at 3.1 km/s against the station, so a body
        // there gets a falling frame, whose velocity is the chord of the step that ended.
        FrameHandle station = Frame(space, DPose.Identity);
        double w = StationRate * Step, r = 10_000;
        var place = new Double3(r, 0, 0);
        Double3 chord = (place - new Double3(r * Math.Cos(w), 0, -r * Math.Sin(w))) / Step;
        FrameHandle pod = FallingFrame(station, place, chord);

        frames.Update();
        frames.BeginStep(Intents((station, Spin(End(1)))), Step, lastStepFailed: false);
        frames.AdvanceNumericFrames(Step);

        ExpectNear(frames.TreeLocalPose(pod).Position, new(r * Math.Cos(w), 0, r * Math.Sin(w)), 1e-6);
    }

    [Fact]
    public void TheOriginOfAFallingFrameInATurningFrameWithAFieldTakesTheFieldTurnedByTheTurnOfTheStep()
    {
        // The point of the test above, in a station whose field is g along its +Z. Jolt's scheme puts the end of the step
        // at x_(n+1) = 2 R_n x_n − R_n R_(n−1) x_(n−1) + R_n g dt²: the field acts at the start of the step, in the axes then,
        // and R_n carries it into the axes at the end. A point at rest in inertial space a step ago, at x_(n−1) = R⁻¹ x_n,
        // ends at R_n (x_n + g dt²). The field left in the axes at the start would end it at R_n x_n + g dt², off by
        // |g| w dt² = 1.4e-5 m.
        FrameHandle station = Frame(space, DPose.Identity);
        var g = new Vector3(0, 0, 10);
        frames.SetGravity(station, GravityField.Uniform(g));
        double w = StationRate * Step, r = 10_000;
        var place = new Double3(r, 0, 0);
        Double3 chord = (place - new Double3(r * Math.Cos(w), 0, -r * Math.Sin(w))) / Step;
        FrameHandle pod = FallingFrame(station, place, chord);

        frames.Update();
        frames.BeginStep(Intents((station, Spin(End(1)))), Step, lastStepFailed: false);
        frames.AdvanceNumericFrames(Step);

        DQuaternion turn = DQuaternion.FromAxisAngle(Double3.UnitY, -w);
        ExpectNear(frames.TreeLocalPose(pod).Position, turn.Rotate(place + (Double3)g * (Step * Step)), 1e-9);
    }

    [Fact]
    public void AFallingFrameInATurningFrameHasNoPushSoItsSleepingMembersStayAsleep()
    {
        // The point of the tests above, in the station with a field. Its origin falls as a free body there falls, with the
        // turned field and the rotation terms of its place and chord, so an accelerometer at the origin reads nothing. The push
        // is what wakes the members of a frame (docs/plans/spin-gravity.md, "The wake"): left with the rotation terms at the
        // origin, the centrifugal and the Coriolis term of a point at rest in inertial space, ω² r = 981 m/s² here, it would
        // change as the frame moves, and wake its sleeping members in every step.
        FrameHandle station = Frame(space, DPose.Identity);
        frames.SetGravity(station, GravityField.Uniform(new(0, 0, 10)));
        double w = StationRate * Step, r = 10_000;
        var place = new Double3(r, 0, 0);
        Double3 chord = (place - new Double3(r * Math.Cos(w), 0, -r * Math.Sin(w))) / Step;
        FrameHandle pod = FallingFrame(station, place, chord);

        frames.Update();
        frames.BeginStep(Intents((station, Spin(End(1)))), Step, lastStepFailed: false);

        frames.PushInStep(pod).Length().Should().BeLessThan(1e-6f);
    }

    [Fact]
    public void AFollowingFrameWithoutAMotionIntentInATurningFrameHoldsStillInItsTurningAxesWithNoTurnTerm()
    {
        // A plain kinematic ball 10,000 km from the station's axis anchors a following frame, which starts with the chord of
        // a free fall there. Its anchor has no motion intent, so its step plan is the record of a kinematic body that holds
        // still where the step starts (ticket ter-zsku): no velocity in the station's coordinates, which is rest in its
        // turning axes. The plan is motion in the semantic frame's coordinates already, so the frame takes the landing
        // acceleration of every plan and none of the rotation terms that a falling frame's origin takes (see
        // FreeAccelerationAtOrigin), and it holds still in the station.
        FrameHandle station = Frame(space, DPose.Identity);
        var place = new Double3(1e7, 0, 0);
        FrameHandle home = FrameHandle.Null;
        var ball = new NumericBody
        {
            Entity = new(100, 1), Semantic = station, Home = station, Position = place, HomePosition = place,
            Velocity = Double3.Zero, Radius = 0.5, Mass = 1, Kinematic = true
        };
        frames.UpdateNumericFrames(bodies: [ball], dt: Step, step: Step, changes: new(), applyMoves: moves => home = moves.Single().To);
        frames.Anchors(home, ball.Entity).Should().BeTrue("a kinematic body 10,000 km out anchors a following frame");

        for (int k = 1; k <= 3; k++)
        {
            Double3 velocity = frames.LinearVelocity(home);
            frames.Update();
            frames.BeginStep(Intents((station, Spin(End(k)))), Step, lastStepFailed: false);
            frames.Turns(station).Turns.Should().BeTrue();
            frames.HomeAcceleration(home, station).Should()
                .Be(-velocity / Step, "the plan has no chord, so the frame stops");
            frames.HomeInstantVelocityOverChord(home, station, Step).Should()
                .Be(Double3.Zero, "the plan has no velocity past its chord");
            frames.HomeInstantVelocityOverChordAtStepStart(home, station, Step).Should()
                .Be(-velocity / Step * (0.5 * Step));
            frames.AdvanceNumericFrames(Step);
            frames.PlaceFollowingFrame(home);
            frames.TreeLocalPose(home).Position.Should()
                .Be(place, "the frame takes the plan's place, where its anchor stands");
            frames.LinearVelocity(home).Should().Be(Double3.Zero, "and the plan's chord");
            frames.TakeStepRecords(stepIntents: Intents((station, Spin(End(k)))), step: Step);
        }
    }

    /// <summary>The falling numeric frame that a numeric update gives one body of <paramref name="semantic"/> at
    /// <paramref name="position"/>.</summary>
    private FrameHandle FallingFrame(FrameHandle semantic, Double3 position, Double3 velocity)
    {
        FrameHandle home = FrameHandle.Null;
        var body = new NumericBody
        {
            Entity = new(100, 1), Semantic = semantic, Home = semantic, Position = position, HomePosition = position,
            Velocity = velocity, Radius = 1, Mass = 1
        };
        frames.UpdateNumericFrames(bodies: [body], dt: 0.1, step: 0.1, changes: new(), applyMoves: moves => home = moves.Single().To);
        frames.Numeric(home).Should().Be(NumericMode.Falling);
        frames.TreeLocalPose(home).Position.Should().Be(position);
        frames.TreeLinearVelocity(home).Should().Be(velocity);
        return home;
    }
}
