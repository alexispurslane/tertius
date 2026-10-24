using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// <see cref="FrameTree.OwnerViewStamp"/>, the number that changes on every change of the state that an owner view reads
/// (see <see cref="FrameTree.VisitOwnerViews"/>): the local pose, the linear velocity and the angular velocity of a frame,
/// and the shape of the tree. The World skips the second mirror pass of a sync point while the stamp has not changed since
/// the first, so a write that leaves the stamp as it is would leave an owner's row behind its frame. Reads leave the stamp
/// as it is, so that the pass is skipped when nothing changed.
/// </summary>
public class FrameTreeOwnerViewStampTests
{
    private readonly FrameTree frames = new();
    private readonly FrameHandle ground;
    private readonly FrameHandle ship;

    public FrameTreeOwnerViewStampTests()
    {
        ground = frames.Create(frameOwner: new(1, 1), parent: FrameHandle.Null, localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(1e9f));
        ship = frames.Create(frameOwner: new(2, 1), parent: ground, localPose: new(X(100), DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(50));
        frames.SetHullMotion(ship, BodyMotion.Dynamic);
    }

    private static Double3 X(double x) => new(x, 0, 0);

    /// <summary>Runs <paramref name="change"/> and returns whether the stamp changed.</summary>
    private bool StampChanges(Action change)
    {
        long before = frames.OwnerViewStamp;
        change();
        return frames.OwnerViewStamp != before;
    }

    /// <summary>
    /// One numeric update over a character body of ground at <paramref name="x"/>, moving at <paramref name="vx"/> along X,
    /// whose physics home is <paramref name="home"/> or ground, with its frame <paramref name="owned"/> if any. Returns the
    /// body's home after the update.
    /// </summary>
    private FrameHandle Update(double x, double vx, FrameHandle home = default, FrameHandle owned = default)
    {
        FrameHandle physicsHome = home.IsNull ? ground : home;
        var body = new NumericBody
        {
            Entity = owned.IsNull ? new(10, 1) : owned.Entity, Semantic = ground, Home = physicsHome, Position = X(x),
            HomePosition = X(x) - frames.HomeOffset(physicsHome, ground), Velocity = X(vx), Radius = 1, Mass = 1,
            Character = true,
            OwnedFrame = owned
        };
        FrameHandle after = physicsHome;
        frames.UpdateNumericFrames(bodies: [body], dt: 0.1, step: 0.1, changes: new(), applyMoves: moves =>
        {
            foreach (NumericMove move in moves)
                after = move.To;
        });
        frames.AssertInvariants();
        return after;
    }

    [Fact]
    public void A_new_frame_changes_the_stamp() =>
        StampChanges(() => frames.Create(frameOwner: new(3, 1), parent: ground, localPose: DPose.Identity,
                frameBounds: FrameBounds.Sphere(5)))
            .Should().BeTrue();

    [Fact]
    public void A_frame_that_dies_changes_the_stamp() =>
        StampChanges(() => frames.Destroy(ship)).Should().BeTrue();

    [Fact]
    public void A_frame_that_moves_under_another_parent_changes_the_stamp()
    {
        FrameHandle dock =
            frames.Create(frameOwner: new(3, 1), parent: ground, localPose: new(X(-100), DQuaternion.Identity),
                frameBounds: FrameBounds.Sphere(500));
        StampChanges(() => frames.Reparent(ship, dock)).Should().BeTrue();
    }

    [Fact]
    public void A_new_local_pose_changes_the_stamp() =>
        StampChanges(() => frames.SetLocalPose(ship, new(X(200), DQuaternion.Identity))).Should().BeTrue();

    [Fact]
    public void A_new_linear_velocity_changes_the_stamp() =>
        StampChanges(() => frames.SetTreeLinearVelocity(ship, X(5))).Should().BeTrue();

    [Fact]
    public void A_new_angular_velocity_changes_the_stamp() =>
        StampChanges(() => frames.SetAngularVelocity(ship, new(0, 0.1f, 0))).Should().BeTrue();

    [Fact]
    public void A_frame_that_takes_its_step_record_changes_the_stamp()
    {
        var record = new MotionSample(Pose: new(X(101), DQuaternion.Identity), ChordVelocity: X(60),
            Velocity: Double3.Zero, Acceleration: Double3.Zero,
            AngularVelocity: Double3.Zero, AngularAcceleration: Double3.Zero);
        StampChanges(() =>
                frames.TakeStepRecords(stepIntents: [], step: 1.0 / 60,
                    owners: new StepOwners().WithHullRecord(ship, record))).Should()
            .BeTrue();
    }

    [Fact]
    public void A_frame_that_holds_still_changes_the_stamp() =>
        StampChanges(() => frames.TakeStepRecords(stepIntents: [], step: 1.0 / 60)).Should().BeTrue();

    // A change of a hull's motion writes no pose, velocity or parent: a parked ship stops through the record of its next
    // step, which changes the stamp (see A_frame_that_takes_its_step_record_changes_the_stamp). So the change leaves the
    // stamp as it is, and the World's second pass of a sync point can still be skipped.
    [Fact]
    public void A_change_of_a_hulls_motion_leaves_the_stamp_as_it_is()
    {
        frames.SetTreeLinearVelocity(ship, X(5));
        StampChanges(() =>
        {
            frames.PlanHullMotion(ship, BodyMotion.Static);
            frames.SetHullMotion(ship, BodyMotion.Static);
        }).Should().BeFalse();
    }

    [Fact]
    public void A_falling_frame_that_moves_over_a_step_changes_the_stamp()
    {
        Update(x: 50, vx: 60);
        frames.Update();
        frames.BeginStep([], 1.0 / 60, lastStepFailed: false);
        StampChanges(() => frames.AdvanceNumericFrames(0.1)).Should().BeTrue();
    }

    [Fact]
    public void A_numeric_update_that_takes_a_frame_into_a_falling_frame_changes_the_stamp()
    {
        frames.SetTreeLinearVelocity(ship, X(60));
        FrameHandle pod = FrameHandle.Null;
        StampChanges(() => pod = Update(x: 100, vx: 60, owned: ship)).Should().BeTrue();
        frames.TreeParent(ship).Should().Be(pod);
    }

    [Fact]
    public void A_nest_that_jumps_after_its_frames_changes_the_stamp()
    {
        // As in FrameTreeNumericUpdateTests: a pod 3·2^30 m out nests, then drifts 2e9 m, and its nest jumps after it.
        const double place = 3L << 30;
        FrameHandle pod = Update(x: place, vx: 60);
        FrameHandle nest = frames.TreeParent(pod);
        frames.Numeric(nest).Should().Be(NumericMode.Nesting);
        frames.Update();
        frames.BeginStep([], 1.0 / 60, lastStepFailed: false);
        frames.AdvanceNumericFrames(2e9 / 60);
        double drift = frames.TreeLocalPose(pod).Position.X;
        Double3 nestPlace = frames.TreeLocalPose(nest).Position;

        StampChanges(() => Update(x: place + drift, vx: 60, home: pod)).Should().BeTrue();
        frames.TreeLocalPose(nest).Position.Should().NotBe(nestPlace, "the nest jumped");
    }

    [Fact]
    public void A_numeric_update_that_changes_no_frame_leaves_the_stamp_as_it_is() =>
        StampChanges(() => Update(x: 100, vx: 0)).Should()
            .BeFalse("a slow body near the origin of its frame needs no numeric frame");

    [Fact]
    public void Reads_leave_the_stamp_as_it_is()
    {
        frames.SetTreeLinearVelocity(ship, X(5));
        frames.Update();
        StampChanges(() =>
        {
            var views = new ViewCounter();
            frames.VisitOwnerViews(ref views);
            _ = frames.OwnerView(ship);
            _ = frames.LocalPose(ship);
            _ = frames.TreeLocalPose(ship);
            _ = frames.LinearVelocity(ship);
            _ = frames.TreeLinearVelocity(ship);
            _ = frames.AngularVelocity(ship);
            _ = frames.Relative(ship, ground);
            _ = frames.InSemantic(ship, ship, X(1));
            _ = frames.Parent(ship);
            _ = frames.ConvertVelocity(from: ship, to: ground, position: X(1), v: X(1));
            frames.GetAll([]);
            frames.Update();
        }).Should().BeFalse();
    }

    private struct ViewCounter : IOwnerViewVisitor
    {
        public int Count;

        public void Visit(in FrameOwnerView view) => Count++;
    }
}
