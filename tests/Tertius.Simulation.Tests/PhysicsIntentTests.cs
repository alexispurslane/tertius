using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// An entity with a body asks physics for things through <see cref="Self.Body"/>: forces, impulses, a new velocity. The
/// asks of one tick, the intent, reach physics at the start of the next step, so a force written in tick 0 acts in the
/// step of tick 1. Forces, torques and impulses add up. Every other ask keeps the last value written. The tests run the
/// world on real Jolt, and the wrapper <see cref="FaultInjectingBackend"/> records the intent that each step receives.
/// </summary>
public sealed class PhysicsIntentTests
{
    private delegate void WriteIntent(BodyHandle body);

    /// <summary>What a test writes through the running entity, for a write that is no request to a body. A delegate of its own, because
    /// <see cref="Self"/> is a ref struct.</summary>
    private delegate void WriteSelfIntent(Self self);

    /// <summary>
    /// A world on real Jolt behind the wrapper that records intents, and a ball whose component runs <paramref name="write"/>
    /// in
    /// tick 0 and then does nothing. The world has run three ticks, so physics has received the intent once.
    /// </summary>
    private static List<IntentRecord> IntentsReceivedAfterWriting(WriteIntent write, out Entity ball) =>
        IntentsReceivedAfterWriting(self => write(self.Body), TestWorld.Ball, out ball);

    /// <summary>Like <see cref="IntentsReceivedAfterWriting(WriteIntent, out Entity)"/>, for a ball with the body
    /// <paramref name="body"/>.</summary>
    private static List<IntentRecord> IntentsReceivedAfterWriting(WriteSelfIntent write, BodyDesc body, out Entity ball)
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        List<IntentRecord> intents = faults.Intents;
        ball = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Body = body,
                Components =
                [
                    Lambda.OnTick(
                        (self, scoped ref events, scoped ref outbox) =>
                        {
                            if (self.Tick == 0)
                                write(self);
                        }
                    ),
                ],
            }
        );

        test.Run(3);

        return intents;
    }

    /// <summary>A ball that is kinematic, which takes an end pose, or dynamic, which takes a steer target (see
    /// <see cref="WriteAimPose"/>).</summary>
    private static BodyDesc BallFor(string writer) =>
        writer == nameof(BodyHandle.SteerTo) ? TestWorld.Ball : TestWorld.Ball with { Motion = BodyMotion.Kinematic };

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
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        List<IntentRecord> intents = faults.Intents;
        Entity pusher = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Body = TestWorld.Ball,
                Components =
                [
                    Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Body.AddForce(Vector3.UnitX)),
                ],
            }
        );
        test.World.Spawn(new() { Frame = test.Root, Body = TestWorld.Ball });

        test.Run(3);

        intents.Select(i => i.Entity).Distinct().Should().Equal(pusher);
    }

    [Fact]
    public void TheIntentsOfTwoEntitiesStaySeparate()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        List<IntentRecord> intents = faults.Intents;
        Entity left = SpawnPusher(test, new(-1, 0, 0));
        Entity right = SpawnPusher(test, new(1, 0, 0));

        test.Run(2);

        intents.Single(i => i.Entity == left).Intent.Force.Should().Be(new Vector3(-1, 0, 0));
        intents.Single(i => i.Entity == right).Intent.Force.Should().Be(new Vector3(1, 0, 0));
    }

    [Fact]
    public void AnIntentDoesNotPileUpFromOneTickToTheNext()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        List<IntentRecord> intents = faults.Intents;
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
            body.AddForce(new(1, 0, 0));
            body.AddForce(new(2, 0, 0));
        });

        intent.Force.Should().Be(new Vector3(3, 0, 0));
    }

    [Fact]
    public void ImpulsesWrittenInOneTickAddUp()
    {
        PhysicsIntent intent = TheOneIntentReceivedAfterWriting(body =>
        {
            body.AddImpulse(new(0, 1, 0));
            body.AddImpulse(new(0, 1, 0));
        });

        intent.Impulse.Should().Be(new Vector3(0, 2, 0));
    }

    [Fact]
    public void TorquesWrittenInOneTickAddUp()
    {
        PhysicsIntent intent = TheOneIntentReceivedAfterWriting(body =>
        {
            body.AddTorque(new(0, 0, 3));
            body.AddTorque(new(0, 0, -1));
        });

        intent.Torque.Should().Be(new Vector3(0, 0, 2));
    }

    [Fact]
    public void AngularImpulsesWrittenInOneTickAddUp()
    {
        PhysicsIntent intent = TheOneIntentReceivedAfterWriting(body =>
        {
            body.AddAngularImpulse(new(4, 0, 0));
            body.AddAngularImpulse(new(1, 0, 0));
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
            body.SetAngularVelocity(new(5, 5, 5));
            body.SetAngularVelocity(new(0, 1, 0));
        });

        intent.AngularVelocity.Should().Be(new Vector3(0, 1, 0));
    }

    [Fact]
    public void ATeleportAndAnEndPoseAreSeparateSoNeitherReplacesTheOther()
    {
        PhysicsIntent intent = IntentsReceivedAfterWriting(
                self =>
                {
                    self.MoveKinematic(TestWorld.At(50));
                    self.Body.Teleport(TestWorld.At(100));
                },
                BallFor(nameof(Self.MoveKinematic)),
                out _
            )
            .Should()
            .ContainSingle()
            .Subject.Intent;

        intent.Move.AimPose.Position.X.Should().Be(50);
        intent.TeleportPose.Position.X.Should().Be(100);
    }

    /// <summary>Writes <paramref name="pose"/> through the writer named <paramref name="writer"/>: an end pose or a steer target.</summary>
    private static void WriteAimPose(Self self, string writer, in DPose pose)
    {
        if (writer == nameof(BodyHandle.SteerTo))
            self.Body.SteerTo(pose);
        else
            self.MoveKinematic(pose);
    }

    [Theory]
    [InlineData(nameof(Self.MoveKinematic), "a position that is not finite")]
    [InlineData(nameof(Self.MoveKinematic), "a zero rotation")]
    [InlineData(nameof(BodyHandle.SteerTo), "a position that is not finite")]
    [InlineData(nameof(BodyHandle.SteerTo), "a zero rotation")]
    public void AnEndPoseOrASteerTargetWithANumberThatIsNotFiniteOrAZeroRotationIsRefused(string writer, string fault)
    {
        DPose pose =
            fault == "a zero rotation"
                ? new(new(1, 0, 0), default)
                : new DPose(new(double.NaN, 0, 0), DQuaternion.Identity);
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        Entity ball = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Body = BallFor(writer),
                Components =
                [
                    Lambda.OnTick(
                        (self, scoped ref events, scoped ref outbox) =>
                        {
                            if (self.Tick == 0)
                                WriteAimPose(self, writer, pose);
                        }
                    ),
                ],
            }
        );

        test.Run(3);

        test.World.Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(nameof(ArgumentException))
            .And.Contain(writer);
        faults
            .Intents.Should()
            .NotContain(record => record.Entity == ball, "the refused write leaves the ball no intent");
    }

    [Theory]
    [InlineData(nameof(Self.MoveKinematic))]
    [InlineData(nameof(BodyHandle.SteerTo))]
    public void AnEndPoseAndASteerTargetReachPhysicsWithAUnitRotation(string writer)
    {
        var turn = DQuaternion.FromAxisAngle(Double3.UnitZ, 0.4);
        var doubled = new DQuaternion(X: 2 * turn.X, Y: 2 * turn.Y, Z: 2 * turn.Z, W: 2 * turn.W);

        PhysicsIntent intent = IntentsReceivedAfterWriting(
                self => WriteAimPose(self, writer, new(new(1, 0, 0), doubled)),
                BallFor(writer),
                out _
            )
            .Should()
            .ContainSingle()
            .Subject.Intent;

        intent
            .Move.AimPose.Rotation.Should()
            .Be(doubled.Normalized(), "the World makes the rotation of a motion intent unit length");
    }

    [Fact]
    public void ACharacterMoveAndAJumpKeepTheirLastValues()
    {
        PhysicsIntent intent = TheOneIntentReceivedAfterWriting(body =>
        {
            body.MoveCharacter(new(1, 0, 0));
            body.MoveCharacter(new(0, 0, 2));
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
            body.Teleport(TestWorld.At(2));
            body.MoveCharacter(Vector3.UnitZ);
            body.Jump(1);
            body.SetCollision(CollisionGroups.None, CollisionMask.Empty);
        });

        intent
            .Flags.Should()
            .Be(
                IntentFlags.SetVelocity
                    | IntentFlags.SetAngularVelocity
                    | IntentFlags.Teleport
                    | IntentFlags.CharacterMove
                    | IntentFlags.Jump
                    | IntentFlags.SetCollision
            );
    }

    [Fact]
    public void ACollisionChangeWrittenTwiceInOneTickKeepsTheLastGroupsAndMask()
    {
        var registry = new CollisionGroupRegistry();
        CollisionGroup debris = registry.Register("Debris"),
            wall = registry.Register("Wall");

        PhysicsIntent intent = TheOneIntentReceivedAfterWriting(body =>
        {
            body.SetCollision(CollisionGroups.Of(wall), CollisionMask.Empty.InAny(wall));
            body.SetCollision(CollisionGroups.Of(debris).And(CollisionGroup.Solid), CollisionMask.Empty.InAny(wall));
        });

        intent.Flags.Should().Be(IntentFlags.SetCollision);
        intent.CollisionGroups.Should().Be(CollisionGroups.Of(debris).And(CollisionGroup.Solid));
        intent.CollisionMask.Should().Be(CollisionMask.Empty.InAny(wall));
    }

    [Theory]
    [InlineData("all-of")]
    [InlineData("none-of")]
    public void ABodyCannotTakeAMaskWithAnAllOfOrANoneOfPartAndTheErrorIsReported(string part)
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        List<IntentRecord> intents = faults.Intents;
        CollisionMask mask =
            part == "all-of"
                ? CollisionMask.Empty.InAll(CollisionGroup.Solid)
                : CollisionMask.Empty.InNone(CollisionGroup.Solid);
        Entity ball = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Body = TestWorld.Ball,
                Components =
                [
                    Lambda.OnTick(
                        (self, scoped ref events, scoped ref outbox) =>
                        {
                            if (self.Tick == 0)
                                self.Body.SetCollision(CollisionGroups.None, mask);
                        }
                    ),
                ],
            }
        );

        test.Run(3);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain(ball.ToString()).And.Contain(part);
        intents.Should().BeEmpty();
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
        using var test = TestWorld.OnJolt(faults: out _);
        PhysicsIntent pendingAtTheEnd = default;
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Body = TestWorld.Ball,
                Components =
                [
                    Lambda.OnTick(
                        (self, scoped ref events, scoped ref outbox) =>
                        {
                            self.Body.AddForce(new(1, 0, 0));
                            self.Body.AddForce(new(2, 0, 0));
                            pendingAtTheEnd = self.Body.Pending;
                        }
                    ),
                ],
            }
        );

        test.Run(1);

        pendingAtTheEnd.Force.Should().Be(new Vector3(3, 0, 0));
    }

    [Fact]
    public void AnEntityWithoutABodyCannotWriteAnIntentAndTheErrorIsReported()
    {
        using var test = TestWorld.OnJolt(faults: out _);
        Entity ghost = test.SpawnWith(
            Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Body.AddForce(Vector3.UnitX))
        );

        test.Run(1);

        test.World.Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(ghost.ToString())
            .And.Contain("has no physics body");
    }

    private static Entity SpawnPusher(TestWorld test, Vector3 force) =>
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Body = TestWorld.Ball,
                Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Body.AddForce(force))],
            }
        );
}
