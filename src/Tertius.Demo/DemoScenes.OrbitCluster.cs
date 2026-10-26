using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// How far orbit-cluster's view <c>watch</c> looks down from the ship's horizontal plane, in radians: 30 degrees, so
    /// that it sees the top faces, which the sun above the ship lights.
    /// </summary>
    private const double WatchTilt = Math.PI / 6;

    /// <summary>
    /// Where the scene orbit-cluster puts the spacewalker at the start, in the ship's axes relative to the ship's center:
    /// 15 m out from the hatch to port, at the height of a person who floats at the hatch's sill.
    /// </summary>
    public static Double3 SpacewalkerStart { get; } = new(-25, -2.4, AirlockZ);

    /// <summary>Where orbit-cluster puts the crate above the ship, in the ship's axes: 50 m up, away from Earth.</summary>
    public static Double3 CrateAboveStart { get; } = new(0, 50, 0);

    /// <summary>Where orbit-cluster puts the crate below the ship, in the ship's axes: 50 m down, toward Earth.</summary>
    public static Double3 CrateBelowStart { get; } = new(0, -50, 0);

    /// <summary>
    /// Where orbit-cluster puts the thrown crate, in the ship's axes: just outside the hatch, its near face 0.5 m from the
    /// port wall.
    /// </summary>
    public static Double3 ThrownCrateStart { get; } = new(-11, -2.4, AirlockZ);

    /// <summary>
    /// The velocity of the thrown crate relative to the ship at the start, in the ship's axes, in m/s: 5 m/s, out from the
    /// port wall and aft, away from the spacewalker's way along the orbit.
    /// </summary>
    public static Double3 ThrownCrateVelocity { get; } = new(-3, 0, 4);

    /// <summary>The half extents of orbit-cluster's crates, in meters: cubes 1 m on a side, big enough to see from afar.</summary>
    public static Vector3 ClusterCrateHalfExtents { get; } = new(0.5f);

    /// <summary>
    /// The light that a marker of <paramref name="color"/> gives off, as radiance in W/(m²·sr): 270 times its color. That
    /// is about 60 % as bright as the color in full sunlight, which the Sun gives at 1,361 times the color over pi, so a
    /// sunlit face still shows brighter than a face in shadow.
    /// </summary>
    private static Vector3 MarkerGlow(Vector4 color) => new Vector3(color.X, color.Y, color.Z) * 270;

    /// <summary>
    /// The pose and the velocity in earth, at <paramref name="time"/>, of a point at rest relative to the ship of the orbit
    /// scene at <paramref name="offset"/> in the ship's axes, as the ship starts on <see cref="ShipOrbit"/>. The point
    /// moves with the ship's center and turns with the ship: its velocity is the ship's plus the ship's angular velocity
    /// crossed with the offset. The rotation is the ship's, so a person placed there faces the ship's nose, along the orbit.
    /// </summary>
    public static (DPose Pose, Double3 Velocity) BesideTheShip(Double3 offset, double time)
    {
        (Double3 position, Double3 velocity) = ShipOrbit.StateAt(time, EarthMu);
        DQuaternion rotation = ShipOrbit.RotationAt(time, EarthMu);
        Double3 arm = rotation.Rotate(offset);
        return (
            new(position + arm, rotation),
            velocity + Double3.Cross(ShipOrbit.AngularVelocityAt(time, EarthMu), arm)
        );
    }

    /// <summary>
    /// The orbit scene (see <see cref="Orbit"/>), with three things near its ship that only falling numeric frames
    /// (docs/plans/numeric-frames.md) get right. Each of them moves at orbital speed in earth, where a float velocity steps
    /// by 0.5 mm/s.
    /// <list type="bullet">
    /// <item>The <c>spacewalker</c>, a person like the orbit scene's, floats in earth 15 m out from the hatch to port (see
    /// <see cref="SpacewalkerStart"/>), at rest relative to the ship, and faces the ship's nose, along the orbit. It is
    /// the camera, so <see cref="PersonControl"/> steers it. It is outside the ship's bubble, so it stays in earth, and its
    /// thrusters push it there.</item>
    /// <item>Two crates, <c>crate-above</c> and <c>crate-below</c>, float in earth 50 m above and 50 m below the ship's
    /// center along the radius, at rest relative to the ship. Nothing touches them, so they drift relative to the ship
    /// as the Clohessy-Wiltshire equations say for the ship's circular orbit.</item>
    /// <item>The <c>thrown-crate</c> starts in the ship's frame just outside the hatch (see <see cref="ThrownCrateStart"/>)
    /// and moves at 5 m/s relative to the ship, out from the port wall and aft (see <see cref="ThrownCrateVelocity"/>),
    /// as if a person in the hatch had thrown it. It leaves the ship's bubble into earth and flies on.</item>
    /// <item>The view <c>watch</c> rides in the ship's frame, above the ship and to port of it, and looks down by
    /// <see cref="WatchTilt"/> to starboard, at a point 95 m ahead of the ship's center. The ship's nose is to the left in
    /// its view. At the start the sun stands straight above the ship, so it lights only top faces, and the view looks
    /// down onto the hull's sunlit roof. Earth's limb crosses the upper third of the view, black space above it. It shows
    /// the hull, the spacewalker's way along the orbit and back, the two crates above and below, and the thrown crate's
    /// start.</item>
    /// </list>
    /// The three crates are orange cubes 1 m on a side and of 50 kg. They and the spacewalker glow faintly in their own
    /// color, as marker lights do (see <see cref="MarkerGlow"/>), so that the watch view shows them where the sun does not
    /// reach: crate-below floats in the hull's shadow, and no side face gets sunlight. Everything else is as in the orbit
    /// scene, which keeps its own person in the cabin.
    /// </summary>
    private static SceneInstance OrbitCluster(World world, IRenderResources resources)
    {
        SceneInstance scene = Orbit(world, resources);
        FrameHandle earth = world.GetOwnedFrame(scene["earth"]);
        FrameHandle shipFrame = world.GetOwnedFrame(scene["ship"]);

        var capsule = new CapsuleShapeDesc(0.6f, 0.3f);
        (DPose walkerPose, Double3 walkerVelocity) = BesideTheShip(SpacewalkerStart, world.Time);
        var suit = new Vector4(x: 0.95f, y: 0.8f, z: 0.2f, w: 1);
        Entity spacewalker = world.Spawn(
            new()
            {
                Frame = earth,
                Pose = walkerPose,
                LinearVelocity = walkerVelocity,
                Body = new(Shape: capsule, Motion: BodyMotion.Character, Mass: 70),
                Render = new RenderData
                {
                    Mesh = resources.CreateMesh(Primitives.Box(new(0.3f, 0.9f, 0.3f))),
                    Material = resources.CreateMaterial(
                        new(BaseColor: suit, Roughness: UsualRoughness, Emissive: MarkerGlow(suit))
                    ),
                    BoundingRadius = 1,
                    Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
                },
                Components = [new PersonControl(new(0, 0.75f, 0)), new Carry(capsule)],
                Subscriptions = [Topics.Input],
            }
        );
        scene.Add("spacewalker", spacewalker);

        var orange = new Vector4(x: 1, y: 0.45f, z: 0.1f, w: 1);
        var crate = new CrateSpec(
            HalfExtents: ClusterCrateHalfExtents,
            Mass: 50,
            Mesh: resources.CreateMesh(Primitives.Box(ClusterCrateHalfExtents)),
            Material: resources.CreateMaterial(
                new(BaseColor: orange, Roughness: UsualRoughness, Emissive: MarkerGlow(orange))
            )
        );
        foreach (
            (string id, Double3 offset) in new[] { ("crate-above", CrateAboveStart), ("crate-below", CrateBelowStart) }
        )
        {
            (DPose pose, Double3 velocity) = BesideTheShip(offset, world.Time);
            scene.Add(id, world.Spawn(crate.Spawn(earth, pose, velocity)));
        }

        scene.Add(
            "thrown-crate",
            world.Spawn(crate.Spawn(shipFrame, new(ThrownCrateStart, DQuaternion.Identity), ThrownCrateVelocity))
        );

        // A view looks along its -Z. A quarter turn about +Y turns that to +X, toward the ship's port side, after a tilt
        // about the view's own +X has tipped it down by WatchTilt. The view stands 160 m from the point 95 m ahead of the
        // ship's center, back along its line of sight: 138.6 m to port and 80 m up. So the spacewalker's whole way, from
        // the hatch to 225 m ahead, and both crates fit in it.
        scene.Add(
            "watch",
            world.Spawn(
                new()
                {
                    Frame = shipFrame,
                    Pose = new(
                        new(-138.6, 80, -95),
                        DQuaternion.FromAxisAngle(Double3.UnitY, -Math.PI / 2)
                            * DQuaternion.FromAxisAngle(Double3.UnitX, -WatchTilt)
                    ),
                    Render = new RenderData(),
                }
            )
        );
        scene.AddView("spacewalker");
        scene.AddView("watch");
        world.Camera = spacewalker;
        return scene;
    }
}
