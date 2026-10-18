using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The two walks of the frame tree that the sync point makes, once per pass: <see cref="FrameTree.VisitFrames"/>, which hands
/// each frame its tree parent and its semantic parent, and <see cref="FrameTree.VisitOwnerViews"/>, which hands each frame
/// the state that the transform row of its owner mirrors. Both visit every frame that is not numeric once, in dense order,
/// which puts parents before children. Numeric frames have no owner, so neither walk visits them. A visitor must not change
/// what its walk reads, and a contract checks that in a Debug build.
/// </summary>
public class FrameTreeVisitTests
{
    /// <summary>One light-year, in meters. A double steps by 2 m there.</summary>
    private const double LightYear = 9.4607e15;

    private static readonly DQuaternion Turned = DQuaternion.FromAxisAngle(Double3.UnitY, 0.5);

    private readonly FrameTree frames = new();

    private static Double3 X(double x) => new(x, 0, 0);

    /// <summary>A frame owned by entity <paramref name="owner"/> under <paramref name="parent"/>, at <paramref name="x"/> on X.</summary>
    private FrameHandle Frame(uint owner, FrameHandle parent, double x = 0, MotionDriver driver = MotionDriver.Static) =>
        frames.Create(new Entity(owner, 1), parent, new DPose(X(x), DQuaternion.Identity), FrameBounds.Sphere(1e9f), driver);

    private struct LinksRecorder(List<FrameLinks> seen) : IFrameVisitor
    {
        public readonly void Visit(in FrameLinks links) => seen.Add(links);
    }

    private struct ViewRecorder(List<FrameOwnerView> seen) : IOwnerViewVisitor
    {
        public readonly void Visit(in FrameOwnerView view) => seen.Add(view);
    }

#if TERTIUS_CONTRACTS
    /// <summary>A visitor that moves the frame <paramref name="frame"/> under <paramref name="parent"/> when the walk reaches it.</summary>
    private struct Mover(FrameTree frames, FrameHandle frame, FrameHandle parent) : IFrameVisitor
    {
        public readonly void Visit(in FrameLinks links)
        {
            if (links.Frame == frame)
                frames.Reparent(frame, parent);
        }
    }

    /// <summary>A visitor that sets the linear velocity of each frame that it visits.</summary>
    private struct Pusher(FrameTree frames) : IOwnerViewVisitor
    {
        public readonly void Visit(in FrameOwnerView view) => frames.SetTreeLinearVelocity(view.Frame, X(1));
    }
#endif

    private List<FrameLinks> Links()
    {
        var seen = new List<FrameLinks>();
        var recorder = new LinksRecorder(seen);
        frames.VisitFrames(ref recorder);
        return seen;
    }

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
        FrameHandle hull = Frame(20, ground, x, MotionDriver.Physics);
        frames.SetTreeLinearVelocity(hull, X(speed));
        var body = new NumericBody
        {
            Entity = new Entity(20, 1), Semantic = ground, Home = ground, Position = X(x), HomePosition = X(x), Velocity = X(speed),
            Radius = 1, Mass = 1, Character = true, OwnedFrame = hull,
        };
        frames.UpdateNumericFrames([body], 0.1, new PhysicsChanges(), _ => { });
        FrameHandle pod = frames.TreeParent(hull);
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        return (ground, hull, pod);
    }

    [Fact]
    public void Each_frame_is_visited_once_in_dense_order_with_its_tree_parent_and_its_parent()
    {
        FrameHandle a = Frame(1, FrameHandle.Null);
        FrameHandle b = Frame(2, a);
        FrameHandle c = Frame(3, a);
        FrameHandle d = Frame(4, b);
        // c moves under d, which was made after it, so the dense order, parents first, puts it last: a, b, d, c.
        frames.Reparent(c, d);

        Links().Should().Equal(
            new FrameLinks(a, FrameHandle.Null, FrameHandle.Null),
            new FrameLinks(b, a, a),
            new FrameLinks(d, b, b),
            new FrameLinks(c, d, d));
    }

    [Fact]
    public void A_numeric_frame_is_not_visited_and_a_frame_in_it_has_it_as_tree_parent_and_the_semantic_frame_as_parent()
    {
        (FrameHandle ground, FrameHandle hull, FrameHandle pod) = HullInPod();

        Links().Should().Equal(
            new FrameLinks(ground, FrameHandle.Null, FrameHandle.Null),
            new FrameLinks(hull, pod, ground));
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
        FrameHandle root = frames.Create(new Entity(1, 1), FrameHandle.Null, new DPose(X(5), Turned), FrameBounds.Sphere(1e9f), MotionDriver.Static);
        var childPose = new DPose(new Double3(7, 8, 9), Turned);
        FrameHandle child = frames.Create(new Entity(2, 1), root, childPose, FrameBounds.Sphere(50), MotionDriver.Physics);
        frames.SetTreeLinearVelocity(child, new Double3(1, 2, 3));
        frames.SetAngularVelocity(child, new Vector3(0, 0.5f, 0));

        Views().Should().Equal(
            new FrameOwnerView(root, FrameHandle.Null, FrameHandle.Null, new DPose(X(5), Turned), X(5), Double3.Zero, Vector3.Zero),
            new FrameOwnerView(child, root, root, childPose, new Double3(7, 8, 9), new Double3(1, 2, 3), new Vector3(0, 0.5f, 0)));
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
        frames.SetTreeLocalPose(hull, new DPose(X(0.5), DQuaternion.Identity));
        // 3/256 s at 64 m/s moves the pod 0.75 m in its nest, exactly.
        frames.Update();
        frames.BeginNumericStep();
        frames.AdvanceNumericFrames(3.0 / 256);

        // The exact place is a light-year plus 1.25 m, and a double there steps by 2 m. Summed from the hull up, it rounds
        // once, to a light-year plus 2 m. The pod's place rounded first is a light-year, and 0.5 m on that rounds back to it.
        FrameOwnerView view = Views().Single(v => v.Frame == hull);
        view.PositionInSemantic.Should().Be(X(LightYear + 2));
        view.Semantic.Should().Be(ground);
    }

#if TERTIUS_CONTRACTS
    // The walks check these with Contract.Assert, which compiles out of Release builds, so the two tests run only in Debug.
    [Fact]
    public void A_frame_visitor_that_moves_a_frame_during_the_walk_breaks_a_contract()
    {
        FrameHandle a = Frame(1, FrameHandle.Null);
        FrameHandle b = Frame(2, a);
        FrameHandle c = Frame(3, a);
        var mover = new Mover(frames, c, b);

        Action walk = () => frames.VisitFrames(ref mover);

        walk.Should().Throw<ContractException>();
    }

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
