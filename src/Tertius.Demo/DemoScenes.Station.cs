using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The scene <c>station</c>: a station whose KeplerMotion carries it round Earth on the orbit scene's circle, 400 km
    /// up, at 7.67 km/s, with the orbit scene's sun and Earth (see <see cref="Orbit"/>). It shows the two cases of
    /// ter-dhkf (docs/plans/numeric-frames.md, "Anchors"). A fast hull on rails lives in an orbit frame of its own, a
    /// numeric frame that follows its orbit, so cargo that drifts out of the station's bubble joins that frame, where
    /// both the cargo and the hull are slow and float velocities stay small. And a crate thrown at 1,200 m/s inside the
    /// station, faster than the 1,000 m/s from which a group of free bodies falls in a numeric frame, stops at a thin static
    /// wall, because the merge test links the crate to the wall, so that the crate lives in the wall's Jolt system, where
    /// Jolt sweeps it against the real wall. The station's interior and the airlock's shut doors keep the crate there too.
    /// <list type="bullet">
    /// <item>The station's frame, owned by <c>station</c>, keeps one attitude on its orbit: its axes do not turn
    /// relative to earth's. At the start, its belly (-Y) faces Earth and its nose (-Z) points along its motion, then it
    /// rolls by 35 degrees, port side up, and pitches by 25 degrees, aft end up, so that the sun, straight above the
    /// station at the start, lights its roof, its port side and its aft faces. The frame has no gravity, so everything
    /// floats in it.</item>
    /// <item>The hull is a module of 8 by 6 by 30 m (<see cref="Hull"/>) with a radiator (<see cref="Radiator"/>), a
    /// plate 24 m long and 30 cm thick that stands out of the module's port side across its length, 3 m ahead of its
    /// center. In earth, physics keeps the hull kinematic. In its own frame the module is hollow: walls 0.4 m thick
    /// around a cabin of 7.2 by 5.2 by 29.2 m. The bubble is the smallest ellipsoid that holds the module grown by 2 m on
    /// every side, about 10.4, 8.7 and 29.4 m along its axes. Most of the radiator lies outside it.</item>
    /// <item>The <c>airlock</c> is a chamber 3 m deep and 4 m wide against the port wall, 9 m aft of the center. The
    /// door <c>airlock-outer</c> closes a cargo hatch 3 by 3 m from the chamber to the outside, and slides aft into the
    /// port wall. The door <c>airlock-inner</c> closes a doorway 1.2 m wide from the cabin into the chamber, and slides up.
    /// The airlock stands open to the outside from the start: the outer door spawns open, slid into the port wall, and the
    /// inner door shut. Neither door moves in the script, and both stay in the station's frame. A body enters a frame's
    /// bubble when its bounding sphere touches the bubble, so a door enters a pod's frame when the pod comes within the
    /// radius of the door's sphere, 2.12 m for the outer door and 1.21 m for the inner door. While the outer door slid
    /// open, pod-2 came within 1.5 m of it, and the door spent the slide in pod-2's frame. Open, it is 2.56 m from the
    /// nearest pod's way, and the inner door 1.96 m.</item>
    /// <item>Three cargo pods, <c>pod-1</c> to <c>pod-3</c>, float in the chamber one above the other and drift at 1 m/s
    /// toward the hatch (see <see cref="Pods"/>), out of the station and along its port side toward the radiator. They
    /// leave the bubble 11.5 to 12.7 s after the start, pod-3 first, and bump the radiator 19.7, 21.4 and 23.7 s after it,
    /// pod-1 first. Each pod is a cube of 0.7 m and 20 kg, and it is a frame of its own with the Physics driver and a
    /// bubble of 1 cm, so that its body is a hull: outside a bubble, a hull meets only static bodies and other hulls
    /// (ARCHITECTURE.md, "Collision groups"), so only a hull bumps the station's hull there. A loose crate would pass
    /// through the radiator. The pods glow faintly in their orange, as marker lights do.</item>
    /// <item>A dump gives a body's velocity as the chord of its last step and a Kepler frame's velocity as its orbit's
    /// velocity at the moment (docs/plans/numeric-frames.md, "Bubbles and ghosts"). So a pod's velocity relative to the
    /// station worked out from two dumped velocities is off by half a step of Earth's pull, 7 cm/s, away from Earth, while
    /// the pods' places drift at exactly 1 m/s relative to the station. The places are the measure.</item>
    /// <item>A thin static <c>wall</c>, 3 by 5.2 m and 10 cm thick, stands across the cabin's starboard half, 10 m ahead
    /// of the center. A green <c>crate</c> of 0.5 m and 25 kg floats 23.5 m aft of the wall's center. A
    /// <see cref="Launch"/> throws it at the wall at 1,200 m/s (<see cref="CrateVelocity"/>), 20 m in each tick: the first
    /// tick of its flight ends with its front 3.2 m before the wall, and the second would end far past it, so that no tick
    /// ends with the crate in or at the wall, and only Jolt's linear cast stops it. It comes to rest 5 mm into the
    /// wall's face, because Jolt's solver pushes two bodies apart only as far as they sink beyond the backend's
    /// penetration slop of 5 mm (<c>JoltBackendOptions.PenetrationSlop</c>). The script throws it 15 s after
    /// the start, when the pods have left the station's frame. The merge test links every body of the frame within 2 s of
    /// the crate's speed, 2,400 m, so every body of the frame. With the pods gone, the crate shares its group with the wall, the station's interior and
    /// the airlock's two kinematic doors, of 1 kg each. Each of them keeps the group out of a falling numeric frame: the
    /// wall and the interior are anchors, which fix the group in the frame's own system, and a kinematic body counts as
    /// infinitely heavy, so the still doors give the group their velocity, zero (docs/plans/numeric-frames.md, "Anchors").
    /// Weighed by their masses, the crate and the doors would move at 1,111 m/s, and the crate alone at 1,200 m/s, both
    /// above the 1,000 m/s from which a group of free bodies falls (<c>NumericFrameOptions.CreateSpeed</c>). So the crate is
    /// the user's case: a fast body that, before ter-dhkf, fell in a numeric frame of its own and passed through the wall's
    /// kinematic ghost there.</item>
    /// <item>Five lamps under the cabin's roof, 6 m apart, light the cabin, at less than half the light of the orbit scene's
    /// lamps, so that the light cabin walls, the dark blue thin wall and the deep green crate stay apart in the image.</item>
    /// <item>The views: <c>watch</c>, outside, level with the pods off the station's port side and aft of it, looks
    /// forward along the port side and sees the hull, the hatch, the pods' way and the radiator. The pods lie one above
    /// the other and spread across their way, so it sees them side by side in each of its captures: projected from their
    /// corners at 1280 by 720 px, their images keep at least 7 px apart, and each is 19 px or more. A view from above
    /// looked along that spread and saw one pod.
    /// <c>radiator-view</c> sees the part of the radiator where the pods bump it, and <c>cabin</c>, in the cabin, sees the
    /// thin wall and the crate's last meters to it.</item>
    /// </list>
    /// </summary>
    public static class Station
    {
        /// <summary>The half extents of the station's module, in meters: 8 m wide, 6 m high and 30 m long.</summary>
        public static Vector3 Hull { get; } = new(4, 3, 15);

        /// <summary>The thickness of the module's floor, roof and walls, in meters.</summary>
        public const float HullWall = 0.4f;

        /// <summary>The half extents of the cabin, in meters: the module less its walls.</summary>
        public static Vector3 Cabin { get; } = Hull - new Vector3(HullWall);

        /// <summary>
        /// The radii of the station's bubble along its axes, in meters: about 10.4, 8.7 and 29.4. It is the smallest
        /// ellipsoid that holds the module grown by 2 m on every side (see <see cref="ShipBubble"/> for the rule).
        /// </summary>
        public static Vector3 Bubble { get; } = (Hull + new Vector3(2)) * MathF.Sqrt(3);

        /// <summary>
        /// The radiator, in the station's axes: a plate 24 m long along -X from the module's port face, as tall as the
        /// module, 30 cm thick, centered 3 m ahead of the module's center. Its aft face, toward the pods, is at z = -2.85.
        /// </summary>
        public static BoundingBox Radiator { get; } = new(new Vector3(-Hull.X - 24, -Hull.Y, -3.15f), new Vector3(-Hull.X, Hull.Y, -2.85f));

        /// <summary>How far aft of the station's center the airlock's chamber is centered, in meters.</summary>
        public const float AirlockZ = 9;

        /// <summary>The airlock's chamber, in the station's axes: 3 m deep against the port wall, 4 m wide, as tall as the cabin.</summary>
        public static BoundingBox Chamber { get; } = new(new Vector3(-Cabin.X, -Cabin.Y, AirlockZ - 2), new Vector3(-Cabin.X + 3, Cabin.Y, AirlockZ + 2));

        /// <summary>The thickness of the chamber's walls, in meters.</summary>
        public const float ChamberWall = 0.2f;

        /// <summary>
        /// The cargo hatch through the module's port wall, from the chamber to the outside, in the station's axes: 3 m wide
        /// and 3 m tall, with a sill 0.2 m high. The door <c>airlock-outer</c> closes it.
        /// </summary>
        public static BoundingBox Hatch { get; } = new(new Vector3(-Hull.X, -Cabin.Y + 0.2f, AirlockZ - 1.5f), new Vector3(-Cabin.X, -Cabin.Y + 3.2f, AirlockZ + 1.5f));

        /// <summary>
        /// The doorway through the chamber's inner wall, from the cabin into the chamber, in the station's axes: 1.2 m wide
        /// and 2.1 m tall, with a sill 0.2 m high. The door <c>airlock-inner</c> closes it.
        /// </summary>
        public static BoundingBox InnerDoorway { get; } =
            new(new Vector3(Chamber.Max.X, -Cabin.Y + 0.2f, AirlockZ - 0.6f), new Vector3(Chamber.Max.X + ChamberWall, -Cabin.Y + 2.3f, AirlockZ + 0.6f));

        /// <summary>Half the size of a cargo pod along each of its axes, in meters.</summary>
        public const float PodHalfSize = 0.35f;

        /// <summary>The mass of a cargo pod, in kilograms.</summary>
        public const float PodMass = 20;

        /// <summary>
        /// The pods' places at the start, in the station's axes, and their velocities relative to the station, in m/s: 1 m/s
        /// out toward the hatch and forward along the hull, at 41, 37 and 33 degrees from the port side's normal. They float
        /// one above the other in the chamber, 0.3 m apart, so that they never touch each other and a view from the side
        /// sees a gap between them, and they pass the hatch's sill and lintel with 0.15 m to spare. pod-1, the lowest, turns
        /// the most toward the radiator and reaches it first.
        /// </summary>
        public static IReadOnlyList<(string Id, Double3 Start, Double3 Velocity)> Pods { get; } =
        [
            ("pod-1", new Double3(-1.8, -1.9, AirlockZ + 1.4), Drift(41)),
            ("pod-2", new Double3(-1.8, -0.9, AirlockZ + 1.4), Drift(37)),
            ("pod-3", new Double3(-1.8, 0.1, AirlockZ + 1.4), Drift(33)),
        ];

        /// <summary>A velocity of 1 m/s out of the port side, turned toward the nose by <paramref name="degrees"/>.</summary>
        private static Double3 Drift(double degrees) => new(-Math.Cos(degrees * Math.PI / 180), 0, -Math.Sin(degrees * Math.PI / 180));

        /// <summary>The thin wall, in the station's axes: 3 m wide, as tall as the cabin, 10 cm thick, 10 m ahead of the center.</summary>
        public static BoundingBox ThinWall { get; } = new(new Vector3(0.5f, -Cabin.Y, -10.05f), new Vector3(3.5f, Cabin.Y, -9.95f));

        /// <summary>Half the size of the thrown crate along each of its axes, in meters.</summary>
        public const float CrateHalfSize = 0.25f;

        /// <summary>
        /// The mass of the thrown crate, in kilograms: heavy enough that it would set the speed of the group that the merge
        /// test links it to, if the doors in that group were not kinematic (see <see cref="Station"/>).
        /// </summary>
        public const float CrateMass = 25;

        /// <summary>
        /// Where the thrown crate starts, in the station's axes: 23.5 m aft of the wall's center, with its back 0.85 m from
        /// the cabin's aft wall. The first tick of its flight, 20 m, ends with its center 3.5 m aft of the wall's center and
        /// its front 3.2 m before the wall's face, so that no tick ends with the crate in or at the wall.
        /// </summary>
        public static Double3 CrateStart { get; } = new(2, 0, 13.5);

        /// <summary>
        /// The velocity that the script's <see cref="Launch"/> gives the crate, relative to the station, in m/s: 1,200 m/s
        /// toward the wall, above the 1,000 m/s from which a group of free bodies falls in a numeric frame, and 20 m in each
        /// tick.
        /// </summary>
        public static Double3 CrateVelocity { get; } = new(0, 0, -1200);

        /// <summary>The roll of the station about its Z axis after it faces Earth, in radians: 35 degrees, port side up.</summary>
        private const double Roll = -35 * Math.PI / 180;

        /// <summary>The pitch of the station about its X axis after the roll, in radians: 25 degrees, aft end up.</summary>
        private const double Pitch = -25 * Math.PI / 180;

        /// <summary>
        /// The turn from the attitude that faces Earth to the station's attitude: the roll, then the pitch. In a world that
        /// starts at time 0, the sun stands straight above a station that faces Earth, so in the station's axes it then
        /// lies along this turn's inverse of +Y, about (-0.57, 0.74, 0.35): it lights the roof, the port side and the aft
        /// faces. The station does not turn, and in the half minute of the script it moves 2 degrees round Earth, so the
        /// sun stays there.
        /// </summary>
        private static DQuaternion Lean => DQuaternion.FromAxisAngle(Double3.UnitZ, Roll) * DQuaternion.FromAxisAngle(Double3.UnitX, Pitch);

        /// <summary>
        /// The station's orbit: the circle of the orbit scene's ship (see <see cref="ShipOrbit"/>), 400 km up and tilted
        /// 51.64 degrees, with an attitude that does not turn: the one that faces Earth at <paramref name="time"/>, leaned by
        /// <see cref="Lean"/>.
        /// </summary>
        public static KeplerOrbit OrbitAt(double time) =>
            ShipOrbit with { Attitude = KeplerAttitude.Spin, AxialTilt = ShipOrbit.RotationAt(time, EarthMu) * Lean, SpinRate = 0, SpinPhaseAtEpoch = 0 };

        public static SceneInstance Build(World world, IRenderResources resources)
        {
            var scene = new SceneInstance();
            FrameHandle earth = SunAndEarth(world, resources, scene);

            // The station starts on its orbit at the world's time, and its KeplerMotion moves it on from the second sync point.
            KeplerOrbit orbit = OrbitAt(world.Time);
            (Double3 stationPlace, Double3 stationVelocity) = orbit.StateAt(world.Time, EarthMu);
            Entity station = world.Spawn(new SpawnDesc
            {
                Frame = earth,
                Pose = new DPose(stationPlace, orbit.RotationAt(world.Time, EarthMu)),
                LinearVelocity = stationVelocity,
                AngularVelocity = orbit.AngularVelocityAt(world.Time, EarthMu).ToVector3(),
                Components = [new KeplerMotion { Orbit = orbit }],
                // The hull outside the frame: the solid module and the radiator. It is kinematic, and its KeplerMotion
                // moves it.
                Body = new BodyDesc(new CompoundShapeDesc([
                        new CompoundChild(new BoxShapeDesc(Hull), new Pose(Vector3.Zero, Quaternion.Identity)),
                        new CompoundChild(new BoxShapeDesc(Radiator.Extents), new Pose(Radiator.Center, Quaternion.Identity)),
                    ]), BodyMotion.Kinematic, Mass: 2e5f,
                    InteriorShape: new CompoundShapeDesc([.. Interior().Select(box => new CompoundChild(new BoxShapeDesc(box.Extents), new Pose(box.Center, Quaternion.Identity)))])),
                AsFrame = new FrameSpec(new FrameBounds(Bubble), MotionDriver.Kepler),
                Render = Drawn(resources, HullMesh(), new Vector4(0.8f, 0.8f, 0.82f, 1), boundingRadius: 33),
            });
            scene.Add("station", station);
            FrameHandle frame = world.GetOwnedFrame(station);

            // Five lamps under the roof, 6 m apart, placed as in the orbit scene's ship, at 40 % of its light. At its light,
            // the tone curve washed the cabin's colors out: the green crate came out as pale as the thin wall beside it, and
            // the lamps' own faces white.
            MeshId lampMesh = resources.CreateMesh(Primitives.Box(new Vector3(0.6f, 0.05f, 0.6f)));
            MaterialId lampMaterial = resources.CreateMaterial(new MaterialDesc(new Vector4(0, 0, 0, 1), SpecularIntensity: 0, Emissive: new Vector3(2, 1.8f, 1.5f)));
            for (int i = -2; i <= 2; i++)
                world.Spawn(new SpawnDesc
                {
                    Frame = frame,
                    Pose = new DPose(new Double3(0, Cabin.Y - 0.05, 6 * i), DQuaternion.Identity),
                    Render = new RenderData
                    {
                        Mesh = lampMesh,
                        Material = lampMaterial,
                        BoundingRadius = 0.85f,
                        Flags = RenderFlags.Visible,
                        Light = new LightData { Enabled = true, Color = new Vector3(1, 0.9f, 0.75f), Intensity = 40, Range = 20, Offset = new Pose(new Vector3(0, -0.1f, 0), Quaternion.Identity) },
                    },
                });

            // The outer door slides aft into the port wall beside the hatch, and the inner door up into the chamber's
            // inner wall. Each slides 0.1 m farther than its doorway is wide or tall, so that its edge hides in the wall.
            // The outer door spawns open, so that it never moves past the pods (see Station).
            Entity outerDoor = world.Spawn(DoorOf(frame, Hatch, new Vector3(0, 0, 2 * Hatch.Extents.Z + 0.1f), open: true, resources));
            Entity innerDoor = world.Spawn(DoorOf(frame, InnerDoorway, new Vector3(0, 2 * InnerDoorway.Extents.Y + 0.1f, 0), open: false, resources));
            scene.Add("airlock-inner", innerDoor);
            scene.Add("airlock-outer", outerDoor);
            scene.Add("airlock", world.Spawn(new SpawnDesc
            {
                Frame = frame,
                Pose = new DPose(Chamber.Center, DQuaternion.Identity),
                Components = [new Airlock(innerDoor, outerDoor, cycleSeconds: 3, outerOpenAtStart: true)],
            }));

            var orange = new Vector4(1, 0.45f, 0.1f, 1);
            RenderData pod = new()
            {
                Mesh = resources.CreateMesh(Primitives.Box(new Vector3(PodHalfSize))),
                Material = resources.CreateMaterial(new MaterialDesc(orange, Emissive: MarkerGlow(orange))),
                BoundingRadius = PodHalfSize * MathF.Sqrt(3),
                Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
            };
            foreach ((string id, Double3 start, Double3 velocity) in Pods)
                scene.Add(id, world.Spawn(new SpawnDesc
                {
                    Frame = frame,
                    Pose = new DPose(start, DQuaternion.Identity),
                    LinearVelocity = velocity,
                    Body = new BodyDesc(new BoxShapeDesc(new Vector3(PodHalfSize)), BodyMotion.Dynamic, Mass: PodMass, LinearDamping: 0, AngularDamping: 0),
                    AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f), MotionDriver.Physics),
                    Render = pod,
                }));

            scene.Add("wall", world.Spawn(new SpawnDesc
            {
                Frame = frame,
                Pose = new DPose(ThinWall.Center, DQuaternion.Identity),
                Body = new BodyDesc(new BoxShapeDesc(ThinWall.Extents), BodyMotion.Static),
                Render = Drawn(resources, Primitives.Box(ThinWall.Extents), new Vector4(0.14f, 0.18f, 0.34f, 1), boundingRadius: ThinWall.Extents.Length()),
            }));
            var crate = new CrateSpec(new Vector3(CrateHalfSize), CrateMass, resources.CreateMesh(Primitives.Box(new Vector3(CrateHalfSize))),
                resources.CreateMaterial(MaterialDesc.Color(new Vector4(0.15f, 0.55f, 0.1f, 1))));
            scene.Add("crate", world.Spawn(new SpawnDesc
            {
                Frame = frame,
                Pose = new DPose(CrateStart, DQuaternion.Identity),
                Body = new BodyDesc(new BoxShapeDesc(crate.HalfExtents), Mass: crate.Mass, LinearDamping: 0, AngularDamping: 0),
                Render = crate.Drawn(),
                Components = [new Crate(crate), new Launchable()],
            }));

            scene.Add("watch", world.Spawn(View(frame, eye: new Double3(-13, -1, 23), target: new Double3(-8, -1, 3))));
            scene.Add("radiator-view", world.Spawn(View(frame, eye: new Double3(-12, 7, 9), target: new Double3(-19, -0.9, -2.5))));
            scene.Add("cabin", world.Spawn(View(frame, eye: new Double3(-2.6, 1.6, -3.5), target: new Double3(2, -0.4, -9.6))));
            scene.AddView("watch");
            scene.AddView("radiator-view");
            scene.AddView("cabin");
            world.Camera = scene["watch"];
            return scene;
        }

        /// <summary>
        /// A view in <paramref name="frame"/> at <paramref name="eye"/> that looks at <paramref name="target"/>, with no
        /// roll: a view looks along its -Z, so the view turns about Y by the target's heading, after a tilt about its own X
        /// by the target's elevation.
        /// </summary>
        private static SpawnDesc View(FrameHandle frame, Double3 eye, Double3 target)
        {
            Double3 look = target - eye;
            double heading = Math.Atan2(-look.X, -look.Z), elevation = Math.Atan2(look.Y, Math.Sqrt(look.X * look.X + look.Z * look.Z));
            return new SpawnDesc
            {
                Frame = frame,
                Pose = new DPose(eye, DQuaternion.FromAxisAngle(Double3.UnitY, heading) * DQuaternion.FromAxisAngle(Double3.UnitX, elevation)),
                Render = new RenderData(),
            };
        }

        /// <summary>
        /// A door of the airlock that closes <paramref name="doorway"/>, a doorway through a wall across X: a kinematic slab
        /// 0.1 m thick in the middle of the wall, which slides by <paramref name="slide"/> in 1.5 s. It spawns shut, or with
        /// <paramref name="open"/> slid open.
        /// </summary>
        private static SpawnDesc DoorOf(FrameHandle frame, BoundingBox doorway, Vector3 slide, bool open, IRenderResources resources)
        {
            Vector3 half = doorway.Extents with { X = 0.05f };
            return new SpawnDesc
            {
                Frame = frame,
                Pose = new DPose(open ? doorway.Center + slide : doorway.Center, DQuaternion.Identity),
                Body = new BodyDesc(new BoxShapeDesc(half), BodyMotion.Kinematic),
                Render = Drawn(resources, Primitives.Box(half), new Vector4(0.85f, 0.45f, 0.1f, 1), boundingRadius: half.Length()),
                Components = [new Door(slide, openSeconds: 1.5f, openAtStart: open)],
            };
        }

        /// <summary>The chamber's two side walls, from the port wall to the cabin side of its inner wall.</summary>
        private static BoundingBox[] ChamberSides =>
        [
            new(new Vector3(-Cabin.X, -Cabin.Y, Chamber.Min.Z - ChamberWall), new Vector3(InnerDoorway.Max.X, Cabin.Y, Chamber.Min.Z)),
            new(new Vector3(-Cabin.X, -Cabin.Y, Chamber.Max.Z), new Vector3(InnerDoorway.Max.X, Cabin.Y, Chamber.Max.Z + ChamberWall)),
        ];

        /// <summary>The chamber's inner wall, between its side walls, which the inner doorway goes through.</summary>
        private static BoundingBox ChamberInnerWall => new(new Vector3(InnerDoorway.Min.X, -Cabin.Y, Chamber.Min.Z), new Vector3(InnerDoorway.Max.X, Cabin.Y, Chamber.Max.Z));

        /// <summary>
        /// The boxes of the station's interior shape, which physics uses inside the station's own frame, in the station's
        /// axes, in meters: the module's floor, roof, side walls and end walls, as in the orbit scene's ship, with the hatch
        /// open in the port wall; the chamber's walls, with the inner doorway open; and the radiator. The boxes touch
        /// without gaps or overlaps.
        /// </summary>
        public static IEnumerable<BoundingBox> Interior()
        {
            var hull = new BoundingBox(-Hull, Hull);
            yield return hull with { Max = Hull with { Y = -Cabin.Y } };
            yield return hull with { Min = (-Hull) with { Y = Cabin.Y } };
            yield return new BoundingBox(new Vector3(Cabin.X, -Cabin.Y, -Hull.Z), new Vector3(Hull.X, Cabin.Y, Hull.Z));
            foreach (BoundingBox part in AroundDoorway(new BoundingBox(new Vector3(-Hull.X, -Cabin.Y, -Hull.Z), new Vector3(-Cabin.X, Cabin.Y, Hull.Z)), Hatch))
                yield return part;
            yield return new BoundingBox(new Vector3(-Cabin.X, -Cabin.Y, -Hull.Z), new Vector3(Cabin.X, Cabin.Y, -Cabin.Z));
            yield return new BoundingBox(new Vector3(-Cabin.X, -Cabin.Y, Cabin.Z), new Vector3(Cabin.X, Cabin.Y, Hull.Z));
            foreach (BoundingBox side in ChamberSides)
                yield return side;
            foreach (BoundingBox part in AroundDoorway(ChamberInnerWall, InnerDoorway))
                yield return part;
            yield return Radiator;
        }

        /// <summary>
        /// The drawn hull, in the station's axes: the outer box and the cabin seen from inside, joined through the hatch,
        /// the chamber's walls, and the radiator.
        /// </summary>
        private static MeshData HullMesh()
        {
            var hatch = Opening.ThroughX(Hatch);
            BoxMesh mesh = new BoxMesh()
                .Box(new BoundingBox(-Hull, Hull), openings: hatch)
                .Box(new BoundingBox(-Cabin, Cabin), inward: true, openings: hatch)
                .Sides(hatch);
            foreach (BoundingBox side in ChamberSides)
                mesh.Box(side);
            var innerDoorway = Opening.ThroughX(InnerDoorway);
            return mesh.Box(ChamberInnerWall, openings: innerDoorway).Sides(innerDoorway).Box(Radiator).Build();
        }
    }
}
