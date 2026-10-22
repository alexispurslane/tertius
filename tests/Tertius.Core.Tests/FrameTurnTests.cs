using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The turn of a frame's axes over one step, and the two-turn update that keeps a free body on its inertial line in those
/// axes (docs/plans/spin-gravity.md, "The two-turn update", "The angular form" and "The chord rule and the two conversion
/// rules"). The tests with literal results use a frame that turns by a quarter turn about +Z in each step of 1 s, so that
/// every place and chord is a small vector that can be checked by hand. The axes turn by +90° about +Z, so the coordinates
/// of a direction that is fixed in inertial space turn by −90°: (x, y, z) becomes (y, −x, z).
/// </summary>
public class FrameTurnTests
{
    private const double QuarterTurn = Math.PI / 2;

    /// <summary>The step of the tests that run many steps, 60 Hz, in s.</summary>
    private const double Step = 1.0 / 60;

    /// <summary>One step of 1 s of a frame that turns steadily by a quarter turn per second about +Z.</summary>
    private static readonly FrameTurn Quarter = new(DQuaternion.FromAxisAngle(Double3.UnitZ, -QuarterTurn),
        new(0, 0, QuarterTurn));

    // ---- The angular form -------------------------------------------------------------------------------------

    // A body that turns at pi/2 rad/s about +X in inertial space, in the frame's axes at t = 1, while the frame turns at
    // pi/2 rad/s about +Z. Over the step that ended, the body turned about the inertial direction that is +X at t = 1, which
    // is +Y at t = 0, so in the frame's axes it turned by Rz(-pi/2) Ry(pi/2) = Rx(pi/2) Rz(-pi/2). That product is the
    // quaternion (1/2, 1/2, -1/2, 1/2): a turn by 2 pi/3 about (1, 1, -1) / sqrt 3. Over the step in flight it turns by
    // Rz(-pi/2) Rx(pi/2), the quaternion (1/2, -1/2, -1/2, 1/2): a turn by 2 pi/3 about (1, -1, -1) / sqrt 3.
    private static readonly double ThirdTurnOverRootThree = 2 * Math.PI / (3 * Math.Sqrt(3));
    private static readonly Double3 AngularChordOfTheStepThatEnded = new Double3(1, 1, -1) * ThirdTurnOverRootThree;
    private static readonly Double3 AngularChordOfTheStepInFlight = new Double3(1, -1, -1) * ThirdTurnOverRootThree;

    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance = 1e-12) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    // ---- The two-turn update ----------------------------------------------------------------------------------

    [Fact]
    public void OverASteadySpinTheUpdateKeepsABodyAtRestInInertialSpaceOnItsInertialPlace()
    {
        // The body rests at (1, 0, 0) in inertial space. In the frame's axes it is at (1, 0, 0) at t = 0, (0, -1, 0) at
        // t = 1 and (-1, 0, 0) at t = 2. Jolt's chord of the step that ended is (0, -1, 0) - (1, 0, 0) = (-1, -1, 0), and
        // the chord of the step in flight must be (-1, 0, 0) - (0, -1, 0) = (-1, 1, 0): a change of (0, 2, 0).
        var turns = new FrameTurns(Quarter, Quarter);

        ExpectNear(turns.FreeChange(new(0, -1, 0), new(-1, -1, 0), 1), new(0, 2, 0));
    }

    [Fact]
    public void OverASteadySpinTheUpdateKeepsAMovingBodyOnItsInertialLine()
    {
        // The body moves along (1, t, 0) in inertial space. In the frame's axes it is at (1, 0, 0) at t = 0, (1, -1, 0) at
        // t = 1 and (-1, -2, 0) at t = 2. The chord of the step that ended is (0, -1, 0), and the chord of the step in
        // flight must be (-2, -1, 0): a change of (-2, 0, 0).
        var turns = new FrameTurns(Quarter, Quarter);

        ExpectNear(turns.FreeChange(new(1, -1, 0), new(0, -1, 0), 1), new(-2, 0, 0));
    }

    [Fact]
    public void WhileTheFrameSpinsUpTheUpdateKeepsABodyAtRestInInertialSpaceOnItsInertialPlace()
    {
        // The frame turns by a quarter turn in the step that ended and by a half turn in the step in flight: a steady angular
        // acceleration of pi/2 rad/s² from pi/4 rad/s, at 3 pi/4 rad/s and 5 pi/4 rad/s at the ends of the two steps. The
        // body rests at (1, 0, 0) in inertial space, so in the frame's axes it is at (1, 0, 0), (0, -1, 0) and, three quarter
        // turns after the start, (0, 1, 0). The chord goes from (-1, -1, 0) to (0, 2, 0): a change of (1, 3, 0).
        FrameTurn last = Quarter with { AngularVelocity = new(0, 0, 3 * Math.PI / 4) };
        var step = new FrameTurn(DQuaternion.FromAxisAngle(Double3.UnitZ, -Math.PI), new(0, 0, 5 * Math.PI / 4));

        ExpectNear(new FrameTurns(step, last).FreeChange(new(0, -1, 0), new(-1, -1, 0), 1), new(1, 3, 0));
    }

    [Fact]
    public void OverTenSecondsOfASpinUpInATurningParentTheUpdateKeepsAFreeBodyOnItsInertialLine()
    {
        // Jolt's scheme: the update changes the chord, and the body then moves by the chord times the step. The expected
        // place comes from the inertial line and the frame's axes alone.
        static Double3 PlaceAt(double t) => SpinUpInATurningParent.FreeBodyPlaceAt(t);

        Double3 place = PlaceAt(0);
        Double3 chord = (place - PlaceAt(-Step)) / Step;
        for (int n = 0; n < 600; n++)
        {
            chord += SpinUpInATurningParent.TurnsOfStep(n).FreeChange(place, chord, Step);
            place += chord * Step;
        }

        ExpectNear(place, PlaceAt(600 * Step), 1e-6);
    }

    [Fact]
    public void InAFrameThatDoesNotTurnTheUpdateChangesNoChord()
    {
        var turns = new FrameTurns(FrameTurn.None, FrameTurn.None);

        ExpectNear(turns.FreeChange(new(100, -3, 7), new(-2, 5, 1.5), 1.0 / 60), Double3.Zero, 1e-9);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void AFrameIsATurningFrameWhenEitherOfItsTwoTurnsIsNotTheIdentity(bool stepTurns, bool lastTurns,
        bool expected)
    {
        var turns = new FrameTurns(stepTurns ? Quarter : FrameTurn.None, lastTurns ? Quarter : FrameTurn.None);

        turns.Turns.Should().Be(expected);
    }

    // ---- The chord rule ---------------------------------------------------------------------------------------

    [Fact]
    public void TheChordBehindTheVelocityOfABodyAtRestInInertialSpaceIsTheChangeOfItsPlaceInTheFramesAxes()
    {
        // At t = 1 the body that rests at (1, 0, 0) in inertial space is at (0, -1, 0) in the frame's axes, and it moves
        // relative to them at -ω × x = (-pi/2, 0, 0). Over the step that ended it went from (1, 0, 0) to (0, -1, 0).
        ExpectNear(Quarter.ChordBehind(new(0, -1, 0), new(-QuarterTurn, 0, 0), 1), new(-1, -1, 0));
    }

    [Fact]
    public void TheInstantVelocityAtTheEndOfTheChordOfABodyAtRestInInertialSpaceIsMinusTheFramesTurnAtItsPlace()
    {
        ExpectNear(Quarter.InstantAtEnd(new(0, -1, 0), new(-1, -1, 0), 1), new(-QuarterTurn, 0, 0));
    }

    [Fact]
    public void TheChordBehindAndTheInstantVelocityAtTheEndUndoEachOther()
    {
        FrameTurn turn = SpinUpInATurningParent.TurnOver(3, 3 + Step);
        var place = new Double3(100, -3, 7);
        var instant = new Double3(-2, 5, 1.5);
        var chord = new Double3(4, -30, 0.5);

        ExpectNear(turn.InstantAtEnd(place, turn.ChordBehind(place, instant, Step), Step), instant, 1e-9);
        ExpectNear(turn.ChordBehind(place, turn.InstantAtEnd(place, chord, Step), Step), chord, 1e-9);
    }

    [Fact]
    public void InASpinUpInATurningParentTheChordBehindTheInstantVelocityIsTheChangeOfTheBodysPlaceOverTheStep()
    {
        // The instant velocity relative to the frame comes from the body's places alone, by a central difference. The turn
        // holds the frame's inertial angular velocity at the step's end, in the frame's axes then. That vector changes its
        // direction in the frame's axes over the step, so the test fixes the axes in which the chord rule reads it.
        static Double3 PlaceAt(double t) => SpinUpInATurningParent.FreeBodyPlaceAt(t);
        const double end = 3;
        const double h = 1e-5;
        Double3 instant = (PlaceAt(end + h) - PlaceAt(end - h)) / (2 * h);
        Double3 chord = (PlaceAt(end) - PlaceAt(end - Step)) / Step;
        FrameTurn turn = SpinUpInATurningParent.TurnOver(end - Step, end);

        ExpectNear(turn.ChordBehind(PlaceAt(end), instant, Step), chord, 1e-8);
        ExpectNear(turn.InstantAtEnd(PlaceAt(end), chord, Step), instant, 1e-8);
    }

    [Fact]
    public void AVelocityChangeThatEntersAsAChangeOfTheChordBehindIsCarriedByTheTurnOfTheStepInFlightAlone()
    {
        // An impulse changes the velocity by (1, 0, 0) at the start of the step in flight. The quarter turn of that step
        // carries it to (0, -1, 0) at the step's end. Added to Jolt's chord as it is, the two turns would carry it to
        // (-1, 0, 0).
        var turns = new FrameTurns(Quarter, Quarter);
        var place = new Double3(0, -1, 0);
        var chord = new Double3(-1, -1, 0);
        Double3 ChordAhead(Double3 c) => c + turns.FreeChange(place, c, 1);

        Double3 changed = ChordAhead(chord + turns.Last.ChangeBehind(new(1, 0, 0)));

        ExpectNear(changed - ChordAhead(chord), new(0, -1, 0));
    }

    [Fact]
    public void TheAngularChordBehindAnInstantAngularVelocityIsTheTurnOfTheBodyInTheFramesAxesOverTheStepThatEnded()
    {
        // Relative to the frame the body turns at (pi/2, 0, 0) - (0, 0, pi/2).
        ExpectNear(Quarter.AngularChordBehind(new(QuarterTurn, 0, -QuarterTurn), 1), AngularChordOfTheStepThatEnded);
    }

    [Fact]
    public void TheInstantAngularVelocityAtTheEndOfAnAngularChordIsTheBodysInertialTurnLessTheFrames()
    {
        ExpectNear(Quarter.InstantAngularAtEnd(AngularChordOfTheStepThatEnded, 1), new(QuarterTurn, 0, -QuarterTurn));
    }

    [Fact]
    public void TheAngularChordBehindAndTheInstantAngularVelocityAtTheEndUndoEachOther()
    {
        FrameTurn turn = SpinUpInATurningParent.TurnOver(3, 3 + Step);
        var instant = new Double3(0.4, -1.1, 2.3);
        var chord = new Double3(-0.7, 0.2, 1.9);

        ExpectNear(turn.InstantAngularAtEnd(turn.AngularChordBehind(instant, Step), Step), instant, 1e-9);
        ExpectNear(turn.AngularChordBehind(turn.InstantAngularAtEnd(chord, Step), Step), chord, 1e-9);
    }

    [Fact]
    public void InASpinUpInATurningParentTheAngularChordBehindTheInstantAngularVelocityIsTheTurnOfTheBodyOverTheStep()
    {
        // The angular form of the chord test in the same station: the instant angular velocity relative to the frame comes
        // from the body's orientations alone, by a central difference.
        static DQuaternion OrientationAt(double t) => SpinUpInATurningParent.TumblingBodyOrientationAt(t);
        const double end = 3;
        const double h = 1e-5;
        Double3 instant = OrientationAt(end - h).AngularVelocityTo(OrientationAt(end + h), 2 * h);
        Double3 chord = OrientationAt(end - Step).AngularVelocityTo(OrientationAt(end), Step);
        FrameTurn turn = SpinUpInATurningParent.TurnOver(end - Step, end);

        ExpectNear(turn.AngularChordBehind(instant, Step), chord, 1e-9);
        ExpectNear(turn.InstantAngularAtEnd(chord, Step), instant, 1e-9);
    }

    [Fact]
    public void OverASteadySpinTheAngularFormKeepsTheInertialAngularVelocityOfAFreeBody()
    {
        var turns = new FrameTurns(Quarter, Quarter);

        ExpectNear(turns.AngularChordAhead(AngularChordOfTheStepThatEnded, Double3.Zero, 1),
            AngularChordOfTheStepInFlight);
    }

    [Fact]
    public void
        AnAngularVelocityChangeAtTheStartOfTheStepJoinsTheInertialAngularVelocityBeforeTheTurnOfTheStepInFlight()
    {
        // The body rests in inertial space, so over the step that ended it turned with the frame's coordinates, by
        // (0, 0, -pi/2). An angular impulse at the start of the step in flight gives it (pi/2, 0, 0) in the axes of that
        // moment: the same inertial angular velocity as the body above.
        var turns = new FrameTurns(Quarter, Quarter);

        ExpectNear(turns.AngularChordAhead(new(0, 0, -QuarterTurn), new(QuarterTurn, 0, 0), 1),
            AngularChordOfTheStepInFlight);
    }

    [Fact]
    public void OverTenSecondsOfASpinUpInATurningParentTheAngularFormKeepsATumblingBodyOnItsInertialTurn()
    {
        // Jolt turns the body by the exact rotation of its angular chord times the step. The expected orientation comes from
        // the body's steady inertial turn and the frame's axes alone.
        static DQuaternion OrientationAt(double t) => SpinUpInATurningParent.TumblingBodyOrientationAt(t);

        DQuaternion orientation = OrientationAt(0);
        Double3 chord = OrientationAt(-Step).AngularVelocityTo(orientation, Step);
        for (int n = 0; n < 600; n++)
        {
            chord = SpinUpInATurningParent.TurnsOfStep(n).AngularChordAhead(chord, Double3.Zero, Step);
            orientation = DQuaternion.FromAxisAngle(chord, chord.Length * Step) * orientation;
        }

        double end = 600 * Step;
        ExpectNear(chord, OrientationAt(end - Step).AngularVelocityTo(OrientationAt(end), Step), 1e-9);
        OrientationAt(end).AngularVelocityTo(orientation, 1).Length.Should()
            .BeLessThan(1e-9, "the body's orientation stays on its inertial turn");
    }

    // ---- The rotation terms -----------------------------------------------------------------------------------

    [Fact]
    public void TheAngularAccelerationIsTheMeanChangeOfTheInertialAngularVelocityOverTheStepInFlight()
    {
        var turns = new FrameTurns(Quarter with { AngularVelocity = new(0, 0, 2) },
            Quarter with { AngularVelocity = new(0, 0, 1) });

        ExpectNear(turns.AngularAcceleration(0.5), new(0, 0, 2));
    }

    [Fact]
    public void TheCentrifugalAccelerationPointsAwayFromTheAxisWithTheSquareOfTheRateTimesTheDistanceFromIt()
    {
        // At 2 rad/s about +Z, the place (3, 0, 5) is 3 m from the axis: 4 · 3 = 12 m/s² along +X.
        var turns = new FrameTurns(Quarter with { AngularVelocity = new(0, 0, 2) }, Quarter);

        ExpectNear(turns.Centrifugal(new(3, 0, 5)), new(12, 0, 0));
    }

    [Fact]
    public void TheEulerAccelerationOfASpinUpPointsAgainstTheTurnAtThePlace()
    {
        // From 1 to 2 rad/s about +Z in 0.5 s is 2 rad/s². At (3, 0, 0) the floor moves along +Y faster and faster, so a
        // free body falls behind it: -α × r = (0, -6, 0).
        var turns = new FrameTurns(Quarter with { AngularVelocity = new(0, 0, 2) },
            Quarter with { AngularVelocity = new(0, 0, 1) });

        ExpectNear(turns.Euler(new(3, 0, 0), 0.5), new(0, -6, 0));
    }

    [Fact]
    public void TheCoriolisAccelerationTurnsAVelocityAgainstTheSpin()
    {
        // At 2 rad/s about +Z, a velocity of (0, 1, 0): -2 ω × v = (4, 0, 0).
        var turns = new FrameTurns(Quarter with { AngularVelocity = new(0, 0, 2) }, Quarter);

        ExpectNear(turns.Coriolis(new(0, 1, 0)), new(4, 0, 0));
    }

    /// <summary>
    /// A station that spins up about its own +Y, from the spike's rate of 0.31321 rad/s at 0.01 rad/s², inside a parent that
    /// turns steadily at 0.05 rad/s about another axis, at 60 Hz. The two turns of a step are then about different axes, so
    /// their order counts, as for the station inside a facing orbit. The parent turns faster than a facing orbit, at about
    /// 1.1e-3 rad/s, so that a wrong order shows above rounding.
    /// </summary>
    private static class SpinUpInATurningParent
    {
        private const double Rate = 0.31321;
        private const double SpinUp = 0.01;
        private const double ParentRate = 0.05;
        private static readonly Double3 ParentAxis = new Double3(0.3, 1, -0.2).Normalized();
        private static readonly DQuaternion Tilt = DQuaternion.FromAxisAngle(new(-1, 0.5, 2), 0.8);

        private static DQuaternion SpinAt(double t) =>
            DQuaternion.FromAxisAngle(Double3.UnitY, Rate * t + 0.5 * SpinUp * t * t);

        /// <summary>The station's axes in inertial space at the time <paramref name="t"/>.</summary>
        public static DQuaternion AxesAt(double t) =>
            DQuaternion.FromAxisAngle(ParentAxis, ParentRate * t) * Tilt * SpinAt(t);

        /// <summary>
        /// The station's inertial angular velocity at <paramref name="t"/> in its axes then: the parent's turn, carried into
        /// the station's axes, plus the station's own spin about its +Y.
        /// </summary>
        private static Double3 AngularVelocityAt(double t) =>
            SpinAt(t).Conjugate().Rotate(Tilt.Conjugate().Rotate(ParentAxis * ParentRate)) +
            Double3.UnitY * (Rate + SpinUp * t);

        /// <summary>
        /// The turn from <paramref name="from"/> to <paramref name="to"/>: how the station's coordinates of an inertial
        /// direction change, and the inertial angular velocity at <paramref name="to"/>.
        /// </summary>
        public static FrameTurn TurnOver(double from, double to) =>
            new(AxesAt(to).Conjugate() * AxesAt(from), AngularVelocityAt(to));

        /// <summary>The two turns that the step from n · <see cref="Step"/> to (n + 1) · <see cref="Step"/> reads.</summary>
        public static FrameTurns TurnsOfStep(int n) =>
            new(TurnOver(n * Step, (n + 1) * Step), TurnOver((n - 1) * Step, n * Step));

        /// <summary>
        /// The place in the station's axes at <paramref name="t"/> of a free body that is at (100, −3, 7) m in inertial
        /// space at t = 0 and moves at (−2, 5, 1.5) m/s.
        /// </summary>
        public static Double3 FreeBodyPlaceAt(double t) =>
            AxesAt(t).Conjugate().Rotate(new Double3(100, -3, 7) + new Double3(-2, 5, 1.5) * t);

        /// <summary>
        /// The orientation in the station's axes at <paramref name="t"/> of a free body that turns steadily at
        /// (0.4, −1.1, 2.3) rad/s in inertial space, from a turn by 0.7 rad about (1, 2, 3) at t = 0.
        /// </summary>
        public static DQuaternion TumblingBodyOrientationAt(double t)
        {
            var spin = new Double3(0.4, -1.1, 2.3);
            return AxesAt(t).Conjugate() * DQuaternion.FromAxisAngle(spin, spin.Length * t) *
                   DQuaternion.FromAxisAngle(new(1, 2, 3), 0.7);
        }
    }
}
