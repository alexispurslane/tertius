using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// The rule of a loose body in a frame (docs/plans/particles.md, "The rule of motion"): <see cref="LooseBodyStep"/>, which
/// <see cref="FrameTree.LastLooseBodyStep"/> fills from the frame tree after step 2. A body that the medium does not lift
/// takes the expression of physics for a free body with no numeric home, to the bit. A body that the medium lifts by its
/// lift share feels the push, the linear gravity less the lift share times the effective gravity, and the drag keeps
/// 1 / (1 + drag · dt) of its chord.
/// <para>
/// The station of the tests holds a uniform field of 2 m/s² along its −Y and spins about its own +Y at 0.31321 rad/s, the
/// rate that gives 9.81 m/s² at a radius of 100 m, as in <c>FrameTreeEffectiveGravityTests</c>. Each tree step runs as the
/// World runs it: the tree updates, fixes the turns of the step, and takes its step records, which is step 2.
/// </para>
/// </summary>
public sealed class LooseBodyStepTests
{
    private const double Step = 1.0 / 60;
    private const double StationRate = 0.31321;
    private const double SpinUpRate = 0.5;
    private const float G = 9.81f;

    private static readonly FrameSpin StationSpin = new(Tilt: DQuaternion.Identity, Rate: StationRate);
    private static readonly GravityField StationField = GravityField.Uniform(new(0, -2, 0));
    private static readonly MediumField Air = MediumField.Uniform(1.225f, 2);
    private static readonly Double3 Rim = new(100, 0, 0);
    private static readonly Double3[] Places = [Rim, new(3, -7, 40), new(-55.5, 12.25, -0.125)];

    private readonly FrameTree frames = new();
    private readonly FrameHandle space;
    private uint nextOwner = 2;

    public LooseBodyStepTests()
    {
        space = frames.Create(
            frameOwner: new(1, 1),
            parent: FrameHandle.Null,
            localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(1e9f)
        );
    }

    private FrameHandle Frame(GravityField field, MediumField medium = default) =>
        frames.Create(
            frameOwner: new(nextOwner++, 1),
            parent: space,
            localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(1e5f),
            field: field,
            medium: medium
        );

    private PhysicsIntent[] Intents(FrameHandle frame, MotionIntent? move)
    {
        var intents = new PhysicsIntent[nextOwner];
        if (move is { } intent)
            intents[frame.Entity.Index] = new() { Frame = frames.Parent(frame), Move = intent };
        return intents;
    }

    /// <summary>The first half of a tree step, up to the physics step: the tree updates and fixes the turns of the step.</summary>
    private void BeginStep(PhysicsIntent[] intents)
    {
        frames.Update();
        frames.BeginStep(intents, Step, lastStepFailed: false);
    }

    /// <summary>The second half of a tree step: step 2 of the sync point takes the step records.</summary>
    private void EndStep(PhysicsIntent[] intents) =>
        frames.TakeStepRecords(stepIntents: intents, step: Step, owners: null);

    private void RunStep(PhysicsIntent[] intents)
    {
        BeginStep(intents);
        EndStep(intents);
    }

    private static double End(int k) => k * Step;

    private static MotionIntent Spin(double stepEnd) => new(StationSpin.Sample(Double3.Zero, stepEnd));

    private static MotionIntent SpinningUp(double stepEnd) =>
        new(
            new MotionSample(
                Pose: new(Double3.Zero, DQuaternion.FromAxisAngle(Double3.UnitY, 0.5 * SpinUpRate * stepEnd * stepEnd)),
                ChordVelocity: Double3.Zero,
                Velocity: Double3.Zero,
                Acceleration: Double3.Zero,
                AngularVelocity: new(0, SpinUpRate * stepEnd, 0),
                AngularAcceleration: new(0, SpinUpRate, 0)
            )
        );

    /// <summary>A station that has spun steadily for three steps.</summary>
    private FrameHandle SpinningStation(GravityField field, MediumField medium = default)
    {
        FrameHandle station = Frame(field, medium);
        for (int k = 1; k <= 3; k++)
            RunStep(Intents(station, Spin(End(k))));
        return station;
    }

    /// <summary>A frame that holds still in <see cref="space"/>, after three steps.</summary>
    private FrameHandle StillFrame(GravityField field, MediumField medium = default)
    {
        FrameHandle frame = Frame(field, medium);
        for (int k = 1; k <= 3; k++)
            RunStep(Intents(frame, null));
        return frame;
    }

    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    [Fact]
    public void TheLastLooseBodyStepCarriesTheFieldTheTurnsTheirStepAndTheMediumOfItsFrame()
    {
        FrameHandle station = SpinningStation(StationField, Air);

        LooseBodyStep step = frames.LastLooseBodyStep(station);

        step.Field.Should().Be(StationField);
        step.Medium.Should().Be(Air);
        step.Turns.Should().Be(frames.Turns(station));
        step.Turns.Turns.Should().BeTrue();
        step.TurnsStep.Should().Be(Step);
    }

    [Fact]
    public void TheEffectiveGravityOfTheLastStepEqualsTheFrameTreesToTheBit()
    {
        // A station that spins up, so that the Euler term is not zero, beside one that spins steadily and one that holds still.
        FrameHandle spinningUp = Frame(StationField);
        FrameHandle steady = Frame(StationField);
        FrameHandle still = Frame(GravityField.Point(4e14));
        for (int k = 1; k <= 3; k++)
        {
            var intents = new PhysicsIntent[nextOwner];
            intents[spinningUp.Entity.Index] = new() { Frame = space, Move = SpinningUp(End(k)) };
            intents[steady.Entity.Index] = new() { Frame = space, Move = Spin(End(k)) };
            RunStep(intents);
        }

        foreach (FrameHandle frame in new[] { spinningUp, steady, still })
        {
            LooseBodyStep step = frames.LastLooseBodyStep(frame);
            foreach (Double3 place in Places)
                step.EffectiveGravity(place, step.LinearGravity(place))
                    .Should()
                    .Be(frames.EffectiveGravity(frame, place));
        }

        frames
            .Turns(spinningUp)
            .AngularAcceleration(Step)
            .Should()
            .NotBe(Double3.Zero, "the spin-up gives an Euler term");
    }

    [Fact]
    public void InVacuumTheChordAheadIsTheFreeBodyUpdateOfPhysicsToTheBit()
    {
        FrameHandle station = Frame(StationField);
        for (int k = 1; k <= 2; k++)
            RunStep(Intents(station, SpinningUp(End(k))));
        PhysicsIntent[] intents = Intents(station, SpinningUp(End(3)));
        BeginStep(intents);
        // What physics reads in the step in flight, for each place.
        Double3[] linears = [.. Places.Select(place => frames.LinearGravity(station, place))];
        FrameTurns turns = frames.Turns(station);
        EndStep(intents);
        var behind = new Double3(3.3, -1.7, 0.9);

        LooseBodyStep step = frames.LastLooseBodyStep(station);

        for (int i = 0; i < Places.Length; i++)
        {
            // The expression of ApplyTurningUpdate (src/Tertius.Physics/JoltPhysicsBackend.Step.cs), for a body whose home is
            // its semantic frame: the home's acceleration and velocity are zero.
            Double3 homeAcceleration = Double3.Zero,
                homeVelocity = Double3.Zero;
            Double3 field = linears[i] + homeAcceleration;
            Double3 physics =
                behind
                + turns.FreeChange(Places[i], behind, Step)
                + (turns.Step.Turn.Rotate(field) - homeAcceleration) * Step
                - homeVelocity;

            step.ChordAhead(place: Places[i], chord: behind, lift: 0, drag: 0, dt: Step).Should().Be(physics);
        }
    }

    [Fact]
    public void AFrameThatDoesNotTurnSkipsTheTwoTurnUpdateToTheBit()
    {
        FrameHandle frame = StillFrame(GravityField.Uniform(new(0.25f, -G, 1.5f)));
        LooseBodyStep step = frames.LastLooseBodyStep(frame);
        step.Turns.Turns.Should().BeFalse();
        var chord = new Double3(3.3, -1.7, 0.9);

        foreach (Double3 place in Places)
        {
            // Physics adds the gravity as a force in a frame that does not turn: Jolt adds the linear gravity times the step.
            Double3 linear = frames.LinearGravity(frame, place);

            step.ChordAhead(place: place, chord: chord, lift: 0, drag: 0, dt: Step).Should().Be(chord + linear * Step);
        }
    }

    [Fact]
    public void TheLooseBodyStepTakesTheProperAccelerationThatTheStepFoundNotTheOneThatTheStepLeaves()
    {
        FrameHandle frame = StillFrame(StationField);
        var thrust = new Double3(0, 1.5, 0);
        var accelerating = new MotionIntent(
            new MotionSample(
                Pose: DPose.Identity,
                ChordVelocity: Double3.Zero,
                Velocity: Double3.Zero,
                Acceleration: thrust,
                AngularVelocity: Double3.Zero,
                AngularAcceleration: Double3.Zero
            )
        );

        RunStep(Intents(frame, accelerating));

        ((Double3)frames.ProperAcceleration(frame))
            .Should()
            .Be(thrust, "step 2 stored the proper acceleration of the step that ended");
        LooseBodyStep found = frames.LastLooseBodyStep(frame);
        found.ProperAcceleration.Should().Be(Double3.Zero, "the step that ended found the frame at rest");
        found.LinearGravity(Rim).Should().Be(StationField.AtPrecise(Rim));

        RunStep(Intents(frame, accelerating));

        LooseBodyStep next = frames.LastLooseBodyStep(frame);
        next.ProperAcceleration.Should().Be(thrust);
        next.LinearGravity(Rim).Should().Be(StationField.AtPrecise(Rim) - thrust);
    }

    [Fact]
    public void ANeutralBodyAtRestInATurningMediumStaysAtRest()
    {
        FrameHandle station = SpinningStation(StationField, Air);
        LooseBodyStep step = frames.LastLooseBodyStep(station);

        (Double3 neutralPlace, Double3 neutralChord) = Fly(step: step, start: Rim, lift: 1, steps: 600);
        (Double3 freePlace, _) = Fly(step: step, start: Rim, lift: 0, steps: 600);

        // A neutral body is the medium itself: its push takes back the rotation terms, so only the Coriolis term of the two-turn
        // update acts on it, and only on its motion, which the drag holds at a rounding error of the update.
        Double3.Distance(neutralPlace, Rim).Should().BeLessThan(1e-3, "the neutral body stays where it started");
        neutralChord.Length.Should().BeLessThan(1e-4);
        Double3
            .Distance(freePlace, Rim)
            .Should()
            .BeGreaterThan(100, "a body that the medium does not hold flies off on its inertial line");
    }

    /// <summary>
    /// The place and the chord of a body that starts at rest at <paramref name="start"/> after <paramref name="steps"/> steps
    /// of <paramref name="step"/>, a frame that turns steadily, with the lift share <paramref name="lift"/> and the drag of the
    /// medium times the lift share.
    /// </summary>
    private static (Double3 Place, Double3 Chord) Fly(in LooseBodyStep step, Double3 start, double lift, int steps)
    {
        Double3 place = start,
            chord = Double3.Zero;
        for (int k = 0; k < steps; k++)
        {
            chord = step.ChordAhead(place: place, chord: chord, lift: lift, drag: step.Medium.Drag * lift, dt: Step);
            place += chord * Step;
        }

        return (place, chord);
    }

    [Fact]
    public void ALightBodyRisesAgainstTheEffectiveGravityAndAHeavyOneSinks()
    {
        // A frame that does not turn: the push is g (1 − β).
        FrameHandle ground = StillFrame(GravityField.Uniform(new(0, -G, 0)), Air);
        LooseBodyStep still = frames.LastLooseBodyStep(ground);
        ExpectNear(
            still.ChordAhead(place: Rim, chord: Double3.Zero, lift: 3, drag: 0, dt: Step),
            new(0, 2 * G * Step, 0),
            1e-9
        );
        ExpectNear(
            still.ChordAhead(place: Rim, chord: Double3.Zero, lift: 0.75, drag: 0, dt: Step),
            new(0, -0.25 * G * Step, 0),
            1e-9
        );

        // A station without a field: the effective gravity is the centrifugal term, outward along +X at the rim. A light body
        // moves toward the axis, and a heavy one moves outward, slower than a free body. The push acts in the axes at the
        // start of the step, so the turn of the step carries each chord into the axes at its end.
        FrameHandle station = SpinningStation(GravityField.None, Air);
        LooseBodyStep spinning = frames.LastLooseBodyStep(station);
        DQuaternion turn = spinning.Turns.Step.Turn;
        double outward = StationRate * StationRate * Rim.X * Step;
        ExpectNear(
            spinning.ChordAhead(place: Rim, chord: Double3.Zero, lift: 3, drag: 0, dt: Step),
            turn.Rotate(new Double3(-2 * outward, 0, 0)),
            1e-6
        );
        ExpectNear(
            spinning.ChordAhead(place: Rim, chord: Double3.Zero, lift: 0.75, drag: 0, dt: Step),
            turn.Rotate(new Double3(0.25 * outward, 0, 0)),
            1e-6
        );
        ExpectNear(
            spinning.ChordAhead(place: Rim, chord: Double3.Zero, lift: 0, drag: 0, dt: Step),
            turn.Rotate(new Double3(outward, 0, 0)),
            1e-6
        );
    }

    [Fact]
    public void TheDragKeepsOneOverOnePlusTheDragTimesTheStepOfTheChord()
    {
        FrameHandle frame = StillFrame(GravityField.None, Air);
        LooseBodyStep step = frames.LastLooseBodyStep(frame);
        var chord = new Double3(10, -4, 0.5);
        const double drag = 6;

        step.ChordAhead(place: Rim, chord: chord, lift: 1, drag: drag, dt: Step).Should().Be(chord / (1 + drag * Step));
        step.ChordAhead(place: Rim, chord: chord, lift: 1, drag: 0, dt: Step)
            .Should()
            .Be(chord, "no drag keeps the chord exactly");
    }

    [Fact]
    public void TheLastLooseBodyStepOfAStaleFrameThrows()
    {
        FrameHandle frame = StillFrame(StationField);
        frames.Destroy(frame);

        frames.Invoking(tree => tree.LastLooseBodyStep(frame)).Should().Throw<InvalidOperationException>();
    }
}
