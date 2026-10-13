using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// An entity with a body asks physics for things through <see cref="Self.Body"/>: forces, impulses, a new velocity. The
/// asks of one tick, the intent, reach physics at the start of the next step, so a force written in tick 0 acts in the
/// step of tick 1. Forces, torques and impulses add up. Every other ask keeps the last value written. A physics double
/// records the intent that each step receives.
/// </summary>
public class PhysicsIntentTests
{
    private delegate void WriteIntent(BodyHandle body);

    /// <summary>
    /// A world with a physics double that records intents, and a ball whose component runs <paramref name="write"/> in
    /// tick 0 and then does nothing. The world has run two ticks, so physics has received the intent once.
    /// </summary>
    private static List<IntentRecord> IntentsReceivedAfterWriting(WriteIntent write, out Entity ball)
    {
        IPhysicsBackend physics = TestPhysics.Create();
        List<IntentRecord> intents = physics.RecordIntents();
        using var test = new TestWorld(physics);
        ball = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Body = TestWorld.Ball,
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                if (self.Tick == 0)
                    write(self.Body);
            })],
        });

        test.Run(3);

        return intents;
    }

    private static PhysicsIntent TheOneIntentReceivedAfterWriting(WriteIntent write) =>
        IntentsReceivedAfterWriting(write, out _).Should().ContainSingle().Subject.Intent;

    [Fact]
    public void AnIntentWrittenInOneTickReachesPhysicsInTheStepOfTheNextTickOnly()
    {
        List<IntentRecord> intents = IntentsReceivedAfterWriting(body => body.AddForce(Vector3.UnitX), out Entity ball);

        intents.Should().ContainSingle();
        intents[0].Tick.Should().Be(1);
        intents[0].Entity.Should().Be(ball);
    }

    [Fact]
    public void PhysicsReceivesAnIntentOnlyFromTheEntitiesThatWroteOne()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        List<IntentRecord> intents = physics.RecordIntents();
        using var test = new TestWorld(physics);
        Entity pusher = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Body = TestWorld.Ball,
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Body.AddForce(Vector3.UnitX))],
        });
        test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });

        test.Run(3);

        intents.Select(i => i.Entity).Distinct().Should().Equal(pusher);
    }

    [Fact]
    public void TheIntentsOfTwoEntitiesStaySeparate()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        List<IntentRecord> intents = physics.RecordIntents();
        using var test = new TestWorld(physics);
        Entity left = SpawnPusher(test, new Vector3(-1, 0, 0));
        Entity right = SpawnPusher(test, new Vector3(1, 0, 0));

        test.Run(2);

        intents.Single(i => i.Entity == left).Intent.Force.Should().Be(new Vector3(-1, 0, 0));
        intents.Single(i => i.Entity == right).Intent.Force.Should().Be(new Vector3(1, 0, 0));
    }

    [Fact]
    public void AnIntentDoesNotPileUpFromOneTickToTheNext()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        List<IntentRecord> intents = physics.RecordIntents();
        using var test = new TestWorld(physics);
        SpawnPusher(test, Vector3.UnitY);

        test.Run(4);

        // The ball pushes in every tick. Steps 1, 2 and 3 each receive the force of one tick.
        intents.Select(i => i.Intent.Force.Y).Should().Equal(1, 1, 1);
    }

    [Fact]
    public void ForcesWrittenInOneTickAddUp()
    {
        PhysicsIntent intent = TheOneIntentReceivedAfterWriting(body =>
        {
            body.AddForce(new Vector3(1, 0, 0));
            body.AddForce(new Vector3(2, 0, 0));
        });

        intent.Force.Should().Be(new Vector3(3, 0, 0));
    }

    [Fact]
    public void ImpulsesWrittenInOneTickAddUp()
    {
        PhysicsIntent intent = TheOneIntentReceivedAfterWriting(body =>
        {
            body.AddImpulse(new Vector3(0, 1, 0));
            body.AddImpulse(new Vector3(0, 1, 0));
        });

        intent.Impulse.Should().Be(new Vector3(0, 2, 0));
    }

    [Fact]
    public void TorquesWrittenInOneTickAddUp()
    {
        PhysicsIntent intent = TheOneIntentReceivedAfterWriting(body =>
        {
            body.AddTorque(new Vector3(0, 0, 3));
            body.AddTorque(new Vector3(0, 0, -1));
        });

        intent.Torque.Should().Be(new Vector3(0, 0, 2));
    }

    [Fact]
    public void AngularImpulsesWrittenInOneTickAddUp()
    {
        PhysicsIntent intent = TheOneIntentReceivedAfterWriting(body =>
        {
            body.AddAngularImpulse(new Vector3(4, 0, 0));
            body.AddAngularImpulse(new Vector3(1, 0, 0));
        });

        intent.AngularImpulse.Should().Be(new Vector3(5, 0, 0));
    }

    [Fact]
    public void AVelocityWrittenTwiceInOneTickKeepsTheLastValue()
    {
        PhysicsIntent intent = TheOneIntentReceivedAfterWriting(body =>
        {
            body.SetVelocity(new Vector3(9, 9, 9));
            body.SetVelocity(new Vector3(7, 0, 0));
        });

        intent.Velocity.Should().Be(new Double3(7, 0, 0));
    }

    [Fact]
    public void AnAngularVelocityWrittenTwiceInOneTickKeepsTheLastValue()
    {
        PhysicsIntent intent = TheOneIntentReceivedAfterWriting(body =>
        {
            body.SetAngularVelocity(new Vector3(5, 5, 5));
            body.SetAngularVelocity(new Vector3(0, 1, 0));
        });

        intent.AngularVelocity.Should().Be(new Vector3(0, 1, 0));
    }

    [Fact]
    public void ATeleportAndAKinematicTargetAreSeparateSoNeitherReplacesTheOther()
    {
        PhysicsIntent intent = TheOneIntentReceivedAfterWriting(body =>
        {
            body.MoveKinematic(TestWorld.At(50));
            body.Teleport(TestWorld.At(100));
        });

        intent.Target.Position.X.Should().Be(50);
        intent.TeleportPose.Position.X.Should().Be(100);
    }

    [Fact]
    public void ACharacterMoveAndAJumpKeepTheirLastValues()
    {
        PhysicsIntent intent = TheOneIntentReceivedAfterWriting(body =>
        {
            body.MoveCharacter(new Vector3(1, 0, 0));
            body.MoveCharacter(new Vector3(0, 0, 2));
            body.Jump(3);
            body.Jump(4);
        });

        intent.CharacterVelocity.Should().Be(new Vector3(0, 0, 2));
        intent.JumpSpeed.Should().Be(4);
    }

    [Fact]
    public void TheFlagsOfAnIntentNameEveryKindOfAskThatWasWritten()
    {
        PhysicsIntent intent = TheOneIntentReceivedAfterWriting(body =>
        {
            body.SetVelocity(Vector3.UnitX);
            body.SetAngularVelocity(Vector3.UnitY);
            body.MoveKinematic(TestWorld.At(1));
            body.Teleport(TestWorld.At(2));
            body.MoveCharacter(Vector3.UnitZ);
            body.Jump(1);
        });

        intent.Flags.Should().Be(IntentFlags.SetVelocity | IntentFlags.SetAngularVelocity | IntentFlags.KinematicTarget |
                                 IntentFlags.Teleport | IntentFlags.CharacterMove | IntentFlags.Jump);
    }

    [Fact]
    public void AForceAloneRaisesNoFlag()
    {
        PhysicsIntent intent = TheOneIntentReceivedAfterWriting(body => body.AddForce(Vector3.UnitX));

        intent.Flags.Should().Be(IntentFlags.None);
    }

    [Fact]
    public void AnEntityCanReadWhatItHasWrittenSoFarInTheSameTick()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        PhysicsIntent pendingAtTheEnd = default;
        test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Body = TestWorld.Ball,
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                self.Body.AddForce(new Vector3(1, 0, 0));
                self.Body.AddForce(new Vector3(2, 0, 0));
                pendingAtTheEnd = self.Body.Pending;
            })],
        });

        test.Run(1);

        pendingAtTheEnd.Force.Should().Be(new Vector3(3, 0, 0));
    }

    [Fact]
    public void AnEntityWithoutABodyCannotWriteAnIntentAndTheErrorIsReported()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity ghost = test.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Body.AddForce(Vector3.UnitX)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain(ghost.ToString()).And.Contain("has no physics body");
    }

    private static Entity SpawnPusher(TestWorld test, Vector3 force) => test.World.Spawn(new SpawnDesc
    {
        Frame = test.Root,
        Body = TestWorld.Ball,
        Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Body.AddForce(force))],
    });
}
