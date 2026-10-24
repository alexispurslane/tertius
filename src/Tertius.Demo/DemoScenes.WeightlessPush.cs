using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>The mass of weightless-push's crate, in kg: 20, so that the pair of 90 kg gains 1.17 m/s² from the thrusters.</summary>
    public const float PushedCrateMass = 20;

    /// <summary>
    /// Where the scene weightless-push puts the astronaut, in the ship's axes relative to the ship's center: 4 m out from
    /// the hull's port face, at the height of a person who floats at the hatch's sill, 10 m aft. The cabin's gravity ends
    /// at the hull, so the astronaut is weightless there.
    /// </summary>
    public static Double3 AstronautStart { get; } = new(-14, -2.4, AirlockZ);

    /// <summary>The half extents of weightless-push's crate, in meters: a cube 1 m on a side.</summary>
    public static Vector3 PushedCrateHalfExtents { get; } = new(0.5f);

    /// <summary>
    /// The orbit scene (see <see cref="Orbit"/>), with a weightless astronaut and a free crate outside the ship's port face,
    /// both at rest relative to the ship, in its frame and inside its bubble (ticket ter-1w06).
    /// <list type="bullet">
    /// <item>The <c>astronaut</c>, a person like the orbit scene's, faces the ship's nose, its -Z. It is the camera, so
    /// <see cref="PersonControl"/> steers it, and its thrusters push it.</item>
    /// <item>The <c>free-crate</c>, an orange cube 1 m on a side and of 20 kg with no damping, floats just ahead of the
    /// astronaut, its rear face 0.02 m from the astronaut's capsule: the gap that a Jolt character keeps.</item>
    /// <item>The view <c>side</c> rides in the ship's frame, 8 m farther to port than the astronaut and a little above it,
    /// and looks at the hull's port face, across the astronaut's way toward the nose, which runs to the left in its view.</item>
    /// </list>
    /// The astronaut and the crate glow faintly in their own colors, as the markers of orbit-cluster do, because the sun
    /// stands above the ship and lights only top faces.
    /// </summary>
    private static SceneInstance WeightlessPush(World world, IRenderResources resources)
    {
        SceneInstance scene = Orbit(world, resources);
        FrameHandle shipFrame = world.GetOwnedFrame(scene["ship"]);

        var capsule = new CapsuleShapeDesc(0.6f, 0.3f);
        var suit = new Vector4(x: 0.95f, y: 0.8f, z: 0.2f, w: 1);
        Entity astronaut = world.Spawn(
            new()
            {
                Frame = shipFrame,
                Pose = new(AstronautStart, DQuaternion.Identity),
                Body = new(Shape: capsule, Motion: BodyMotion.Character, Mass: 70),
                Render = new RenderData
                {
                    Mesh = resources.CreateMesh(Primitives.Box(new(0.3f, 0.9f, 0.3f))),
                    Material = resources.CreateMaterial(new(BaseColor: suit, Emissive: MarkerGlow(suit))),
                    BoundingRadius = 1,
                    Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
                },
                Components = [new PersonControl(new(0, 0.75f, 0))],
                Subscriptions = [Topics.Input],
            }
        );
        scene.Add("astronaut", astronaut);

        var orange = new Vector4(x: 1, y: 0.45f, z: 0.1f, w: 1);
        var crate = new CrateSpec(
            HalfExtents: PushedCrateHalfExtents,
            Mass: PushedCrateMass,
            Mesh: resources.CreateMesh(Primitives.Box(PushedCrateHalfExtents)),
            Material: resources.CreateMaterial(new(BaseColor: orange, Emissive: MarkerGlow(orange)))
        );
        var cratePose = new DPose(
            AstronautStart - new Double3(0, 0, capsule.Radius + 0.02 + PushedCrateHalfExtents.Z),
            DQuaternion.Identity
        );
        scene.Add("free-crate", world.Spawn(crate.Spawn(shipFrame, cratePose, Double3.Zero)));

        // A view looks along its -Z. A quarter turn about +Y turns that to +X, toward the hull's port face, after a tilt
        // about the view's own +X has tipped it down by 10 degrees. It stands 8 m to port of the astronaut, 1.5 m above it,
        // and 4.5 m toward the nose. A view 60 degrees high and 16 by 9 sees 8.2 m to each side at that distance, so the
        // astronaut's way of about 11 m toward the nose in the script stays in view.
        scene.Add(
            "side",
            world.Spawn(
                new()
                {
                    Frame = shipFrame,
                    Pose = new(
                        AstronautStart + new Double3(-8, 1.5, -4.5),
                        DQuaternion.FromAxisAngle(Double3.UnitY, -Math.PI / 2)
                            * DQuaternion.FromAxisAngle(Double3.UnitX, -10 * Math.PI / 180)
                    ),
                    Render = new RenderData(),
                }
            )
        );
        scene.AddView("astronaut");
        scene.AddView("side");
        world.Camera = astronaut;
        return scene;
    }
}
