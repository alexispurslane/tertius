using System.Numerics;
using Tertius.Demo;
using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Host.Tests;

/// <summary>
/// Ticket ter-swgm. The scene <c>orbit</c> puts the chase camera in the ship's frame, and the ship's frame turns with the
/// ship's hull in <c>earth</c>. Jolt keeps the hull's rotation as a float quaternion, so its length is not exactly 1.
/// The rotation of the ship's frame, widened to double, is off by up to 2E-7 in the square of its length. Earth is 6.77E6 m
/// from the ship, so a rotation that is not unit length puts earth, and so every object in it, up to a meter off in the
/// view from the ship's frame. This was seen as a crate that jumps by 20 to 25 pixels in a tick after it leaves the ship's
/// bubble. The tests run the real world on the real Jolt backend, put two crates in earth 104 m and 115 m behind the ship's
/// center, and compare what the render snapshot and the pose query say with the truth in double precision.
/// <para>
/// The pose query composes along a path with a single frame in it, and a rotation that is off by the same amount cancels in
/// that path. So the query was right before the fix, and its test guards the acceptance criterion of the ticket. The render
/// snapshot walks up from the camera's frame to earth, and it was a meter off.
/// </para>
/// </summary>
public sealed class CameraInShipCrateInEarthTests : IDisposable
{
    // Ticks to run. The ship's hull takes a new rotation from Jolt every tick, and the length of each one is different.
    private const int Ticks = 300;

    // The crates start in the ship's axes at these places, in meters. The chase camera is at (0, 12, 70), so they are 34 m and
    // 45 m from it. Both are farther from the ship's center than 102.35 m, the distance at which a body passes into earth, and
    // than 100.9 m, the distance at which it would come back, so they stay in earth.
    private static readonly Double3[] CrateOffsets = [new(0, 12, 104), new(8, 20, 115)];

    // How fast a crate moves away from the ship, along the ship's +Z, in m/s.
    private const float DriftSpeed = 3;

    private readonly World world = new(new WorldOptions { MaxDegreeOfParallelism = 1, DisposePhysics = true }, new JoltPhysicsBackend());
    private readonly SceneInstance scene;
    private readonly FrameHandle earth, ship;
    private readonly Entity[] crates;

    public CameraInShipCrateInEarthTests()
    {
        scene = DemoScenes.All.Single(s => s.Name == "orbit").Build(world, RenderResources.None);
        earth = world.GetOwnedFrame(scene["earth"]);
        ship = world.GetOwnedFrame(scene["ship"]);
        var spec = new CrateSpec(new Vector3(0.2f), 5, new MeshId(1), new MaterialId(1));
        // One tick first, so that the ship has the rotation that Jolt keeps. The crates then start at their places in the
        // ship's axes, with the ship's velocity and a drift away from it.
        world.Tick();
        DPose shipPose = world.Frames.LocalPose(ship);
        DQuaternion shipRotation = shipPose.Rotation.Normalized();
        Vector3 shipVelocity = world.Snapshot.VelocityInOwnFrame(scene["ship"]);
        Vector3 away = shipRotation.Rotate(new Vector3(0, 0, DriftSpeed));
        crates = [.. CrateOffsets.Select(offset => world.Spawn(spec.Spawn(
            earth, new DPose(shipPose.Position + shipRotation.Rotate(offset), shipRotation), shipVelocity + away)))];
    }

    public void Dispose() => world.Dispose();

    /// <summary>The chase camera's eye in the ship's frame, in the ship's axes: the camera has no view offset.</summary>
    private Double3 Eye => world.Snapshot.PoseInOwnFrame(scene["chase"]).Position;

    /// <summary>
    /// Where <paramref name="crate"/> is relative to the ship's center, in the ship's axes, in double precision: the crate's
    /// offset in earth from the ship's center, turned by the inverse of the ship's rotation. The ship's rotation is
    /// normalized here, because the rotation that is the truth is a rotation, whatever length the tree holds.
    /// </summary>
    private Double3 TruthInShipAxes(Entity crate)
    {
        DPose shipInEarth = world.Frames.LocalPose(ship);
        DPose crateInEarth = world.Snapshot.PoseInOwnFrame(crate);
        return shipInEarth.Rotation.Normalized().Conjugate().Rotate(crateInEarth.Position - shipInEarth.Position);
    }

    [Fact]
    public void ACrateInEarthIsWhereTheCameraInTheShipsFrameSeesItOnEveryTick()
    {
        double worst = 0;
        for (int tick = 0; tick < Ticks; tick++)
        {
            world.Tick();
            world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot snapshot).Should().BeTrue();
            foreach (Entity crate in crates)
            {
                world.Snapshot.FrameOf(crate).Should().Be(earth, "the crate drifts away from the ship and stays in earth");
                RenderInstance instance = snapshot.Instances.Single(i => i.EntityIndex == crate.Index);
                Double3 seen = TruthInShipAxes(crate) - Eye;
                worst = Math.Max(worst, Double3.Distance((Double3)instance.Position, seen));
            }
        }

        worst.Should().BeLessThan(1e-4, "the render snapshot puts the crate within 0.1 mm of the truth on every tick");
    }

    [Fact]
    public void AskingForACrateInEarthInTheShipsFrameGivesItsPlaceInTheShipsAxesOnEveryTick()
    {
        double worstPosition = 0, worstTurn = 0;
        for (int tick = 0; tick < Ticks; tick++)
        {
            world.Tick();
            DQuaternion shipTurn = world.Frames.LocalPose(ship).Rotation.Normalized().Conjugate();
            foreach (Entity crate in crates)
            {
                world.Snapshot.TryGetPoseIn(crate, ship, out DPose pose).Should().BeTrue();
                worstPosition = Math.Max(worstPosition, Double3.Distance(pose.Position, TruthInShipAxes(crate)));
                DQuaternion truth = (shipTurn * world.Snapshot.PoseInOwnFrame(crate).Rotation).Normalized();
                // Two unit quaternions turn the same way when their dot product is 1 or -1.
                double dot = pose.Rotation.X * truth.X + pose.Rotation.Y * truth.Y + pose.Rotation.Z * truth.Z + pose.Rotation.W * truth.W;
                worstTurn = Math.Max(worstTurn, 1 - Math.Abs(dot));
            }
        }

        worstPosition.Should().BeLessThan(1e-4, "the pose query puts the crate within 0.1 mm of the truth on every tick");
        worstTurn.Should().BeLessThan(1e-9, "the pose query turns the crate as the ship's inverse rotation turns it");
    }
}
