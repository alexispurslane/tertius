using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Demo;

/// <summary>
/// One jet of the shuttle, with its scene id <see cref="Id"/>: where it sits on the hull, <see cref="Place"/>, in meters
/// in the hull's axes, the direction in which it pushes the hull, <see cref="Push"/>, a signed axis of the hull, and its
/// force at full throttle, <see cref="MaxThrust"/>, in newtons. Its exhaust leaves against <see cref="Push"/>.
/// </summary>
public readonly record struct Jet(string Id, Double3 Place, Double3 Push, double MaxThrust)
{
    /// <summary>
    /// The force of the jet at <paramref name="throttle"/>, from 0 to 1, in newtons, in the axes that
    /// <paramref name="rotation"/> turns the hull's axes into. A jet part that keeps the hull's rotation passes its own
    /// rotation in its frame. The two jets of a couple push along exact negatives of one axis with one thrust, so at one
    /// throttle and one rotation their forces are exact negatives, and the couple gives the hull no net force at all.
    /// </summary>
    public Double3 Force(DQuaternion rotation, float throttle) => rotation.Rotate(Push) * (throttle * MaxThrust);
}

/// <summary>
/// Two jets that always take one throttle, with the pair's name <see cref="Name"/>. In a couple they push in opposite
/// directions with one thrust, so they turn the hull with no net force. In a translation pair they push in one direction,
/// with thrusts in the shares that put their sum on the line through the center of mass, so they push the hull with no
/// torque.
/// </summary>
public readonly record struct JetPair(string Name, Jet First, Jet Second);

/// <summary>
/// The jets of the shuttle and the allocation of their throttles (docs/plans/blue-danube-demo.md, "The jets as parts"),
/// for a hull whose center of mass lies at <c>centerOfMass</c>, in meters in the hull's axes. Every place,
/// force and torque here is in the hull's axes: +X starboard, +Y up, -Z the nose.
/// <list type="bullet">
/// <item>The main engine sits at <see cref="MainEnginePlace"/>, in the tail pocket on the line of thrust through the
/// center of mass, so its push gives the hull no torque.</item>
/// <item>Six couples turn the hull, one for each sense of each axis. Each is two jets that push in opposite directions
/// with one thrust at two places, so it gives a torque about its axis and no net force, whatever the center of mass. At
/// full throttle each gives the most torque that the attitude autopilot asks for about its axis,
/// <see cref="AttitudeAutopilot.MostTorque"/> of <c>inertia</c>.</item>
/// <item>Six translation pairs push the hull, one for each direction along the hull's three axes. Each is
/// two jets that push in one direction from two places, with thrusts in the shares that put the line of their sum through
/// the center of mass. So each gives no torque about the center of mass, and at full throttle the two together give
/// <c>translationThrust</c>.</item>
/// </list>
/// Every jet sits on the solid skin of the full cross-section, off the canopy's glass: on the rib where the nose's loft
/// ends, on the band aft of the last rib, on the ribs at z = ±3 m, on the belly, on the lower sides below the sill, or on
/// the tail face beside the engine's pocket. The constructor throws <see cref="ArgumentOutOfRangeException"/> for a center
/// of mass outside the box that the jets reach, |x| &lt; 0.5 m, |y| &lt; 1.2 m and |z| &lt; 3 m, for a thrust that is not
/// positive and finite, and for a moment of inertia that is not positive and finite.
/// </summary>
public sealed class ShuttleJets
{
    /// <summary>The number of pairs, <see cref="Pairs"/>, and of the throttles that <see cref="Allocate"/> writes.</summary>
    public const int PairCount = 12;

    /// <summary>Where the nose's loft ends and the full cross-section starts, in meters along Z: a rib.</summary>
    private const double NoseRing = -5;

    /// <summary>The middle of the solid band from the last rib, at z = 7 m, to the tail face, in meters along Z.</summary>
    private const double TailRing = 7.5;

    /// <summary>The ribs at z = ±3 m, which hold the translation pairs across Z.</summary>
    private const double TranslationRib = 3;

    /// <summary>The height of the jets on the lower sides, in meters: between the sill at -1.2 m and the bevel at -1.8 m.</summary>
    private const double LowerSide = -1.5;

    /// <summary>How far each roll jet lies from the middle line, in meters, on the belly and on the roof.</summary>
    private const double RollSpan = 2;

    /// <summary>How far each tail jet lies from the line of thrust, in meters, clear of the pocket's housing.</summary>
    private const double TailSpread = 1.6;

    /// <summary>The most torque of each axis's couples at full throttle, in N·m, about each of the hull's axes.</summary>
    private readonly Double3 coupleTorque;

    /// <summary>The force of each translation pair at full throttle, in newtons.</summary>
    private readonly double translationThrust;

    public ShuttleJets(Double3 centerOfMass, PrincipalInertia inertia, double translationThrust)
    {
        if (!(Math.Abs(centerOfMass.X) < 0.5 && Math.Abs(centerOfMass.Y) < 1.2 &&
              Math.Abs(centerOfMass.Z) < TranslationRib))
            throw new ArgumentOutOfRangeException(nameof(centerOfMass), centerOfMass,
                "The jets reach a center of mass within |x| < 0.5 m, |y| < 1.2 m and |z| < 3 m.");
        if (!(translationThrust > 0 && double.IsFinite(translationThrust)))
            throw new ArgumentOutOfRangeException(nameof(translationThrust), translationThrust,
                "The thrust of a translation pair must be positive and finite.");
        if (!(inertia.Moments.X > 0 && inertia.Moments.Y > 0 && inertia.Moments.Z > 0 &&
              float.IsFinite(inertia.Moments.X + inertia.Moments.Y + inertia.Moments.Z)))
            throw new ArgumentOutOfRangeException(nameof(inertia), inertia,
                "The moments of inertia must be positive and finite.");
        CenterOfMass = centerOfMass;
        this.translationThrust = translationThrust;
        coupleTorque = AttitudeAutopilot.MostTorque(inertia);
        MainEnginePlace = new(centerOfMass.X, centerOfMass.Y, Shuttle.NozzleZ);

        Double3 c = centerOfMass;
        double side = Shuttle.Hull.X, roof = Shuttle.Hull.Y, tail = Shuttle.Hull.Z;
        Double3 x = Double3.UnitX, y = Double3.UnitY, z = Double3.UnitZ;
        Pairs =
        [
            // The couples, in the order of Couple(axis, negative). Pitch and yaw push across Z at the nose's ring and the
            // tail's ring, 12.5 m apart. Roll pushes across X between the belly and the roof, 4 m apart.
            Couple("pitch-up", new(0, -roof, NoseRing), new(0, roof, TailRing), y, x),
            Couple("pitch-down", new(0, -roof, TailRing), new(0, roof, NoseRing), y, -x),
            Couple("yaw-port", new(-side, LowerSide, TailRing), new(side, LowerSide, NoseRing), x, y),
            Couple("yaw-starboard", new(-side, LowerSide, NoseRing), new(side, LowerSide, TailRing), x, -y),
            Couple("roll-port", new(RollSpan, -roof, TailRing), new(-RollSpan, roof, TailRing), y, z),
            Couple("roll-starboard", new(-RollSpan, -roof, NoseRing), new(RollSpan, roof, NoseRing), y, -z),
            // The translation pairs, in the order of Translation(axis, negative). Each pair's two places differ along one
            // axis across its push, and both take the center of mass's value on the other.
            Translation("move-starboard", new(-side, c.Y, -TranslationRib), new(-side, c.Y, TranslationRib), x, c.Z),
            Translation("move-port", new(side, c.Y, -TranslationRib), new(side, c.Y, TranslationRib), -x, c.Z),
            Translation("move-up", new(c.X, -roof, -TranslationRib), new(c.X, -roof, TranslationRib), y, c.Z),
            Translation("move-down", new(c.X, roof, -TranslationRib), new(c.X, roof, TranslationRib), -y, c.Z),
            Translation("move-aft", new(-side, c.Y, NoseRing), new(side, c.Y, NoseRing), z, c.X),
            Translation("move-forward", new(c.X - TailSpread, c.Y, tail), new(c.X + TailSpread, c.Y, tail), -z, c.X)
        ];
    }

    /// <summary>The center of mass that the jets were placed for, in meters in the hull's axes.</summary>
    public Double3 CenterOfMass { get; }

    /// <summary>Where the main engine sits, in meters in the hull's axes: in the nozzle's place, on the line along Z through the center of mass.</summary>
    public Double3 MainEnginePlace { get; }

    /// <summary>
    /// The twelve pairs: the couples at <see cref="Couple(int, bool)"/>, then the translation pairs at
    /// <see cref="Translation(int, bool)"/>.
    /// <see cref="Allocate"/> writes one throttle for each, which both of its jets take.
    /// </summary>
    public IReadOnlyList<JetPair> Pairs { get; }

    /// <summary>
    /// The index in <see cref="Pairs"/> of the couple that turns the hull about its <paramref name="axis"/>, 0 for X, 1 for Y
    /// and 2 for Z, in the positive sense, or in the negative sense when <paramref name="negative"/>.
    /// </summary>
    public static int Couple(int axis, bool negative) => 2 * axis + (negative ? 1 : 0);

    /// <summary>
    /// The index in <see cref="Pairs"/> of the translation pair that pushes the hull along its <paramref name="axis"/>, 0 for
    /// X, 1 for Y and 2 for Z, in the positive direction, or in the negative direction when <paramref name="negative"/>.
    /// </summary>
    public static int Translation(int axis, bool negative) => 6 + 2 * axis + (negative ? 1 : 0);

    /// <summary>
    /// Writes into <paramref name="throttles"/>, one for each of <see cref="Pairs"/>, the throttles from 0 to 1 that give the
    /// hull <paramref name="torque"/> about its center of mass, in N·m, and <paramref name="force"/>, in newtons, both in the
    /// hull's axes. Each axis takes one couple and one translation pair, in the sense that it asks for, and the other two
    /// stay off. An axis that asks for more than its jets give takes full throttle on that axis, and the other axes keep
    /// theirs, so each axis is limited on its own. The couples give no force and the translation
    /// pairs no torque, so the torque and the force do not mix. It allocates nothing. Throws
    /// <see cref="ArgumentOutOfRangeException"/> for a torque or a force that is not finite, and
    /// <see cref="ArgumentException"/> for a span whose length is not <see cref="PairCount"/>.
    /// </summary>
    public void Allocate(Double3 torque, Double3 force, Span<float> throttles)
    {
        if (!IsFinite(torque))
            throw new ArgumentOutOfRangeException(nameof(torque), torque, "A torque is finite.");
        if (!IsFinite(force))
            throw new ArgumentOutOfRangeException(nameof(force), force, "A force is finite.");
        if (throttles.Length != PairCount)
            throw new ArgumentException(
                $"The throttles take {PairCount} values, one for each pair, not {throttles.Length}.",
                nameof(throttles));
        for (int axis = 0; axis < 3; axis++)
        {
            double turn = Along(torque, axis) / Along(coupleTorque, axis),
                push = Along(force, axis) / translationThrust;
            throttles[Couple(axis, false)] = (float)Math.Clamp(turn, 0, 1);
            throttles[Couple(axis, true)] = (float)Math.Clamp(-turn, 0, 1);
            throttles[Translation(axis, false)] = (float)Math.Clamp(push, 0, 1);
            throttles[Translation(axis, true)] = (float)Math.Clamp(-push, 0, 1);
        }
    }

    /// <summary>
    /// The couple <paramref name="name"/>: a jet at <paramref name="first"/> that pushes along <paramref name="push"/> and
    /// one at <paramref name="second"/> that pushes against it, with the thrust that gives this axis's most torque about
    /// <paramref name="turn"/>, the axis and sense that the couple turns the hull about.
    /// </summary>
    private JetPair Couple(string name, Double3 first, Double3 second, Double3 push, Double3 turn)
    {
        // A couple's torque does not depend on the point that it is taken about: it is (first - second) × push per newton.
        double perNewton = Double3.Dot(Double3.Cross(first - second, push), turn);
        double most = Double3.Dot(coupleTorque, new(Math.Abs(turn.X), Math.Abs(turn.Y), Math.Abs(turn.Z)));
        double thrust = most / perNewton;
        return new(name, new($"jet-{name}-first", first, push, thrust),
            new($"jet-{name}-second", second, -push, thrust));
    }

    /// <summary>
    /// The translation pair <paramref name="name"/>: jets at <paramref name="first"/> and <paramref name="second"/> that both
    /// push along <paramref name="push"/>. The two places differ along one axis, on which the center of mass lies at
    /// <paramref name="center"/>. Each jet's share of the thrust is the part of the span between them that lies on the
    /// other jet's side of the center, so the line of their sum runs through the center of mass.
    /// </summary>
    private JetPair Translation(string name, Double3 first, Double3 second, Double3 push, double center)
    {
        Double3 span = second - first;
        int axis = Math.Abs(span.X) > 0 ? 0 : Math.Abs(span.Y) > 0 ? 1 : 2;
        double firstShare = (Along(second, axis) - center) / Along(span, axis);
        return new(name, new($"jet-{name}-first", first, push, firstShare * translationThrust),
            new($"jet-{name}-second", second, push, (1 - firstShare) * translationThrust));
    }

    private static double Along(Double3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private static bool IsFinite(Double3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
}
