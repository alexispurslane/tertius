using System.Numerics;
using AwesomeAssertions;
using Tertius.Simulation;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The entity table keeps, in each chunk, the entities that wrote a change of their hull's motion in the tick
/// (<see cref="EntityTable.WriteMotionChange"/>). So step 4 of the sync point visits only them
/// (<see cref="EntityTable.CollectMotionChanges"/>), not every entity that wrote a force or a velocity.
/// </summary>
public class EntityTableMotionChangeTests
{
    private static List<Entity> MotionChanges(EntityTable table)
    {
        var changes = new List<Entity>();
        table.CollectMotionChanges(changes);
        return changes;
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
