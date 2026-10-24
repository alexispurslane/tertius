using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>The emitters, looks and reads that the tests of puffs on the World share.</summary>
internal static class PuffTesting
{
    /// <summary>Hot exhaust that lives 10 s: it glows, and its own gas is three times as hot as the medium.</summary>
    public static readonly PuffLook Hot = new(Life: 10, StartRadius: 0.2f, EndRadius: 1, TemperatureRatio: 3,
        Shading: PuffShading.Glow(new(6, 5.4f, 4.5f)));

    /// <summary>Cold gas that lives 10 s, at three quarters of the medium's temperature.</summary>
    public static readonly PuffLook Cold = Hot with { TemperatureRatio = 0.75f, Shading = PuffShading.Lit(new(0.5f)) };

    /// <summary>An emitter along its entity's local -Z with no spread.</summary>
    public static PuffEmitter Jet(PuffLook look, float rate = 60, float speed = 0) =>
        new(Rate: rate, Speed: speed, Spread: 0, Look: look);

    public static Entity SpawnEmitter(World world, FrameHandle frame, Double3 place, PuffEmitter emitter,
        params IComponent[] components) =>
        world.Spawn(new()
        {
            Frame = frame,
            Pose = new(place, DQuaternion.Identity),
            Render = new RenderData { Emitter = emitter },
            Components = components
        });

    /// <summary>An emitter attached to <paramref name="parent"/> at <paramref name="offset"/>, as a part of it.</summary>
    public static Entity AttachEmitter(World world, Entity parent, Vector3 offset, PuffEmitter emitter) => world.Spawn(
        new()
        {
            AttachTo = parent,
            AttachOffset = new(offset, Quaternion.Identity),
            Render = new RenderData { Emitter = emitter }
        });

    /// <summary>The rows of <paramref name="frame"/>, oldest first, or none.</summary>
    public static Puff[] Rows(World world, FrameHandle frame)
    {
        PuffView view = world.Puffs.View;
        foreach (PuffRange range in view.Ranges)
            if (range.Frame == frame)
                return view.Rows.Slice(range.Start, range.Count).ToArray();
        return [];
    }

    public static FrameHandle[] RangeFrames(World world) =>
        [.. world.Puffs.View.Ranges.ToArray().Select(range => range.Frame)];

    /// <summary>A root frame without a field, and in it a station that spins about its +Y at <paramref name="rate"/> rad/s from its first step.</summary>
    public static (FrameHandle Space, FrameHandle Station) SpinningStation(World world, double rate,
        MediumField medium = default)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        Entity owner = world.Spawn(new SpawnDesc
                { Frame = space, AsFrame = new(FrameBounds.Sphere(150), default, medium) }
            .Spinning(world, new(Tilt: DQuaternion.Identity, Rate: rate), firstMove: true));
        return (space, world.GetOwnedFrame(owner));
    }

    /// <summary>A dynamic hull of 1,000 kg in <paramref name="space"/> that owns a frame with a bubble of 20 m.</summary>
    public static Entity Hull(World world, FrameHandle space, Double3 place = default, Double3 velocity = default,
        params IComponent[] components) =>
        world.Spawn(new()
        {
            Frame = space,
            Pose = new(place, DQuaternion.Identity),
            LinearVelocity = velocity,
            Body = new(Shape: new BoxShapeDesc(new(2, 1, 5)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new(FrameBounds.Sphere(20)),
            Components = components
        });
}

/// <summary>
/// How puffs move on the World and the real Jolt backend (docs/plans/particles.md, "The rule of motion" and "The flow per
/// kind of frame"): a puff is a loose body without a shape in the frame of its birth, which its frame's motion, its field
/// and its medium move. The expected values come from the free body's straight line in inertial space, from the motion of
/// the frame, or from a real body that physics moves beside the puff.
/// </summary>
public sealed class PuffPhysicsTests
{
    private const double StationRate = 0.31321;

    [Fact]
    public void APuffInOrbitStaysWithTheFallingShip()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle earth = world.CreateRootFrame(new(FrameBounds.Sphere(1e9f), GravityField.Point(Scenes.EarthMu)));
        const double radius = 6.8e6;
        Entity hull = PuffTesting.Hull(world: world, space: earth, place: new(radius, 0, 0),
            velocity: new(0, 0, Math.Sqrt(Scenes.EarthMu / radius)));
        // A part with speed 0 at the stern: its puffs leave at the hull's own velocity.
        Entity jet = PuffTesting.AttachEmitter(world: world, parent: hull, offset: new(0, 0, -6),
            emitter: PuffTesting.Jet(PuffTesting.Hot));

        Tick(world, 120);

        Puff[] rows = PuffTesting.Rows(world, earth);
        rows.Should().HaveCount(120, "the jet makes one puff per step, in the hull's parent frame");
        Double3 jetNow = world.PoseIn(jet, earth).Position;
        double worst = rows.Max(row => Double3.Distance(row.Place, jetNow));
        // A puff that took the jet's velocity as its chord would be 0.14 m off after 2 s.
        worst.Should().BeLessThan(0.01,
            "each puff falls with the ship, beside the place on the hull where it was born");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APuffInTheSpinningStationCurvesAsAnInertialLineDoes()
    {
        using World world = NewWorld(backend: out _);
        (FrameHandle space, FrameHandle station) = PuffTesting.SpinningStation(world, StationRate);
        Tick(world, 10);
        // At 60 puffs per second each newborn is born at the end of its step, to within a millionth of a step.
        PuffTesting.SpawnEmitter(world: world, frame: station, place: new(8, 0, 0),
            emitter: PuffTesting.Jet(PuffTesting.Hot, speed: 5));
        world.Tick();
        Puff born = PuffTesting.Rows(world, station)[0];
        DPose atBirth = world.Frames.Relative(station, space);
        double birthTime = world.Time;

        Tick(world, 60);

        DPose now = world.Frames.Relative(station, space);
        Double3 start = atBirth.TransformPoint(born.Place);
        Double3 inertial = atBirth.TransformDirection(new(0, 0, -5)) +
                           Double3.Cross(new(0, StationRate, 0), start - atBirth.Position);
        Double3 expected = start + inertial * (world.Time - birthTime);
        Double3 actual = now.TransformPoint(PuffTesting.Rows(world, station)[0].Place);
        Double3.Distance(actual, expected).Should().BeLessThan(1e-6);
        Double3.Distance(now.InverseTransformPoint(expected), born.Place).Should()
            .BeGreaterThan(1, "the station turned under the puff");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APuffMovesAsALooseBodyThatLeavesTheEmitterWithTheSameVelocity()
    {
        using World world = NewWorld(backend: out _);
        (_, FrameHandle station) = PuffTesting.SpinningStation(world, StationRate);
        Tick(world, 10);
        PuffTesting.SpawnEmitter(world: world, frame: station, place: new(8, 0, 0),
            emitter: PuffTesting.Jet(PuffTesting.Hot, speed: 5));
        world.Tick();
        Puff born = PuffTesting.Rows(world, station)[0];
        // The sphere starts where the puff is, with the puff's instant velocity in the station: the ejection velocity.
        Entity sphere = world.Spawn(new()
        {
            Frame = station,
            Pose = new(born.Place, DQuaternion.Identity),
            LinearVelocity = new(0, 0, -5),
            Body = new(Shape: new SphereShapeDesc(0.05f), Mass: 0.1f, LinearDamping: 0, AngularDamping: 0)
        });

        Tick(world, 60);

        Double3 puff = PuffTesting.Rows(world, station)[0].Place;
        Double3 body = world.PoseIn(sphere, station).Position;
        Double3.Distance(puff, body).Should().BeLessThan(1e-4, "the two differ by the rounding of Jolt's floats");
        Double3.Distance(puff, born.Place).Should().BeGreaterThan(4);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APuffInAThrustingHullsFrameFallsBehindTheHull()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        var thruster = new Thruster(1000) { Acceleration = 4.9f, Direction = Vector3.UnitZ };
        FrameHandle cabin = world.GetOwnedFrame(PuffTesting.Hull(world: world, space: space, components: thruster));
        Tick(world, 10);
        // An emitter without a body at rest in the cabin, with speed 0.
        PuffTesting.SpawnEmitter(world: world, frame: cabin, place: new(1, 0, 2),
            emitter: PuffTesting.Jet(PuffTesting.Hot));
        world.Tick();
        Puff born = PuffTesting.Rows(world, cabin)[0];
        double birthTime = world.Time;

        Tick(world, 60);

        Puff puff = PuffTesting.Rows(world, cabin)[0];
        double age = world.Time - birthTime;
        // Half the thrust times the square of its age toward the stern, as a loose crate in the cabin falls.
        puff.Place.Z.Should().BeApproximately(born.Place.Z - 0.5 * 4.9 * age * age, 1e-3);
        puff.Place.X.Should().BeApproximately(born.Place.X, 1e-9);
        puff.Place.Y.Should().BeApproximately(born.Place.Y, 1e-9);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AJetsPuffsFallBehindItsThrustingHullInTheHullsParentFrame()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        var thruster = new Thruster(1000) { Acceleration = 4.9f, Direction = Vector3.UnitZ };
        Entity hull = PuffTesting.Hull(world: world, space: space, velocity: new(0, 0, 3), components: thruster);
        Tick(world, 10);
        PuffTesting.AttachEmitter(world: world, parent: hull, offset: new(0, 0, -6),
            emitter: PuffTesting.Jet(PuffTesting.Hot));
        world.Tick();
        Puff born = PuffTesting.Rows(world, space)[0];
        Double3 offsetAtBirth = born.Place - world.PoseIn(hull, space).Position;
        double birthTime = world.Time;

        Tick(world, 60);

        PuffTesting.RangeFrames(world).Should()
            .Equal([space], "a jet attached to the hull puts its puffs in the hull's parent frame");
        Double3 offset = PuffTesting.Rows(world, space)[0].Place - world.PoseIn(hull, space).Position;
        double age = world.Time - birthTime;
        Double3 expected = offsetAtBirth - new Double3(0, 0, 0.5 * 4.9 * age * age);
        Double3.Distance(offset, expected).Should().BeLessThan(1e-3);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APuffInAFrameThatHoldsStillInAFieldFallsWithTheField()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        FrameHandle cabin = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = ground,
            Pose = At(0, 100),
            Body = new(Shape: new BoxShapeDesc(new(2, 0.5f, 2)), Motion: BodyMotion.Static),
            AsFrame = new(FrameBounds.Sphere(30))
        }));
        Tick(world, 5);
        PuffTesting.SpawnEmitter(world: world, frame: cabin, place: new(0, 5, 0),
            emitter: PuffTesting.Jet(PuffTesting.Hot));
        world.Tick();
        Puff born = PuffTesting.Rows(world, cabin)[0];
        double birthTime = world.Time;

        Tick(world, 60);

        double age = world.Time - birthTime;
        PuffTesting.Rows(world, cabin)[0].Place.Y.Should().BeApproximately(born.Place.Y - 0.5 * 9.81f * age * age, 1e-6,
            "the cabin holds still against the ground's gravity, so its linear gravity is that gravity");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void HotExhaustInAMediumRisesAndColdGasSinks()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f),
            GravityField.Uniform(new(0, -9.81f, 0)),
            MediumField.Uniform(1.225f, 2)));
        PuffTesting.SpawnEmitter(world: world, frame: ground, place: new(-5, 0, 0),
            emitter: PuffTesting.Jet(PuffTesting.Hot));
        PuffTesting.SpawnEmitter(world: world, frame: ground, place: new(5, 0, 0),
            emitter: PuffTesting.Jet(PuffTesting.Cold));

        Tick(world, 60);

        Puff[] rows = PuffTesting.Rows(world, ground);
        Puff hot = rows.First(row => world.Puffs.View.Looks[row.Look] == PuffTesting.Hot);
        Puff cold = rows.First(row => world.Puffs.View.Looks[row.Look] == PuffTesting.Cold);
        hot.Place.Y.Should().BeGreaterThan(0.5, "hot exhaust is lighter than the air");
        // Its terminal speed at the age of 1 s: a push of 9.81 · 1.8 up against a drag of 2 · 2.8 per second, 3.15 m/s.
        hot.Chord.Y.Should().BeInRange(2.9, 3.3);
        cold.Place.Y.Should().BeLessThan(-0.2, "cold gas is heavier than the air");
        cold.Chord.Y.Should().BeLessThan(-0.5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void HotGasInASpinningMediumRisesTowardTheAxis()
    {
        using World world = NewWorld(backend: out _);
        (_, FrameHandle station) = PuffTesting.SpinningStation(world, StationRate, MediumField.Uniform(1.225f, 2));
        Tick(world, 10);
        PuffTesting.SpawnEmitter(world: world, frame: station, place: new(8, 0, 0),
            emitter: PuffTesting.Jet(PuffTesting.Hot));
        PuffTesting.SpawnEmitter(world: world, frame: station, place: new(0, 0, 8),
            emitter: PuffTesting.Jet(PuffTesting.Cold));
        world.Tick();

        Tick(world, 60);

        Puff[] rows = PuffTesting.Rows(world, station);
        Puff hot = rows.First(row => world.Puffs.View.Looks[row.Look] == PuffTesting.Hot);
        Puff cold = rows.First(row => world.Puffs.View.Looks[row.Look] == PuffTesting.Cold);
        static double FromAxis(Double3 place) => Math.Sqrt(place.X * place.X + place.Z * place.Z);
        FromAxis(hot.Place).Should()
            .BeLessThan(7.9, "the station's air presses outward, so hot gas rises toward the axis");
        FromAxis(cold.Place).Should().BeGreaterThan(8.05, "and cold gas sinks toward the rim");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APuffInARootFrameFeelsTheRootsFieldAndNoProperAcceleration()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        PuffTesting.SpawnEmitter(world: world, frame: ground, place: new(0, 10, 0),
            emitter: PuffTesting.Jet(PuffTesting.Hot));
        world.Tick();
        Puff born = PuffTesting.Rows(world, ground)[0];
        double birthTime = world.Time;

        Tick(world, 60);

        world.Frames.LastLooseBodyStep(ground).ProperAcceleration.Should().Be(Double3.Zero);
        double age = world.Time - birthTime;
        PuffTesting.Rows(world, ground)[0].Place.Y.Should()
            .BeApproximately(born.Place.Y - 0.5 * 9.81f * age * age, 1e-6);
        world.Errors.Should().BeEmpty();
    }
}
