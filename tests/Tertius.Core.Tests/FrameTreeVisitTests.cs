using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The walk of the frame tree that the sync point makes once per pass of the owners' mirror,
/// <see cref="FrameTree.VisitOwnerViews"/>, which hands each frame the state that the transform row of its owner mirrors.
/// It visits every frame that is not numeric once, in dense order, which puts parents before children. Numeric frames have
/// no owner, so the walk does not visit them. A visitor must not change what the walk reads, and a contract checks that in a
/// Debug build. The pass of step 2 walks the tree the same way (see FrameTreeTakeStepRecordsTests).
/// </summary>
public sealed class FrameTreeVisitTests
{
    /// <summary>One light-year, in meters. A double steps by 2 m there.</summary>
    private const double LightYear = 9.4607e15;

    private static readonly DQuaternion Turned = DQuaternion.FromAxisAngle(Double3.UnitY, 0.5);

    private readonly FrameTree frames = new();

    private static Double3 X(double x) => new(x, 0, 0);

    /// <summary>A frame owned by entity <paramref name="owner"/> under <paramref name="parent"/>, at <paramref name="x"/> on X.</summary>
    private FrameHandle Frame(uint owner, FrameHandle parent, double x = 0) =>
        frames.Create(frameOwner: new(owner, 1), parent: parent, localPose: new(X(x), DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(1e9f));

    private struct ViewRecorder(List<FrameOwnerView> seen) : IOwnerViewVisitor
    {
        public readonly void Visit(in FrameOwnerView view) => seen.Add(view);
    }

#if TERTIUS_CONTRACTS
    /// <summary>A visitor that sets the linear velocity of each frame that it visits.</summary>
    private struct Pusher(FrameTree frames) : IOwnerViewVisitor
    {
        public readonly void Visit(in FrameOwnerView view) => frames.SetTreeLinearVelocity(view.Frame, X(1));
    }
#endif

    private List<FrameOwnerView> Views()
    {
        var seen = new List<FrameOwnerView>();
        var recorder = new ViewRecorder(seen);
        frames.VisitOwnerViews(ref recorder);
        return seen;
    }

    /// <summary>
    /// A root frame ground, and the frame of a ship's hull that a numeric update puts in a falling frame, the pod, because
    /// the hull is a character at 60 m/s (see FrameTreeNumericUpdateTests): ground holds the pod, which holds the hull.
    /// </summary>
    private (FrameHandle Ground, FrameHandle Hull, FrameHandle Pod) HullInPod(double x = 50, double speed = 60)
    {
        FrameHandle ground = Frame(1, FrameHandle.Null);
        FrameHandle hull = Frame(20, ground, x);
        frames.SetHullMotion(hull, BodyMotion.Dynamic);
        frames.SetTreeLinearVelocity(hull, X(speed));
        var body = new NumericBody
        {
            Entity = new(20, 1), Semantic = ground, Home = ground, Position = X(x), HomePosition = X(x),
            Velocity = X(speed),
            Radius = 1, Mass = 1, Character = true, OwnedFrame = hull
        };
        frames.UpdateNumericFrames(bodies: [body], dt: 0.1, step: 0.1, changes: new(), applyMoves: _ => { });
        FrameHandle pod = frames.TreeParent(hull);
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        return (ground, hull, pod);
    }

    [Fact]
    public void Owner_views_come_once_each_in_dense_order()
    {
        FrameHandle a = Frame(1, FrameHandle.Null);
        FrameHandle b = Frame(2, a);
        FrameHandle c = Frame(3, a);
        FrameHandle d = Frame(4, b);
        frames.Reparent(c, d);

        Views().Select(view => view.Frame).Should().Equal(a, b, d, c);
    }

    [Fact]
    public void The_owner_view_of_a_frame_holds_its_parent_as_home_and_semantic_frame_its_pose_and_its_velocities()
    {
        FrameHandle root = frames.Create(frameOwner: new(1, 1), parent: FrameHandle.Null, localPose: new(X(5), Turned),
            frameBounds: FrameBounds.Sphere(1e9f));
        var childPose = new DPose(new(7, 8, 9), Turned);
        FrameHandle child = frames.Create(frameOwner: new(2, 1), parent: root, localPose: childPose,
            frameBounds: FrameBounds.Sphere(50));
        frames.SetTreeLinearVelocity(child, new(1, 2, 3));
        frames.SetAngularVelocity(child, new(0, 0.5f, 0));

        Views().Should().Equal(
            new FrameOwnerView(Frame: root, Home: FrameHandle.Null, Semantic: FrameHandle.Null,
                LocalPose: new(X(5), Turned), PositionInSemantic: X(5), LinearVelocity: Double3.Zero,
                AngularVelocity: Vector3.Zero),
            new FrameOwnerView(Frame: child, Home: root, Semantic: root, LocalPose: childPose,
                PositionInSemantic: new(7, 8, 9), LinearVelocity: new(1, 2, 3), AngularVelocity: new(0, 0.5f, 0)));
    }

    [Fact]
    public void A_numeric_frame_has_no_owner_view_and_a_frame_in_it_has_it_as_home_and_the_semantic_frame_as_its_frame()
    {
        (FrameHandle ground, FrameHandle hull, FrameHandle pod) = HullInPod();

        List<FrameOwnerView> views = Views();

        views.Select(view => view.Frame).Should().Equal(ground, hull);
        views[1].Home.Should().Be(pod);
        views[1].Semantic.Should().Be(ground);
        views[1].LocalPose.Should().Be(frames.TreeLocalPose(hull));
        views[1].PositionInSemantic.Should().Be(X(50), "the hull stands where it stood in ground when the pod took it");
    }

    [Fact]
    public void The_place_of_a_frame_in_nested_frames_a_light_year_out_rounds_once_in_its_semantic_frame()
    {
        (FrameHandle ground, FrameHandle hull, FrameHandle pod) = HullInPod(LightYear, 64);
        frames.Numeric(frames.TreeParent(pod)).Should().Be(NumericMode.Nesting);
        frames.SetTreeLocalPose(hull, new(X(0.5), DQuaternion.Identity));
        // 3/256 s at 64 m/s moves the pod 0.75 m in its nest, exactly.
        frames.Update();
        frames.BeginStep([], 1.0 / 60, lastStepFailed: false);
        frames.AdvanceNumericFrames(3.0 / 256);

        // The exact place is a light-year plus 1.25 m, and a double there steps by 2 m. Summed from the hull up, it rounds
        // once, to a light-year plus 2 m. The pod's place rounded first is a light-year, and 0.5 m on that rounds back to it.
        FrameOwnerView view = Views().Single(v => v.Frame == hull);
        view.PositionInSemantic.Should().Be(X(LightYear + 2));
        view.Semantic.Should().Be(ground);
    }

#if TERTIUS_CONTRACTS
    // The walk checks it with Contract.Assert, which compiles out of Release builds, so the test runs only in Debug.
    [Fact]
    public void An_owner_view_visitor_that_writes_a_frame_during_the_walk_breaks_a_contract()
    {
        Frame(1, FrameHandle.Null);
        var pusher = new Pusher(frames);

        Action walk = () => frames.VisitOwnerViews(ref pusher);

        walk.Should().Throw<ContractException>();
    }
#endif
}
