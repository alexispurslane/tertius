using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The attachment chains on their own, without a world around them. An attached entity follows its parent with a fixed
/// offset in the parent's axes. <see cref="Attachments.Derive"/> computes the back transform of every attached entity from the
/// back row of its chain root, which is the one entity of a chain that is not attached. The tests read the result from
/// the back rows of the entity table.
/// </summary>
public class AttachmentsSubsystemTests
{
    /// <summary>A quarter turn about +Y, which maps +X to -Z and +Z to +X.</summary>
    private static readonly Quaternion QuarterTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);

    private static Pose Offset(float x, float y, float z = 0) => new(new(x, y, z), Quaternion.Identity);

    private static Pose TurnedOffset(float x, float y, float z) => new(new(x, y, z), QuarterTurn);

    private static Double3 PositionOf(EntityTable entities, Entity entity) => entities.BackRow(entity).Pose.Position;

    /// <summary>Gives a chain root a velocity and a spin, as physics would have written them into its back row.</summary>
    private static void SetMotion(EntityTable entities, Entity entity, Vector3 velocity, Vector3 spin)
    {
        TransformRow row = entities.BackRow(entity);
        row.LinearVelocity = velocity;
        row.AngularVelocity = spin;
        entities.SetBackTransform(entity, row);
    }

    // ---- Releasing ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleasingAChainDetachesEveryLinkAndTheRootLeavesTheTree(bool parentFirst)
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity root = entities.SpawnLive(x: 10);
        Entity child = entities.SpawnLive();
        Entity grandchild = entities.SpawnLive();
        attachments.Attach(child, root, Offset(1, 0));
        attachments.Attach(grandchild, child, Offset(0, 1));
        attachments.Derive();
        attachments.Attached.ToArray().Should().Equal(child, grandchild);
        PositionOf(entities, grandchild).Should().Be(new Double3(11, 1, 0));
        attachments.AssertInvariants();

        // The world releases an attached entity together with its children, in either order.
        Entity[] order = parentFirst ? [child, grandchild] : [grandchild, child];
        foreach (Entity entity in order)
            entities.Release(entity);

        attachments.Attached.ToArray().Should().BeEmpty();
        attachments.ChildrenOf(root).IsEmpty.Should().BeTrue();
        // The released links are clear: the table checks every column entry of a slot that is not live.
        entities.AssertInvariants();
        attachments.AssertInvariants();

        // The root can take a new child, which starts a new chain.
        Entity next = entities.SpawnLive();
        attachments.Attach(next, root, Offset(2, 0));
        attachments.Derive();
        PositionOf(entities, next).Should().Be(new Double3(12, 0, 0));
        attachments.AssertInvariants();
    }

    [Fact]
    public void ACommonParentKeepsItsOtherChildrenWhenOneChildIsReleased()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity root = entities.SpawnLive();
        Entity first = entities.SpawnLive();
        Entity second = entities.SpawnLive();
        attachments.Attach(first, root, Offset(1, 0));
        attachments.Attach(second, root, Offset(2, 0));

        entities.Release(first);

        attachments.ChildrenOf(root).ToArray().Should().Equal(second);
        attachments.Attached.ToArray().Should().Equal(second);
        attachments.AssertInvariants();
    }

    [Fact]
    public void AnEntityThatLostItsLastChildCanBeAttachedToAParentOfItsOwn()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity parent = entities.SpawnLive(x: 20);
        Entity middle = entities.SpawnLive(x: 10);
        Entity child = entities.SpawnLive();
        attachments.Attach(child, middle, Offset(1, 0));
        entities.Release(child);

        attachments.Attach(middle, parent, Offset(3, 0));
        attachments.Derive();

        PositionOf(entities, middle).Should().Be(new Double3(23, 0, 0));
        attachments.AssertInvariants();
    }

    // ---- Who is attached to whom ----------------------------------------------------------------------------------

    [Fact]
    public void AnEntityThatIsNotAttachedHasNoParent()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity loner = entities.SpawnLive();

        attachments.ParentOf(loner).IsNull.Should().BeTrue();
    }

    [Fact]
    public void AnAttachedEntityNamesItsParentAndTheParentListsItAsAChild()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity parent = entities.SpawnLive();
        Entity child = entities.SpawnLive();

        attachments.Attach(child, parent, Offset(1, 0));

        attachments.ParentOf(child).Should().Be(parent);
        attachments.ChildrenOf(parent).ToArray().Should().Equal(child);
    }

    [Fact]
    public void TheChildrenOfAParentAreListedInSlotOrderWhateverTheOrderOfAttaching()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity parent = entities.SpawnLive();
        Entity first = entities.SpawnLive(), second = entities.SpawnLive(), third = entities.SpawnLive();

        attachments.Attach(third, parent, Offset(3, 0));
        attachments.Attach(first, parent, Offset(1, 0));
        attachments.Attach(second, parent, Offset(2, 0));

        attachments.ChildrenOf(parent).ToArray().Should().Equal(first, second, third);
        attachments.Attached.ToArray().Should().Equal(first, second, third);
    }

    [Fact]
    public void AnEntityWithNoChildrenHasAnEmptyChildList()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity loner = entities.SpawnLive();

        attachments.ChildrenOf(loner).IsEmpty.Should().BeTrue();
    }

    // ---- What a parent accepts ------------------------------------------------------------------------------------

    [Fact]
    public void ALiveParentIsAcceptedAndItsNewChildTakesItsFrame()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        FrameHandle frame = FakeFrame(2);
        Entity parent = entities.SpawnLive(frame);

        attachments.CheckParent(parent, out FrameHandle childFrame).Should().BeNull();
        childFrame.Should().Be(frame);
    }

    [Fact]
    public void ANewChildOfAnAttachedParentTakesTheFrameOfTheChainRootBeforeTheNextDerive()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        FrameHandle before = FakeFrame(2), after = FakeFrame(3);
        Entity root = entities.SpawnLive(before);
        Entity middle = entities.SpawnLive(before);
        attachments.Attach(middle, root, Offset(1, 0));
        attachments.Derive();

        // Physics hands the root off. The row of the middle link shows the old frame until the next Derive.
        TransformRow row = entities.BackRow(root);
        row.Frame = after;
        entities.SetBackTransform(root, row);

        attachments.CheckParent(middle, out FrameHandle childFrame).Should().BeNull();
        childFrame.Should().Be(after);
    }

    [Fact]
    public void AParentThatIsNotLiveIsRefusedWithAReasonThatNamesIt()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity parent = entities.SpawnLive(FakeFrame(2));
        entities.Release(parent);

        string? reason = attachments.CheckParent(parent, out FrameHandle childFrame);

        reason.Should().Contain(parent.ToString()).And.Contain("not live");
        childFrame.IsNull.Should().BeTrue();
    }

    [Fact]
    public void TheNullEntityIsNotAParent()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);

        attachments.CheckParent(Entity.Null, out _).Should().NotBeNull();
    }

    [Fact]
    public void TheOwnerOfARootFrameIsRefusedBecauseItIsInNoFrame()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity rootOwner = entities.SpawnLive(FrameHandle.Null);

        string? reason = attachments.CheckParent(rootOwner, out FrameHandle childFrame);

        reason.Should().Contain(rootOwner.ToString()).And.Contain("no frame");
        childFrame.IsNull.Should().BeTrue();
    }

    // ---- Writing an offset ----------------------------------------------------------------------------------------

    [Fact]
    public void AnEntityThatIsNotAttachedCannotWriteAnOffset()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity loner = entities.SpawnLive();

        Action write = () => attachments.WritableOffset(loner) = Offset(1, 0);

        write.Should().Throw<InvalidOperationException>().WithMessage($"*{loner}*not attached*");
    }

    [Fact]
    public void ANewOffsetMovesTheChildAtTheNextDeriveAndNotBefore()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity parent = entities.SpawnLive(x: 10);
        Entity child = entities.SpawnLive();
        attachments.Attach(child, parent, Offset(1, 0));
        attachments.Derive();

        attachments.WritableOffset(child) = Offset(5, 0);
        PositionOf(entities, child).Should().Be(new Double3(11, 0, 0));
        attachments.Derive();

        PositionOf(entities, child).Should().Be(new Double3(15, 0, 0));
    }

    // ---- Deriving the transform of a child ------------------------------------------------------------------------

    [Fact]
    public void ADerivedChildSitsAtItsOffsetFromItsParent()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity parent = entities.SpawnLive(x: 10);
        Entity child = entities.SpawnLive();
        attachments.Attach(child, parent, Offset(1, 2, 3));

        attachments.Derive();

        PositionOf(entities, child).Should().Be(new Double3(11, 2, 3));
    }

    [Fact]
    public void AnOffsetIsMeasuredInTheAxesOfTheParent()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity parent = entities.SpawnLive(x: 10);
        TurnTo(entities, parent, QuarterTurn);
        Entity child = entities.SpawnLive();
        attachments.Attach(child, parent, Offset(0, 0, 2));

        attachments.Derive();

        // The quarter turn maps the parent's +Z to the frame's +X.
        PoseChecks.AssertNear(PositionOf(entities, child), new(12, 0, 0), 1e-6);
    }

    [Fact]
    public void ADerivedChildTurnsWithItsParentAndAddsTheTurnOfItsOffset()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity parent = entities.SpawnLive();
        TurnTo(entities, parent, QuarterTurn);
        Entity child = entities.SpawnLive();
        attachments.Attach(child, parent, TurnedOffset(0, 0, 0));

        attachments.Derive();

        // Two quarter turns about +Y make a half turn, which maps +X to -X.
        Double3 turnedX = entities.BackRow(child).Pose.Rotation.Rotate(Double3.UnitX);
        PoseChecks.AssertNear(turnedX, new(-1, 0, 0), 1e-6);
    }

    [Fact]
    public void AGrandchildIsDerivedFromTheDerivedPoseOfItsParent()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity root = entities.SpawnLive(x: 10);
        Entity child = entities.SpawnLive();
        Entity grandchild = entities.SpawnLive();
        attachments.Attach(child, root, TurnedOffset(1, 0, 0));
        attachments.Attach(grandchild, child, Offset(0, 0, 1));

        attachments.Derive();

        // The child is at (11, 0, 0) and turned a quarter turn, which maps the grandchild's +Z offset to +X: (12, 0, 0).
        PoseChecks.AssertNear(PositionOf(entities, grandchild), new(12, 0, 0), 1e-6);
    }

    [Fact]
    public void AChildIsDerivedFromItsParentEvenWhenTheChildHasTheLowerSlot()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity root = entities.SpawnLive(x: 10);
        Entity grandchild = entities.SpawnLive();
        Entity child = entities.SpawnLive();
        // The chain is made from the top: the child with the higher slot is attached first.
        attachments.Attach(child, root, TurnedOffset(1, 0, 0));
        attachments.Attach(grandchild, child, Offset(0, 0, 1));
        grandchild.Index.Should().BeLessThan(child.Index);

        attachments.Derive();

        PoseChecks.AssertNear(PositionOf(entities, grandchild), new(12, 0, 0), 1e-6);
    }

    [Fact]
    public void ALongChainIsDerivedLinkByLink()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity root = entities.SpawnLive();
        Entity a = entities.SpawnLive(), b = entities.SpawnLive(), c = entities.SpawnLive(), d = entities.SpawnLive();
        attachments.Attach(a, root, Offset(1, 0));
        attachments.Attach(b, a, Offset(1, 0));
        attachments.Attach(c, b, Offset(1, 0));
        attachments.Attach(d, c, Offset(1, 0));

        attachments.Derive();

        PositionOf(entities, d).Should().Be(new Double3(4, 0, 0));
    }

    [Fact]
    public void TheChildrenOfOneParentAreDerivedIndependentlyOfEachOther()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity parent = entities.SpawnLive(x: 10);
        Entity left = entities.SpawnLive(), right = entities.SpawnLive();
        attachments.Attach(left, parent, Offset(-1, 0));
        attachments.Attach(right, parent, Offset(1, 0));

        attachments.Derive();

        PositionOf(entities, left).Should().Be(new Double3(9, 0, 0));
        PositionOf(entities, right).Should().Be(new Double3(11, 0, 0));
    }

    [Fact]
    public void ANewPoseOfTheChainRootMovesTheWholeChainAtTheNextDerive()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity root = entities.SpawnLive(x: 10);
        Entity child = entities.SpawnLive();
        Entity grandchild = entities.SpawnLive();
        attachments.Attach(child, root, Offset(1, 0));
        attachments.Attach(grandchild, child, Offset(0, 1));
        attachments.Derive();

        TransformRow moved = entities.BackRow(root);
        moved.Pose = TestWorld.At(100);
        entities.SetBackTransform(root, moved);
        attachments.Derive();

        PositionOf(entities, child).Should().Be(new Double3(101, 0, 0));
        PositionOf(entities, grandchild).Should().Be(new Double3(101, 1, 0));
    }

    [Fact]
    public void ADerivedChildIsInTheFrameOfItsChainRoot()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        FrameHandle home = FakeFrame(2), station = FakeFrame(3);
        Entity root = entities.SpawnLive(home);
        Entity child = entities.SpawnLive(home);
        Entity grandchild = entities.SpawnLive(home);
        attachments.Attach(child, root, Offset(1, 0));
        attachments.Attach(grandchild, child, Offset(1, 0));

        TransformRow moved = entities.BackRow(root);
        moved.Frame = station;
        entities.SetBackTransform(root, moved);
        attachments.Derive();

        entities.BackRow(child).Frame.Should().Be(station);
        entities.BackRow(grandchild).Frame.Should().Be(station);
    }

    [Fact]
    public void DerivingWhenNothingIsAttachedChangesNothing()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity loner = entities.SpawnLive(x: 4);

        attachments.Derive();

        PositionOf(entities, loner).Should().Be(new Double3(4, 0, 0));
        attachments.AssertInvariants();
    }

    [Fact]
    public void AnOffsetWhoseRotationIsAllZeroesCountsAsNoRotation()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity parent = entities.SpawnLive(x: 10);
        Entity child = entities.SpawnLive();
        // A defaulted Pose has the quaternion (0, 0, 0, 0), which is not a rotation.
        attachments.Attach(child, parent, new(new(1, 0, 0), default));

        attachments.Derive();

        PositionOf(entities, child).Should().Be(new Double3(11, 0, 0));
        PoseChecks.AssertSameRotation(entities.BackRow(child).Pose.Rotation, DQuaternion.Identity);
    }

    // ---- Velocity -------------------------------------------------------------------------------------------------

    [Fact]
    public void ADerivedChildMovesWithTheVelocityOfItsRootPlusTheSpinOfItsLeverArm()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity root = entities.SpawnLive();
        SetMotion(entities: entities, entity: root, velocity: new(1, 0, 0), spin: new(0, 1, 0));
        Entity child = entities.SpawnLive();
        attachments.Attach(child, root, Offset(0, 0, 2));

        attachments.Derive();

        // The velocity of a point on a spinning body is v + w x r. Here (1, 0, 0) + (0, 1, 0) x (0, 0, 2) = (3, 0, 0).
        PoseChecks.AssertNear(entities.BackRow(child).LinearVelocity, new(3, 0, 0));
    }

    [Fact]
    public void TheLeverArmTurnsWithTheRoot()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity root = entities.SpawnLive();
        TurnTo(entities, root, QuarterTurn);
        SetMotion(entities: entities, entity: root, velocity: new(1, 0, 0), spin: new(0, 1, 0));
        Entity child = entities.SpawnLive();
        attachments.Attach(child, root, Offset(0, 0, 2));

        attachments.Derive();

        // The turned root puts the child 2 m along +X, so the lever arm is (2, 0, 0), and (0, 1, 0) x (2, 0, 0) = (0, 0, -2).
        PoseChecks.AssertNear(entities.BackRow(child).LinearVelocity, new(1, 0, -2), 1e-5f);
    }

    [Fact]
    public void TheLeverArmOfAGrandchildRunsFromTheChainRootThroughTheWholeChain()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity root = entities.SpawnLive();
        SetMotion(entities: entities, entity: root, velocity: Vector3.Zero, spin: new(0, 0, 1));
        Entity child = entities.SpawnLive();
        Entity grandchild = entities.SpawnLive();
        attachments.Attach(child, root, Offset(1, 0));
        attachments.Attach(grandchild, child, Offset(0, 1));

        attachments.Derive();

        // The grandchild is 1 m along +X and 1 m along +Y from the root: (0, 0, 1) x (1, 1, 0) = (-1, 1, 0).
        PoseChecks.AssertNear(entities.BackRow(grandchild).LinearVelocity, new(-1, 1, 0));
        PoseChecks.AssertNear(entities.BackRow(child).LinearVelocity, new(0, 1, 0));
    }

    [Fact]
    public void EveryLinkOfAChainSpinsLikeItsRoot()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity root = entities.SpawnLive();
        SetMotion(entities: entities, entity: root, velocity: Vector3.Zero, spin: new(0.5f, 0, 2));
        Entity child = entities.SpawnLive();
        Entity grandchild = entities.SpawnLive();
        attachments.Attach(child, root, Offset(1, 0));
        attachments.Attach(grandchild, child, Offset(0, 1));

        attachments.Derive();

        entities.BackRow(child).AngularVelocity.Should().Be(new Vector3(0.5f, 0, 2));
        entities.BackRow(grandchild).AngularVelocity.Should().Be(new Vector3(0.5f, 0, 2));
    }

    [Fact]
    public void AChildOfARootThatDoesNotMoveDoesNotMoveEither()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity root = entities.SpawnLive();
        Entity child = entities.SpawnLive();
        attachments.Attach(child, root, Offset(1, 0));

        attachments.Derive();

        entities.BackRow(child).LinearVelocity.Should().Be(Vector3.Zero);
        entities.BackRow(child).AngularVelocity.Should().Be(Vector3.Zero);
    }

    // ---- Carrying the pushes -------------------------------------------------------------------------------------

    /// <summary>A live entity with a dynamic ball in <paramref name="frame"/>, as a chain root is.</summary>
    private static Entity BodyRoot(EntityTable entities, FrameHandle frame)
    {
        Entity entity = entities.Reserve();
        entities.Activate(entity: entity, row: new() { Frame = frame, PhysicsHome = frame }, render: new(), components: [], wants: [], body: TestWorld.Ball,
            bodyCenterOfMass: Vector3.Zero, ownedFrame: FrameHandle.Null);
        return entity;
    }

    [Fact]
    public void TheFlowEmptiesTheFrontIntentOfEveryPartThatItVisitsAndGivesTheRootOnlyTheLivingPartsPush()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        FrameHandle space = FakeFrame(1);
        Entity root = BodyRoot(entities, space);
        Entity living = entities.SpawnLive(space);
        Entity dying = entities.SpawnLive(space);
        attachments.Attach(living, root, Offset(0, 0));
        attachments.Attach(dying, root, Offset(0, 0));
        entities.BeginTick(spawnPoolSize: 0);
        entities.WritablePush(living).Force += new Vector3(1, 0, 0);
        entities.WritablePush(dying).Force += new Vector3(0, 2, 0);
        entities.EndGameplay();
        entities.SwapIntents(keep: true);
        bool[] marks = new bool[entities.Capacity];
        marks[dying.Index] = true;

        attachments.CarryPushes(marks, []);

        entities.FrontIntents.Span[(int)root.Index].Force.Should().Be(new Vector3(1, 0, 0));
        entities.HasEmptyFrontIntent(living).Should().BeTrue("the living part's push moved to its root");
        entities.HasEmptyFrontIntent(dying).Should()
            .BeTrue("the dying part's push does not act and leaves its intent too");
        entities.EndTick();
        entities.AssertInvariants();
        attachments.AssertInvariants();
    }

#if TERTIUS_CONTRACTS
    [Fact]
    public void CarryingThePushesWhileGameplayRunsBreaksAContract()
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        entities.BeginTick(spawnPoolSize: 0);

        Action carry = () => attachments.CarryPushes(new bool[entities.Capacity], []);

        carry.Should()
            .Throw<ContractException>("the flow reads the front intents, which only the sync point may change");
    }
#endif

    // ---- Helpers --------------------------------------------------------------------------------------------------

    /// <summary>A frame handle for a test that needs only distinct frames: the subsystem compares frames and does not look them up.</summary>
    private static FrameHandle FakeFrame(int index) => new(new((uint)index, 1), index, 1);

    private static void TurnTo(EntityTable entities, Entity entity, Quaternion rotation)
    {
        TransformRow row = entities.BackRow(entity);
        row.Pose = row.Pose with { Rotation = DQuaternion.FromQuaternion(rotation) };
        entities.SetBackTransform(entity, row);
    }
}
