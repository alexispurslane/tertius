using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The fault-injecting backend is real Jolt plus edits that a test asks for. With no edit, it must not change anything
/// that the World sees. These tests lock in that promise, and the behavior of each edit and each recording.
/// </summary>
public class FaultInjectingBackendTests
{
    /// <summary>
    /// A ship in space with a deck and two balls inside it, run for 6 s. One ball is shot down onto the deck, so it makes a
    /// contact. The other is shot along the ship, so it leaves the bubble of the ship after about 4 s, and physics hands it
    /// off to the space frame. Returns the hash of the world at the end.
    /// </summary>
    private static ulong RunShipWithContactAndHandOff(World world)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        Scenes.ShipScene ship = Scenes.ShipWithInterior(world, space);
        world.Spawn(new()
        {
            Frame = ship.Interior, Pose = At(-3, Scenes.DeckTop + 1), LinearVelocity = new Vector3(0, -4, 0),
            Body = Ball()
        });
        world.Spawn(
            new() { Frame = ship.Interior, Pose = At(3), LinearVelocity = new Vector3(0, 0, 12), Body = Ball() });

        Tick(world, 360);

        return WorldHash.Of(world);
    }

    [Fact]
    public void AWorldOnTheWrapperEndsInTheSameWorldAsAWorldOnBareJoltWhenNothingIsEdited()
    {
        ulong onBareJolt;
        using (World world = NewWorld(out _))
        {
            onBareJolt = RunShipWithContactAndHandOff(world);
        }

        ulong onTheWrapper;
        FaultInjectingBackend faults;
        using (World world = NewWorldWithFaults(out faults))
        {
            onTheWrapper = RunShipWithContactAndHandOff(world);
            world.Errors.Should().BeEmpty();
        }

        faults.Outputs.SelectMany(output => output.Contacts).Should()
            .NotBeEmpty("the scene must make a contact, or the equal hashes prove less");
        faults.Outputs.SelectMany(output => output.HandOffs).Should()
            .NotBeEmpty("the scene must hand a body off, or the equal hashes prove less");
        onTheWrapper.Should().Be(onBareJolt);
    }

    [Fact]
    public void EveryStepOutputThatTheWorldReceivesIsRecordedAndTheWorldHoldsTheStateOfTheLastOne()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scenes.FloorAndBall scene = Scenes.FloorWithBall(world);

        Tick(world, 30);

        faults.Outputs.Should().HaveCount(30, "the World steps physics once in every tick");
        BodyState sent = faults.Outputs[^1].Bodies.Single(body => body.Entity == scene.Ball);
        world.PoseInOwnFrame(scene.Ball).Should().Be(sent.Pose,
            "the World takes the pose of the body from the output that Jolt sent");
        world.VelocityInOwnFrame(scene.Ball).Should().Be(sent.LinearVelocity);
    }

    [Fact]
    public void EveryChangeBatchThatReachesJoltIsRecordedAsACopyThatTheWorldDoesNotClear()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scenes.FloorAndBall scene = Scenes.FloorWithBall(world);

        world.Tick();
        Entity second = world.Spawn(new() { Frame = scene.Ground, Pose = At(5, 3), Body = Ball() });
        world.Tick();
        Tick(world, 5);

        List<PhysicsChanges> withChanges = [.. faults.AppliedChanges.Where(HoldsAChange)];
        withChanges.Should().HaveCount(2, "the World sends a batch that holds a change only when it has one");
        withChanges[0].FramesCreated.Should().Equal(scene.Ground);
        withChanges[0].BodiesCreated.Select(create => create.Entity).Should().BeEquivalentTo([scene.Floor, scene.Ball]);
        withChanges[1].BodiesCreated.Select(create => create.Entity).Should().Equal(second);
        faults.AppliedChanges.Count(batch => !HoldsAChange(batch)).Should().Be(9,
            "the last batch of every numeric update goes also when it is empty: 7 sync points, and 2 updates for spawns outside a tick");
    }

    /// <summary>True when <paramref name="batch"/> holds a structural change. A batch that holds none is the last batch of a numeric update.</summary>
    private static bool HoldsAChange(PhysicsChanges batch) =>
        batch.FramesCreated.Count > 0 || batch.BodiesDestroyed.Count > 0 || batch.FramesDestroyed.Count > 0 ||
        batch.FramesShifted.Count > 0 ||
        batch.BodiesMoved.Count > 0 || batch.BodiesCreated.Count > 0;

    /// <summary>The ball is released 0.5 m above the floor, so it lands in 0.32 s, and then lies on the floor.</summary>
    private static CollisionLog ContactsHeardByABallOnTheFloor(bool removeContacts, out List<PhysicsStepOutput> outputs)
    {
        var heard = new CollisionLog();
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scenes.FloorWithBall(world, height: 0.5, ballComponents: heard);
        if (removeContacts)
            faults.EditOutput = output => output.Contacts.Clear();

        Tick(world, 90);

        outputs = faults.Outputs;
        return heard;
    }

    [Fact]
    public void AnEditOfTheOutputReachesTheWorldAndTheRecordInEveryStep()
    {
        CollisionLog withoutEdit =
            ContactsHeardByABallOnTheFloor(removeContacts: false, out List<PhysicsStepOutput> unedited);
        CollisionLog withEdit =
            ContactsHeardByABallOnTheFloor(removeContacts: true, out List<PhysicsStepOutput> edited);

        withoutEdit.Seen.Should().NotBeEmpty("the ball landed on the floor");
        unedited.SelectMany(output => output.Contacts).Should().NotBeEmpty();
        withEdit.Seen.Should().BeEmpty("the edit took every contact out of the output before the World saw it");
        edited.SelectMany(output => output.Contacts).Should()
            .BeEmpty("the record holds the output as the World received it");
    }

    [Fact]
    public void EveryContactThatEndsIsRecordedInTheOutputOfTheStepThatSentIt()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var pilot = new Pilot();
        Scenes.FloorAndBall scene = Scenes.FloorWithBall(world, height: 0, ballComponents: pilot);
        Tick(world, 10);
        faults.Outputs.SelectMany(output => output.ContactsEnded).Should().BeEmpty("the ball rests on the floor");

        pilot.Once = body => body.SetVelocity(new(0, 3, 0));
        Tick(world, 10);

        faults.Outputs.SelectMany(output => output.ContactsEnded).Should().Equal([new(scene.Floor, scene.Ball)],
            "the ball left the floor once, and the record keeps the ended contact as the World received it");
    }

    [Fact]
    public void AnExceptionFromTheStepIsThrownOnceAndTheWorldRecordsAFailedStep()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scenes.FloorWithBall(world);
        faults.ThrowFromStep = new InvalidOperationException("the step broke");

        world.Tick();
        Tick(world, 5);

        world.Errors.Should().ContainSingle().Which.Should().Contain("the physics step failed").And
            .Contain("the step broke");
        faults.ThrowFromStep.Should().BeNull("the exception is thrown once");
        faults.Outputs.Should().HaveCount(5, "the step that threw has no entry");
    }

    [Fact]
    public void AnExceptionFromApplyChangesIsThrownOnceAndTheNextBatchStillReachesJolt()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 9.81f);
        faults.ThrowFromApplyChanges = new InvalidOperationException("the batch broke");

        world.Tick();
        Entity ball = world.Spawn(new() { Frame = ground, Pose = At(0, 5), Body = Ball() });
        world.Tick();

        world.Errors.Should().ContainSingle().Which.Should().Contain("failed to apply changes").And
            .Contain("the batch broke");
        faults.ThrowFromApplyChanges.Should().BeNull("the exception is thrown once");
        faults.AppliedChanges.Where(HoldsAChange).Should()
            .ContainSingle("the batch that threw has no entry, and the next batch applied").Which
            .BodiesCreated.Select(create => create.Entity).Should().Equal(ball);
        faults.Jolt.TryGetBodyFrame(ball, out FrameHandle frame).Should().BeTrue();
        frame.Should().Be(ground);
    }

    [Fact]
    public void EveryStepIsRecordedWithItsTickItsTimeAndTheThreadThatRanIt()
    {
        using World world =
            NewWorldWithFaults(out FaultInjectingBackend faults, configure: options => options.TickRate = 64);
        Scenes.FloorWithBall(world);

        Tick(world, 3);

        faults.Steps.Select(step => step.Tick).Should().Equal(0L, 1, 2);
        faults.Steps.Select(step => step.Time).Should().Equal(0, 1.0 / 64, 2.0 / 64);
        faults.Steps.Select(step => step.ThreadId).Distinct().Should().ContainSingle().Which.Should()
            .NotBe(Environment.CurrentManagedThreadId, "the World steps physics on a thread of its own");
    }

    [Fact]
    public void AStepThatThrowsIsRecordedBecauseTheWorldStartedIt()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scenes.FloorWithBall(world);
        faults.ThrowFromStep = new InvalidOperationException("the step broke");

        Tick(world, 2);

        faults.Steps.Select(step => step.Tick).Should().Equal(0L, 1);
        faults.Outputs.Should().HaveCount(1, "the step that threw has no output");
    }

    [Fact]
    public void TheIntentOfAnEntityIsRecordedInTheStepAfterTheTickThatWroteItAsACopy()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var pilot = new Pilot { EveryTick = body => body.AddForce(new(0, 0, 4)) };
        Scenes.FloorAndBall scene = Scenes.FloorWithBall(world, ballComponents: pilot);

        Tick(world, 4);

        faults.Intents.Select(record => (record.Tick, record.Entity)).Should()
            .Equal((1L, scene.Ball), (2L, scene.Ball), (3L, scene.Ball));
        faults.Intents.Should().OnlyContain(record => record.Intent.Force == new Vector3(0, 0, 4),
            "the intent of one tick does not pile up on the next");
    }
}
