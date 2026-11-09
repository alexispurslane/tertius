using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The scene <c>spin-station</c>: a station that turns about its axis, so that its floor holds people and crates against
    /// spin gravity (docs/plans/spin-gravity.md, "Content" and "Scenes and tests"). The name <c>station</c> is taken by the
    /// orbiting station of ter-dhkf (see <see cref="Station"/>). Angles θ about the station's axis follow
    /// <see cref="RevolvedSolid"/>: they run from the station's +X toward its -Z, the way the station turns, so a point of
    /// the floor moves toward growing θ, and spinward is the way of growing θ.
    /// <list type="bullet">
    /// <item>The root frame <c>space</c> has no field, and its axes are inertial space.</item>
    /// <item>The <c>station</c> is a ring about its +Y, a kinematic hull that owns the station's frame, at the origin of
    /// space. Its <see cref="SpinMotion"/> turns it in place about +Y at <see cref="Rate"/>, 0.31321 rad/s, the rate that
    /// gives 9.81 m/s² at 100 m. Its spawn carries its first motion intent, so it turns from its first step, and the
    /// person and the crates spawned on its floor turn with it from their first step. Its frame has no field, so only the
    /// turn holds things on the floor. The ring runs from <see cref="FloorRadius"/>, 100 m, to 101 m from the axis and is
    /// 20 m wide along it, in <see cref="Segments"/>, 256, segments. In space its shape is the compound of one hull per
    /// segment (<see cref="RevolvedSolid.Shape"/>). In its own frame it is the ring's exact mesh, the floor people walk on,
    /// as the note of ter-fb3w asks. The note chose the mesh because a box that slid across the joints of the hull
    /// compound tripped at each one. Physics now gives edge removal to every shape made of sharp pieces, and with it the
    /// box trips on neither shape (see <see cref="RevolvedSolid"/>), so the mesh follows the note and no longer fixes a
    /// fault. Each facet of the floor is 2.45 m long, and its middle lies 7.5 mm inside the circle of 100 m. The bubble is a sphere of 110 m.</item>
    /// <item>Two static walls, <c>wall-left</c> and <c>wall-right</c>, rings 4 m high and 0.3 m thick on the floor at
    /// its two edges, keep crates on it and close the view to the sides.</item>
    /// <item>The <c>person</c>, a capsule character 1.8 m tall, stands on the middle of facet 0, in the middle of the
    /// floor's width, and faces spinward. Its eye, 1.65 m above the floor, is a view. While that view is on,
    /// <see cref="PersonControl"/> steers it, and with <see cref="Carry"/>, E picks up a crate within reach and the left
    /// mouse button throws it.</item>
    /// <item>The green <c>carried-crate</c>, a <see cref="Crate"/> 0.4 m on a side and of 5 kg, lies on facet 0, 1.5 m to
    /// the person's left along the axis, within its reach.</item>
    /// <item>The orange <c>dropped-crate</c>, 0.4 m on a side and of 5 kg, with Jolt's default damping, starts at rest in
    /// the station's axes 2 m above the middle of facet 1, 2 m to the person's right along the axis, and drops onto the
    /// floor in about 0.6 s. It has no <see cref="Crate"/>, so no one picks it up.</item>
    /// <item>The views, both in the station's frame, so that they turn with it: <c>view</c>, the camera at the start,
    /// stands 2.2 m above the floor about 10 m antispinward of the person and looks spinward along the floor, which curves
    /// up behind the person. <c>side</c> stands 1.4 m above the floor 8.5 m to the person's left, near the left wall,
    /// about 6.1 m spinward of the person's start (7.3 m from the station's +X), and looks across the floor at the right wall, so that the walk, the jump and
    /// the throw run across its frame, spinward to the left. <c>wide</c> stands 8 m above the floor, 25 m antispinward, and
    /// looks up the ring.</item>
    /// <item>Lamps on both walls, 3.8 m above the floor, one on each wall over every fourth joint, light the floor all
    /// round the ring. The dim <c>hub-lamp</c>, a point light at rest in the station on its spin axis 25 m along +Y from its
    /// center, lights the floor radially with 0.6 W/m² and turns with the station. It casts shadows once the shadow atlas
    /// gives point lights shadows, so the person and the crates will cast them there. The ring and the walls cast
    /// none.</item>
    /// </list>
    /// </summary>
    public static class SpinStation
    {
        /// <summary>The station's rate of turn about its +Y, in rad/s: ω² · 100 m = 9.81 m/s².</summary>
        public const double Rate = 0.31321;

        /// <summary>The radius of the ring's inner face, the floor, in meters, at its joints.</summary>
        public const float FloorRadius = 100;

        /// <summary>The ring's segments, and so the facets of its floor.</summary>
        public const int Segments = 256;

        /// <summary>Half the ring's width along the axis, in meters.</summary>
        public const float HalfWidth = 10;

        /// <summary>How far a standing person's center lies from the floor, in meters: the capsule's half height and radius.</summary>
        public const double StandingHeight = 0.9;

        /// <summary>Half the size of each crate along each of its axes, in meters.</summary>
        public const float CrateHalfSize = 0.2f;

        /// <summary>The mass of each crate, in kilograms.</summary>
        public const float CrateMass = 5;

        private const float HullThickness = 1,
            WallHeight = 4,
            WallThickness = 0.3f,
            Bubble = 110;

        /// <summary>The lamps: one on each wall over every fourth joint of the floor, this high above it, in meters.</summary>
        private const int LampEvery = 4;

        private const double LampHeight = 3.8;

        /// <summary>How far the hub lamp sits from the station's center along its +Y spin axis, in meters.</summary>
        private const double HubLampHeight = 25;

        /// <summary>The irradiance that the hub lamp sends to the floor, in W/m²: a dim light, as the old sun was.</summary>
        private const double HubLampFloorIrradiance = 0.6;

        /// <summary>How far the floor lies from the hub lamp, in meters: about √(100² + 25²).</summary>
        private const double HubLampReach = 103;

        /// <summary>The angle of one facet of the floor, in radians.</summary>
        private static double Facet => Math.Tau / Segments;

        /// <summary>How far the middle of a facet lies from the axis, in meters.</summary>
        private static double FacetMiddle => FloorRadius * Math.Cos(Facet / 2);

        /// <summary>The middle of facet <paramref name="k"/>, at <paramref name="height"/> above the floor and
        /// <paramref name="y"/> along the axis.</summary>
        private static Double3 OnFacet(int k, double height, double y)
        {
            double theta = (k + 0.5) * Facet,
                radius = FacetMiddle - height;
            return new(radius * Math.Cos(theta), y, -radius * Math.Sin(theta));
        }

        /// <summary>
        /// The turn whose +Y points from the floor toward the axis at the angle <paramref name="theta"/>, and whose -Z points
        /// spinward: a quarter turn about +Z takes +Y to -X, and the turn by θ about +Y takes -X to the direction toward the
        /// axis at θ.
        /// </summary>
        private static DQuaternion Upright(double theta) =>
            DQuaternion.FromAxisAngle(Double3.UnitY, theta) * DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2);

        /// <summary>
        /// A view at the angle <paramref name="theta"/>, <paramref name="height"/> above the floor in the middle of its width,
        /// upright there and looking spinward, tilted up by <paramref name="pitch"/> radians.
        /// </summary>
        private static DPose View(double theta, double height, double pitch)
        {
            double radius = FloorRadius - height;
            return new(
                new(radius * Math.Cos(theta), 0, -radius * Math.Sin(theta)),
                Upright(theta) * DQuaternion.FromAxisAngle(Double3.UnitX, pitch)
            );
        }

        /// <summary>
        /// A view at the angle <paramref name="theta"/>, <paramref name="height"/> above the floor and <paramref name="y"/>
        /// along the axis, upright there and looking along the station's +Y, across the floor: a quarter turn about its up
        /// takes the spinward look of <see cref="Upright"/> to the station's +Y, so spinward lies to its left.
        /// </summary>
        private static DPose SideView(double theta, double height, double y)
        {
            double radius = FloorRadius - height;
            return new(
                new(radius * Math.Cos(theta), y, -radius * Math.Sin(theta)),
                Upright(theta) * DQuaternion.FromAxisAngle(Double3.UnitY, -Math.PI / 2)
            );
        }

        private static MeshShapeDesc MeshShape(MeshData mesh) =>
            new([.. mesh.Vertices.Select(v => v.Position)], [.. mesh.Indices.Select(i => (int)i)]);

        /// <summary>A gray-box render row that casts no shadow.</summary>
        private static RenderData Unshadowed(
            IRenderResources resources,
            MeshData mesh,
            Vector4 color,
            float boundingRadius
        ) =>
            Drawn(resources: resources, mesh: mesh, color: color, boundingRadius: boundingRadius) with
            {
                Flags = RenderFlags.Visible,
            };

        public static SceneInstance Build(World world, IRenderResources resources)
        {
            var scene = new SceneInstance();
            FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
            scene.Add("space", space);
            FrameHandle frame = SpawnStation(world: world, resources: resources, scene: scene, space: space);
            SpawnWalls(world: world, resources: resources, scene: scene, frame: frame);
            SpawnWallLamps(world, resources, frame);
            SpawnPerson(world: world, resources: resources, scene: scene, frame: frame);
            SpawnCrates(world: world, resources: resources, scene: scene, frame: frame);
            SpawnViews(world, scene, frame);
            Light(world, scene, frame);

            scene.AddView("view");
            scene.AddView("side");
            scene.AddView("wide");
            scene.AddView("person");
            return scene;
        }

        /// <summary>The turning ring <c>station</c> in <paramref name="space"/>. Returns its frame.</summary>
        private static FrameHandle SpawnStation(
            World world,
            IRenderResources resources,
            SceneInstance scene,
            FrameHandle space
        )
        {
            // The station turns from its first step: its spawn carries the spin's sample of the step of the next tick, and
            // its SpinMotion writes the samples from the second step on. A kinematic hull keeps its spawn velocities as its
            // frame's velocities.
            var spin = new FrameSpin(Tilt: DQuaternion.Identity, Rate: Rate);
            var ring = RevolvedSolid.Ring(
                innerRadius: FloorRadius,
                outerRadius: FloorRadius + HullThickness,
                halfWidth: HalfWidth,
                segments: Segments
            );
            MeshData ringMesh = ring.Mesh();
            ShapeDesc hull = ring.Shape();
            Entity station = world.Spawn(
                new()
                {
                    Frame = space,
                    Pose = new(Double3.Zero, spin.RotationAt(world.Time)),
                    AngularVelocity = spin.AngularVelocity.ToVector3(),
                    FirstMove = new(spin.Sample(Double3.Zero, world.TimeAt(world.TickNumber + 1))),
                    Components = [new SpinMotion { Spin = spin }],
                    Body = new(
                        Shape: hull,
                        Motion: BodyMotion.Kinematic,
                        Mass: 1e6f,
                        InteriorShape: MeshShape(ringMesh)
                    ),
                    AsFrame = new(FrameBounds.Sphere(Bubble)),
                    Render = Unshadowed(
                        resources: resources,
                        mesh: ringMesh,
                        color: new(x: 0.62f, y: 0.63f, z: 0.66f, w: 1),
                        boundingRadius: hull.BoundingRadius()
                    ),
                }
            );
            scene.Add("station", station);
            return world.GetOwnedFrame(station);
        }

        /// <summary>The two walls at the ring's edges.</summary>
        private static void SpawnWalls(World world, IRenderResources resources, SceneInstance scene, FrameHandle frame)
        {
            var wall = RevolvedSolid.Ring(
                innerRadius: FloorRadius - WallHeight,
                outerRadius: FloorRadius,
                halfWidth: WallThickness / 2,
                segments: Segments
            );
            ShapeDesc wallShape = wall.Shape();
            RenderData wallRender = Unshadowed(
                resources: resources,
                mesh: wall.Mesh(),
                color: new(x: 0.42f, y: 0.44f, z: 0.5f, w: 1),
                boundingRadius: wallShape.BoundingRadius()
            );
            foreach (
                (string id, double y) in new[]
                {
                    ("wall-left", -(HalfWidth - WallThickness / 2)),
                    ("wall-right", HalfWidth - WallThickness / 2),
                }
            )
                scene.Add(
                    id,
                    world.Spawn(
                        new()
                        {
                            Frame = frame,
                            Pose = new(new(0, y, 0), DQuaternion.Identity),
                            Body = new(Shape: wallShape, Motion: BodyMotion.Static),
                            Render = wallRender,
                        }
                    )
                );
        }

        /// <summary>The lamps on both walls, one on each wall over every fourth joint.</summary>
        private static void SpawnWallLamps(World world, IRenderResources resources, FrameHandle frame)
        {
            // Each lamp is a glowing panel 0.6 m by 1.2 m on the inner face of a wall, whose face toward the hall is a cabin
            // lamp's rectangle light.
            MeshId lampMesh = resources.CreateMesh(Primitives.Box(new(0.05f, 0.3f, 0.6f)));
            static RectLightData Face(double side) =>
                PanelLight(
                    offset: new((float)-side * 0.05f, 0, 0),
                    front: new((float)-side, 0, 0),
                    across: Vector3.UnitZ,
                    width: 1.2f,
                    height: 0.6f,
                    power: CabinLampPower
                );
            MaterialId lampMaterial = LampPanelMaterial(resources: resources, light: Face(1));
            for (int k = 0; k < Segments; k += LampEvery)
            {
                double theta = k * Facet,
                    radius = FloorRadius - LampHeight;
                foreach (double side in new[] { -1.0, 1.0 })
                    world.Spawn(
                        new()
                        {
                            Frame = frame,
                            Pose = new(
                                new(
                                    radius * Math.Cos(theta),
                                    side * (HalfWidth - WallThickness - 0.05),
                                    -radius * Math.Sin(theta)
                                ),
                                Upright(theta)
                            ),
                            Render = new RenderData
                            {
                                Mesh = lampMesh,
                                Material = lampMaterial,
                                BoundingRadius = 0.85f,
                                Flags = RenderFlags.Visible,
                                Light = Face(side),
                            },
                        }
                    );
            }
        }

        /// <summary>The <c>person</c> on facet 0.</summary>
        private static void SpawnPerson(World world, IRenderResources resources, SceneInstance scene, FrameHandle frame)
        {
            // The person stands on the middle of facet 0, 2 cm above the floor, upright and facing spinward. Physics turns
            // its up against the effective gravity, which points away from the axis.
            var capsule = new CapsuleShapeDesc(0.6f, 0.3f);
            scene.Add(
                "person",
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = new(OnFacet(0, StandingHeight + 0.02, 0), Upright(Facet / 2)),
                        Body = new(Shape: capsule, Motion: BodyMotion.Character, Mass: 70),
                        Render = Drawn(
                            resources: resources,
                            mesh: Primitives.Box(new(0.3f, 0.9f, 0.3f)),
                            color: new(x: 0.2f, y: 0.45f, z: 0.8f, w: 1),
                            boundingRadius: 1
                        ),
                        Components = [new PersonControl(new(0, 0.75f, 0)), new Carry(capsule)],
                        Subscriptions = [Topics.Input],
                    }
                )
            );
        }

        /// <summary>The <c>carried-crate</c> and the <c>dropped-crate</c>.</summary>
        private static void SpawnCrates(World world, IRenderResources resources, SceneInstance scene, FrameHandle frame)
        {
            // The person's left is the station's -Y: its up is toward the axis and its forward spinward.
            var crate = new CrateSpec(
                HalfExtents: new(CrateHalfSize),
                Mass: CrateMass,
                Mesh: resources.CreateMesh(Primitives.Box(new(CrateHalfSize))),
                Material: resources.CreateMaterial(
                    MaterialDesc.Color(new(x: 0.3f, y: 0.6f, z: 0.25f, w: 1), UsualRoughness)
                )
            );
            scene.Add(
                "carried-crate",
                world.Spawn(
                    crate.Spawn(
                        frame,
                        new(OnFacet(0, CrateHalfSize, -1.5), DQuaternion.FromAxisAngle(Double3.UnitY, Facet / 2)),
                        Double3.Zero
                    )
                )
            );
            scene.Add(
                "dropped-crate",
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = new(
                            OnFacet(1, CrateHalfSize + 2, 2),
                            DQuaternion.FromAxisAngle(Double3.UnitY, 1.5 * Facet)
                        ),
                        Body = new(Shape: new BoxShapeDesc(new(CrateHalfSize)), Mass: CrateMass),
                        Render = Drawn(
                            resources: resources,
                            mesh: Primitives.Box(new(CrateHalfSize)),
                            color: new(x: 1, y: 0.45f, z: 0.1f, w: 1),
                            boundingRadius: crate.BoundingRadius
                        ),
                    }
                )
            );
        }

        /// <summary>The views <c>view</c>, the camera at the start, <c>side</c> and <c>wide</c>.</summary>
        private static void SpawnViews(World world, SceneInstance scene, FrameHandle frame)
        {
            Entity view = world.Spawn(
                new()
                {
                    Frame = frame,
                    Pose = View(-0.09, 2.2, 0.05),
                    Render = new RenderData(),
                }
            );
            scene.Add("view", view);
            scene.Add(
                "side",
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = SideView(0.073, 1.4, -8.5),
                        Render = new RenderData(),
                    }
                )
            );
            scene.Add(
                "wide",
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = View(-0.25, 8, 0.2),
                        Render = new RenderData(),
                    }
                )
            );
            world.Camera = view;
        }

        /// <summary>The <c>hub-lamp</c> on the spin axis and the dim ambient light.</summary>
        private static void Light(World world, SceneInstance scene, FrameHandle frame)
        {
            scene.Add(
                "hub-lamp",
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = new(new(0, HubLampHeight, 0), DQuaternion.Identity),
                        Render = new RenderData
                        {
                            Light = new PointLightData(
                                Offset: new(Vector3.Zero, Quaternion.Identity),
                                Color: Vector3.One,
                                Power: (float)(HubLampFloorIrradiance * 4 * Math.PI * HubLampReach * HubLampReach),
                                Radius: LampRadius,
                                CastsShadow: true
                            ),
                        },
                    }
                )
            );
            world.Ambient = new()
            {
                SkyColor = new(0.12f),
                GroundColor = new(0.12f),
                Intensity = 1,
            };
        }
    }
}
