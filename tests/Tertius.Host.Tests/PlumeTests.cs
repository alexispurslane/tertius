using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo's plumes, on the World and the real Jolt backend (docs/plans/blue-danube-demo.md, "The plumes"): the main
/// engine, each of the shuttle's jets and the person's back jet set the emitter of their own part in the tick of their
/// push, from their own throttle, so every force shows and no force shows none. The hot exhaust of the main engine glows,
/// and the cold gas of the jets is lit. Each part's -Z, the way its puffs leave, points against its push.
/// </summary>
public sealed class PlumeTests : IDisposable
{
    private readonly World world = HostTesting.NewJoltWorld();
    private readonly FrameHandle space;
    private Entity shuttle;

    public PlumeTests() => space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));

    public void Dispose() => world.Dispose();

    /// <summary>Parks the shuttle in <paramref name="frame"/>, turned by <paramref name="turn"/>, with <paramref name="components"/>, and fits its jets.</summary>
    private void Launch(FrameHandle frame, DQuaternion turn, params IComponent[] components)
    {
        shuttle = Shuttle.Park(world, frame, new DPose(Double3.Zero, turn), components: [new Unpark(), .. components]);
        Shuttle.FitJets(world, shuttle);
    }

    private ShuttleJetControl Control => world.GetComponent<ShuttleJetControl>(shuttle)!;

    private PuffEmitter EmitterOf(Entity part) => world.Snapshot.RenderOf(part).Emitter;

    /// <summary>The way that the puffs of <paramref name="part"/> leave it, its -Z, in the axes of <paramref name="frame"/>.</summary>
    private Double3 ExhaustOf(Entity part, FrameHandle frame) => world.PoseIn(part, frame).Rotation.Rotate(-Double3.UnitZ);

    /// <summary>The rows of the puffs in <paramref name="frame"/>, or none.</summary>
    private Puff[] PuffsIn(FrameHandle frame)
    {
        PuffView view = world.Puffs.View;
        foreach (PuffRange range in view.Ranges)
            if (range.Frame == frame)
                return view.Rows.Slice(range.Start, range.Count).ToArray();
        return [];
    }

    private static void ShouldBeNear(Double3 actual, Double3 expected, double tolerance, string because)
    {
        (actual - expected).Length.Should().BeLessThan(tolerance, $"{because}: the value is {actual}, not {expected}");
    }

    /// <summary>
    /// A plume's rate grows with the square root of the throttle, from none at 0 to its full rate at 1, so a jet at a
    /// hundredth of its thrust still makes a tenth of its full rate. Its speed, its spread and its look stay the same, so
    /// its puffs share one look however the throttle moves.
    /// </summary>
    [Theory]
    [InlineData(0.01f, 0.1f)]
    [InlineData(0.25f, 0.5f)]
    [InlineData(1f, 1f)]
    public void APlumesRateGrowsWithTheSquareRootOfTheThrottleAndKeepsItsLook(float throttle, float share)
    {
        Plume plume = MainEngine.Plume;

        PuffEmitter emitter = plume.At(throttle);

        emitter.Rate.Should().BeApproximately(share * plume.FullRate, 1e-4f * plume.FullRate);
        emitter.Speed.Should().Be(plume.Speed);
        emitter.Spread.Should().Be(plume.Spread);
        emitter.Look.Should().Be(plume.Look);
        emitter.CanMakePuffs.Should().BeTrue();
    }

    /// <summary>A plume at throttle 0 is the default emitter, which makes no puffs.</summary>
    [Fact]
    public void APlumeAtThrottleZeroMakesNoPuffs()
    {
        PuffEmitter emitter = JetThruster.Plume.At(0);

        emitter.Should().Be(default(PuffEmitter));
        emitter.CanMakePuffs.Should().BeFalse();
    }

    /// <summary>The main engine's exhaust is hot gas that glows: its own gas is hotter than the air, and it emits its light.</summary>
    [Fact]
    public void TheMainEnginesExhaustIsHotAndGlows()
    {
        PuffLook look = MainEngine.Plume.Look;

        look.IsValid.Should().BeTrue();
        look.TemperatureRatio.Should().BeGreaterThan(1);
        look.Shading.TryGetValue(out GlowPuffs _).Should().BeTrue("hot exhaust emits its own light");
        MainEngine.Plume.FullRate.Should().BePositive();
    }

    /// <summary>
    /// The shuttle's jets and the person's back jet blow cold gas: its own gas is colder than the air, and the sun and the
    /// lamps light it.
    /// </summary>
    [Fact]
    public void TheJetsAndTheBackJetBlowColdLitGas()
    {
        foreach (Plume plume in new[] { JetThruster.Plume, BackJet.Plume })
        {
            plume.Look.IsValid.Should().BeTrue();
            plume.Look.TemperatureRatio.Should().BeLessThan(1);
            plume.Look.Shading.TryGetValue(out LitPuffs _).Should().BeTrue("cold gas only scatters the light that reaches it");
            plume.FullRate.Should().BePositive();
        }
    }

    /// <summary>
    /// In each tick, the main engine's part sets its own emitter from the throttle that it pushes with, so its rate follows
    /// the throttle.
    /// </summary>
    [Theory]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void TheMainEnginesEmitterFollowsItsThrottle(float throttle)
    {
        Launch(space, DQuaternion.Identity, new ThrottleFor(int.MaxValue, throttle));

        Tick(world, 4);

        EmitterOf(Control.MainEnginePart).Should().Be(MainEngine.Plume.At(throttle));
    }

    /// <summary>At throttle 0 the main engine pushes nothing, and its part makes no puffs.</summary>
    [Fact]
    public void TheMainEngineAtThrottleZeroMakesNoPuffs()
    {
        Launch(space, DQuaternion.Identity, new ThrottleFor(int.MaxValue, 0));

        Tick(world, 30);

        EmitterOf(Control.MainEnginePart).CanMakePuffs.Should().BeFalse();
        world.Puffs.Count.Should().Be(0);
    }

    /// <summary>
    /// A force along the hull's +X at half the thrust of a translation pair fires the pair <c>move-starboard</c> at half
    /// throttle. Both of its jets set their emitters at half the jets' full rate, and every other jet's part, which gets no
    /// throttle, makes no puffs.
    /// </summary>
    [Fact]
    public void EachJetsEmitterFollowsItsPairsThrottleAndTheOtherJetsShowNone()
    {
        Launch(space, DQuaternion.Identity, new TranslationFor(new Double3(Shuttle.TranslationThrust / 2, 0, 0)));

        Tick(world, 4);

        int fired = ShuttleJets.Translation(0, negative: false);
        for (int pair = 0; pair < ShuttleJets.PairCount; pair++)
            foreach (bool second in new[] { false, true })
            {
                PuffEmitter emitter = EmitterOf(Control.JetParts[ShuttleJetControl.PartIndex(pair, second)]);
                if (pair == fired)
                    emitter.Should().Be(JetThruster.Plume.At(0.5f), $"pair {pair} fires at half throttle");
                else
                    emitter.CanMakePuffs.Should().BeFalse($"pair {pair} gets no throttle");
            }
    }

    /// <summary>
    /// The main engine burns for 20 ticks and then gets no throttle. Two ticks after its last push its part has cleared
    /// its emitter, and from then on no puff is born: the count of the puffs, which live longer than the rest of the run,
    /// stays the same.
    /// </summary>
    [Fact]
    public void APartWithNoPushInATickMakesNoPuffs()
    {
        Launch(space, DQuaternion.Identity, new ThrottleFor(20, 1));
        Tick(world, 26);
        int puffs = world.Puffs.Count;

        Tick(world, 30);

        puffs.Should().BePositive("the burn made puffs");
        EmitterOf(Control.MainEnginePart).CanMakePuffs.Should().BeFalse();
        world.Puffs.Count.Should().Be(puffs, "no part pushes, so no puff is born and none has lived its life yet");
    }

    /// <summary>
    /// On a turned hull, the main engine's part blows its puffs aft, along the hull's +Z, against its push toward the nose,
    /// and each jet's part blows them against the jet's push.
    /// </summary>
    [Fact]
    public void EachPartsPuffsLeaveAgainstItsPush()
    {
        DQuaternion turn = DQuaternion.FromAxisAngle(new Double3(1, 2, -0.5).Normalized(), 0.7);
        Launch(space, turn);

        Tick(world, 2);

        ShouldBeNear(ExhaustOf(Control.MainEnginePart, space), turn.Rotate(Double3.UnitZ), 1e-6, "the main engine blows aft");
        for (int pair = 0; pair < ShuttleJets.PairCount; pair++)
        {
            JetPair each = Control.Layout.Pairs[pair];
            foreach ((Jet jet, bool second) in new[] { (each.First, false), (each.Second, true) })
                ShouldBeNear(ExhaustOf(Control.JetParts[ShuttleJetControl.PartIndex(pair, second)], space), -turn.Rotate(jet.Push), 1e-6,
                    $"{jet.Id} blows against its push");
        }
    }

    /// <summary>
    /// The person's back jet sets its emitter from the size of the force that it pushes with, as a share of the person's
    /// most thrust, and turns its offset so that its puffs leave against the force. A force of zero makes no puffs.
    /// </summary>
    [Theory]
    [InlineData(52.5f, 0, 0)]
    [InlineData(0, -105, 0)]
    [InlineData(30, 40, -60)]
    public void TheBackJetsEmitterFollowsItsForceAndBlowsAgainstIt(float x, float y, float z)
    {
        var force = new Vector3(x, y, z);
        DQuaternion turn = DQuaternion.FromAxisAngle(new Double3(-0.3, 1, 0.4).Normalized(), 1.1);
        var person = new JetForceFor(force);
        Entity body = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(Double3.Zero, turn),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.3f, 0.9f, 0.3f)), Mass: 70),
            Components = [person],
        });

        Tick(world, 5);

        // The box is no character, so the jet's lever turns it: the force is in its axes as they are now.
        DQuaternion now = world.PoseIn(body, space).Rotation;
        EmitterOf(person.Jet).Should().Be(BackJet.Plume.At(force.Length() / PersonControl.ThrustForce));
        ShouldBeNear(ExhaustOf(person.Jet, space), -now.Rotate(((Double3)force).Normalized()), 1e-6, "the back jet blows against its force");
    }

    /// <summary>A back jet that gets a force of zero makes no puffs.</summary>
    [Fact]
    public void ABackJetWithNoForceMakesNoPuffs()
    {
        var person = new JetForceFor(Vector3.Zero);
        world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = DPose.Identity,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.3f, 0.9f, 0.3f)), Mass: 70),
            Components = [person],
        });

        Tick(world, 30);

        EmitterOf(person.Jet).CanMakePuffs.Should().BeFalse();
        world.Puffs.Count.Should().Be(0);
    }

    /// <summary>
    /// The shuttle burns its main engine at full throttle for 2 s, in vacuum and in air. Its puffs stay in the frame of
    /// their birth while the hull speeds away along its nose, so every puff lies aft of the nozzle, in the hull's axes.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABurnsPuffsFallBehindTheHullInItsFrame(bool air)
    {
        FrameHandle frame = air
            ? world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Uniform(new Vector3(0, -9.81f, 0)), DemoScenes.Air.Filling(null)))
            : space;
        Launch(frame, DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 3), new ThrottleFor(int.MaxValue, 1));

        Tick(world, 120);

        Puff[] puffs = PuffsIn(frame);
        puffs.Should().NotBeEmpty();
        DPose hull = world.PoseIn(shuttle, frame);
        foreach (Puff puff in puffs)
            hull.Inverse().TransformPoint(puff.Place).Z.Should().BeGreaterThan(Shuttle.NozzleZ, "a puff falls behind the hull");
    }

    /// <summary>
    /// The frames of the long script's scene carry their air: earth-surface holds Earth's air, which thins by the factor e
    /// every 8.5 km, earth and the sun have none, the shuttle's frame has air in its cabin and none outside its hull, and the
    /// station's frame has air in the bay of its hub and none at the hold point outside it.
    /// </summary>
    [Fact]
    public void TheFramesOfTheLongScriptsSceneCarryTheirAir()
    {
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "demo-surface").Build(world, RenderResources.None);
        FrameTree frames = world.Frames;
        MediumField Of(string id) => frames.Medium(world.GetOwnedFrame(scene[id]));

        MediumField ground = Of("earth-surface");
        ground.Fills(new Double3(0, DemoScenes.EarthRadius, 0)).Should().BeTrue();
        ground.DensityAt(new Double3(0, DemoScenes.EarthRadius, 0)).Should().BeApproximately(1.225, 1e-6);
        ground.DensityAt(new Double3(0, DemoScenes.EarthRadius + 8500, 0)).Should().BeApproximately(1.225 / Math.E, 1e-6);
        ground.Drag.Should().BePositive();
        Of("earth").HasValue.Should().BeFalse();
        frames.Medium(world.SunFrame).HasValue.Should().BeFalse();

        MediumField cabin = Of("shuttle");
        cabin.Fills((Double3)Shuttle.SeatStand).Should().BeTrue();
        cabin.Fills(new Double3(Shuttle.Hull.X + 1, 0, 0)).Should().BeFalse();
        cabin.Drag.Should().Be(ground.Drag);

        MediumField station = Of("station");
        station.Fills(new Double3(0, 5, 0)).Should().BeTrue("the bay holds air");
        station.Fills(DemoScenes.DemoStation.HoldPoint).Should().BeFalse("the hold point is in space");
        station.Drag.Should().Be(ground.Drag);
    }

    /// <summary>A test component that adds a <see cref="Throttle"/> of <paramref name="value"/> to its entity's events in each of its first <paramref name="ticks"/> ticks.</summary>
    private sealed class ThrottleFor(int ticks, float value) : IComponent
    {
        private int handled;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (handled++ < ticks)
                events.Add(new Throttle { Value = value });
        }
    }

    /// <summary>A test component that adds a <see cref="Translation"/> of <paramref name="force"/> to its entity's events in every tick.</summary>
    private sealed class TranslationFor(Double3 force) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) => events.Add(new Translation { Force = force });
    }

    /// <summary>
    /// A test component that spawns a back jet on its entity in its first tick, as <see cref="Passenger"/> does, and sends it
    /// <paramref name="force"/>, in the entity's own axes, in every later tick.
    /// </summary>
    private sealed class JetForceFor(Vector3 force) : IComponent
    {
        public Entity Jet { get; private set; }

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (Jet.IsNull)
                Jet = BackJet.Spawn(self);
            else
                outbox.Send(Jet, new JetForce { Force = force });
        }
    }
}
