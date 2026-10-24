using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// An attached entity follows its parent with a fixed offset in the parent's axes. It has no body and no transform of its
/// own to write: it changes its offset, and the sync point derives its pose from its chain root. It lives and dies with
/// its parent. The velocity of a child and the effect of a hand-off have their own test classes.
/// </summary>
public sealed class AttachmentTests
{
    private static Pose Offset(float x, float y, float z = 0) => new(new(x, y, z), Quaternion.Identity);

    /// <summary>A parent at (10, 0, 0) turned a quarter turn about +Y that moves 1 m along +X each tick, a child 2 m along its +Z, and a grandchild 1 m above the child.</summary>
    private sealed class MovingChain
    {
        public MovingChain(TestWorld test)
        {
            Parent = test.World.Spawn(
                new()
                {
                    Frame = test.Root,
                    Pose = new(new(10, 0, 0), PoseChecks.QuarterTurnAboutY),
                    Components = [new Mover(new(1, 0, 0))],
                }
            );
            Child = test.World.Spawn(
                new()
                {
                    Frame = test.Root,
                    AttachTo = Parent,
                    AttachOffset = Offset(0, 0, 2),
                }
            );
            Grandchild = test.World.Spawn(
                new()
                {
                    Frame = test.Root,
                    AttachTo = Child,
                    AttachOffset = Offset(0, 1),
                }
            );
        }

        public Entity Parent { get; }
        public Entity Child { get; }
        public Entity Grandchild { get; }
    }

    // ---- Following the parent -------------------------------------------------------------------------------------

    [Fact]
    public void AChildIsPlacedAtOnceWhenItIsSpawnedOutsideATick()
    {
        using var test = new TestWorld();

        var chain = new MovingChain(test);

        // The quarter turn maps the parent's +Z to the frame's +X, so the child is 2 m along +X from (10, 0, 0).
        PoseChecks.AssertNear(test.World.PoseInOwnFrame(chain.Child).Position, new(12, 0, 0), 1e-6);
        PoseChecks.AssertNear(test.World.PoseInOwnFrame(chain.Grandchild).Position, new(12, 1, 0), 1e-6);
    }

    [Fact]
    public void AChildKeepsItsOffsetInTheAxesOfItsParentAsTheParentMoves()
    {
        using var test = new TestWorld();
        var chain = new MovingChain(test);

        test.Run(3);

        // The parent is at (13, 0, 0) now. Its turn has not changed, so the child is still 2 m along +X from it.
        PoseChecks.AssertNear(test.World.PoseInOwnFrame(chain.Child).Position, new(15, 0, 0), 1e-6);
    }

    [Fact]
    public void AGrandchildFollowsTheWholeChainOfOffsets()
    {
        using var test = new TestWorld();
        var chain = new MovingChain(test);

        test.Run(3);

        PoseChecks.AssertNear(test.World.PoseInOwnFrame(chain.Grandchild).Position, new(15, 1, 0), 1e-6);
    }

    [Fact]
    public void AChildTurnsWithItsParent()
    {
        using var test = new TestWorld();
        var chain = new MovingChain(test);

        test.Run(3);

        PoseChecks.AssertSameRotation(
            test.World.PoseInOwnFrame(chain.Child).Rotation,
            PoseChecks.QuarterTurnAboutY,
            1e-6
        );
    }

    [Fact]
    public void AChildIsInTheFrameOfItsParent()
    {
        using var test = new TestWorld();
        var chain = new MovingChain(test);

        test.Run(1);

        test.World.GetFrame(chain.Child).Should().Be(test.Root);
        test.World.GetFrame(chain.Grandchild).Should().Be(test.Root);
    }

    [Fact]
    public void AChildSwingsRoundWhenItsParentTurnsDuringATick()
    {
        using var test = new TestWorld();
        Entity parent = test.SpawnAt(
            new(10, 0, 0),
            Lambda.OnTick(
                (self, scoped ref events, scoped ref outbox) =>
                    self.SetTransform(self.PoseInOwnFrame() with { Rotation = PoseChecks.QuarterTurnAboutY })
            )
        );
        Entity child = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                AttachTo = parent,
                AttachOffset = Offset(0, 0, 2),
            }
        );
        PoseChecks.AssertNear(test.World.PoseInOwnFrame(child).Position, new(10, 0, 2), 1e-6);

        test.Run(1);

        // The parent turned in tick 0, and the sync point of that tick derived the child from the turned parent.
        PoseChecks.AssertNear(test.World.PoseInOwnFrame(child).Position, new(12, 0, 0), 1e-6);
    }

    [Fact]
    public void AnOffsetWithAZeroedRotationMeansNoRotation()
    {
        using var test = new TestWorld();
        Entity parent = test.SpawnAt(new(10, 0, 0));

        // A defaulted Pose has the quaternion (0, 0, 0, 0), which is not a rotation.
        Entity child = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                AttachTo = parent,
                AttachOffset = new(new(1, 0, 0), default),
            }
        );

        PoseChecks.AssertNear(test.World.PoseInOwnFrame(child).Position, new(11, 0, 0));
        PoseChecks.AssertSameRotation(test.World.PoseInOwnFrame(child).Rotation, DQuaternion.Identity);
    }

    // ---- Reading the parent and changing the offset ---------------------------------------------------------------

    [Fact]
    public void AnAttachedEntityCanReadItsParent()
    {
        using var test = new TestWorld();
        Entity parent = test.SpawnAt(new(10, 0, 0));
        var seenByChild = new List<Entity>();
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                AttachTo = parent,
                AttachOffset = Offset(1, 0),
                Components =
                [
                    Lambda.OnTick((self, scoped ref events, scoped ref outbox) => seenByChild.Add(self.AttachedTo)),
                ],
            }
        );

        test.Run(2);

        seenByChild.Should().Equal(parent, parent);
    }

    [Fact]
    public void AnEntityThatIsNotAttachedHasNoAttachmentParent()
    {
        using var test = new TestWorld();
        var seenByLoner = new List<Entity>();
        test.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => seenByLoner.Add(self.AttachedTo)));

        test.Run(1);

        seenByLoner.Should().ContainSingle().Which.IsNull.Should().BeTrue();
    }

    [Fact]
    public void ANewOffsetMovesTheChildAtTheEndOfTheTickInWhichItWasWritten()
    {
        using var test = new TestWorld();
        Entity parent = test.SpawnAt(new(10, 0, 0));
        Entity child = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                AttachTo = parent,
                AttachOffset = Offset(1, 0),
                Components =
                [
                    Lambda.OnTick(
                        (self, scoped ref events, scoped ref outbox) =>
                        {
                            if (self.Tick == 0)
                                self.AttachOffset = Offset(4, 0);
                        }
                    ),
                ],
            }
        );

        test.Run(1);

        PoseChecks.AssertNear(test.World.PoseInOwnFrame(child).Position, new(14, 0, 0));
    }

    [Fact]
    public void AnAttachedEntityCannotWriteItsTransformBecauseItsParentOwnsIt()
    {
        using var test = new TestWorld();
        Entity parent = test.SpawnAt(new(10, 0, 0));
        var writer = Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.SetTransform(TestWorld.At(0)));
        Entity child = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                AttachTo = parent,
                AttachOffset = Offset(1, 0),
                Components = [writer],
            }
        );

        test.Run(2);

        test.World.Errors.Should()
            .HaveCount(2)
            .And.OnlyContain(e => e.Contains(child.ToString()) && e.Contains("attached"));
        PoseChecks.AssertNear(test.World.PoseInOwnFrame(child).Position, new(11, 0, 0));
        writer.Runs.Should().Be(2, "the refused write does not stop the entity");
    }

    [Fact]
    public void AnEntityThatIsNotAttachedCannotWriteAnOffset()
    {
        using var test = new TestWorld();
        Entity loner = test.SpawnWith(
            Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.AttachOffset = Pose.Identity)
        );

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain("not attached");
        test.World.IsAlive(loner).Should().BeTrue();
    }

    // ---- Spawning -------------------------------------------------------------------------------------------------

    [Fact]
    public void AChildSpawnedDuringATickCanAttachToAParentSpawnedInTheSameTick()
    {
        using var test = new TestWorld();
        Entity parent = default,
            child = default;
        test.SpawnWith(
            Lambda.OnTick(
                (self, scoped ref events, scoped ref outbox) =>
                {
                    if (self.Tick != 0)
                        return;
                    parent = self.Spawn(new() { Frame = self.Frame, Pose = TestWorld.At(7) });
                    child = self.Spawn(
                        new()
                        {
                            Frame = self.Frame,
                            AttachTo = parent,
                            AttachOffset = Offset(0, 3),
                        }
                    );
                }
            )
        );

        test.Run(1);

        test.World.IsAlive(child).Should().BeTrue();
        PoseChecks.AssertNear(test.World.PoseInOwnFrame(child).Position, new(7, 3, 0));
    }

    [Fact]
    public void AChildSpawnedOutsideATickIsInTheFrameOfItsParentWhateverFrameItNames()
    {
        using var test = new TestWorld();
        FrameHandle other = test.SpawnStaticFrame(TestWorld.At(500), radius: 10);
        Entity parent = test.World.SpawnAt(other, new(1, 0, 0));
        var frameChanges = new Recorder(EventMask.Of<FrameChanged>());

        Entity child = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                AttachTo = parent,
                AttachOffset = Offset(0, 3),
                Components = [frameChanges],
            }
        );

        test.World.GetFrame(child).Should().Be(other);
        PoseChecks.AssertNear(test.World.PoseInOwnFrame(child).Position, new(1, 3, 0));
        test.Run(2);
        frameChanges.Seen.Should().BeEmpty("the child was never in the frame that it named");
    }

    [Fact]
    public void AChildSpawnedDuringATickBecomesLiveInTheFrameOfItsParentWhateverFrameItNames()
    {
        using var test = new TestWorld();
        FrameHandle other = test.SpawnStaticFrame(TestWorld.At(500), radius: 10);
        Entity parent = test.World.SpawnAt(other, new(1, 0, 0));
        Entity child = default;
        var frameChanges = new Recorder(EventMask.Of<FrameChanged>());
        test.SpawnWith(
            Lambda.OnTick(
                (self, scoped ref events, scoped ref outbox) =>
                {
                    if (self.Tick == 0)
                        child = self.Spawn(
                            new()
                            {
                                Frame = test.Root,
                                AttachTo = parent,
                                AttachOffset = Offset(0, 3),
                                Components = [frameChanges],
                            }
                        );
                }
            )
        );

        test.Run(3);

        test.World.GetFrame(child).Should().Be(other);
        PoseChecks.AssertNear(test.World.PoseInOwnFrame(child).Position, new(1, 3, 0));
        frameChanges.Seen.Should().BeEmpty("the child was never in the frame that it named");
        test.World.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAttachedSpawnCanLeaveItsFrameNull(bool duringATick)
    {
        using var test = new TestWorld();
        FrameHandle other = test.SpawnStaticFrame(TestWorld.At(500), radius: 10);
        Entity parent = test.World.SpawnAt(other, new(1, 0, 0));
        var desc = new SpawnDesc { AttachTo = parent, AttachOffset = Offset(0, 3) };
        Entity child = default;
        if (duringATick)
        {
            test.SpawnWith(
                Lambda.OnTick(
                    (self, scoped ref events, scoped ref outbox) =>
                    {
                        if (self.Tick == 0)
                            child = self.Spawn(desc);
                    }
                )
            );
            test.Run(1);
        }
        else
        {
            child = test.World.Spawn(desc);
        }

        test.World.GetFrame(child).Should().Be(other);
        PoseChecks.AssertNear(test.World.PoseInOwnFrame(child).Position, new(1, 3, 0));
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AChildSpawnedInTheTickInWhichItsParentsFrameDiesBecomesLiveInTheFrameThatTheParentMovedTo()
    {
        using var test = new TestWorld();
        // The ship is 100 m along +X, and it destroys itself in tick 0. Its contents then move to the root.
        Entity shipOwner = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = TestWorld.At(100),
                AsFrame = new(FrameBounds.Sphere(10)),
                Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy())],
            }
        );
        FrameHandle ship = test.World.GetOwnedFrame(shipOwner);
        Entity child = default;
        var frameChanges = new Recorder(EventMask.Of<FrameChanged>());
        // The person sees itself in the ship during tick 0, and it names the ship as its child's frame, as Carry does.
        Entity person = test.World.Spawn(
            new()
            {
                Frame = ship,
                Pose = TestWorld.At(1),
                Components =
                [
                    Lambda.OnTick(
                        (self, scoped ref events, scoped ref outbox) =>
                        {
                            if (self.Tick == 0)
                                child = self.Spawn(
                                    new()
                                    {
                                        Frame = self.Frame,
                                        AttachTo = self.Entity,
                                        AttachOffset = Offset(0, 3),
                                        Components = [frameChanges],
                                    }
                                );
                        }
                    ),
                ],
            }
        );

        test.Run(3);

        test.World.IsAlive(child).Should().BeTrue();
        test.World.GetFrame(person).Should().Be(test.Root);
        test.World.GetFrame(child).Should().Be(test.Root);
        PoseChecks.AssertNear(test.World.PoseInOwnFrame(child).Position, new(101, 3, 0));
        frameChanges.Seen.Should().BeEmpty("the child was never in the ship");
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AChildSpawnedDuringATickForAParentThatDiesInThatTickNeverBecomesLiveAndTheErrorIsReported()
    {
        using var test = new TestWorld();
        Entity parent = test.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy()));
        Entity child = default;
        test.SpawnWith(
            Lambda.OnTick(
                (self, scoped ref events, scoped ref outbox) =>
                {
                    if (self.Tick == 0)
                        child = self.Spawn(new() { Frame = self.Frame, AttachTo = parent });
                }
            )
        );

        test.Run(1);

        test.World.IsAlive(child).Should().BeFalse();
        test.World.Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(child.ToString())
            .And.Contain($"{parent} is not live");
    }

    [Fact]
    public void AChildSpawnedDuringATickForTheOwnerOfARootFrameNeverBecomesLiveAndTheErrorIsReported()
    {
        using var test = new TestWorld();
        Entity child = default;
        test.SpawnWith(
            Lambda.OnTick(
                (self, scoped ref events, scoped ref outbox) =>
                {
                    if (self.Tick == 0)
                        child = self.Spawn(new() { AttachTo = test.Root.Entity });
                }
            )
        );

        test.Run(1);

        // The owner of a root frame is in no frame, so it has no frame to give a child.
        test.World.IsAlive(child).Should().BeFalse();
        test.World.Errors.Should().ContainSingle().Which.Should().Contain(child.ToString()).And.Contain("no frame");
    }

    [Fact]
    public void TwoSpawnsThatNameEachOtherAsParentsNeverBecomeLiveAndBothErrorsAreReported()
    {
        using var test = new TestWorld();
        Entity first = default,
            second = default,
            forged = default;
        test.SpawnWith(
            Lambda.OnTick(
                (self, scoped ref events, scoped ref outbox) =>
                {
                    if (self.Tick != 0)
                        return;
                    // Only a bug makes such a loop: the first spawn names a handle that it guessed for the second. The slots of a
                    // new chunk pool come in order, so the guess is two slots past a probe.
                    Entity probe = self.Spawn(new() { Frame = self.Frame });
                    forged = new(probe.Index + 2, probe.Generation);
                    first = self.Spawn(new() { Frame = self.Frame, AttachTo = forged });
                    second = self.Spawn(new() { Frame = self.Frame, AttachTo = first });
                }
            )
        );

        // The second tick shows that the world goes on with its invariants whole.
        test.Run(2);

        second.Should().Be(forged, "the test must make a real loop");
        test.World.IsAlive(first).Should().BeFalse();
        test.World.IsAlive(second).Should().BeFalse();
        test.World.Errors.Should().HaveCount(2);
        test.World.Errors[0].Should().Contain(first.ToString()).And.Contain($"{second} is not live");
        test.World.Errors[1].Should().Contain(second.ToString()).And.Contain($"{first} is not live");
    }

    // ---- Lifetime -------------------------------------------------------------------------------------------------

    [Fact]
    public void DestroyingAParentDestroysItsChildrenAndGrandchildren()
    {
        using var test = new TestWorld();
        Entity parent = test.SpawnWith();
        Entity child = test.World.Spawn(new() { Frame = test.Root, AttachTo = parent });
        Entity grandchild = test.World.Spawn(new() { Frame = test.Root, AttachTo = child });

        test.World.Destroy(parent);

        test.World.IsAlive(child).Should().BeFalse();
        test.World.IsAlive(grandchild).Should().BeFalse();
    }

    [Fact]
    public void DestroyingAChildLeavesItsParentAndItsSiblings()
    {
        using var test = new TestWorld();
        Entity parent = test.SpawnWith();
        Entity child = test.World.Spawn(new() { Frame = test.Root, AttachTo = parent });
        Entity sibling = test.World.Spawn(new() { Frame = test.Root, AttachTo = parent });

        test.World.Destroy(child);

        test.World.IsAlive(parent).Should().BeTrue();
        test.World.IsAlive(sibling).Should().BeTrue();
    }

    [Fact]
    public void DestroyingAChildInTheMiddleOfAChainDestroysTheLinksBelowItAndNotTheOnesAboveIt()
    {
        using var test = new TestWorld();
        Entity root = test.SpawnWith();
        Entity middle = test.World.Spawn(new() { Frame = test.Root, AttachTo = root });
        Entity leaf = test.World.Spawn(new() { Frame = test.Root, AttachTo = middle });

        test.World.Destroy(middle);

        test.World.IsAlive(root).Should().BeTrue();
        test.World.IsAlive(leaf).Should().BeFalse();
    }

    [Fact]
    public void AParentThatDestroysItselfDuringATickTakesItsChildrenWithIt()
    {
        using var test = new TestWorld();
        Entity parent = test.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy()));
        Entity child = test.World.Spawn(new() { Frame = test.Root, AttachTo = parent });
        Entity grandchild = test.World.Spawn(new() { Frame = test.Root, AttachTo = child });

        test.Run(1);

        test.World.IsAlive(parent).Should().BeFalse();
        test.World.IsAlive(child).Should().BeFalse();
        test.World.IsAlive(grandchild).Should().BeFalse();
    }

    [Fact]
    public void AParentThatIsDestroyedByAMessageTakesItsChildrenWithIt()
    {
        using var test = new TestWorld();
        Entity parent = test.SpawnWith();
        Entity child = test.World.Spawn(new() { Frame = test.Root, AttachTo = parent });
        test.SpawnWith(SendOnce.Of(target: parent, payload: new Destroy()));

        test.Run(2);

        test.World.IsAlive(parent).Should().BeFalse();
        test.World.IsAlive(child).Should().BeFalse();
    }

    [Fact]
    public void ANewChainOnTheSlotsOfADestroyedChainWorksAsUsual()
    {
        using var test = new TestWorld();
        Entity parent = test.SpawnWith();
        Entity child = test.World.Spawn(new() { Frame = test.Root, AttachTo = parent });
        test.World.Destroy(parent);

        Entity again = test.SpawnAt(new(5, 0, 0));
        Entity follower = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                AttachTo = again,
                AttachOffset = Offset(0, 1),
            }
        );
        test.Run(1);

        test.World.IsAlive(child).Should().BeFalse();
        PoseChecks.AssertNear(test.World.PoseInOwnFrame(follower).Position, new(5, 1, 0));
    }
}

/// <summary>
/// Spatial queries through the world: the grid that <see cref="Snapshot.QueryRadius"/> reads follows moves and
/// destruction, answers exactly, and may be read from the gameplay workers. These tests live in this file for now; they
/// have nothing to do with attachments.
/// </summary>
public sealed class SpatialQueryTests
{
    [Fact]
    public void ARadiusQueryFindsExactlyTheEntitiesWithinTheRadiusInSlotOrder()
    {
        using var test = new TestWorld();
        FrameHandle other = test.SpawnStaticFrame(TestWorld.At(0), radius: 1000);
        var random = new Random(1234);
        // The owner of the inner frame is itself an entity of the root frame, at its origin.
        var points = new List<(Entity Entity, Double3 Position)> { (other.Entity, Double3.Zero) };
        for (int i = 0; i < 400; i++)
        {
            var p = new Double3(
                random.NextDouble() * 400 - 200,
                random.NextDouble() * 400 - 200,
                random.NextDouble() * 400 - 200
            );
            points.Add((test.SpawnAt(p), p));
            test.World.SpawnAt(other, p);
        }

        Entity onEdge = test.SpawnAt(new(50, 0, 0));
        points.Add((onEdge, new(50, 0, 0)));
        var results = new List<Entity>();

        foreach (double radius in new[] { 0.0, 10, 50, 75, 150, 1e6 })
        {
            test.World.Snapshot.QueryRadius(frame: test.Root, center: Double3.Zero, radius: radius, results: results);

            List<Entity> expected =
            [
                .. points
                    .Where(p => p.Position.LengthSquared <= radius * radius)
                    .Select(p => p.Entity)
                    .OrderBy(e => e.Index),
            ];
            results.Should().Equal(expected, $"for radius {radius}");
        }
    }

    [Fact]
    public void ARadiusQueryIncludesAnEntityExactlyOnTheEdge()
    {
        using var test = new TestWorld();
        Entity onEdge = test.SpawnAt(new(50, 0, 0));
        var results = new List<Entity>();

        test.World.Snapshot.QueryRadius(frame: test.Root, center: Double3.Zero, radius: 50, results: results);

        results.Should().Equal(onEdge);
    }

    [Fact]
    public void ARadiusQueryOnlyFindsEntitiesOfTheAskedFrame()
    {
        using var test = new TestWorld();
        FrameHandle other = test.SpawnStaticFrame(TestWorld.At(0), radius: 1000);
        Entity inRoot = test.SpawnAt(new(1, 0, 0));
        Entity inOther = test.World.SpawnAt(other, new(1, 0, 0));
        var results = new List<Entity>();

        test.World.Snapshot.QueryRadius(frame: other, center: Double3.Zero, radius: 10, results: results);

        results.Should().Equal(inOther);
        results.Should().NotContain(inRoot);
    }

    [Fact]
    public void AQueryInAFrameWithNothingInItFindsNothingAndClearsTheResultList()
    {
        using var test = new TestWorld();
        FrameHandle empty = test.SpawnStaticFrame(TestWorld.At(0), radius: 10);
        test.SpawnAt(Double3.Zero);
        var found = new List<Entity> { test.Root.Entity };

        test.World.Snapshot.QueryRadius(frame: empty, center: Double3.Zero, radius: 100, results: found);

        found.Should().BeEmpty();
    }

    [Fact]
    public void AQueryWithoutAFrameIsRefused()
    {
        using var test = new TestWorld();

        Action query = () =>
            test.World.Snapshot.QueryRadius(frame: FrameHandle.Null, center: Double3.Zero, radius: 1, results: []);

        query.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AQueryWithANegativeRadiusIsRefused()
    {
        using var test = new TestWorld();

        Action query = () =>
            test.World.Snapshot.QueryRadius(frame: test.Root, center: Double3.Zero, radius: -1, results: []);

        query.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TheGridFollowsAnEntityThatMovesFarEachTick()
    {
        using var test = new TestWorld();
        Entity runner = test.SpawnWith(new Mover(new(40, 0, 0)));
        var results = new List<Entity>();

        for (int tick = 0; tick < 8; tick++)
        {
            test.World.Snapshot.QueryRadius(
                frame: test.Root,
                center: new(40 * tick, 0, 0),
                radius: 1,
                results: results
            );

            results.Should().Equal(runner);
            test.Run(1);
        }
    }

    [Fact]
    public void TheGridForgetsAnEntityThatIsDestroyed()
    {
        using var test = new TestWorld();
        Entity post = test.SpawnAt(new(200, 50, 0));
        var results = new List<Entity>();
        test.World.Snapshot.QueryRadius(frame: test.Root, center: new(200, 50, 0), radius: 1, results: results);
        results.Should().Equal(post);

        test.World.Destroy(post);

        test.World.Snapshot.QueryRadius(frame: test.Root, center: new(200, 50, 0), radius: 1, results: results);
        results.Should().BeEmpty();
    }

    [Fact]
    public void ComponentsCanQueryTheSnapshotFromWorkerThreads()
    {
        // This is one of the few tests that use several threads: the point is that the query is safe on workers.
        using var test = new TestWorld(maxDegreeOfParallelism: 8, chunkSize: 4);
        int[] counts = new int[64];
        for (int i = 0; i < 64; i++)
        {
            int index = i;
            test.World.Spawn(
                new()
                {
                    Frame = test.Root,
                    Pose = TestWorld.At(i * 10),
                    Components =
                    [
                        Lambda.OnTick(
                            (self, scoped ref events, scoped ref outbox) =>
                            {
                                var found = new List<Entity>();
                                self.Snapshot.QueryRadius(
                                    frame: self.Frame,
                                    center: self.PoseInOwnFrame().Position,
                                    radius: 15,
                                    results: found
                                );
                                counts[index] = found.Count;
                            }
                        ),
                    ],
                }
            );
        }

        test.Run(1);

        // Entities are 10 m apart, so each one has its neighbors on both sides within 15 m, and itself. The two at the ends have one neighbor.
        counts[0].Should().Be(2);
        counts[63].Should().Be(2);
        counts[1..63].Should().OnlyContain(c => c == 3);
    }
}
