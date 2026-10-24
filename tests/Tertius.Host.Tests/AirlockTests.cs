using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// Airlock: a use cycles the airlock's two doors, so that one of them is always shut. Here each door is a slab that
/// slides 2 m up in half a second, and a cycle opens the other door one second after it closes the open one. The
/// room has no gravity.
/// </summary>
public sealed class AirlockTests : IDisposable
{
    private const int Second = 60;

    private readonly World world = NewJoltWorld();
    private readonly FrameHandle room;
    private readonly Entity inner, outer, airlock;

    public AirlockTests()
    {
        room = world.CreateRootFrame(new(FrameBounds.Sphere(1e4f)));
        (inner, outer, airlock) = SpawnAirlock(x: 0, cycleSeconds: 1);
    }

    public void Dispose() => world.Dispose();

    /// <summary>
    /// Spawns an airlock with its inner door at z = 0 and its outer door at z = 3, at <paramref name="x"/> in the room. With
    /// <paramref name="outerOpenAtStart"/>, the airlock opens its outer door when it starts.
    /// </summary>
    private (Entity Inner, Entity Outer, Entity Airlock) SpawnAirlock(double x, float cycleSeconds,
        bool outerOpenAtStart = false)
    {
        Entity innerDoor = SpawnDoor(x, z: 0), outerDoor = SpawnDoor(x, z: 3);
        Entity cycler = world.Spawn(new()
        {
            Frame = room,
            Pose = new(new(x, 1, 1.5), DQuaternion.Identity),
            Components =
            [
                new Airlock(innerDoor: innerDoor, outerDoor: outerDoor, cycleSeconds: cycleSeconds,
                    outerOpenAtStart: outerOpenAtStart)
            ]
        });
        return (innerDoor, outerDoor, cycler);
    }

    private Entity SpawnDoor(double x, double z) => world.Spawn(new()
    {
        Frame = room,
        Pose = new(new(x, 1, z), DQuaternion.Identity),
        Body = new(Shape: new BoxShapeDesc(new(0.6f, 1, 0.05f)), Motion: BodyMotion.Kinematic),
        Components = [new Door(new(0, 2, 0), openSeconds: 0.5f)]
    });

    private void Ticks(int count)
    {
        for (int i = 0; i < count; i++)
            world.Tick();
    }

    private void Use() => Use(airlock);

    private void Use(Entity of) =>
        world.Post(Event.Create(target: of, sender: Entity.Null, payload: new Use { User = Entity.Null }));

    private double Height(Entity door) => world.Snapshot.PoseInOwnFrame(door).Position.Y - 1;

    /// <summary>True while the door is more than 0.5 percent of the way open: 1 cm of its 2 m slide.</summary>
    private bool NotShut(Entity door) => Height(door) > 0.005 * 2;

    private void ExpectOpen(Entity door, string name) =>
        Height(door).Should().BeApproximately(2, 1e-4, $"the {name} door is open");

    private void ExpectShut(Entity door, string name) =>
        Height(door).Should().BeApproximately(0, 1e-4, $"the {name} door is shut");

    [Fact]
    public void AtItsStartTheAirlockOpensTheInnerDoorAndKeepsTheOuterDoorShut()
    {
        Ticks(Second);

        ExpectOpen(inner, "inner");
        ExpectShut(outer, "outer");
    }

    [Fact]
    public void AnAirlockThatStartsOpenToTheOutsideOpensTheOuterDoorAndKeepsTheInnerDoorShut()
    {
        (Entity innerDoor, Entity outerDoor, _) = SpawnAirlock(x: 5, cycleSeconds: 1, outerOpenAtStart: true);
        Ticks(Second);

        ExpectOpen(outerDoor, "outer");
        ExpectShut(innerDoor, "inner");
    }

    [Fact]
    public void AUseCyclesAnAirlockThatStartsOpenToTheOutsideBackToItsInnerDoor()
    {
        (Entity innerDoor, Entity outerDoor, Entity cycler) =
            SpawnAirlock(x: 5, cycleSeconds: 1, outerOpenAtStart: true);
        Ticks(Second);
        Use(cycler);
        Ticks(Second * 3 / 4);
        ExpectShut(innerDoor, "inner");
        ExpectShut(outerDoor, "outer");
        Ticks(Second);

        ExpectOpen(innerDoor, "inner");
        ExpectShut(outerDoor, "outer");
    }

    [Fact]
    public void AUseClosesTheOpenDoorAndOpensTheOtherOneACycleLater()
    {
        Ticks(Second);
        Use();
        // The inner door is shut after half a second. The outer door's open order comes one second after the inner
        // door's close order.
        Ticks(Second * 3 / 4);
        ExpectShut(inner, "inner");
        ExpectShut(outer, "outer");
        Ticks(Second);

        ExpectShut(inner, "inner");
        ExpectOpen(outer, "outer");
    }

    [Fact]
    public void AUseDuringACycleDoesNothing()
    {
        Ticks(Second);
        Use();
        Ticks(Second / 3);
        Use();
        Ticks(Second * 3);

        // A second cycle would have shut the outer door again and opened the inner one.
        ExpectShut(inner, "inner");
        ExpectOpen(outer, "outer");
    }

    [Fact]
    public void AUseAfterTheCycleCyclesTheAirlockBack()
    {
        Ticks(Second);
        Use();
        Ticks(Second * 2);
        Use();
        Ticks(Second * 3 / 4);
        ExpectShut(inner, "inner");
        ExpectShut(outer, "outer");
        Ticks(Second);

        ExpectOpen(inner, "inner");
        ExpectShut(outer, "outer");
    }

    [Theory]
    [InlineData(58)]
    [InlineData(59)]
    [InlineData(60)]
    [InlineData(61)]
    [InlineData(62)]
    public void ASecondUseAtTheEndOfACycleNeverLeavesBothDoorsOpen(int ticksAfterTheFirstUse)
    {
        Ticks(Second);
        Use();
        // A use posted 60 ticks after the first one reaches the airlock in the tick whose routing carries the first
        // cycle's open order. If the airlock took it, its close order would reach the outer door together with that
        // open order.
        var bothOpen = new List<int>();
        for (int ticks = 0; ticks < Second * 4; ticks++)
        {
            if (ticks == ticksAfterTheFirstUse)
                Use();
            Ticks(1);
            if (NotShut(inner) && NotShut(outer))
                bothOpen.Add(ticks + 1);
        }

        // The list holds the ticks after the first use at which both doors were open.
        bothOpen.Should().BeEmpty("one of the doors is always shut");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void AnAirlockRefusesACycleTimeThatIsNotPositiveAndFinite(float cycleSeconds)
    {
        Action create = () => _ = new Airlock(innerDoor: inner, outerDoor: outer, cycleSeconds: cycleSeconds);

        create.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("cycleSeconds")
            .WithMessage("*cycle time*positive and finite*");
    }

    [Theory]
    [InlineData(Second, false)]
    [InlineData(Second + 1, true)]
    public void AUseStartsAnotherCycleOnlyFromTheFirstTickAfterTheCycleHasEnded(int ticksAfterTheFirstUse,
        bool startsACycle)
    {
        Ticks(Second);
        Use();
        // The airlock handles a use as many ticks after the first use as the test posts it after the first use. The first
        // cycle lasts 60 ticks, from the close order to the open order, so the airlock takes a use 61 ticks after the first
        // one, and not a use 60 ticks after it.
        Ticks(ticksAfterTheFirstUse);
        Use();
        Ticks(Second * 4);

        // A second cycle shuts the outer door again and opens the inner one.
        if (startsACycle)
        {
            ExpectOpen(inner, "inner");
            ExpectShut(outer, "outer");
        }
        else
        {
            ExpectShut(inner, "inner");
            ExpectOpen(outer, "outer");
        }
    }

    [Fact]
    public void ACycleShorterThanATickStillOpensTheOtherDoor()
    {
        // A quarter of a tick rounds to no tick at all, so the cycle lasts one tick.
        (Entity quickInner, Entity quickOuter, Entity quick) = SpawnAirlock(x: 5, cycleSeconds: 0.25f / Second);
        Ticks(Second);
        Use(quick);
        Ticks(Second);

        world.Errors.Should().BeEmpty();
        ExpectShut(quickInner, "inner");
        ExpectOpen(quickOuter, "outer");
    }
}
