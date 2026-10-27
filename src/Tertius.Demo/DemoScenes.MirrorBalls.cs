using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>The radius of each mirror ball, in meters.</summary>
    private const float MirrorBallRadius = 0.5f;

    /// <summary>The distance between the centers of two neighbor mirror balls, in meters.</summary>
    private const double MirrorBallSpacing = 1.5;

    /// <summary>How far to starboard of the shuttle's center the row of mirror balls floats, in meters.</summary>
    private const double MirrorBallsX = 7;

    /// <summary>
    /// How far along its orbit the shuttle of mirror-balls stands at the world's time 0, past the point under the sun,
    /// in radians: 55 degrees, so the sun stands 55 degrees from the zenith toward the tail, and the ground below is in
    /// day.
    /// </summary>
    private const double MirrorBallsOrbitPhase = 55 * Math.PI / 180;

    /// <summary>The perceptual roughness of the mirror balls, from the nose to the tail.</summary>
    private static readonly float[] MirrorBallRoughnesses = [0, 0.06f, 0.2f, 0.5f];

    // The half extents of the mirror plate: 3 by 3 m, and 4 cm thick along its normal, its local Z.
    private static readonly Vector3 MirrorPlateHalfExtents = new(1.5f, 1.5f, 0.02f);

    /// <summary>The eye of the view <c>plate-camera</c> in the shuttle's frame, port of the hull.</summary>
    private static readonly Double3 PlateCameraEye = new(-5, 1, 0);

    /// <summary>
    /// The center of the mirror plate in the shuttle's frame: 7 m farther to port than its view, and 2.1 m higher.
    /// </summary>
    private static readonly Double3 MirrorPlateCenter = new(-12, 3.1, 0);

    /// <summary>
    /// The direction in the shuttle's frame that the plate reflects the center of its view into: up from Earth and
    /// toward the nose, so 92 degrees from the sun, and away from the hull.
    /// </summary>
    private static readonly Double3 PlateReflection = new Double3(0, 0.8, -0.6).Normalized();

    /// <summary>
    /// The scene mirror-balls, the review scene of the environment map (docs/plans/environment-map.md, "Part 6"): the
    /// shuttle of <see cref="Shuttle"/> in Earth orbit, with mirrors beside it.
    /// <list type="bullet">
    /// <item>The <c>shuttle</c> starts 400 km up on <see cref="ShipOrbit"/>, <see cref="MirrorBallsOrbitPhase"/> past
    /// the point under the sun, with its belly toward Earth and its nose along its motion. So the sun stands up and
    /// toward the tail, and Earth's day side lies below. It falls freely and carries its nozzle and its panes.</item>
    /// <item>Four white metal spheres, <c>ball-0</c> to <c>ball-3</c>, 0.5 m in radius and 1.5 m apart, float in a row
    /// along the hull 7 m to starboard, with the roughness of <see cref="MirrorBallRoughnesses"/> from the nose.</item>
    /// <item>The <c>plate</c>, a flat white metal mirror of roughness 0, 3 by 3 m, floats 12 m to port. It is turned so
    /// that its view sees open sky in it, along <see cref="PlateReflection"/>.</item>
    /// <item>The view <c>balls-camera</c> looks at the row from 7.5 m farther to starboard, so each ball shows Earth in
    /// its lower half. The view <c>plate-camera</c> looks at the plate from 7 m, up past the horizon, so space fills
    /// its background. The view <c>belly-camera</c> looks up at the shuttle's belly from below and to starboard, so it
    /// shows the light that Earth sends up. The vertical field of view is 35 degrees.</item>
    /// </list>
    /// The spheres, the plate and the views have no bodies: they ride in the shuttle's frame and never leave it.
    /// </summary>
    private static SceneInstance MirrorBalls(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle earth = SunAndEarth(world: world, resources: resources, scene: scene, date: EarthDate.MarchEquinox);
        FrameHandle cabin = MirrorBallsShuttle(world: world, resources: resources, scene: scene, earth: earth);

        // A narrow view keeps the balls at the sides of the frame round.
        world.VerticalFov = 35 * MathF.PI / 180;

        MeshId ballMesh = resources.CreateMesh(Primitives.Sphere(MirrorBallRadius, segments: 96, rings: 48));
        for (int i = 0; i < MirrorBallRoughnesses.Length; i++)
        {
            double z = (i - (MirrorBallRoughnesses.Length - 1) / 2.0) * MirrorBallSpacing;
            scene.Add(
                $"ball-{i}",
                world.Spawn(
                    Mirror(
                        frame: cabin,
                        pose: new(new(MirrorBallsX, 0, z), DQuaternion.Identity),
                        mesh: ballMesh,
                        material: WhiteMetal(resources, MirrorBallRoughnesses[i]),
                        boundingRadius: MirrorBallRadius
                    )
                )
            );
        }
        scene.Add("plate", world.Spawn(MirrorPlate(cabin, resources)));

        Entity ballsCamera = world.Spawn(
            ViewIn(cabin, LookingAt(new(MirrorBallsX + 7.5, 0.5, 0), new(MirrorBallsX, 0, 0)))
        );
        scene.Add("balls-camera", ballsCamera);
        scene.Add("plate-camera", world.Spawn(ViewIn(cabin, LookingAt(PlateCameraEye, MirrorPlateCenter))));
        scene.Add("belly-camera", world.Spawn(ViewIn(cabin, LookingAt(new(6, -7, 9), new(0, -2, 0)))));

        scene.AddView("balls-camera");
        scene.AddView("plate-camera");
        scene.AddView("belly-camera");
        world.Camera = ballsCamera;
        return scene;
    }

    /// <summary>
    /// The shuttle of mirror-balls in <paramref name="earth"/>, falling freely on <see cref="ShipOrbit"/> from
    /// <see cref="MirrorBallsOrbitPhase"/>, with its nozzle and its panes. Returns its frame.
    /// </summary>
    private static FrameHandle MirrorBallsShuttle(
        World world,
        IRenderResources resources,
        SceneInstance scene,
        FrameHandle earth
    )
    {
        // Stryker disable once String : SunAndEarth gives earth point gravity, so nothing reaches this guard.
        double earthMu = world.Frames.Gravity(earth) is PointGravity { Mu: var mu }
            ? mu
            : throw new InvalidOperationException("Earth has no point gravity.");
        KeplerOrbit orbit = ShipOrbit with { MeanAnomalyAtEpoch = MirrorBallsOrbitPhase };
        (Double3 position, Double3 velocity) = orbit.StateAt(world.Time, earthMu);
        Entity shuttle = world.Spawn(
            new()
            {
                Frame = earth,
                Pose = new(position, orbit.RotationAt(world.Time, earthMu)),
                LinearVelocity = velocity,
                AngularVelocity = orbit.AngularVelocityAt(world.Time, earthMu).ToVector3(),
                Body = Shuttle.Body(BodyMotion.Dynamic),
                AsFrame = new(new(Shuttle.Bubble)),
                Render = Shuttle.DrawnHull(resources),
            }
        );
        scene.Add("shuttle", shuttle);
        FrameHandle cabin = world.GetOwnedFrame(shuttle);
        Shuttle.SpawnNozzle(world: world, cabin: cabin, scene: scene, resources: resources, prefix: "");
        Shuttle.Panes(world: world, cabin: cabin, scene: scene, resources: resources, prefix: "");
        return cabin;
    }

    /// <summary>
    /// The mirror plate in <paramref name="cabin"/>, the shuttle's frame: at <see cref="MirrorPlateCenter"/>, turned so
    /// that the center of the view <c>plate-camera</c> sees the sky along <see cref="PlateReflection"/>.
    /// </summary>
    private static SpawnDesc MirrorPlate(FrameHandle cabin, IRenderResources resources)
    {
        // The plate's normal halves the angle between the way back to its view and the reflection. LookingAt turns
        // local -Z toward its target, so local +Z, the normal, away from it.
        Double3 normal = (PlateReflection - (MirrorPlateCenter - PlateCameraEye).Normalized()).Normalized();
        return Mirror(
            frame: cabin,
            pose: LookingAt(MirrorPlateCenter, MirrorPlateCenter - normal),
            mesh: resources.CreateMesh(Primitives.Box(MirrorPlateHalfExtents)),
            material: WhiteMetal(resources, roughness: 0),
            boundingRadius: MirrorPlateHalfExtents.Length()
        );
    }

    /// <summary>A white metal of <paramref name="roughness"/>, as polished silver is nearly.</summary>
    private static MaterialId WhiteMetal(IRenderResources resources, float roughness) =>
        resources.CreateMaterial(
            new(
                BaseColor: new(x: 0.95f, y: 0.95f, z: 0.95f, w: 1),
                Roughness: roughness,
                Model: new StandardShading(Metallic: 1)
            )
        );

    /// <summary>
    /// A mirror of mirror-balls in <paramref name="frame"/>: no body, never an impostor, so each shades in full, and it
    /// casts shadows.
    /// </summary>
    private static SpawnDesc Mirror(
        FrameHandle frame,
        DPose pose,
        MeshId mesh,
        MaterialId material,
        float boundingRadius
    ) =>
        new()
        {
            Frame = frame,
            Pose = pose,
            Render = new RenderData
            {
                Mesh = mesh,
                Material = material,
                BoundingRadius = boundingRadius,
                Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
            },
        };
}
