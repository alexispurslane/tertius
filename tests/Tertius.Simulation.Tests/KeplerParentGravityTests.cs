using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A Kepler frame takes its G·M from the point gravity of its parent frame, so a Kepler frame and the loose bodies in
/// its parent fall on the same G·M. Its <see cref="KeplerMotion"/> checks that rule, and the World checks nothing
/// (docs/plans/rails-components.md, "Where the checks of rails motion live"): a KeplerMotion whose parent has no point
/// gravity with a G·M above 0 writes no sample, so its frame holds still, and it logs that once. So a spawn, a
/// <see cref="KeplerMotion"/> added later and a new gravity field for the parent never throw, and a new G·M of the parent moves the frame from the next sample
/// on. A frame whose parent dies moves up into the grandparent, and orbits the grandparent's G·M, or holds still there.
/// </summary>
public class KeplerParentGravityTests
{
    private const double Mu = 1e9;

    private static readonly KeplerOrbit MoonOrbit = KeplerOrbit.Circular(1000);

    /// <summary>The fields that a Kepler frame cannot orbit in, by the names that the theories use.</summary>
    public static TheoryData<string, GravityField> FieldsWithoutPointGravity => new()
    {
        { "no gravity", GravityField.None },
        { "a uniform field", GravityField.Uniform(new Vector3(0, -9.81f, 0)) },
        { "a point field with G·M 0", GravityField.Point(0) },
    };

    /// <summary>A frame that does not move in the root, with the given field. A Kepler frame orbits in it.</summary>
    private static FrameHandle SpawnPlanet(TestWorld t, GravityField field) =>
        t.World.GetOwnedFrame(t.World.Spawn(new SpawnDesc
        {
            Frame = t.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e6f), field),
        }));

    /// <summary>A frame without a body at the parent's origin, whose <see cref="KeplerMotion"/> follows <see cref="MoonOrbit"/>.</summary>
    private static SpawnDesc KeplerMoon(FrameHandle parent) => new()
    {
        Frame = parent,
        AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        Components = [new KeplerMotion { Orbit = MoonOrbit }],
    };

    /// <summary>A frame that does not move, without a rails component.</summary>
    private static FrameHandle SpawnParkedMoon(TestWorld t, FrameHandle parent) =>
        t.World.GetOwnedFrame(t.World.Spawn(new SpawnDesc
        {
            Frame = parent,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        }));

    // ---- Spawn -----------------------------------------------------------------------------------------------------

    [Fact]
    public void AKeplerFrameCanBeSpawnedInAParentWithPointGravity()
    {
        using var t = new TestWorld();
        FrameHandle planet = SpawnPlanet(t, GravityField.Point(Mu));

        var log = new List<string>();
        t.World.Log += log.Add;

        FrameHandle moon = t.World.GetOwnedFrame(t.World.Spawn(KeplerMoon(planet)));
        t.Run(2);

        t.World.Frames.LocalPose(moon).Position.Should().Be(MoonOrbit.StateAt(t.World.Time, Mu).Position, "the frame stands on its orbit from the second sync point after its spawn");
        log.Should().BeEmpty();
        t.World.Errors.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(FieldsWithoutPointGravity))]
    public void AKeplerFrameWhoseParentHasNoGMSpawnsWithoutAnErrorHoldsStillAndLogsOnce(string why, GravityField field)
    {
        using var t = new TestWorld();
        var log = new List<string>();
        t.World.Log += log.Add;
        FrameHandle planet = SpawnPlanet(t, field);

        FrameHandle moon = t.World.GetOwnedFrame(t.World.Spawn(KeplerMoon(planet)));
        DPose spawned = t.World.Frames.LocalPose(moon);
        t.Run(3);

        t.World.Frames.LocalPose(moon).Should().Be(spawned, why);
        t.World.Frames.LinearVelocity(moon).Should().Be(Double3.Zero);
        log.Should().ContainSingle(line => line.Contains($"{moon}"));
        t.World.Errors.Should().BeEmpty("the game chose the scene, so this is not a bug");
    }

    [Fact]
    public void AKeplerFrameWithoutAParentSpawnsWithoutAnErrorHoldsStillAndLogsOnce()
    {
        using var t = new TestWorld();
        var log = new List<string>();
        t.World.Log += log.Add;

        FrameHandle moon = t.World.GetOwnedFrame(t.World.Spawn(new SpawnDesc
        {
            Pose = TestWorld.At(5),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
            Components = [new KeplerMotion { Orbit = MoonOrbit }],
        }));
        t.Run(3);

        t.World.Frames.LocalPose(moon).Should().Be(TestWorld.At(5));
        log.Should().ContainSingle(line => line.Contains($"{moon}"));
        t.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AWorldWithAKeplerFrameUnderAParentWithoutGMPassesItsInvariantChecks()
    {
        // The test world runs its invariant checks at the end of every sync point.
        using var t = new TestWorld();
        FrameHandle planet = SpawnPlanet(t, GravityField.None);
        t.World.Spawn(KeplerMoon(planet));

        t.Run(3);

        t.World.Invoking(world => world.AssertInvariants()).Should().NotThrow();
    }

    // ---- A KeplerMotion added later --------------------------------------------------------------------------------

    [Fact]
    public void AFrameInAParentWithPointGravityThatGetsAKeplerMotionFollowsItsOrbit()
    {
        using var t = new TestWorld();
        FrameHandle moon = SpawnParkedMoon(t, SpawnPlanet(t, GravityField.Point(Mu)));

        t.World.AddComponent(moon.Entity, new KeplerMotion { Orbit = MoonOrbit });
        t.Run(2);

        t.World.Frames.LocalPose(moon).Position.Should().Be(MoonOrbit.StateAt(t.World.Time, Mu).Position);
        t.World.Errors.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(FieldsWithoutPointGravity))]
    public void AFrameInAParentWithoutPointGravityThatGetsAKeplerMotionHoldsStillAndLogsOnce(string why, GravityField field)
    {
        using var t = new TestWorld();
        var log = new List<string>();
        t.World.Log += log.Add;
        FrameHandle moon = SpawnParkedMoon(t, SpawnPlanet(t, field));
        DPose parked = t.World.Frames.LocalPose(moon);

        t.World.AddComponent(moon.Entity, new KeplerMotion { Orbit = MoonOrbit });
        t.Run(3);

        t.World.Frames.LocalPose(moon).Should().Be(parked, why);
        log.Should().ContainSingle(line => line.Contains($"{moon}"));
        t.World.Errors.Should().BeEmpty("the game chose the scene, so this is not a bug");
    }

    [Fact]
    public void ARootFrameThatGetsAKeplerMotionHoldsStillAndLogsOnce()
    {
        using var t = new TestWorld();
        var log = new List<string>();
        t.World.Log += log.Add;
        FrameHandle root = t.World.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(10)));
        DPose parked = t.World.Frames.LocalPose(root);

        t.World.AddComponent(root.Entity, new KeplerMotion { Orbit = MoonOrbit });
        t.Run(3);

        t.World.Frames.LocalPose(root).Should().Be(parked);
        log.Should().ContainSingle(line => line.Contains($"{root}"));
        t.World.Errors.Should().BeEmpty();
    }

    // ---- A new gravity field for the parent ------------------------------------------------------------------------

    [Fact]
    public void TheParentOfAKeplerFrameCanTakeAPointFieldWithTheSameGM()
    {
        using var t = new TestWorld();
        FrameHandle planet = SpawnPlanet(t, GravityField.Point(Mu));
        t.World.Spawn(KeplerMoon(planet));

        t.World.SetFrameGravity(planet, GravityField.Point(Mu));

        t.World.Frames.Gravity(planet).Should().Be(GravityField.Point(Mu));
    }

    [Theory]
    [InlineData("a larger G·M", 2.0)]
    [InlineData("a smaller G·M", 0.5)]
    public void ANewGMOfAParentMovesTheFramesOnKeplerOrbitsInItFromTheNextSampleOn(string why, double factor)
    {
        using var t = new TestWorld();
        FrameHandle planet = SpawnPlanet(t, GravityField.Point(Mu));
        FrameHandle moon = t.World.GetOwnedFrame(t.World.Spawn(KeplerMoon(planet)));
        t.Run(3);

        t.World.SetFrameGravity(planet, GravityField.Point(factor * Mu));
        t.Run(1);

        // The moon took the sample of the step that ended, which its component wrote before the change.
        t.World.Frames.LocalPose(moon).Position.Should().Be(MoonOrbit.StateAt(t.World.Time, Mu).Position, why);
        t.Run(1);
        (Double3 place, Double3 velocity) = MoonOrbit.StateAt(t.World.Time, factor * Mu);
        t.World.Frames.LocalPose(moon).Position.Should().Be(place, why);
        t.World.Frames.LinearVelocity(moon).Should().Be(velocity, why);
        t.World.Errors.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(FieldsWithoutPointGravity))]
    public void AKeplerFrameWhoseParentLosesItsGMHoldsStillAndLogsOnce(string why, GravityField field)
    {
        using var t = new TestWorld();
        var log = new List<string>();
        t.World.Log += log.Add;
        FrameHandle planet = SpawnPlanet(t, GravityField.Point(Mu));
        FrameHandle moon = t.World.GetOwnedFrame(t.World.Spawn(KeplerMoon(planet)));
        t.Run(3);

        t.World.SetFrameGravity(planet, field);
        // The moon takes the sample that its component wrote before the change, for the step after it.
        t.Run(1);
        DPose stopped = t.World.Frames.LocalPose(moon);
        t.Run(3);

        t.World.Frames.LocalPose(moon).Should().Be(stopped, why);
        t.World.Frames.LinearVelocity(moon).Should().Be(Double3.Zero);
        log.Should().ContainSingle(line => line.Contains($"{moon}"));
        t.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheParentOfAFrameThatIsNotAKeplerFrameCanTakeAnyField()
    {
        using var t = new TestWorld();
        FrameHandle planet = SpawnPlanet(t, GravityField.Point(Mu));
        SpawnParkedMoon(t, planet);

        t.World.SetFrameGravity(planet, GravityField.None);

        t.World.Frames.Gravity(planet).Should().Be(GravityField.None);
    }

    // ---- A parent that is destroyed --------------------------------------------------------------------------------

    /// <summary>
    /// A planet with point gravity in the root, which has none unless the test gives it a field. A moon orbits the planet.
    /// The world ran two ticks, so the moon is away from its first place. The log lines of the world are in
    /// <see cref="Lines"/>.
    /// </summary>
    private sealed class PlanetScene : IDisposable
    {
        public TestWorld T { get; } = new();
        public List<string> Lines { get; } = new();
        public Entity PlanetOwner { get; }
        public FrameHandle Planet { get; }
        public FrameHandle Moon { get; }

        public PlanetScene(GravityField rootField = default, params IComponent[] planetComponents)
        {
            T.World.Log += Lines.Add;
            if (rootField.HasValue)
                T.World.SetFrameGravity(T.Root, rootField);
            PlanetOwner = T.World.Spawn(new SpawnDesc
            {
                Frame = T.Root,
                Pose = TestWorld.At(5e6),
                Components = planetComponents,
                AsFrame = new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Point(Mu)),
            });
            Planet = T.World.GetOwnedFrame(PlanetOwner);
            Moon = T.World.GetOwnedFrame(T.World.Spawn(KeplerMoon(Planet)));
            T.Run(2);
        }

        public void Dispose() => T.Dispose();
    }

    [Fact]
    public void AKeplerFrameWhoseParentDiesMovesUpIntoTheNewParent()
    {
        using var s = new PlanetScene(GravityField.Point(4 * Mu));

        s.T.World.Destroy(s.PlanetOwner);
        s.T.World.Frames.Parent(s.Moon).Should().Be(s.T.Root);
        s.T.Run(2);

        // From the second sync point after the destroy, the moon takes the samples that its component wrote with the root's
        // G·M: it keeps its elements, so it jumps to the same orbit round the root.
        (Double3 place, Double3 velocity) = MoonOrbit.StateAt(s.T.World.Time, 4 * Mu);
        s.T.World.Frames.LocalPose(s.Moon).Position.Should().Be(place);
        s.T.World.Frames.LinearVelocity(s.Moon).Should().Be(velocity);
        s.Lines.Should().NotContain(line => line.Contains($"{s.Moon}"));
        s.T.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void WhenTheNewParentOfAKeplerFrameHasNoGMTheFrameHoldsStillAndLogsOnce()
    {
        using var s = new PlanetScene();

        s.T.World.Destroy(s.PlanetOwner);
        s.T.World.Frames.Parent(s.Moon).Should().Be(s.T.Root);
        // The sample that the moon's component wrote before the destroy, for the next step, moves it for that step.
        s.T.Run(1);
        DPose stopped = s.T.World.Frames.LocalPose(s.Moon);
        s.T.Run(5);

        s.T.World.Frames.LocalPose(s.Moon).Should().Be(stopped);
        s.T.World.Frames.LinearVelocity(s.Moon).Should().Be(Double3.Zero);
        s.T.World.PoseInOwnFrame(s.Moon.Entity).Should().Be(s.T.World.Frames.LocalPose(s.Moon), "the owner mirrors its frame");
        s.Lines.Should().ContainSingle(line => line.Contains($"{s.Moon}"));
        s.T.World.Errors.Should().BeEmpty("the game destroyed the planet, so this is not a bug");
        s.T.World.Invoking(world => world.AssertInvariants()).Should().NotThrow();
    }

    [Fact]
    public void AKeplerFrameMovesUpWhenItsParentIsDestroyedDuringATick()
    {
        using var s = new PlanetScene(default, Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy()));

        s.T.Run(1);

        s.T.World.Frames.Parent(s.Moon).Should().Be(s.T.Root);
        s.T.World.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFrameWithoutAKeplerMotionMovesUpWithoutALogWhenItsParentIsDestroyedAndTheGrandparentHasNoPointGravity(bool spins)
    {
        using var s = new PlanetScene();
        FrameHandle pad = s.T.World.GetOwnedFrame(s.T.World.Spawn(new SpawnDesc
        {
            Frame = s.Planet,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
            Components = spins ? [new SpinMotion { Spin = new FrameSpin(DQuaternion.Identity, 0.1) }] : [],
        }));

        s.T.World.Destroy(s.PlanetOwner);
        s.T.Run(2);

        s.T.World.Frames.Parent(pad).Should().Be(s.T.Root);
        s.Lines.Should().NotContain(line => line.Contains($"{pad}"));
        s.T.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AKeplerFrameMovesUpToItsGreatGrandparentWhenBothItsParentAndItsGrandparentAreDestroyed()
    {
        using var t = new TestWorld();
        Entity outerOwner = t.World.Spawn(new SpawnDesc
        {
            Frame = t.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e7f), GravityField.Point(Mu)),
        });
        FrameHandle outer = t.World.GetOwnedFrame(outerOwner);
        Entity innerOwner = t.World.Spawn(new SpawnDesc
        {
            Frame = outer,
            Pose = TestWorld.At(1e6),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Point(Mu)),
        });
        FrameHandle moon = t.World.GetOwnedFrame(t.World.Spawn(KeplerMoon(t.World.GetOwnedFrame(innerOwner))));
        t.Run(1);
        t.SpawnWith(SendOnce.Of(outerOwner, new Destroy()), SendOnce.Of(innerOwner, new Destroy()));

        t.Run(2);

        t.World.Frames.IsAlive(moon).Should().BeTrue();
        t.World.Frames.Parent(moon).Should().Be(t.Root);
        t.World.Errors.Should().BeEmpty();
    }
}
