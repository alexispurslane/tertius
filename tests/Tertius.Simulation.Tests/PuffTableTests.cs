using AwesomeAssertions;
using Tertius.Testing;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The puff store, through emitters in a World on the real Jolt backend (docs/plans/particles.md, "The puff store"): each
/// frame holds its puffs in one range, the ranges keep a fixed order, and the look table shares equal looks, keeps each
/// puff's look of its birth and reuses a freed index. The emitters of the tests have no body, so they hold still in their
/// frames.
/// </summary>
public class PuffTableTests
{
    private static readonly PuffLook Hot = new(Life: 2, StartRadius: 0.2f, EndRadius: 1, TemperatureRatio: 3,
        PuffShading.Glow(new(6, 5.4f, 4.5f)));

    private static readonly PuffLook Cold = new(Life: 2, StartRadius: 0.3f, EndRadius: 2, TemperatureRatio: 0.75f,
        PuffShading.Lit(new(0.5f)));

    private static readonly PuffLook Brief = Hot with { Life = 0.25f };

    internal static PuffEmitter Jet(PuffLook look, float rate = 60, float speed = 10) => new(rate, speed, 0, look);

    internal static Entity SpawnEmitter(World world, FrameHandle frame, Double3 place, PuffEmitter emitter,
        params IComponent[] components) =>
        world.Spawn(new()
        {
            Frame = frame,
            Pose = new(place, DQuaternion.Identity),
            Render = new RenderData { Emitter = emitter },
            Components = components
        });

    private static PuffRange[] Ranges(World world) => world.Puffs.View.Ranges.ToArray();

    private static Puff[] Rows(World world, FrameHandle frame)
    {
        PuffView view = world.Puffs.View;
        foreach (PuffRange range in view.Ranges)
            if (range.Frame == frame)
                return view.Rows.Slice(range.Start, range.Count).ToArray();
        return [];
    }

    [Fact]
    public void EachFrameHoldsItsPuffsInOneContiguousRange()
    {
        using TestWorld test = TestWorld.OnJolt(out _);
        FrameHandle a = test.SpawnStaticFrame(TestWorld.At(-100), radius: 50);
        FrameHandle b = test.SpawnStaticFrame(TestWorld.At(100), radius: 50);
        // In slot order the emitters take turns between the two frames.
        SpawnEmitter(test.World, a, new(0, 0, 0), Jet(Hot));
        SpawnEmitter(test.World, b, new(0, 0, 0), Jet(Hot));
        SpawnEmitter(test.World, a, new(5, 0, 0), Jet(Hot));

        test.Run(5);

        PuffRange[] ranges = Ranges(test.World);
        ranges.Select(range => range.Frame).Should().Equal(a, b);
        ranges[0].Should().Be(new PuffRange(a, 0, 10), "two emitters of a make one puff each in each of 5 steps");
        ranges[1].Should().Be(new PuffRange(b, 10, 5));
        test.World.Puffs.Count.Should().Be(15);
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void NewbornsJoinTheEndOfTheirFramesRange()
    {
        using TestWorld test = TestWorld.OnJolt(out _);
        FrameHandle a = test.SpawnStaticFrame(TestWorld.At(0), radius: 50);
        SpawnEmitter(test.World, a, Double3.Zero, Jet(Hot, rate: 120));

        for (int tick = 0; tick < 6; tick++)
        {
            test.Run(1);
            float[] ages = [.. Rows(test.World, a).Select(row => row.Age)];
            ages.Should().HaveCount(2 * (tick + 1));
            ages.Should()
                .BeInDescendingOrder(
                    "the survivors keep their order and the newborns come last, in their order of birth");
            ages[^1].Should().BeLessThan(test.World.DeltaTime);
        }
    }

    [Fact]
    public void AFrameThatGetsItsFirstPuffsTakesTheLastRange()
    {
        using TestWorld test = TestWorld.OnJolt(out _);
        // b is made before a, so it comes first in the frame tree and in slot order, but it gets its puffs later.
        FrameHandle b = test.SpawnStaticFrame(TestWorld.At(100), radius: 50);
        FrameHandle a = test.SpawnStaticFrame(TestWorld.At(-100), radius: 50);
        SpawnEmitter(test.World, a, Double3.Zero, Jet(Hot));
        test.Run(3);

        SpawnEmitter(test.World, b, Double3.Zero, Jet(Hot));
        test.Run(2);

        Ranges(test.World).Select(range => range.Frame).Should().Equal(a, b);
    }

    [Fact]
    public void ARangeThatEmptiesLeavesTheOrderOfTheOthers()
    {
        using TestWorld test = TestWorld.OnJolt(out _);
        FrameHandle a = test.SpawnStaticFrame(TestWorld.At(-200), radius: 50);
        FrameHandle b = test.SpawnStaticFrame(TestWorld.At(0), radius: 50);
        FrameHandle c = test.SpawnStaticFrame(TestWorld.At(200), radius: 50);
        SpawnEmitter(test.World, a, Double3.Zero, Jet(Hot));
        var pilot = new SelfPilot();
        SpawnEmitter(test.World, b, Double3.Zero, Jet(Brief), pilot);
        SpawnEmitter(test.World, c, Double3.Zero, Jet(Hot));
        test.Run(3);
        Ranges(test.World).Select(range => range.Frame).Should().Equal(a, b, c);

        pilot.EveryTick = self => self.Render.Emitter = default;
        test.Run(20);

        Ranges(test.World).Select(range => range.Frame).Should().Equal(a, c);
        PuffRange[] ranges = Ranges(test.World);
        ranges[1].Start.Should().Be(ranges[0].Count, "the ranges lie packed from row 0");
    }

    [Fact]
    public void PuffsThatAFramesDeathMovesInJoinTheHeirsRangeInTheOrderOfTheRanges()
    {
        foreach (bool heirFirst in new[] { true, false })
        {
            using TestWorld test = TestWorld.OnJolt(out _);
            FrameHandle dying = test.SpawnStaticFrame(TestWorld.At(100), radius: 50);
            if (heirFirst)
                SpawnEmitter(test.World, test.Root, Double3.Zero, Jet(Hot));
            SpawnEmitter(test.World, dying, Double3.Zero, Jet(Cold));
            test.Run(2);
            if (!heirFirst)
                SpawnEmitter(test.World, test.Root, Double3.Zero, Jet(Hot));
            test.Run(2);
            int cold = Rows(test.World, dying)[0].Look;

            test.World.Destroy(dying.Entity);

            Ranges(test.World).Select(range => range.Frame).Should().Equal([test.Root], "heir first: {0}", heirFirst);
            bool[] fromDying = [.. Rows(test.World, test.Root).Select(row => row.Look == cold)];
            fromDying.Should().HaveCount(heirFirst ? 8 : 6);
            fromDying.Should().Equal(
                heirFirst
                    ? [false, false, false, false, true, true, true, true]
                    : [true, true, true, true, false, false],
                "the moved puffs follow the heir's own when the heir's range came first, and come before them otherwise");
        }
    }

    [Fact]
    public void AnEmitterThatGathersLessThanOnePuffPerStepMakesItsPuffsAtItsRate()
    {
        using TestWorld test = TestWorld.OnJolt(out _);
        // 45 puffs per second gather 0.75 of a puff in each step, so most steps make none.
        Entity emitter = SpawnEmitter(test.World, test.Root, Double3.Zero, Jet(Hot, rate: 45));

        test.Run(1);
        test.World.Puffs.Count.Should().Be(0);
        test.World.Puffs.BirthPhase(emitter).Should()
            .BeApproximately(0.75, 1e-6, "a step that makes no puff still gathers the phase");
        test.Run(7);

        test.World.Puffs.Count.Should().Be(6, "8 steps of 0.75 make 6 puffs");
    }

    [Fact]
    public void EmittersWithEqualLooksShareOneLook()
    {
        using TestWorld test = TestWorld.OnJolt(out _);
        FrameHandle a = test.SpawnStaticFrame(TestWorld.At(-100), radius: 50);
        FrameHandle b = test.SpawnStaticFrame(TestWorld.At(100), radius: 50);
        SpawnEmitter(test.World, a, Double3.Zero, Jet(Hot));
        SpawnEmitter(test.World, b, Double3.Zero, Jet(Hot with { }, rate: 30, speed: 4));

        test.Run(10);

        Puff[] rows = [.. Rows(test.World, a), .. Rows(test.World, b)];
        rows.Select(row => row.Look).Distinct().Should()
            .ContainSingle("equal looks share one entry, whatever the rate and the speed");
        test.World.Puffs.Looks.InUse.Should().Be(1);
        test.World.Puffs.Looks.UsersOf(rows[0].Look).Should().Be(rows.Length);
        test.World.Puffs.View.Looks[rows[0].Look].Should().Be(Hot);
    }

    [Fact]
    public void APuffKeepsTheLookOfItsBirthWhenItsEmitterChangesItsLook()
    {
        using TestWorld test = TestWorld.OnJolt(out _);
        FrameHandle a = test.SpawnStaticFrame(TestWorld.At(0), radius: 50);
        var pilot = new SelfPilot();
        SpawnEmitter(test.World, a, Double3.Zero, Jet(Hot), pilot);
        test.Run(3);
        int hot = Rows(test.World, a)[0].Look;

        pilot.EveryTick = self => self.Render.Emitter = Jet(Cold);
        test.Run(4);

        Puff[] rows = Rows(test.World, a);
        // The write of tick 3 acts in step 4, so the sync point of tick 3 still makes a hot puff.
        rows.Take(4).Should().AllSatisfy(row => row.Look.Should().Be(hot));
        rows.Skip(4).Should().HaveCount(3).And.AllSatisfy(row => row.Look.Should().NotBe(hot));
        test.World.Puffs.View.Looks[hot].Should().Be(Hot);
        test.World.Puffs.View.Looks[rows[^1].Look].Should().Be(Cold);
        test.World.Puffs.Looks.InUse.Should().Be(2);
    }

    [Fact]
    public void ALookIsFreedWithItsLastPuffAndItsIndexIsReused()
    {
        using TestWorld test = TestWorld.OnJolt(out _);
        FrameHandle a = test.SpawnStaticFrame(TestWorld.At(0), radius: 50);
        var pilot = new SelfPilot();
        SpawnEmitter(test.World, a, Double3.Zero, Jet(Brief), pilot);
        test.Run(3);
        int brief = Rows(test.World, a)[0].Look;

        pilot.EveryTick = self => self.Render.Emitter = Jet(Hot);
        test.Run(30);
        int hot = Rows(test.World, a)[^1].Look;
        hot.Should().NotBe(brief);
        test.World.Puffs.Looks.UsersOf(brief).Should().Be(0, "the brief puffs died after a quarter of a second");
        test.World.Puffs.Looks.InUse.Should().Be(1);

        pilot.EveryTick = self => self.Render.Emitter = Jet(Cold);
        test.Run(3);

        Rows(test.World, a)[^1].Look.Should().Be(brief, "a new look takes the freed index first");
        test.World.Puffs.View.Looks[brief].Should().Be(Cold);
        test.World.Puffs.Looks.InUse.Should().Be(2);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(2024)]
    public void ThePuffTableKeepsItsInvariantsInEverySyncPoint(int seed)
    {
        // The workload checks every invariant of the World after every tick and after the changes between ticks, which
        // include the puff table's. A cap of 150 binds often.
        using var workload = new ChaoticWorkload(seed, puffCapacity: 150);

        workload.Run(150);

        workload.World.Puffs.Count.Should().BeGreaterThan(0, "seed {0}: the workload made puffs", seed);
        workload.World.Puffs.View.Ranges.Length.Should().BeGreaterThan(1, "seed {0}: in more than one frame", seed);
        workload.Tally.OutsideFrameDestroys.Should().BeGreaterThan(0);
        workload.World.Errors.Where(error => error.Contains("Exception") || error.Contains("puff")).Should()
            .BeEmpty($"seed {seed}");
    }

    /// <summary>
    /// A world without physics, so that every byte that the ticking thread allocates comes from the sync point: emitters in
    /// a frame with air and in one without, a look that changes every 20 ticks, deaths at the end of a life, and a cap that
    /// binds.
    /// </summary>
    [Fact]
    public void AStepAndABirthAllocateNothingAfterWarmUp()
    {
        using var test = new TestWorld(debugChecks: false, configure: options => options.PuffCapacity = 200);
        FrameHandle air = test.World.GetOwnedFrame(test.World.Spawn(new()
        {
            Frame = test.Root,
            Pose = TestWorld.At(100),
            AsFrame = new(FrameBounds.Sphere(50), GravityField.Uniform(new(0, -9.81f, 0)),
                MediumField.Uniform(1.225f, 2))
        }));
        for (int i = 0; i < 4; i++)
        {
            SpawnEmitter(test.World, air, new(i, 0, 0), new(70, 6, 0.6f, Hot));
            SpawnEmitter(test.World, test.Root, new(0, i, 0), new(50, 3, 0.2f, Brief));
        }

        var changer = new SelfPilot
            { EveryTick = self => self.Render.Emitter = Jet(self.Tick / 20 % 2 == 0 ? Cold : Brief) };
        SpawnEmitter(test.World, air, new(0, 0, 5), default, changer);
        test.Run(200);
        test.World.Puffs.Count.Should().Be(200, "the cap binds");

        long allocated = Allocations.Fewest(() => test.Run(200));

        // The total, not a mean per tick: one small object in 200 ticks would vanish in an integer mean.
        allocated.Should().Be(0, "a sync point allocates nothing for the puffs once their arrays have grown");
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ANegativeCapacityThrowsWhenTheWorldIsMade()
    {
        Action make = () => new World(new() { PuffCapacity = -1 });

        make.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should()
            .Be(nameof(WorldOptions.PuffCapacity));
    }
}
