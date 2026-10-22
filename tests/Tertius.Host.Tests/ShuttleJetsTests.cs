using System.Numerics;
using Tertius.Demo;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;
using Tertius.Testing;

namespace Tertius.Host.Tests;

/// <summary>
/// The layout of the shuttle's jets on its hull and the allocation of their throttles, <see cref="ShuttleJets"/>
/// (docs/plans/blue-danube-demo.md, "The jets as parts"), with no World: each couple turns the hull about one of its axes
/// with no net force, each translation pair pushes along one axis with no torque about the center of mass, the
/// allocation gives the torque and the force asked for within the jets' thrust, the couples hold the attitude
/// autopilot's largest turn, and every jet sits on the hull's solid skin. Every place, force and torque is in the hull's
/// own axes, and every jet's force is that of a hull with the identity rotation, unless a test turns it.
/// </summary>
public sealed class ShuttleJetsTests
{
    private static readonly Double3 DemoCenter = HostTesting.ShuttleCenterOfMass;

    /// <summary>A center of mass off the hull's middle on every axis, within the jets' reach.</summary>
    private static readonly Double3 OffCenter = new(0.3, 0.5, -1.2);

    private static readonly Double3 MostTorque = AttitudeAutopilot.MostTorque(Shuttle.Inertia);

    /// <summary>The largest angular acceleration that the attitude autopilot asks for, in rad/s², while the body turns within its cap.</summary>
    private static double LargestAcceleration => 2 * AttitudeAutopilot.MaxTurnRate / AttitudeAutopilot.RateTime;

    private static ShuttleJets Jets(Double3 center) => new(center, Shuttle.Inertia, Shuttle.TranslationThrust);

    private static ShuttleJets DemoJets() => Jets(DemoCenter);

    private static Double3 Axis(int axis) => axis switch { 0 => Double3.UnitX, 1 => Double3.UnitY, _ => Double3.UnitZ };

    private static double Along(Double3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private static IEnumerable<Jet> Both(JetPair pair) => [pair.First, pair.Second];

    /// <summary>The torque about each of the body's own axes for <paramref name="acceleration"/>, from the moments about its principal axes.</summary>
    private static Double3 InertiaTimes(Double3 acceleration)
    {
        DQuaternion axes = DQuaternion.FromQuaternion(Shuttle.Inertia.Axes).Normalized();
        return axes.Rotate(axes.Conjugate().Rotate(acceleration) * Shuttle.Inertia.Moments);
    }

    /// <summary>The net force of every jet at its pair's throttle, and their torque about <paramref name="about"/>.</summary>
    private static (Double3 Force, Double3 Torque) Push(ShuttleJets jets, ReadOnlySpan<float> throttles, Double3 about)
    {
        Double3 force = Double3.Zero, torque = Double3.Zero;
        for (int i = 0; i < jets.Pairs.Count; i++)
            foreach (Jet jet in Both(jets.Pairs[i]))
            {
                Double3 each = jet.Force(DQuaternion.Identity, throttles[i]);
                force += each;
                torque += Double3.Cross(jet.Place - about, each);
            }

        return (force, torque);
    }

    private static float[] Allocated(ShuttleJets jets, Double3 torque, Double3 force)
    {
        float[] throttles = new float[ShuttleJets.PairCount];
        jets.Allocate(torque, force, throttles);
        return throttles;
    }

    private static void ShouldBeNear(Double3 actual, Double3 expected, double tolerance, string what)
    {
        for (int axis = 0; axis < 3; axis++)
            Along(actual, axis).Should()
                .BeApproximately(Along(expected, axis), tolerance, $"{what} is {actual}, not {expected}");
    }

    private static Double3 RandomIn(Random random, Double3 half) =>
        new((2 * random.NextDouble() - 1) * half.X, (2 * random.NextDouble() - 1) * half.Y,
            (2 * random.NextDouble() - 1) * half.Z);

    private static Double3 RandomDirection(Random random)
    {
        while (true)
        {
            Double3 v = RandomIn(random, new(1, 1, 1));
            if (v.Length is > 0.1 and <= 1)
                return v.Normalized();
        }
    }

    [Fact]
    public void TheTwoJetsOfEachCouplePushInExactlyOppositeDirectionsWithOneThrust()
    {
        ShuttleJets jets = DemoJets();
        for (int axis = 0; axis < 3; axis++)
            foreach (bool negative in new[] { false, true })
            {
                JetPair couple = jets.Pairs[ShuttleJets.Couple(axis, negative)];
                couple.Second.Push.Should().Be(-couple.First.Push,
                    $"the jets of {couple.Name} push in opposite directions");
                couple.Second.MaxThrust.Should()
                    .Be(couple.First.MaxThrust, $"the jets of {couple.Name} have one thrust");
                couple.First.MaxThrust.Should().BePositive();
            }
    }

    /// <summary>
    /// The parts design's rule: forces that only nearly cancel leave a net force on the hull. The two jets of a couple give
    /// exact negatives of one force whatever the rotation that they read and whatever their throttle.
    /// </summary>
    [Fact]
    public void TheTwoJetsOfACoupleGiveExactNegativesOfOneForceInEveryRotation()
    {
        ShuttleJets jets = DemoJets();
        var random = new Random(7);
        for (int sample = 0; sample < 200; sample++)
        {
            DQuaternion rotation =
                DQuaternion.FromAxisAngle(RandomDirection(random), random.NextDouble() * 2 * Math.PI);
            float throttle = (float)random.NextDouble();
            for (int i = 0; i < 6; i++)
            {
                JetPair couple = jets.Pairs[i];
                (couple.First.Force(rotation, throttle) + couple.Second.Force(rotation, throttle)).Should().Be(
                    Double3.Zero,
                    $"the jets of {couple.Name} cancel exactly at the throttle {throttle} and the rotation {rotation}");
            }
        }
    }

    [Fact]
    public void AJetPushesAlongItsDirectionInTheRotationThatItReadsWithItsThrottleTimesItsThrust()
    {
        var jet = new Jet("jet", new(1, 2, 3), Double3.UnitY, 1000);
        DQuaternion quarter = DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2);
        ShouldBeNear(jet.Force(quarter, 0.25f), new(-250, 0, 0), 1e-9, "the force of a jet turned a quarter about Z");
    }

    [Fact]
    public void EachCoupleTurnsTheHullAboutItsOwnAxisInItsOwnSenseWithTheAutopilotsMostTorque()
    {
        foreach (Double3 center in new[] { DemoCenter, OffCenter })
        {
            ShuttleJets jets = Jets(center);
            for (int axis = 0; axis < 3; axis++)
                foreach (bool negative in new[] { false, true })
                {
                    float[] throttles = new float[ShuttleJets.PairCount];
                    throttles[ShuttleJets.Couple(axis, negative)] = 1;
                    (Double3 force, Double3 torque) = Push(jets, throttles, center);
                    force.Should().Be(Double3.Zero, $"couple {axis} {negative} gives no net force");
                    ShouldBeNear(torque, Axis(axis) * ((negative ? -1 : 1) * Along(MostTorque, axis)),
                        1e-9 * MostTorque.Length,
                        $"the torque of couple {jets.Pairs[ShuttleJets.Couple(axis, negative)].Name}");
                }
        }
    }

    [Fact]
    public void EachTranslationPairPushesAlongItsAxisWithTheThrustersForceAndNoTorqueAboutTheCenterOfMass()
    {
        foreach (Double3 center in new[] { DemoCenter, OffCenter })
        {
            ShuttleJets jets = Jets(center);
            for (int axis = 0; axis < 3; axis++)
                foreach (bool negative in new[] { false, true })
                {
                    float[] throttles = new float[ShuttleJets.PairCount];
                    throttles[ShuttleJets.Translation(axis, negative)] = 1;
                    (Double3 force, Double3 torque) = Push(jets, throttles, center);
                    string name = jets.Pairs[ShuttleJets.Translation(axis, negative)].Name;
                    ShouldBeNear(force, Axis(axis) * ((negative ? -1 : 1) * Shuttle.TranslationThrust),
                        1e-9 * Shuttle.TranslationThrust, $"the force of {name}");
                    ShouldBeNear(torque, Double3.Zero, 1e-9 * Shuttle.TranslationThrust * 10,
                        $"the torque of {name} about {center}");
                }
        }
    }

    [Fact]
    public void TheJetsGiveTheTorqueAndTheForceAskedForWithinTheirThrust()
    {
        var random = new Random(11);
        foreach (Double3 center in new[] { DemoCenter, OffCenter })
        {
            ShuttleJets jets = Jets(center);
            for (int sample = 0; sample < 200; sample++)
            {
                Double3 torque = RandomIn(random, MostTorque);
                Double3 force = RandomIn(random, new Double3(1, 1, 1) * Shuttle.TranslationThrust);
                float[] throttles = Allocated(jets, torque, force);
                throttles.Should().OnlyContain(throttle => throttle >= 0 && throttle <= 1);
                (Double3 net, Double3 turn) = Push(jets, throttles, center);
                ShouldBeNear(net, force, 1e-6 * Shuttle.TranslationThrust, "the net force");
                ShouldBeNear(turn, torque, 1e-6 * MostTorque.Length, "the torque about the center of mass");
            }
        }
    }

    [Fact]
    public void OnlyOneSenseOfEachAxisFires()
    {
        float[] throttles = Allocated(DemoJets(), new Double3(0.5, -0.5, 0.25) * MostTorque.Length,
            new(-1000, 2000, -3000));
        for (int axis = 0; axis < 3; axis++)
        {
            Math.Min(throttles[ShuttleJets.Couple(axis, false)], throttles[ShuttleJets.Couple(axis, true)]).Should()
                .Be(0, $"one couple of axis {axis} is off");
            Math.Min(throttles[ShuttleJets.Translation(axis, false)], throttles[ShuttleJets.Translation(axis, true)])
                .Should().Be(0, $"one translation pair of axis {axis} is off");
        }
    }

    [Fact]
    public void AnAskForNothingFiresNoJet() =>
        Allocated(DemoJets(), Double3.Zero, Double3.Zero).Should().OnlyContain(throttle => throttle == 0);

    [Fact]
    public void AnAxisAskedForMoreThanItsJetsGiveTakesFullThrottleAndTheOtherAxesStayExact()
    {
        ShuttleJets jets = DemoJets();
        var torque = new Double3(-3 * MostTorque.X, 0.5 * MostTorque.Y, -0.2 * MostTorque.Z);
        Double3 force = new Double3(0.4, 5, -0.1) * Shuttle.TranslationThrust;
        float[] throttles = Allocated(jets, torque, force);
        throttles[ShuttleJets.Couple(0, negative: true)].Should().Be(1);
        throttles[ShuttleJets.Translation(1, negative: false)].Should().Be(1);
        (Double3 net, Double3 turn) = Push(jets, throttles, DemoCenter);
        ShouldBeNear(net, new(force.X, Shuttle.TranslationThrust, force.Z), 1e-6 * Shuttle.TranslationThrust,
            "the net force");
        ShouldBeNear(turn, new(-MostTorque.X, torque.Y, torque.Z), 1e-6 * MostTorque.Length, "the torque");
    }

    /// <summary>
    /// The sizing rule: while the hull turns within the autopilot's cap, the autopilot asks for at most twice the cap over
    /// its rate time, in any direction. The couples give the torque of that acceleration in every direction.
    /// </summary>
    [Fact]
    public void TheCouplesHoldTheAutopilotsLargestTurnInEveryDirection()
    {
        ShuttleJets jets = DemoJets();
        var random = new Random(13);
        for (int sample = 0; sample < 500; sample++)
        {
            Double3 torque = InertiaTimes(RandomDirection(random) * LargestAcceleration);
            float[] throttles = Allocated(jets, torque, Double3.Zero);
            (_, Double3 turn) = Push(jets, throttles, DemoCenter);
            ShouldBeNear(turn, torque, 1e-6 * MostTorque.Length, "the torque of the largest turn");
        }
    }

    /// <summary>The couples are no stronger than that rule: the largest turn in one direction takes each couple to full throttle.</summary>
    [Fact]
    public void TheLargestTurnInTheWorstDirectionOfEachAxisTakesItsCoupleToFullThrottle()
    {
        ShuttleJets jets = DemoJets();
        for (int axis = 0; axis < 3; axis++)
        {
            // The tensor is symmetric, so the torque about the axis is the dot product of the acceleration with the tensor's
            // column for that axis, which is largest along that column.
            Double3 worst = InertiaTimes(Axis(axis)).Normalized() * LargestAcceleration;
            float[] throttles = Allocated(jets, InertiaTimes(worst), Double3.Zero);
            throttles[ShuttleJets.Couple(axis, false)].Should().BeApproximately(1, 1e-6f,
                $"the largest turn about axis {axis} takes its couple to full throttle");
        }
    }

    [Fact]
    public void TheMostTorqueOfTheAutopilotIsTheLargestAccelerationTimesTheLengthOfEachRowOfTheInertia()
    {
        Double3 most = AttitudeAutopilot.MostTorque(Shuttle.Inertia);
        for (int axis = 0; axis < 3; axis++)
            Along(most, axis).Should().BeApproximately(InertiaTimes(Axis(axis)).Length * LargestAcceleration,
                1e-6 * Along(most, axis));
    }

    [Fact]
    public void TheAllocationAllocatesNothing()
    {
        ShuttleJets jets = DemoJets();
        Span<float> warm = stackalloc float[ShuttleJets.PairCount];
        jets.Allocate(new(1, 2, 3), new(4, 5, 6), warm);
        long allocated = Allocations.Fewest(() =>
        {
            Span<float> throttles = stackalloc float[ShuttleJets.PairCount];
            for (int i = 0; i < 100; i++)
                jets.Allocate(new(i, -i, 2 * i), new(-i, i, 3 * i), throttles);
        });
        allocated.Should().Be(0);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(0, double.NegativeInfinity)]
    public void ATorqueOrAForceThatIsNotFiniteIsAnError(double torque, double force)
    {
        ShuttleJets jets = DemoJets();
        Action allocate = () => jets.Allocate(new(0, torque, 0), new(force, 0, 0), new float[ShuttleJets.PairCount]);
        allocate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AThrottleListOfTheWrongLengthIsAnError()
    {
        ShuttleJets jets = DemoJets();
        Action allocate = () => jets.Allocate(Double3.Zero, Double3.Zero, new float[ShuttleJets.PairCount - 1]);
        allocate.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0, 0, 3.5)]
    [InlineData(0, 0, -3.5)]
    [InlineData(0.6, 0, 0)]
    [InlineData(0, 1.3, 0)]
    [InlineData(0, -1.3, 0)]
    [InlineData(double.NaN, 0, 0)]
    public void ACenterOfMassOutsideTheJetsReachIsAnError(double x, double y, double z)
    {
        Action build = () => Jets(new(x, y, z));
        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TheMainEngineSitsOnTheLineOfThrustThroughTheCenterOfMassInTheTailPocket()
    {
        foreach (Double3 center in new[] { DemoCenter, OffCenter })
            Jets(center).MainEnginePlace.Should().Be(new Double3(center.X, center.Y, Shuttle.NozzleZ));
    }

    /// <summary>The full cross-section of the outer skin, from the hull's convex shape: its corners at the tail face.</summary>
    private static Vector2[] Section() =>
    [
        .. ((ConvexHullShapeDesc)Shuttle.HullShape).Points.Where(point => point.Z == Shuttle.Hull.Z)
        .Select(point => new Vector2(point.X, point.Y))
    ];

    /// <summary>
    /// Every jet sits on the outer skin of the full cross-section, which reaches from the nose's rib at z = -5 m to the
    /// tail face, and pushes into the hull or along its skin, so that its exhaust leaves the hull.
    /// </summary>
    [Fact]
    public void EveryJetSitsOnTheHullsSkinAndPushesIntoTheHullOrAlongIt()
    {
        Vector2[] section = Section();
        Vector2 middle = section.Aggregate(Vector2.Zero, (sum, corner) => sum + corner) / section.Length;
        foreach (Double3 center in new[] { DemoCenter, OffCenter })
        foreach (Jet jet in Jets(center).Pairs.SelectMany(Both))
        {
            jet.Place.Z.Should().BeInRange(-5, Shuttle.Hull.Z, $"{jet.Id} lies in the full cross-section");
            var flat = new Vector2((float)jet.Place.X, (float)jet.Place.Y);
            Double3 outward;
            if (jet.Place.Z == Shuttle.Hull.Z)
            {
                outward = Double3.UnitZ;
            }
            else
            {
                int edge = Enumerable.Range(0, section.Length).MinBy(k =>
                    DistanceToSegment(flat, section[k], section[(k + 1) % section.Length]));
                Vector2 a = section[edge], b = section[(edge + 1) % section.Length];
                DistanceToSegment(flat, a, b).Should().BeLessThan(1e-5f, $"{jet.Id} at {jet.Place} lies on the skin");
                Vector2 normal = Vector2.Normalize(new(b.Y - a.Y, a.X - b.X));
                if (Vector2.Dot(normal, (a + b) / 2 - middle) < 0)
                    normal = -normal;
                outward = new(normal.X, normal.Y, 0);
            }

            Double3.Dot(jet.Push, outward).Should()
                .BeLessThanOrEqualTo(1e-9, $"{jet.Id} pushes into the hull or along its skin");
        }
    }

    [Fact]
    public void TheJetsOnTheTailFaceStayClearOfTheEnginesPocket()
    {
        foreach (Double3 center in new[] { DemoCenter, OffCenter })
        foreach (Jet jet in Jets(center).Pairs.SelectMany(Both).Where(jet => jet.Place.Z == Shuttle.Hull.Z))
            Math.Max(Math.Abs(jet.Place.X - center.X), Math.Abs(jet.Place.Y - center.Y)).Should()
                .BeGreaterThan(0.9 + 0.5, $"{jet.Id} lies 0.5 m clear of the pocket's housing");
    }

    /// <summary>
    /// No jet lies on a pane of the canopy, or within 0.09 m of one: each sits on solid skin. A jet on a rib keeps the
    /// rib's half width, 0.1 m, to the glass on each side, so a nozzle up to 0.18 m wide fits.
    /// </summary>
    [Fact]
    public void NoJetSitsOnTheGlass()
    {
        foreach (Jet jet in DemoJets().Pairs.SelectMany(Both))
        foreach (HullPane pane in Shuttle.Glass)
            DistanceToPolygon(jet.Place, pane.Outer).Should()
                .BeGreaterThan(0.09, $"{jet.Id} at {jet.Place} lies clear of {pane.Id}");
    }

    [Fact]
    public void EveryJetHasItsOwnIdAndPlace()
    {
        Jet[] all = [.. DemoJets().Pairs.SelectMany(Both)];
        all.Select(jet => jet.Id).Should().OnlyHaveUniqueItems();
        all.Select(jet => jet.Place).Should().OnlyHaveUniqueItems();
        all.Should().HaveCount(24);
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 along = b - a;
        float t = Math.Clamp(Vector2.Dot(point - a, along) / along.LengthSquared(), 0, 1);
        return Vector2.Distance(point, a + along * t);
    }

    /// <summary>The distance from <paramref name="point"/> to the flat convex polygon <paramref name="corners"/>, zero inside it.</summary>
    private static double DistanceToPolygon(Double3 point, Vector3[] corners)
    {
        Double3[] c = [.. corners.Select(corner => (Double3)corner)];
        Double3 normal = Double3.Zero;
        for (int k = 0; k < c.Length; k++)
            normal += Double3.Cross(c[k], c[(k + 1) % c.Length]);
        normal = normal.Normalized();
        double height = Double3.Dot(point - c[0], normal);
        Double3 onPlane = point - normal * height;
        bool inside = Enumerable.Range(0, c.Length).All(k =>
            Double3.Dot(Double3.Cross(c[(k + 1) % c.Length] - c[k], onPlane - c[k]), normal) >= 0);
        double across = inside
            ? 0
            : Enumerable.Range(0, c.Length).Min(k =>
            {
                Double3 a = c[k], along = c[(k + 1) % c.Length] - a;
                double t = Math.Clamp(Double3.Dot(onPlane - a, along) / along.LengthSquared, 0, 1);
                return (onPlane - (a + along * t)).Length;
            });
        return Math.Sqrt(across * across + height * height);
    }
}
