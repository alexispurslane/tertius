using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The entity table's share of the flow of the parts' pushes (docs/plans/parts.md, "The state that the change adds"). A
/// part writes its push into its own back intent (<see cref="EntityTable.WritablePush"/>), and its first push of the tick
/// lists it among its chunk's pushers, not among the intent writers. The swap hands the pushers on as the front pushers,
/// in chunk order, and keeps them out of the intent list. The flow then writes each root's sum into its front intent
/// (<see cref="EntityTable.AddPush"/>) and empties each part's (<see cref="EntityTable.ClearPush"/>). The table also keeps
/// the center of mass of each body from its spawn.
/// </summary>
public sealed class EntityTablePushTests
{
    /// <summary>A frame handle that names no frame tree: the table only copies it.</summary>
    private static readonly FrameHandle Space = new(new(99, 1), 1, 1);

    /// <summary>A live entity without a body in <see cref="Space"/>, as a part is.</summary>
    private static Entity Part(EntityTable table) => table.SpawnLive(Space);

    /// <summary>A live entity with a dynamic ball in <see cref="Space"/>, as a chain root is.</summary>
    private static Entity Root(EntityTable table)
    {
        Entity entity = table.Reserve();
        table.Activate(entity: entity, row: new() { Frame = Space, PhysicsHome = Space }, render: new(), components: [],
            wants: [], body: TestWorld.Ball,
            bodyCenterOfMass: Vector3.Zero, ownedFrame: FrameHandle.Null);
        return entity;
    }

    private static void TickAndSwap(EntityTable table, Action write, bool keep = true)
    {
        table.BeginTick(spawnPoolSize: 0);
        write();
        table.EndGameplay();
        table.SwapIntents(keep);
    }

    [Fact]
    public void TheFirstPushOfAPartInATickListsItOnce()
    {
        EntityTable table = TableSupport.NewTable();
        Entity part = Part(table);

        TickAndSwap(table, () =>
        {
            table.WritablePush(part).Force += new Vector3(1, 0, 0);
            table.WritablePush(part).Force += new Vector3(2, 0, 0);
        });

        table.FrontPushers.ToArray().Should().Equal(part);
        table.FrontIntents.Span[(int)part.Index].Force.Should().Be(new Vector3(3, 0, 0));
        table.FrontIntents.Span[(int)part.Index].Frame.Should().Be(Space);
        table.ClearPush(part);
        table.EndTick();
        table.AssertInvariants();
    }

    [Fact]
    public void TheSwapHandsThePartsThatPushedOnAndLeavesThemOutOfTheIntentList()
    {
        EntityTable table = TableSupport.NewTable();
        // Chunks of 4 slots: the first part is in chunk 1, the second in chunk 0.
        Entity root = Root(table);
        Entity filler1 = Part(table), filler2 = Part(table), early = Part(table);
        Entity late = Part(table);

        TickAndSwap(table, () =>
        {
            table.WritablePush(late).Force += new Vector3(1, 0, 0);
            table.WritableIntent(root).Force += new Vector3(0, 1, 0);
            table.WritablePush(early).Torque += new Vector3(0, 0, 1);
        });

        table.FrontPushers.ToArray().Should().Equal(early, late);
        table.IntentEntities.ToArray().Should().Equal(root);
        table.ClearPush(early);
        table.ClearPush(late);
        table.EndTick();
        table.AssertInvariants();
        _ = (filler1, filler2);
    }

    [Fact]
    public void WithoutKeepTheSwapClearsThePushesAndListsNoPart()
    {
        EntityTable table = TableSupport.NewTable();
        Entity part = Part(table);

        TickAndSwap(table, () => table.WritablePush(part).Force += new Vector3(1, 0, 0), keep: false);

        table.FrontPushers.ToArray().Should().BeEmpty();
        table.FrontIntents.Span[(int)part.Index].Should().Be(default(PhysicsIntent));
        table.EndTick();
        table.AssertInvariants();
    }

    [Fact]
    public void ClearingAPushEmptiesThePartsFrontIntent()
    {
        EntityTable table = TableSupport.NewTable();
        Entity part = Part(table);
        TickAndSwap(table, () => table.WritablePush(part).Force += new Vector3(1, 0, 0));

        table.ClearPush(part);

        table.FrontIntents.Span[(int)part.Index].Should().Be(default(PhysicsIntent));
        table.EndTick();
        table.AssertInvariants();
    }

    [Fact]
    public void ARootThatWroteNothingGetsAnIntentThatNamesItsFrontFrame()
    {
        EntityTable table = TableSupport.NewTable();
        Entity root = Root(table);
        TickAndSwap(table, () => { });

        table.AddPush(root,
            new(Force: new(1, 2, 3), Torque: new(4, 5, 6), Impulse: new(7, 8, 9), AngularImpulse: new(10, 11, 12)));

        table.IntentEntities.ToArray().Should().Equal(root);
        PhysicsIntent intent = table.NextStepIntent(root);
        intent.Frame.Should().Be(Space);
        intent.Force.Should().Be(new Vector3(1, 2, 3));
        intent.Torque.Should().Be(new Vector3(4, 5, 6));
        intent.Impulse.Should().Be(new Vector3(7, 8, 9));
        intent.AngularImpulse.Should().Be(new Vector3(10, 11, 12));
        table.EndTick();
        table.AssertInvariants();
    }

    [Fact]
    public void ARootThatWroteItsOwnIntentTakesTheSumInPlaceAndIsListedOnce()
    {
        EntityTable table = TableSupport.NewTable();
        Entity root = Root(table);
        TickAndSwap(table, () =>
        {
            table.WritableIntent(root).Force += new Vector3(1, 0, 0);
            table.WritableIntent(root).Flags |= IntentFlags.SetAngularVelocity;
        });

        table.AddPush(root,
            new(Force: new(5, 0, 0), Torque: Double3.Zero, Impulse: Double3.Zero, AngularImpulse: Double3.Zero));

        table.IntentEntities.ToArray().Should().Equal(root);
        table.NextStepIntent(root).Force.Should().Be(new Vector3(5, 0, 0));
        table.NextStepIntent(root).Flags.Should().Be(IntentFlags.SetAngularVelocity);
        table.EndTick();
        table.AssertInvariants();
    }

    [Fact]
    public void TheCenterOfMassOfABodyIsKeptFromItsSpawn()
    {
        EntityTable table = TableSupport.NewTable();
        Entity entity = table.Reserve();

        table.Activate(entity: entity, row: new() { Frame = Space, PhysicsHome = Space }, render: new(), components: [],
            wants: [], body: TestWorld.Ball,
            bodyCenterOfMass: new(0.25f, -1, 3), ownedFrame: FrameHandle.Null);

        table.BodyCenterOfMass(entity).Should().Be(new Vector3(0.25f, -1, 3));
        table.AssertInvariants();
    }
}
