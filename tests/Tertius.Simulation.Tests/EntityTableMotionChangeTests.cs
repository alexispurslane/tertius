using System.Numerics;
using AwesomeAssertions;
using Tertius.Simulation;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The entity table keeps, in each chunk, the entities that wrote a change of their hull's motion in the tick
/// (<see cref="EntityTable.WriteMotionChange"/>). So step 4 of the sync point visits only them
/// (<see cref="EntityTable.CollectMotionChanges"/>), not every entity that wrote a force or a velocity. The swap hands the
/// changes to the front intents with the list of their writers, which the step 4 after the next step visits to store them
/// (<see cref="EntityTable.CollectFrontMotionChanges"/>).
/// </summary>
public class EntityTableMotionChangeTests
{
    private static List<Entity> MotionChanges(EntityTable table)
    {
        var changes = new List<Entity>();
        table.CollectMotionChanges(changes);
        return changes;
    }

    private static List<Entity> FrontMotionChanges(EntityTable table)
    {
        var changes = new List<Entity>();
        table.CollectFrontMotionChanges(changes);
        return changes;
    }

    /// <summary>Runs one tick in which <paramref name="write"/> writes intents, then swaps them with <paramref name="keep"/>.</summary>
    private static void TickAndSwap(EntityTable table, Action write, bool keep = true)
    {
        table.BeginTick(spawnPoolSize: 0);
        write();
        table.EndGameplay();
        table.SwapIntents(keep);
        table.EndTick();
    }

    [Fact]
    public void TheSwapHandsTheChangesOfMotionToTheFrontIntentsWithTheirWriters()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        Entity b = table.SpawnLiveWith(TestWorld.Ball);
        Entity c = table.SpawnLiveWith(TestWorld.Ball);

        TickAndSwap(table, () =>
        {
            table.WriteMotionChange(c, BodyMotion.Kinematic);
            table.WritableIntent(b).Force = new Vector3(1, 0, 0);
            table.WriteMotionChange(a, BodyMotion.Static);
        });

        FrontMotionChanges(table).Should().Equal(c, a);
        table.NextStepIntent(c).Motion.Should().Be(BodyMotion.Kinematic);
        table.AssertInvariants();
    }

    [Fact]
    public void TheFrontChangesOfMotionLastUntilTheNextSwap()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        TickAndSwap(table, () => table.WriteMotionChange(a, BodyMotion.Static));

        TickAndSwap(table, () => table.WritableIntent(a).Force = new Vector3(1, 0, 0));

        FrontMotionChanges(table).Should().BeEmpty();
        table.AssertInvariants();
    }

    [Fact]
    public void AnEntityThatDiesAfterTheSwapIsNotAFrontChangeOfMotion()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        Entity b = table.SpawnLiveWith(TestWorld.Ball);
        TickAndSwap(table, () =>
        {
            table.WriteMotionChange(a, BodyMotion.Static);
            table.WriteMotionChange(b, BodyMotion.Static);
        });

        table.Release(a);

        FrontMotionChanges(table).Should().Equal(b);
        table.AssertInvariants();
    }

    /// <summary>
    /// Without a physics backend nothing applies a request, but the World still counts its steps, and the stored motion of
    /// a hull takes a change one sync point after the tick that wrote it. So the swap keeps the change of motion alone.
    /// </summary>
    [Fact]
    public void WithoutAPhysicsBackendTheSwapKeepsOnlyTheChangeOfMotion()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        Entity b = table.SpawnLiveWith(TestWorld.Ball);

        TickAndSwap(table, () =>
        {
            table.WritableIntent(a).Force = new Vector3(1, 0, 0);
            table.WriteMotionChange(a, BodyMotion.Static);
            table.WritableIntent(b).Force = new Vector3(1, 0, 0);
        }, keep: false);

        table.IntentEntities.ToArray().Should().Equal(a);
        PhysicsIntent front = table.NextStepIntent(a);
        (front.Flags, front.Motion, front.Force).Should().Be((IntentFlags.SetMotion, BodyMotion.Static, Vector3.Zero));
        FrontMotionChanges(table).Should().Equal(a);
        table.AssertInvariants();
    }

    [Fact]
    public void TheWrittenChangeOfMotionIsTheLastOneThatTheEntityWroteInTheTick()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        Entity b = table.SpawnLiveWith(TestWorld.Ball);
        table.BeginTick(spawnPoolSize: 0);
        table.WritableIntent(b).Force = new Vector3(1, 0, 0);
        BodyMotion? before = table.WrittenMotionChange(a);
        table.WriteMotionChange(a, BodyMotion.Kinematic);
        table.WriteMotionChange(a, BodyMotion.Static);

        (before, table.WrittenMotionChange(a), table.WrittenMotionChange(b)).Should().Be(((BodyMotion?)null, (BodyMotion?)BodyMotion.Static, (BodyMotion?)null));
        table.EndGameplay();
        table.SwapIntents(keep: true);
        table.WrittenMotionChange(a).Should().BeNull("the change went to the front intents");
    }

    [Fact]
    public void TheInvariantsCatchAFrontChangeOfMotionThatStepFourWouldNotVisit()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        TickAndSwap(table, () => table.WritableIntent(a).Force = new Vector3(1, 0, 0));

        table.NextStepIntent(a).Flags |= IntentFlags.SetMotion;

        ((Action)table.AssertInvariants).Should().Throw<ContractException>();
    }

    [Fact]
    public void StepFourCollectsTheEntitiesThatWroteAChangeOfMotionInTheOrderInWhichTheyFirstWroteOne()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        Entity b = table.SpawnLiveWith(TestWorld.Ball);
        Entity c = table.SpawnLiveWith(TestWorld.Ball);
        table.BeginTick(spawnPoolSize: 0);
        table.WritableIntent(c).Force = new Vector3(1, 0, 0);
        table.WriteMotionChange(b, BodyMotion.Static);
        table.WritableIntent(a).Force = new Vector3(1, 0, 0);
        table.WriteMotionChange(c, BodyMotion.Kinematic);
        table.WriteMotionChange(c, BodyMotion.Static);
        table.EndGameplay();

        MotionChanges(table).Should().Equal(b, c);
    }

    [Fact]
    public void TheLastChangeOfMotionInATickWins()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        table.BeginTick(spawnPoolSize: 0);
        table.WriteMotionChange(a, BodyMotion.Kinematic);
        table.WriteMotionChange(a, BodyMotion.Static);
        table.EndGameplay();

        PhysicsIntent intent = table.PendingIntent(a);
        (intent.Motion, (intent.Flags & IntentFlags.SetMotion) != 0).Should().Be((BodyMotion.Static, true));
    }

    [Fact]
    public void AnEntityThatDiesAfterItWroteAChangeOfMotionIsNotCollected()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        Entity b = table.SpawnLiveWith(TestWorld.Ball);
        table.BeginTick(spawnPoolSize: 0);
        table.WriteMotionChange(a, BodyMotion.Static);
        table.WriteMotionChange(b, BodyMotion.Static);
        table.EndGameplay();

        table.Release(a);

        MotionChanges(table).Should().Equal(b);
    }

    [Fact]
    public void TheNextTickStartsWithNoChangesOfMotion()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        table.BeginTick(spawnPoolSize: 0);
        table.WriteMotionChange(a, BodyMotion.Static);
        table.EndGameplay();
        table.SwapIntents(keep: true);
        table.EndTick();

        table.BeginTick(spawnPoolSize: 0);
        table.EndGameplay();

        MotionChanges(table).Should().BeEmpty();
    }

    [Fact]
    public void TheInvariantsHoldForEntitiesThatWroteChangesOfMotion()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        Entity b = table.SpawnLiveWith(TestWorld.Ball);
        table.BeginTick(spawnPoolSize: 0);
        table.WritableIntent(a).Force = new Vector3(1, 0, 0);
        table.WriteMotionChange(b, BodyMotion.Static);
        table.WriteMotionChange(a, BodyMotion.Static);
        table.EndGameplay();
        table.Release(b);

        table.AssertInvariants();
    }

    [Fact]
    public void TheInvariantsCatchAChangeOfMotionThatStepFourWouldNotVisit()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        table.BeginTick(spawnPoolSize: 0);
        table.WritableIntent(a).Flags |= IntentFlags.SetMotion;
        table.EndGameplay();

        ((Action)table.AssertInvariants).Should().Throw<ContractException>();
    }
}
