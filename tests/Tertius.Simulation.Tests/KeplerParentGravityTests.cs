using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A Kepler frame takes its G·M from the point gravity of its parent frame, so a Kepler frame and the loose bodies in
/// its parent fall on the same G·M. So the World refuses every call that would leave a Kepler frame under a parent
/// without that field: a spawn, a new driver, a new orbit, a new gravity field for the parent. A destroyed parent is
/// not a call that the World can refuse. There the Kepler frame goes to the grandparent, and stops if the grandparent
/// has no point gravity.
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
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static, field),
        }));

    /// <summary>Checks that two poses are at most 1 micrometre and a hair of rotation apart, and shows both if they are not.</summary>
    private static void ShouldBeNear(DPose actual, DPose expected)
    {
        (actual.Position - expected.Position).Length.Should().BeLessThanOrEqualTo(1e-6, "{0} should be near {1}", actual, expected);
        Math.Abs(actual.Rotation.X * expected.Rotation.X + actual.Rotation.Y * expected.Rotation.Y +
                 actual.Rotation.Z * expected.Rotation.Z + actual.Rotation.W * expected.Rotation.W).Should().BeGreaterThan(1 - 1e-12);
    }

    private static SpawnDesc KeplerMoon(FrameHandle parent) => new()
    {
        Frame = parent,
        AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Kepler, Orbit: MoonOrbit),
    };

    /// <summary>A frame that does not move and has an orbit, which it keeps for a later Kepler driver.</summary>
    private static FrameHandle SpawnParkedMoon(TestWorld t, FrameHandle parent) =>
        t.World.GetOwnedFrame(t.World.Spawn(new SpawnDesc
        {
            Frame = parent,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static, Orbit: MoonOrbit),
        }));

    // ---- Spawn -----------------------------------------------------------------------------------------------------

    [Fact]
    public void AKeplerFrameCanBeSpawnedInAParentWithPointGravity()
    {
        using var t = new TestWorld();
        FrameHandle planet = SpawnPlanet(t, GravityField.Point(Mu));

        Entity moon = t.World.Spawn(KeplerMoon(planet));

        t.World.Frames.Driver(t.World.GetOwnedFrame(moon)).Should().Be(MotionDriver.Kepler);
    }

    [Theory]
    [MemberData(nameof(FieldsWithoutPointGravity))]
    public void AKeplerFrameIsNotSpawnedInAParentWithoutPointGravity(string why, GravityField field)
    {
        using var t = new TestWorld();
        FrameHandle planet = SpawnPlanet(t, field);
        int live = t.World.Stats.LiveEntities;

        Action spawn = () => t.World.Spawn(KeplerMoon(planet));

        spawn.Should().Throw<ArgumentException>(why).WithMessage($"*{planet}*point gravity*");
        t.World.Stats.LiveEntities.Should().Be(live);
    }

    [Fact]
    public void AKeplerFrameIsNotSpawnedAsARootFrame()
    {
        using var t = new TestWorld();
        int live = t.World.Stats.LiveEntities;

        Action spawn = () => t.World.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Kepler, Orbit: MoonOrbit));

        spawn.Should().Throw<ArgumentException>().WithMessage("*root frame*no parent*");
        t.World.Stats.LiveEntities.Should().Be(live);
    }

    [Fact]
    public void AKeplerFrameIsNotSpawnedDuringATickInAParentWithoutPointGravity()
    {
        using var t = new TestWorld();
        FrameHandle planet = SpawnPlanet(t, GravityField.None);
        ArgumentException? caught = null;
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            try
            {
                self.Spawn(KeplerMoon(planet));
            }
            catch (ArgumentException ex)
            {
                caught = ex;
            }
        }));

        t.Run(1);

        caught.Should().NotBeNull();
        caught!.Message.Should().Contain($"{planet}");
    }

    // ---- A new driver ----------------------------------------------------------------------------------------------

    [Fact]
    public void AFrameInAParentWithPointGravityTakesTheKeplerDriver()
    {
        using var t = new TestWorld();
        FrameHandle moon = SpawnParkedMoon(t, SpawnPlanet(t, GravityField.Point(Mu)));

        t.World.SetFrameDriver(moon, MotionDriver.Kepler);

        t.World.Frames.Driver(moon).Should().Be(MotionDriver.Kepler);
    }

    [Theory]
    [MemberData(nameof(FieldsWithoutPointGravity))]
    public void TheKeplerDriverIsRefusedForAFrameInAParentWithoutPointGravity(string why, GravityField field)
    {
        using var t = new TestWorld();
        FrameHandle moon = SpawnParkedMoon(t, SpawnPlanet(t, field));

        Action set = () => t.World.SetFrameDriver(moon, MotionDriver.Kepler);

        set.Should().Throw<ArgumentException>(why).WithMessage($"*{moon}*point gravity*");
        t.World.Frames.Driver(moon).Should().Be(MotionDriver.Static);
    }

    [Fact]
    public void TheKeplerDriverIsRefusedForARootFrame()
    {
        using var t = new TestWorld();
        FrameHandle root = t.World.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static, Orbit: MoonOrbit));

        Action set = () => t.World.SetFrameDriver(root, MotionDriver.Kepler);

        set.Should().Throw<ArgumentException>().WithMessage($"*{root}*no parent*");
        t.World.Frames.Driver(root).Should().Be(MotionDriver.Static);
    }

    // ---- A new orbit -----------------------------------------------------------------------------------------------

    [Fact]
    public void AnOrbitCanBeSetForAFrameInAParentWithPointGravity()
    {
        using var t = new TestWorld();
        FrameHandle moon = SpawnParkedMoon(t, SpawnPlanet(t, GravityField.Point(Mu)));
        KeplerOrbit wider = KeplerOrbit.Circular(2000);

        t.World.SetFrameOrbit(moon, wider);

        t.World.Frames.Orbit(moon).Should().Be(wider);
    }

    [Theory]
    [MemberData(nameof(FieldsWithoutPointGravity))]
    public void AnOrbitIsRefusedForAFrameInAParentWithoutPointGravity(string why, GravityField field)
    {
        using var t = new TestWorld();
        FrameHandle moon = SpawnParkedMoon(t, SpawnPlanet(t, field));
        KeplerOrbit wider = KeplerOrbit.Circular(2000);

        Action set = () => t.World.SetFrameOrbit(moon, wider);

        set.Should().Throw<ArgumentException>(why).WithMessage($"*{moon}*point gravity*");
        t.World.Frames.Orbit(moon).Should().Be(MoonOrbit);
    }

    [Fact]
    public void AnOrbitIsRefusedForARootFrame()
    {
        using var t = new TestWorld();

        Action set = () => t.World.SetFrameOrbit(t.Root, MoonOrbit);

        set.Should().Throw<ArgumentException>().WithMessage($"*{t.Root}*no parent*");
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
    [InlineData("no gravity", 0)]
    [InlineData("a uniform field", 1)]
    [InlineData("a point field with G·M 0", 2)]
    [InlineData("a point field with a larger G·M", 3)]
    [InlineData("a point field with a smaller G·M", 4)]
    public void TheParentOfAKeplerFrameCannotTakeAFieldThatWouldMoveTheFrameOffItsOrbit(string why, int which)
    {
        using var t = new TestWorld();
        FrameHandle planet = SpawnPlanet(t, GravityField.Point(Mu));
        FrameHandle moon = t.World.GetOwnedFrame(t.World.Spawn(KeplerMoon(planet)));
        GravityField field = which switch
        {
            0 => GravityField.None,
            1 => GravityField.Uniform(new Vector3(0, -9.81f, 0)),
            2 => GravityField.Point(0),
            3 => GravityField.Point(Mu * 2),
            4 => GravityField.Point(Mu / 2),
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, "No such field."),
        };

        Action set = () => t.World.SetFrameGravity(planet, field);

        set.Should().Throw<ArgumentException>(why).WithMessage($"*{planet}*{moon}*");
        t.World.Frames.Gravity(planet).Should().Be(GravityField.Point(Mu));
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

    [Fact]
    public void OnlyTheFrameThatAKeplerFrameOrbitsKeepsItsGM()
    {
        using var t = new TestWorld();
        FrameHandle planet = SpawnPlanet(t, GravityField.Point(Mu));
        // A station in the planet's frame has a point gravity of its own, and a Kepler frame orbits the station.
        FrameHandle station = t.World.SpawnStaticFrame(planet, TestWorld.At(1e6), radius: 1e4f);
        t.World.SetFrameGravity(station, GravityField.Point(1e3));
        FrameHandle satellite = t.World.GetOwnedFrame(t.World.Spawn(new SpawnDesc
        {
            Frame = station,
            AsFrame = new FrameSpec(FrameBounds.Sphere(1), MotionDriver.Kepler, Orbit: KeplerOrbit.Circular(100)),
        }));

        Action planetChange = () => t.World.SetFrameGravity(planet, GravityField.None);
        Action stationChange = () => t.World.SetFrameGravity(station, GravityField.Point(2e3));

        planetChange.Should().NotThrow("the satellite is not a child of the planet, so it does not orbit the planet's G·M");
        stationChange.Should().Throw<ArgumentException>().WithMessage($"*{station}*{satellite}*");
    }

    // ---- A parent that is destroyed --------------------------------------------------------------------------------

    /// <summary>
    /// A planet with point gravity in the root, which has none. A moon orbits the planet. The world ran two ticks, so
    /// the moon is away from its first place. The log lines of the world are in <see cref="Lines"/>.
    /// </summary>
    private sealed class PlanetScene : IDisposable
    {
        public TestWorld T { get; } = new();
        public List<string> Lines { get; } = new();
        public Entity PlanetOwner { get; }
        public FrameHandle Planet { get; }
        public FrameHandle Moon { get; }
        public DPose MoonInRootBefore { get; }

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
                AsFrame = new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static, GravityField.Point(Mu)),
            });
            Planet = T.World.GetOwnedFrame(PlanetOwner);
            Moon = T.World.GetOwnedFrame(T.World.Spawn(KeplerMoon(Planet)));
            T.Run(2);
            MoonInRootBefore = T.World.Frames.Relative(Moon, T.Root);
        }

        public void Dispose() => T.Dispose();
    }

    [Fact]
    public void AKeplerFrameStopsWhereItIsWhenItsParentIsDestroyedAndTheGrandparentHasNoPointGravity()
    {
        using var s = new PlanetScene();

        s.T.World.Destroy(s.PlanetOwner);

        s.T.World.Frames.Parent(s.Moon).Should().Be(s.T.Root);
        s.T.World.Frames.Driver(s.Moon).Should().Be(MotionDriver.Static);
        ShouldBeNear(s.T.World.Frames.Relative(s.Moon, s.T.Root), s.MoonInRootBefore);
        s.T.World.Frames.LinearVelocity(s.Moon).Should().Be(Double3.Zero);
        s.T.World.GetPose(s.Moon.Entity).Should().Be(s.T.World.Frames.LocalPose(s.Moon), "the owner mirrors its frame");
        s.T.World.Invoking(world => world.AssertInvariants()).Should().NotThrow();
    }

    [Fact]
    public void TheMoonThatStoppedStaysWhereItStoppedAsTimeGoesOn()
    {
        using var s = new PlanetScene();
        s.T.World.Destroy(s.PlanetOwner);

        s.T.Run(5);

        ShouldBeNear(s.T.World.Frames.Relative(s.Moon, s.T.Root), s.MoonInRootBefore);
        s.T.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheWorldSaysThatAKeplerFrameStoppedBecauseItsParentWasDestroyed()
    {
        using var s = new PlanetScene();

        s.T.World.Destroy(s.PlanetOwner);

        s.Lines.Should().ContainSingle(line => line.Contains($"{s.Moon}") && line.Contains("Static"))
            .Which.Should().Contain($"{s.T.Root}");
        s.T.World.Errors.Should().BeEmpty("the game destroyed the planet, so this is not a bug");
    }

    [Fact]
    public void AKeplerFrameStopsWhenItsParentIsDestroyedDuringATick()
    {
        using var s = new PlanetScene(default, Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy()));

        s.T.Run(1);

        s.T.World.Frames.Parent(s.Moon).Should().Be(s.T.Root);
        s.T.World.Frames.Driver(s.Moon).Should().Be(MotionDriver.Static);
        s.T.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AKeplerFrameKeepsItsDriverWhenItsParentIsDestroyedAndTheGrandparentHasPointGravity()
    {
        using var s = new PlanetScene(GravityField.Point(4 * Mu));

        s.T.World.Destroy(s.PlanetOwner);

        s.T.World.Frames.Parent(s.Moon).Should().Be(s.T.Root);
        s.T.World.Frames.Driver(s.Moon).Should().Be(MotionDriver.Kepler);
        s.Lines.Should().NotContain(line => line.Contains($"{s.Moon}"));
        s.T.World.Invoking(world => world.AssertInvariants()).Should().NotThrow();
    }

    [Theory]
    [InlineData(MotionDriver.Static)]
    [InlineData(MotionDriver.Spin)]
    public void AFrameWithAnotherDriverKeepsItsDriverWhenItsParentIsDestroyedAndTheGrandparentHasNoPointGravity(MotionDriver driver)
    {
        using var s = new PlanetScene();
        FrameHandle pad = s.T.World.GetOwnedFrame(s.T.World.Spawn(new SpawnDesc
        {
            Frame = s.Planet,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), driver, Spin: new FrameSpin(DQuaternion.Identity, 0.5)),
        }));

        s.T.World.Destroy(s.PlanetOwner);

        s.T.World.Frames.Parent(pad).Should().Be(s.T.Root);
        s.T.World.Frames.Driver(pad).Should().Be(driver);
        s.Lines.Should().NotContain(line => line.Contains($"{pad}"));
    }

    [Fact]
    public void AKeplerFrameThatMovedToTheGrandparentOrbitsTheGMOfTheGrandparent()
    {
        using var s = new PlanetScene(GravityField.Point(4 * Mu));
        s.T.World.Frames.LinearVelocity(s.Moon).Length.Should().BeApproximately(Math.Sqrt(Mu / 1000), 1e-6, "the moon orbited the planet's G·M");

        s.T.World.Destroy(s.PlanetOwner);
        s.T.Run(1);

        // A circle of 1000 m round a parent with four times the G·M goes twice as fast.
        s.T.World.Frames.LinearVelocity(s.Moon).Length.Should().BeApproximately(Math.Sqrt(4 * Mu / 1000), 1e-6);
        s.T.World.Frames.LocalPose(s.Moon).Position.Length.Should().BeApproximately(1000, 1e-6, "the orbit is round the grandparent now");
    }

    [Fact]
    public void AKeplerFrameStopsWhenBothItsParentAndItsGrandparentAreDestroyedAndTheGreatGrandparentHasNoPointGravity()
    {
        using var t = new TestWorld();
        Entity outerOwner = t.World.Spawn(new SpawnDesc
        {
            Frame = t.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static, GravityField.Point(Mu)),
        });
        FrameHandle outer = t.World.GetOwnedFrame(outerOwner);
        Entity innerOwner = t.World.Spawn(new SpawnDesc
        {
            Frame = outer,
            Pose = TestWorld.At(1e6),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static, GravityField.Point(Mu)),
        });
        FrameHandle moon = t.World.GetOwnedFrame(t.World.Spawn(KeplerMoon(t.World.GetOwnedFrame(innerOwner))));
        t.Run(1);
        t.SpawnWith(SendOnce.Of(outerOwner, new Destroy()), SendOnce.Of(innerOwner, new Destroy()));

        t.Run(2);

        t.World.Frames.IsAlive(moon).Should().BeTrue();
        t.World.Frames.Parent(moon).Should().Be(t.Root);
        t.World.Frames.Driver(moon).Should().Be(MotionDriver.Static);
        t.World.Errors.Should().BeEmpty();
    }
}
