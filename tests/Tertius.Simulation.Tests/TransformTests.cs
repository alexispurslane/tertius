using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// An entity's transform has two views. Reads see the front snapshot, the state at the end of the previous tick. Writes
/// go to the back rows and show after the sync point. Only gameplay may write the transform, and only when nothing else
/// owns it: physics, an attachment parent and a frame driver each own the transform of the entities they move.
/// </summary>
public class TransformTests
{
    // ---- Reads see the previous tick, writes show after the sync point -------------------------------------------

    [Fact]
    public void AnEntityReadsAnotherEntitysPoseAsItWasAtTheEndOfThePreviousTick()
    {
        using var test = new TestWorld();
        var seenByEarlierWatcher = new List<double>();
        var seenByLaterWatcher = new List<double>();
        Entity mover = default;
        // Entities run in slot order on this one thread, so the first watcher runs before the mover and the second runs
        // after it. Both must see the pose from before the mover's write.
        test.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => seenByEarlierWatcher.Add(self.Snapshot.PoseInOwnFrame(mover).Position.X)));
        mover = test.SpawnWith(new Mover(new Double3(1, 0, 0)));
        test.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => seenByLaterWatcher.Add(self.Snapshot.PoseInOwnFrame(mover).Position.X)));

        test.Run(4);

        seenByEarlierWatcher.Should().Equal(0, 1, 2, 3);
        seenByLaterWatcher.Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void AnEntityReadsItsOwnPoseAsItWasAtTheEndOfThePreviousTickEvenAfterWritingANewOne()
    {
        using var test = new TestWorld();
        var seenAfterTheWrite = new List<double>();
        // The reader's priority is higher than the mover's, so it runs after the mover has written the new pose.
        test.SpawnWith(new Mover(new Double3(1, 0, 0)),
            Lambda.OnTick((self, scoped ref events, scoped ref outbox) => seenAfterTheWrite.Add(self.PoseInOwnFrame().Position.X), priority: 10));

        test.Run(4);

        seenAfterTheWrite.Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void APoseWrittenDuringATickShowsInTheWorldAtTheEndOfThatTick()
    {
        using var test = new TestWorld();
        Entity mover = test.SpawnWith(new Mover(new Double3(1, 0, 0)));

        for (int tick = 1; tick <= 3; tick++)
        {
            test.Run(1);

            test.World.PoseInOwnFrame(mover).Position.Should().Be(new Double3(tick, 0, 0));
        }
    }

    [Fact]
    public void AnEntityReadsAnotherEntitysVelocityAsItWasAtTheEndOfThePreviousTick()
    {
        // Physics reports the velocity tick + 1 for the step of each tick, so the velocity at the end of tick t is t + 1.
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity ball = default;
        physics.OnStep((context, output) =>
            output.Bodies.Add(new BodyState(ball, test.Root, TestWorld.At(0), new Vector3(context.Tick + 1, 0, 0), Vector3.Zero)));
        var seenThroughSelf = new List<float>();
        var seenThroughSnapshot = new List<float>();
        ball = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Body = TestWorld.Ball,
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                seenThroughSelf.Add(self.VelocityInOwnFrame().X);
                seenThroughSnapshot.Add(self.Snapshot.VelocityInOwnFrame(self.Entity).X);
            })],
        });

        test.Run(4);

        seenThroughSelf.Should().Equal(0, 1, 2, 3);
        seenThroughSnapshot.Should().Equal(0, 1, 2, 3);
        test.World.VelocityInOwnFrame(ball).X.Should().Be(4);
    }

    // ---- Physics owns the transform of a body ---------------------------------------------------------------------

    [Fact]
    public void AnEntityWithABodyCannotWriteItsTransformBecausePhysicsOwnsIt()
    {
        using var test = new TestWorld(TestPhysics.Create());
        Entity ball = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Body = TestWorld.Ball,
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.SetTransform(TestWorld.At(5)))],
        });

        test.Run(1);

        string error = test.World.Errors.Should().ContainSingle().Subject;
        error.Should().Contain(ball.ToString()).And.Contain(nameof(InvalidOperationException)).And.Contain("physics owns its transform");
    }

    [Fact]
    public void AnEntityWithABodyCannotWriteItsTransformEvenWhenTheWorldHasNoPhysicsBackend()
    {
        // The body is part of the entity, not of the backend, so the rule does not depend on a backend being present.
        using var test = new TestWorld();
        test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Body = TestWorld.Ball,
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.SetTransform(TestWorld.At(5)))],
        });

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain("physics owns its transform");
    }

    [Fact]
    public void AnEntityKeepsRunningAfterItsTransformWriteIsRefusedAndItsPoseDoesNotChange()
    {
        using var test = new TestWorld(TestPhysics.Create());
        var refusedWriter = Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.SetTransform(TestWorld.At(5)));
        // A later component of the same entity: the refused write must not end the entity's run.
        var laterComponent = Lambda.OnTick((self, scoped ref events, scoped ref outbox) => { }, priority: 1);
        Entity ball = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(1),
            Body = TestWorld.Ball,
            Components = [refusedWriter, laterComponent],
        });

        test.Run(3);

        refusedWriter.Runs.Should().Be(3, "the entity runs again on every tick");
        laterComponent.Runs.Should().Be(3, "the refused write does not stop the components after it");
        test.World.Errors.Should().HaveCount(3, "each refused write is recorded once");
        test.World.IsAlive(ball).Should().BeTrue();
        test.World.PoseInOwnFrame(ball).Should().Be(TestWorld.At(1));
    }

    [Fact]
    public void ABodilessEntityHasNoBodyHandle()
    {
        using var test = new TestWorld();
        bool? bodilessExists = null;
        bool? bodiedExists = null;
        test.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => bodilessExists = self.Body.Exists));
        test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Body = TestWorld.Ball,
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) => bodiedExists = self.Body.Exists)],
        });

        test.Run(1);

        bodilessExists.Should().BeFalse();
        bodiedExists.Should().BeTrue();
    }

    [Fact]
    public void ABodilessEntityCannotWriteAPhysicsIntent()
    {
        using var test = new TestWorld(TestPhysics.Create());
        test.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Body.AddForce(Vector3.UnitX)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain("has no physics body");
    }

    // ---- A frame driver owns the transform of the frame's owner ----------------------------------------------------

    [Theory]
    [InlineData(MotionDriver.Kepler)]
    [InlineData(MotionDriver.Physics)]
    public void TheOwnerOfAFrameThatADriverMovesCannotWriteItsTransform(MotionDriver driver)
    {
        using var test = new TestWorld();
        // A Kepler frame needs an orbit and a G·M to orbit. A physics frame without a hull body reaches the driver check of the world.
        test.GiveRootPointGravity(1e9);
        var orbit = driver == MotionDriver.Kepler ? KeplerOrbit.Circular(1000) : default;
        test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), driver, Orbit: orbit),
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.SetTransform(DPose.Identity))],
        });

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"owns a {driver} frame");
    }

    [Fact]
    public void TheOwnerOfAStaticFrameMayWriteItsTransform()
    {
        using var test = new TestWorld();
        Entity owner = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
            Components = [new Mover(new Double3(0, 2, 0))],
        });

        test.Run(1);

        test.World.Errors.Should().BeEmpty();
        test.World.PoseInOwnFrame(owner).Position.Should().Be(new Double3(0, 2, 0));
    }
}
