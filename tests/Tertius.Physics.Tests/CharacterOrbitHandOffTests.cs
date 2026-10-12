using System.Numerics;

namespace Tertius.Physics.Tests;

/// <summary>
/// A person drifts out of a ship in orbit, as in the demo scene "orbit" (ticket ter-l6oo). The ship's frame falls freely
/// in the point gravity of the Kepler frame <c>earth</c>, so the person is weightless in the ship's frame outside the
/// cabin. Past the ship's bubble, physics hands the person to <c>earth</c>, where gravity is 8.7 m/s². The person falls
/// freely there, so it does not stand, and the one rule for a character that does not stand (User) holds: it keeps its
/// momentum, gravity and forces change its velocity, and nothing turns it.
/// </summary>
public class CharacterOrbitHandOffTests
{
    private const double SunMu = 1.32712440018e20;
    private const double EarthMu = 3.986004418e14;
    private const double OrbitRadius = 6_371_000 + 400_000;
    private const float PersonMass = 70;
    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: PersonMass);

    /// <summary>
    /// The ship's attitude in <c>earth</c>: 90 degrees about +X, so the ship's +Y is earth's +Z, along the orbit, and the
    /// ship's +Z is earth's -Y. Neither is along the radius, which is earth's +X where the ship starts.
    /// </summary>
    private static readonly DQuaternion ShipAttitude = DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2);

    /// <summary>What happened to the person on the way out of the ship, in <c>earth</c>, and on the way back in.</summary>
    private sealed class Drift
    {
        public FrameHandle Earth;
        public FrameHandle Ship;
        public Entity Person;

        /// <summary>The person's last state in the ship before the hand-off out, and the hand-off itself.</summary>
        public BodyState BeforeExit;
        public HandOff Exit;
        /// <summary>The ship's state after the step of the hand-off out, relative to <c>earth</c>.</summary>
        public FrameState ShipAtExit;

        /// <summary>The person's state in <c>earth</c> after each step there, the step of the hand-off out first.</summary>
        public List<BodyState> InEarth = new();
        /// <summary>Whether physics stood the person, after each step, from the start. Null for a step that did not say.</summary>
        public List<bool?> Standing = new();

        /// <summary>The state in <c>earth</c> before the step of the push, and the force of the push, in newtons.</summary>
        public BodyState BeforePush;
        public BodyState AfterPush;
        public Vector3 Push;

        /// <summary>The person's last state in <c>earth</c> before the hand-off back, the hand-off, and the ship's pose after its step.</summary>
        public BodyState BeforeEntry;
        public HandOff Entry;
        public DPose ShipPoseAtEntry;
    }

    /// <summary>
    /// Runs the drift: the person floats 97 m aft of the ship's center, outside the cabin, and drifts aft at 3 m/s. Physics
    /// hands it to <c>earth</c> after about 2 s. After 30 steps in <c>earth</c>, a push of one step reverses its drift
    /// relative to the ship, and it drifts back into the ship's bubble.
    /// </summary>
    private static Drift Run()
    {
        var run = new Drift();
        using var h = new PhysicsHarness();
        FrameHandle sun = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e13f), MotionDriver.Static, GravityField.Point(SunMu));
        run.Earth = h.AddFrame(sun, DPose.Identity, FrameBounds.Sphere(9.25e8f), MotionDriver.Kepler, GravityField.Point(EarthMu),
            KeplerOrbit.Circular(1.496e11, SunMu));
        var hull = new BodyDesc(new BoxShapeDesc(new Vector3(10, 4, 30)), Mass: 1e5f, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(1));
        var cabin = new BoundingBox(new Vector3(-9.5f, -3.5f, -29.5f), new Vector3(9.5f, 3.5f, 29.5f));
        run.Ship = h.AddFrameWithBody(run.Earth, new DPose(new Double3(OrbitRadius, 0, 0), ShipAttitude), FrameBounds.Sphere(100), MotionDriver.Physics, hull,
            GravityField.Uniform(new Vector3(0, -9.81f, 0), cabin), velocity: new Vector3(0, 0, (float)Math.Sqrt(EarthMu / OrbitRadius)));
        run.Person = h.AddBody(run.Ship, Person, Shapes.At(0, 0, 97), velocity: new Vector3(0, 0, 3));
        Entity person = run.Person;

        HandOff? handOff = null;
        FrameState shipState = default;
        h.AfterStep = output =>
        {
            handOff = null;
            foreach (HandOff each in output.HandOffs)
                if (each.Entity == person)
                    handOff = each;
            shipState = output.Frames.Single(f => f.Frame == run.Ship);
        };
        void Step()
        {
            h.Step();
            run.Standing.Add(h.Standing.TryGetValue(person, out bool standing) ? standing : null);
            if (h[person].Frame == run.Earth)
                run.InEarth.Add(h[person]);
        }

        // Out of the ship.
        for (int i = 0; i < 300 && handOff is null; i++)
        {
            run.BeforeExit = h[person];
            Step();
        }
        run.Exit = handOff ?? throw new InvalidOperationException("The person never left the ship.");
        run.ShipAtExit = shipState;

        // In earth for 30 steps, then the push. 25,200 N for one step on 70 kg is 6 m/s along earth's +Y, which is
        // the ship's -Z: the 3 m/s aft drift becomes 3 m/s forward.
        for (int i = 0; i < 30; i++)
            Step();
        run.BeforePush = h[person];
        run.Push = new Vector3(0, 25_200, 0);
        h.Intent(person).Force = run.Push;
        Step();
        run.AfterPush = h[person];

        // Back into the ship.
        for (int i = 0; i < 300 && h[person].Frame != run.Ship; i++)
        {
            run.BeforeEntry = h[person];
            Step();
        }
        run.Entry = handOff ?? throw new InvalidOperationException("The person never came back into the ship.");
        run.ShipPoseAtEntry = h.Frames.LocalPose(run.Ship);
        return run;
    }

    private static Vector3 UpOf(DQuaternion rotation) => rotation.Rotate(Vector3.UnitY);

    [Fact]
    public void ThePersonLeavesTheShipIntoEarthWithItsPositionAndVelocity()
    {
        Drift run = Run();

        run.Exit.From.Should().Be(run.Ship);
        run.Exit.To.Should().Be(run.Earth);
        // In the ship the person coasts in a straight line, so at the end of the step it is one step further along.
        Double3 inShip = run.BeforeExit.Pose.Position + (Double3)run.BeforeExit.LinearVelocity * PhysicsHarness.Dt;
        inShip.Length.Should().BeInRange(102, 104, "the person leaves the bubble of 100 m once it is fully outside it, with a margin");
        DPose ship = run.ShipAtExit.LocalPose;
        (run.Exit.Pose.Position - ship.TransformPoint(inShip)).Length.Should().BeLessThan(1e-2);
        Double3 velocity = run.ShipAtExit.LinearVelocity + ship.TransformDirection(run.BeforeExit.LinearVelocity);
        ((Double3)run.Exit.LinearVelocity - velocity).Length.Should().BeLessThan(1e-2);
    }

    [Fact]
    public void InEarthThePersonKeepsTheUpAndTheRotationThatItHadInTheShip()
    {
        Drift run = Run();

        // The person never turned in the ship, so its rotation in earth is the ship's attitude.
        Quaternion attitude = ShipAttitude.ToQuaternion();
        run.InEarth.Should().HaveCountGreaterThan(30);
        foreach (BodyState state in run.InEarth)
        {
            Vector3.Distance(UpOf(state.Pose.Rotation), UpOf(ShipAttitude)).Should().BeLessThan(1e-4f, "gravity does not turn a person in free fall");
            Math.Abs(Quaternion.Dot(state.Pose.Rotation.ToQuaternion(), attitude)).Should().BeGreaterThan(1 - 1e-6f, "the body does not turn either");
        }
    }

    [Fact]
    public void InEarthGravityBendsThePersonsPathAndAForceChangesItsVelocity()
    {
        Drift run = Run();

        // Over the 30 steps before the push, gravity adds g · 0.5 s, about 4.35 m/s toward Earth's center.
        BodyState first = run.InEarth[0];
        BodyState last = run.BeforePush;
        Double3 middle = (first.Pose.Position + last.Pose.Position) * 0.5;
        Double3 gravity = middle * (-EarthMu / Math.Pow(middle.Length, 3));
        Double3 expected = (Double3)first.LinearVelocity + gravity * (30 * PhysicsHarness.Dt);
        ((Double3)last.LinearVelocity - expected).Length.Should().BeLessThan(0.02);
        (gravity * (30 * PhysicsHarness.Dt)).Length.Should().BeGreaterThan(4, "the bend is far larger than the tolerance");

        // The push adds F / m · dt to the velocity, besides one step of gravity.
        Double3 pushed = (Double3)run.BeforePush.LinearVelocity + gravity * PhysicsHarness.Dt + (Double3)(run.Push / PersonMass * PhysicsHarness.Dt);
        ((Double3)run.AfterPush.LinearVelocity - pushed).Length.Should().BeLessThan(0.01);
    }

    [Fact]
    public void ThePersonComesBackIntoTheShipWithItsPositionVelocityAndUp()
    {
        Drift run = Run();

        run.Entry.From.Should().Be(run.Earth);
        run.Entry.To.Should().Be(run.Ship);
        // The person enters the bubble of 100 m on its first touch. Its radius is 0.9 m.
        run.Entry.Pose.Position.Length.Should().BeInRange(100, 101.5);
        // It drifts forward at 3 m/s relative to the ship: along the ship's -Z.
        Vector3.Distance(run.Entry.LinearVelocity, new Vector3(0, 0, -3)).Should().BeLessThan(0.05f);
        // The earth state of the step before, one step further along, in the ship's axes after the step.
        Double3 inEarth = run.BeforeEntry.Pose.Position + (Double3)run.BeforeEntry.LinearVelocity * PhysicsHarness.Dt;
        (run.Entry.Pose.Position - run.ShipPoseAtEntry.InverseTransformPoint(inEarth)).Length.Should().BeLessThan(1e-2);
        Vector3.Distance(UpOf(run.Entry.Pose.Rotation), Vector3.UnitY).Should().BeLessThan(1e-4f, "the person comes back as upright in the ship as it left");
    }

    [Fact]
    public void ThePersonNeverStandsOnTheWayOutAndBack()
    {
        Drift run = Run();

        run.Standing.Should().NotBeEmpty().And.AllSatisfy(standing => standing.Should().Be(false));
    }
}
