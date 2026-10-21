using System.Numerics;
using System.Runtime.CompilerServices;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The life of a puff on the World and the real Jolt backend (docs/plans/particles.md, "The sync point" and "The flow per
/// event in a puff's life"): which step an emitter's puffs start and stop in, what a hand-off, a reparent, a new medium
/// and the death of a frame do to a puff, the end of its life, the cap and its switch, and determinism.
/// </summary>
public class PuffLifeTests
{
    private static int Count(World world) => world.Puffs.Count;

    private static Entity SpawnFrameOwner(World world, FrameHandle parent, Double3 place, float radius = 50, MediumField medium = default,
        params IComponent[] components) =>
        world.Spawn(new SpawnDesc
        {
            Frame = parent,
            Pose = new DPose(place, DQuaternion.Identity),
            AsFrame = new FrameSpec(FrameBounds.Sphere(radius), default, medium),
            Components = components,
        });

    // ---- When an emitter's puffs start and stop -------------------------------------------------------------------

    [Fact]
    public void AJetsFirstPuffsAreBornInTheStepOfItsFirstForce()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        var pilot = new SelfPilot();
        world.Spawn(new SpawnDesc { Frame = space, Body = Ball(), Components = [pilot] });
        Tick(world, 3);

        // Tick N writes the jet's first force and its emitter. The force acts in step N + 1.
        pilot.EveryTick = self =>
        {
            self.Body.AddForce(new Vector3(0, 0, 10));
            self.Render.Emitter = PuffTesting.Jet(PuffTesting.Hot);
        };
        world.Tick();
        Count(world).Should().Be(0, "the sync point of tick N closes step N, in which neither the force nor the emitter acted");

        world.Tick();
        Count(world).Should().Be(1, "the sync point of tick N + 1 closes the step of the first force");
    }

    [Fact]
    public void AnEmitterThatSwitchesOffInTickNMakesItsLastPuffsInStepN()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        var pilot = new SelfPilot();
        PuffTesting.SpawnEmitter(world, space, Double3.Zero, PuffTesting.Jet(PuffTesting.Hot), pilot);
        Tick(world, 3);
        Count(world).Should().Be(3);

        pilot.EveryTick = self => self.Render.Emitter = default;
        world.Tick();
        Count(world).Should().Be(4, "the emitter that tick N - 1 wrote makes the puffs of step N");

        Tick(world, 3);
        Count(world).Should().Be(4, "the emitter of tick N, the default, makes none");
    }

    [Fact]
    public void ASpawnedEmitterMakesItsFirstPuffsInTheStepAfterItsSpawn()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        var spawner = new SelfPilot();
        world.Spawn(new SpawnDesc { Frame = space, Components = [spawner] });
        world.Tick();

        spawner.Once = self => self.Spawn(new SpawnDesc { Frame = space, Render = new RenderData { Emitter = PuffTesting.Jet(PuffTesting.Hot) } });
        world.Tick();
        Count(world).Should().Be(0, "an entity that step 6 made live was not in the step that the sync point closes");

        world.Tick();
        Count(world).Should().Be(1, "its first step is the step of the next tick");

        // An emitter spawned between ticks is live before the next step, so it makes that step's puffs.
        PuffTesting.SpawnEmitter(world, space, Double3.Zero, PuffTesting.Jet(PuffTesting.Hot));
        world.Tick();
        Count(world).Should().Be(3);
    }

    [Fact]
    public void AnEmitterThatDiesMakesNoPuffsForItsLastStepAndItsPuffsLiveOn()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        var pilot = new SelfPilot();
        Entity emitter = PuffTesting.SpawnEmitter(world, space, Double3.Zero, PuffTesting.Jet(PuffTesting.Hot, speed: 2), pilot);
        Tick(world, 3);

        pilot.Once = self => self.Destroy();
        world.Tick();

        world.IsAlive(emitter).Should().BeFalse();
        Count(world).Should().Be(3, "the dead emitter makes no puffs for its last step");
        Double3 before = PuffTesting.Rows(world, space)[0].Place;
        Tick(world, 30);
        Count(world).Should().Be(3, "its puffs live on");
        PuffTesting.Rows(world, space)[0].Place.Should().NotBe(before, "and they move on");
    }

    // ---- A puff's frame hands off, is reparented or dies ----------------------------------------------------------

    /// <summary>A ship whose hull Jolt hands into the bubble of a carrier in the step of tick 20, with an emitter at rest in the ship.</summary>
    private static (FrameHandle Carrier, FrameHandle Ship) ShipFlyingIntoCarrier(World world, FrameHandle space)
    {
        FrameHandle carrier = world.GetOwnedFrame(SpawnFrameOwner(world, space, new Double3(1000, 0, 0), radius: 500));
        Scenes.Approach approach = Scenes.ApproachToBubble(world, carrier, tick: 20, ballRadius: 5);
        FrameHandle ship = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = approach.Pose,
            LinearVelocity = approach.Velocity,
            Body = new BodyDesc(new SphereShapeDesc(5), Layer: BodyLayer.FrameHull, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        }));
        PuffTesting.SpawnEmitter(world, ship, new Double3(1, 0, 0), PuffTesting.Jet(PuffTesting.Hot, speed: 2));
        return (carrier, ship);
    }

    [Fact]
    public void APuffKeepsItsPlaceInItsFrameWhenTheFrameHandsOff()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        (FrameHandle carrier, FrameHandle ship) = ShipFlyingIntoCarrier(world, space);
        world.Tick();
        Puff born = PuffTesting.Rows(world, ship)[0];
        double birthTime = world.Time;

        Tick(world, 30);

        world.Frames.Parent(ship).Should().Be(carrier, "Jolt handed the ship into the carrier");
        Puff puff = PuffTesting.Rows(world, ship)[0];
        Double3 expected = born.Place + new Double3(0, 0, -2) * (world.Time - birthTime);
        Double3.Distance(puff.Place, expected).Should().BeLessThan(1e-6, "the puff keeps flying straight in its frame");
        PuffTesting.RangeFrames(world).Should().Equal([ship]);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APuffKeepsItsPlaceInItsFrameWhenTheGameReparentsTheFrame()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        FrameHandle dock = world.GetOwnedFrame(SpawnFrameOwner(world, space, new Double3(-50, 0, 0)));
        FrameHandle ship = world.GetOwnedFrame(SpawnFrameOwner(world, space, new Double3(-40, 0, 0), radius: 10));
        PuffTesting.SpawnEmitter(world, ship, new Double3(1, 0, 0), PuffTesting.Jet(PuffTesting.Hot, speed: 2));
        Tick(world, 3);
        Puff before = PuffTesting.Rows(world, ship)[0];

        world.Frames.Reparent(ship, dock);
        world.Tick();

        world.Frames.Parent(ship).Should().Be(dock);
        Puff after = PuffTesting.Rows(world, ship)[0];
        after.Place.Should().Be(before.Place + before.Chord * world.DeltaTime, "the reparent leaves the puff's place and chord in its frame");
        after.Chord.Should().Be(before.Chord);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AJetsPuffsKeepTheirAirAfterTheHullLeavesTheFrameOfTheAirAndItsLaterPuffsHaveNone()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        // The air of a planet below, in a frame of 200 m with its own gravity. The parent frame has neither.
        FrameHandle air = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = space,
            AsFrame = new FrameSpec(FrameBounds.Sphere(200), GravityField.Uniform(new Vector3(0, -9.81f, 0)), MediumField.Planet(0, 1.225f, 8500, 2)),
        }));
        Entity hull = PuffTesting.Hull(world, air, new Double3(0, 150, 0), new Double3(0, 100, 0));
        Entity jet = PuffTesting.AttachEmitter(world, hull, new Vector3(0, -6, 0), PuffTesting.Jet(PuffTesting.Hot));

        Tick(world, 150);

        world.GetFrame(hull).Should().Be(space, "the hull climbed out of the frame of the air");
        PuffTesting.RangeFrames(world).Should().Equal(air, space);
        Puff[] inAir = PuffTesting.Rows(world, air);
        Puff[] above = PuffTesting.Rows(world, space);
        inAir.Should().NotBeEmpty();
        above.Should().NotBeEmpty();
        // The air keeps the puffs born in it: they hang there at their terminal speed, far slower than the hull.
        inAir.Should().AllSatisfy(row => row.Chord.Length.Should().BeLessThan(10));
        // The puffs born above have no air: they fly on with the hull, beside the jet.
        Double3 jetNow = world.PoseIn(jet, space).Position;
        above.Should().AllSatisfy(row => Double3.Distance(row.Place, jetNow).Should().BeLessThan(1e-3));
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APuffInADyingFrameMovesIntoTheNearestSurvivingAncestor()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        FrameHandle outer = world.GetOwnedFrame(SpawnFrameOwner(world, space, new Double3(10, 0, 0), radius: 1000));
        // The dying frame moves and turns in the frame that survives it.
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = outer,
            Pose = new DPose(new Double3(5, 2, 0), DQuaternion.FromAxisAngle(Double3.UnitZ, 0.3)),
            LinearVelocity = new Double3(3, 0, 1),
            AngularVelocity = new Vector3(0, 0.5f, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2, 1, 5)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30)),
        });
        FrameHandle dying = world.GetOwnedFrame(owner);
        PuffTesting.SpawnEmitter(world, dying, new Double3(2, 0, 1), PuffTesting.Jet(PuffTesting.Hot, speed: 3));
        Tick(world, 20);
        Puff puff = PuffTesting.Rows(world, dying)[5];
        // A small ball at the puff's place, with the puff's chord as its velocity, which the World rehomes with its row. It
        // goes before the next step, so its row still holds the place and the float velocity of its spawn.
        Entity beside = world.Spawn(new SpawnDesc
        {
            Frame = dying,
            Pose = new DPose(puff.Place, DQuaternion.Identity),
            LinearVelocity = puff.Chord,
            Body = Ball(radius: 0.05f),
        });

        world.Destroy(owner);

        PuffTesting.RangeFrames(world).Should().Equal([outer], "the puffs moved into the nearest surviving ancestor");
        Puff moved = PuffTesting.Rows(world, outer)[5];
        Double3.Distance(moved.Place, world.PoseIn(beside, outer).Position).Should().BeLessThan(1e-12, "the place converts as the entity's does");
        Double3.Distance(moved.Chord, world.VelocityIn(beside, outer)).Should().BeLessThan(1e-5, "the chord converts as the entity's float velocity does");
        moved.Age.Should().Be(puff.Age);
        world.Puffs.AssertInvariants();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APuffOfAFastFrameThatDiesLandsWhereItWasAtTheEndOfTheStep()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f)));
        var pilot = new SelfPilot();
        Entity hull = PuffTesting.Hull(world, space, velocity: new Double3(7500, 0, 0), components: pilot);
        FrameHandle fast = world.GetOwnedFrame(hull);
        PuffTesting.SpawnEmitter(world, fast, new Double3(1, 2, 3), PuffTesting.Jet(PuffTesting.Hot));
        Tick(world, 5);
        Entity beside = world.Spawn(new SpawnDesc { Frame = fast, Pose = new DPose(PuffTesting.Rows(world, fast)[0].Place, DQuaternion.Identity) });

        pilot.Once = self => self.Destroy();
        world.Tick();

        world.IsAlive(hull).Should().BeFalse();
        // A puff converted with its place of the step before would lie 125 m behind.
        Double3.Distance(PuffTesting.Rows(world, space)[0].Place, world.PoseIn(beside, space).Position).Should().BeLessThan(1e-6);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APuffInADyingRootFrameDiesWithIt()
    {
        using World world = NewWorld(out _);
        FrameHandle doomed = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        FrameHandle other = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        PuffTesting.SpawnEmitter(world, doomed, Double3.Zero, PuffTesting.Jet(PuffTesting.Hot, speed: 1));
        PuffTesting.SpawnEmitter(world, other, Double3.Zero, PuffTesting.Jet(PuffTesting.Cold, speed: 1));
        Tick(world, 5);
        int hot = PuffTesting.Rows(world, doomed)[0].Look;

        world.Destroy(doomed.Entity);

        PuffTesting.RangeFrames(world).Should().Equal([other]);
        Count(world).Should().Be(5);
        world.Puffs.Looks.UsersOf(hot).Should().Be(0, "the dead puffs freed their look");
        world.Puffs.Looks.InUse.Should().Be(1);
        world.Puffs.AssertInvariants();
        world.Tick();
        Count(world).Should().Be(6);
    }

    [Fact]
    public void AnEmitterWhoseFrameDiesMakesItsPuffsInTheHeir()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        var pilot = new SelfPilot();
        FrameHandle dying = world.GetOwnedFrame(SpawnFrameOwner(world, space, new Double3(100, 0, 0), components: pilot));
        Entity emitter = PuffTesting.SpawnEmitter(world, dying, new Double3(0, 0, 4), PuffTesting.Jet(PuffTesting.Hot));
        Tick(world, 5);

        pilot.Once = self => self.Destroy();
        world.Tick();

        world.GetFrame(emitter).Should().Be(space);
        PuffTesting.RangeFrames(world).Should().Equal([space]);
        Puff[] rows = PuffTesting.Rows(world, space);
        rows.Should().HaveCount(6, "the 5 old puffs moved into the heir, and the newborn of the step was born there");
        rows[^1].Age.Should().BeLessThan(world.DeltaTime);
        Double3.Distance(rows[^1].Place, new Double3(100, 0, 4)).Should().BeLessThan(1e-9);
        world.Errors.Should().BeEmpty();
    }

    // ---- A puff's medium changes ----------------------------------------------------------------------------------

    /// <summary>The chord that the step of a puff with <paramref name="chord"/> and <paramref name="age"/> keeps in a medium of drag 2, with no field.</summary>
    private static Double3 DraggedChord(Double3 chord, float age, in PuffLook look)
    {
        double own = 1 - age / (double)look.Life;
        double lift = 1 + own * (look.TemperatureRatio - 1);
        return chord / (1 + 2 * lift * (double)(1f / 60));
    }

    [Fact]
    public void APuffMovedIntoAnHeirFeelsTheHeirsMediumFromTheNextStep()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), default, MediumField.Uniform(1.225f, 2)));
        Entity owner = SpawnFrameOwner(world, space, new Double3(50, 0, 0));
        FrameHandle bare = world.GetOwnedFrame(owner);
        PuffTesting.SpawnEmitter(world, bare, Double3.Zero, PuffTesting.Jet(PuffTesting.Hot, speed: 10));
        Tick(world, 5);
        PuffTesting.Rows(world, bare).Should().AllSatisfy(row => row.Chord.Should().Be(new Double3(0, 0, -10)), "the frame has no medium");

        world.Destroy(owner);
        Puff moved = PuffTesting.Rows(world, space)[0];
        moved.Chord.Should().Be(new Double3(0, 0, -10), "the move converts the chord, and the heir's air has not acted yet");
        world.Tick();

        PuffTesting.Rows(world, space)[0].Chord.Should().Be(DraggedChord(moved.Chord, moved.Age, PuffTesting.Hot));
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ANewMediumActsOnThePuffsFromTheNextStep()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        PuffTesting.SpawnEmitter(world, space, Double3.Zero, PuffTesting.Jet(PuffTesting.Hot, speed: 10));
        Tick(world, 5);
        Puff before = PuffTesting.Rows(world, space)[0];
        before.Chord.Should().Be(new Double3(0, 0, -10));

        world.SetFrameMedium(space, MediumField.Uniform(1.225f, 2));
        world.Tick();

        PuffTesting.Rows(world, space)[0].Chord.Should().Be(DraggedChord(before.Chord, before.Age, PuffTesting.Hot));
        world.Errors.Should().BeEmpty();
    }

    // ---- The end of a life, the cap and its switch ----------------------------------------------------------------

    [Fact]
    public void APuffIsGoneAtTheFirstSyncPointPastItsLife()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        var pilot = new SelfPilot { EveryTick = self => self.Render.Emitter = default };
        PuffLook brief = PuffTesting.Hot with { Life = 5.5f / 60 };
        // The spawn's emitter makes the one puff of step 0. Tick 0 writes the default over it.
        PuffTesting.SpawnEmitter(world, space, Double3.Zero, PuffTesting.Jet(brief), pilot);

        Tick(world, 6);
        Count(world).Should().Be(1, "after 5 steps of its life it is 5 steps old, less than its life");

        world.Tick();
        Count(world).Should().Be(0, "the step that brings its age past its life ends it");
        world.Puffs.Looks.InUse.Should().Be(0);
    }

    [Fact]
    public void TheCapDropsTheOldestPuffsFirst()
    {
        using World world = NewWorld(out _, configure: options => options.PuffCapacity = 6);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        FrameHandle a = world.GetOwnedFrame(SpawnFrameOwner(world, space, new Double3(-100, 0, 0)));
        FrameHandle b = world.GetOwnedFrame(SpawnFrameOwner(world, space, new Double3(100, 0, 0)));
        PuffTesting.SpawnEmitter(world, a, Double3.Zero, PuffTesting.Jet(PuffTesting.Hot, speed: 1));
        Tick(world, 3);
        PuffTesting.SpawnEmitter(world, b, Double3.Zero, PuffTesting.Jet(PuffTesting.Hot, speed: 1));

        Tick(world, 3);

        // a's puffs of steps 0 to 2 were the oldest, and the cap dropped them across the frames.
        Count(world).Should().Be(6);
        PuffTesting.Rows(world, a).Should().HaveCount(3);
        PuffTesting.Rows(world, b).Should().HaveCount(3);
        world.Puffs.View.Rows.ToArray().Should().AllSatisfy(row => row.Age.Should().BeLessThan(2.5f / 60));
    }

    [Fact]
    public void ACapacityOfZeroMakesNoPuffs()
    {
        using World world = NewWorld(out _, configure: options => options.PuffCapacity = 0);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        Entity emitter = PuffTesting.SpawnEmitter(world, space, Double3.Zero, PuffTesting.Jet(PuffTesting.Hot, rate: 45));

        Tick(world, 10);

        Count(world).Should().Be(0);
        world.Puffs.View.Ranges.Length.Should().Be(0);
        world.Puffs.Looks.InUse.Should().Be(0);
        world.Puffs.BirthPhase(emitter).Should().Be(0, "the births do nothing");
        world.Errors.Should().BeEmpty();
    }

    // ---- Determinism ----------------------------------------------------------------------------------------------

    /// <summary>
    /// A scene with puffs of every kind: a ground with air, a ball that bounces on the floor with a jet, a hull that thrusts
    /// with a jet and an emitter with a spread in its frame, and an emitter whose look changes every 50 ticks.
    /// </summary>
    private static World BusyScene(int capacity)
    {
        World world = NewWorld(out _, configure: options => options.PuffCapacity = capacity);
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Uniform(new Vector3(0, -9.81f, 0)),
            MediumField.Uniform(1.225f, 2)));
        Floor(world, ground);
        world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, 5),
            Body = Ball(),
            Render = new RenderData { Emitter = new PuffEmitter(90, 8, 0.4f, PuffTesting.Hot) },
        });
        var thruster = new Thruster(1000) { Acceleration = 2, Direction = Vector3.UnitY };
        Entity hull = PuffTesting.Hull(world, ground, new Double3(30, 3, 0), components: thruster);
        PuffTesting.AttachEmitter(world, hull, new Vector3(0, -2, 0), new PuffEmitter(120, 20, 0.2f, PuffTesting.Hot));
        PuffTesting.SpawnEmitter(world, world.GetOwnedFrame(hull), new Double3(0, 1, 0), new PuffEmitter(30, 3, 1.5f, PuffTesting.Cold));
        var changer = new SelfPilot
        {
            EveryTick = self => self.Render.Emitter = new PuffEmitter(60, 5, 0.1f,
                PuffTesting.Cold with { TemperatureRatio = 0.5f + self.Tick / 50 * 0.25f, Life = 1.5f }),
        };
        PuffTesting.SpawnEmitter(world, ground, new Double3(-20, 10, 0), default, changer);
        return world;
    }

    private static ulong PuffHash(World world)
    {
        PuffView view = world.Puffs.View;
        ulong hash = 0;
        foreach (Puff row in view.Rows)
        {
            hash = WorldHash.Mix(hash, row.Place.X);
            hash = WorldHash.Mix(hash, row.Place.Y);
            hash = WorldHash.Mix(hash, row.Place.Z);
            hash = WorldHash.Mix(hash, row.Chord.X);
            hash = WorldHash.Mix(hash, row.Chord.Y);
            hash = WorldHash.Mix(hash, row.Chord.Z);
            hash = WorldHash.Mix(hash, (ulong)BitConverter.SingleToUInt32Bits(row.Age));
            hash = WorldHash.Mix(hash, (ulong)row.Look);
        }
        foreach (PuffRange range in view.Ranges)
            hash = WorldHash.Mix(WorldHash.Mix(WorldHash.Mix(hash, range.Frame.Entity.Raw), (ulong)range.Start), (ulong)range.Count);
        // Each field of each look, because the hash code of a union mixes with a seed that changes from process to process.
        foreach (PuffLook look in view.Looks)
        {
            hash = WorldHash.Mix(WorldHash.Mix(hash, look.Life), look.TemperatureRatio);
            hash = WorldHash.Mix(WorldHash.Mix(hash, look.StartRadius), look.EndRadius);
            if (look.Shading.TryGetValue(out GlowPuffs glow))
                hash = WorldHash.Mix(WorldHash.Mix(hash, 1UL), glow.Radiance);
            else if (look.Shading.TryGetValue(out LitPuffs lit))
                hash = WorldHash.Mix(WorldHash.Mix(hash, 2UL), lit.Albedo);
        }
        return hash;
    }

    [Fact]
    public void PuffsChangeNothingThatGameplayOrPhysicsReads()
    {
        using World off = BusyScene(capacity: 0);
        using World on = BusyScene(capacity: 4096);

        Tick(off, 120);
        Tick(on, 120);

        on.Puffs.Count.Should().BeGreaterThan(100);
        WorldHash.Of(on).Should().Be(WorldHash.Of(off));
    }

    [Fact]
    public void TheSameRunGivesTheSamePuffsBitForBit()
    {
        using World first = BusyScene(capacity: 500);
        using World second = BusyScene(capacity: 500);

        Tick(first, 300);
        Tick(second, 300);

        first.Puffs.Count.Should().Be(500, "the cap binds in this scene");
        PuffHash(second).Should().Be(PuffHash(first));
        // tools/test-all.sh runs this test in Debug and in Release, so both builds must give this hash.
        PuffHash(first).Should().Be(PinnedHash);
        first.Errors.Should().BeEmpty();
    }

    /// <summary>The hash of the puffs of <see cref="BusyScene"/> after 300 ticks.</summary>
    private const ulong PinnedHash = 7840000220236764960;

    // ---- A state that only a bug makes ----------------------------------------------------------------------------

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ranges")]
    private static extern ref List<PuffRange> RangesOf(PuffTable table);

    [Fact]
    public void ARangeInAFrameThatIsNotLiveFailsAContract()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        FrameHandle doomed = world.GetOwnedFrame(SpawnFrameOwner(world, space, new Double3(100, 0, 0)));
        FrameHandle kept = world.GetOwnedFrame(SpawnFrameOwner(world, space, new Double3(-100, 0, 0)));
        PuffTesting.SpawnEmitter(world, doomed, Double3.Zero, PuffTesting.Jet(PuffTesting.Hot));
        PuffTesting.SpawnEmitter(world, kept, Double3.Zero, PuffTesting.Jet(PuffTesting.Cold));
        Tick(world, 4);
        int hot = PuffTesting.Rows(world, doomed)[0].Look;
        // The bug: the frame dies, and its range stays. The proper destroy path moves the range into the heir first.
        world.Destroy(doomed.Entity);
        List<PuffRange> ranges = RangesOf(world.Puffs);
        ranges.Select(range => range.Frame).Should().Equal(space, kept);
        ranges[0] = ranges[0] with { Frame = doomed };

#if TERTIUS_CONTRACTS
        Action tick = () => world.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*{doomed}*");
#else
        world.Tick();

        world.Errors.Should().ContainSingle().Which.Should().Contain(doomed.ToString()).And.Contain("Only a bug");
        PuffTesting.RangeFrames(world).Should().Equal([kept, space], "the range was dropped, and the newborn of the emitter in the heir takes a new range");
        world.Puffs.Looks.UsersOf(hot).Should().Be(1, "the dropped puffs freed their counts, and the newborn of the emitter in the heir holds one");
#endif
    }
}
