using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The first part of the Blue Danube demo (docs/plans/blue-danube-demo.md, "The sequence", step 1): a launch pad and a
    /// boarding terminal on Earth's surface, a parked shuttle on the pad, and a passenger who walks through the terminal and
    /// boards the shuttle.
    /// <list type="bullet">
    /// <item>The ground is the surface yard's (see <see cref="EarthSurface"/>): the root frame <c>earth-surface</c> at
    /// Earth's center, which does not turn, with Earth's point gravity, and at the north pole a static gray slab, the
    /// <c>floor</c>, 140 by 100 m, with its top at y = R = <see cref="EarthRadius"/>. +Y is up.</item>
    /// <item>The <c>terminal</c> is a static gray box building, 10 m deep along X, 16 m wide along Z and 5 m tall, centered
    /// at x = 22 m (see <see cref="TerminalCenter"/>): walls and a roof 0.3 m thick, standing on the slab, which is the
    /// hall's floor. Its entrance faces +X, 2.5 m wide and 3 m tall above a sill 3 cm high, centered at z = 0. Its gate, of the same size, faces -X toward the pad, centered
    /// at z = -4 m. Benches stand along the +Z wall and a check-in counter by the gate. Two lamps under the roof light the
    /// hall.</item>
    /// <item>The <c>shuttle</c> (see <see cref="Shuttle"/>) stands parked on the slab at the origin, unturned: its nose
    /// points along -Z and its hatch faces +X, toward the terminal. A dark gray <c>pad</c>, only drawn, marks the ground
    /// round it, and a service <c>tower</c> stands on its port side. The <c>seat</c> is drawn in blue, and two lamps light
    /// the cabin.</item>
    /// <item>The <c>person</c>, the capsule character of the other scenes, 1.8 m tall and of 70 kg, starts 9 m outside the
    /// entrance, at x = 36 m. Its <see cref="Walker"/> walks it through the entrance, round to the gate, out across the pad
    /// to <see cref="Shuttle.OutsideHatch"/>, through the hatch, and along the cabin to <see cref="Shuttle.SeatStand"/>,
    /// where it stops and stays (see <see cref="Path"/>). The person enters the shuttle's frame on the pad, about 9.4 m
    /// from the shuttle's axis, when its bounding sphere touches the bubble. The slab has a ghost in the shuttle's bubble
    /// (ARCHITECTURE.md, "Ghosts"), so the person walks on in the shuttle's frame on the same ground, and climbs the
    /// hatch's sill as a stair.</item>
    /// <item>The views: <c>overview</c>, the camera at the start, high over the terminal's entrance side, sees the terminal
    /// and the shuttle; <c>terminal-camera</c>, high in the hall's corner by the gate, sees the walk through the hall;
    /// <c>apron-camera</c>, 3 m up off the shuttle's aft starboard quarter, between the shuttle and the terminal's gate side,
    /// sees the hatch, the walk across the pad and the person's step into the hatch, with the terminal off to its right;
    /// <c>cabin-camera</c>, in the shuttle's frame in the cabin's aft port corner, sees the seat and the hatch.</item>
    /// <item>The launch (docs/plans/blue-danube-demo.md, "The sequence", step 2): the <c>ramp</c> (see
    /// <see cref="LaunchRamp"/>) starts under the parked shuttle and rises at 30 degrees along -Z. The shuttle carries its
    /// <see cref="Shuttle.FlightControls"/>, which wait for an <see cref="Ignition"/> that a script posts to it. Then the
    /// shuttle unparks, slides up the ramp at three gravities, and flies to a circle 200 km up. A red <c>pen</c>, 14 cm
    /// long, hangs in a <see cref="Clip"/> on the cabin's port wall, at head height, aft of the seat (see
    /// <see cref="ClippedPen"/>). The clip holds it through the climb, and lets it go with a small push and a slow spin a
    /// second after the cabin becomes weightless in orbit, so that the pen drifts into the middle of the cabin. The person
    /// is a <see cref="Passenger"/>: its belt holds it at <see cref="Shuttle.SeatStand"/> through the climb, and in the
    /// weightless cabin it floats to the drifting pen and takes it.</item>
    /// <item>The <c>earth</c>, only drawn, is a sphere 1 m smaller than Earth, with the grid of the orbit scene, so the
    /// slab hides it at the pad and the climb shows the planet below.</item>
    /// <item>More views: <c>ramp-camera</c> stands on the ground beside the ramp and sees all of it; <c>aft-camera</c>, in
    /// the shuttle's frame in the cabin's forward starboard corner, looks aft along the cabin, past the seat, to the aft
    /// wall; <c>chase-camera</c> rides in the shuttle's frame, 45 m behind it and 10 m above its roof, and looks at
    /// it.</item>
    /// <item>The frame <c>sun</c> lies far away, toward +X and +Z and high up, so its light falls the same way on the pad and
    /// in orbit.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>A character has no sitting pose for now, so the person stands at <see cref="Shuttle.SeatStand"/>, upright on
    /// the cabin floor just aft of the seat. The launch step of the demo (docs/plans/blue-danube-demo.md, "The sequence",
    /// step 2) says that the character leaves its seat: that means that the person floats away from
    /// <see cref="Shuttle.SeatStand"/>.</para>
    /// <para>Under the climb's thrust, the engine turns a character's up against the cabin's gravity, so the belted person
    /// tips toward the nose, and it floats that way in orbit, because a weightless character keeps its last up. Ticket
    /// ter-yrfa will let a weightless character turn its own up, which will right the person in orbit.</para>
    /// </remarks>
    public static class DemoSurface
    {
        /// <summary>Where the slab's top lies under the terminal's center: 22 m along +X, on the ground.</summary>
        public static Double3 TerminalCenter { get; } = new(22, EarthRadius, 0);

        /// <summary>The terminal's half extents, in meters: 10 m deep along X, 5 m tall and 16 m wide along Z.</summary>
        public static Vector3 TerminalHalf { get; } = new(5, 2.5f, 8);

        /// <summary>
        /// How much higher than the ramp's top the shuttle climbs before its autopilot steers, in meters, so that the
        /// shuttle is clear of the ramp.
        /// </summary>
        public const double ClimbClearance = 30;

        /// <summary>The pen's half extents, in meters: 14 cm long along its own Z, and 12 mm thick.</summary>
        public static Vector3 PenHalf { get; } = new(0.006f, 0.006f, 0.07f);

        /// <summary>The pen's mass, in kilograms.</summary>
        public const float PenMass = 0.02f;

        /// <summary>
        /// Where the clip holds the pen, in the shuttle's axes: on the port wall, 1.5 m above the cabin floor, 2.75 m aft of
        /// <see cref="Shuttle.SeatStand"/>, with 1 cm between the pen and the wall. Its length lies along the cabin.
        /// </summary>
        public static DPose PenClip { get; } = new(new Double3(-Shuttle.Cabin.X + PenHalf.X + 0.01, -Shuttle.Cabin.Y + 1.5, -1), DQuaternion.Identity);

        /// <summary>
        /// The push with which the clip lets the pen go, in m/s, in the shuttle's axes: away from the port wall, toward the
        /// middle of the cabin, and a little up.
        /// </summary>
        public static Double3 PenPush { get; } = new(0.2, 0.03, 0);

        /// <summary>
        /// The spin with which the clip lets the pen go, in rad/s, in the shuttle's axes: 0.5 rad/s, a turn in about 13 s,
        /// end over end about an axis across the pen. The pen's two short sides are equal, so every axis across it is a
        /// principal axis, and the spin stays steady. Its ends move at 3.5 cm/s, slower than the push carries the pen from
        /// the wall, so the pen never swings into it.
        /// </summary>
        public static Vector3 PenSpin { get; } = new(0.3f, 0.4f, 0);

        /// <summary>
        /// The loose <paramref name="pen"/> in <paramref name="cabin"/>, the shuttle's frame, at <see cref="PenClip"/>, with
        /// a <see cref="Clip"/> that holds it there while the cabin has weight and lets it go with <see cref="PenPush"/> and
        /// <see cref="PenSpin"/>.
        /// </summary>
        public static SpawnDesc ClippedPen(CrateSpec pen, FrameHandle cabin) =>
            pen.Spawn(cabin, PenClip, Double3.Zero, new Clip(PenClip, PenPush, PenSpin));

        /// <summary>The eye of the person, relative to its center, in its axes: 1.65 m above its feet.</summary>
        public static Vector3 Eye { get; } = new(0, 0.75f, 0);

        /// <summary>Where the person starts: the center of its capsule, 9 m outside the entrance, 0.05 m above the slab's top.</summary>
        public static Double3 PersonStart { get; } = new(36, EarthRadius + 0.95, 0);

        // The thickness of the terminal's walls and roof, in meters.
        private const float TerminalWall = 0.3f;
        // The height of the hall's drawn floor and of the doorways' sills above the slab, in meters. A doorway of a drawn
        // box must lie clear of the face's edges, so each sill stands a little above the drawn floor.
        private const float HallFloor = 0.01f;
        private const float Sill = 0.03f;

        // In the terminal's axes, with the origin on the slab's top under its center. The doorways are 2.5 m wide and 3 m
        // tall above their sills.
        private static BoundingBox Entrance { get; } = Doorway(TerminalHalf.X - TerminalWall, TerminalHalf.X, 0);
        private static BoundingBox Gate { get; } = Doorway(-TerminalHalf.X, -TerminalHalf.X + TerminalWall, -4);

        private static BoundingBox Doorway(float x0, float x1, float z) =>
            new(new Vector3(x0, Sill, z - 1.25f), new Vector3(x1, Sill + 3, z + 1.25f));

        // The furniture in the terminal's axes: two benches along the +Z wall, and the check-in counter on the +Z side of
        // the gate, clear of the way from the entrance to the gate.
        private static readonly BoundingBox[] Furniture =
        [
            new(new Vector3(-3, 0, 6), new Vector3(-0.5f, 0.45f, 6.8f)),
            new(new Vector3(0.5f, 0, 6), new Vector3(3, 0.45f, 6.8f)),
            new(new Vector3(-3.6f, 0, 1.5f), new Vector3(-2.8f, 1.1f, 4.5f)),
        ];

        /// <summary>The boxes of the terminal's static shape, in its axes: the roof, the four walls round the two doorways, and the furniture.</summary>
        private static IEnumerable<BoundingBox> TerminalShape()
        {
            Vector3 half = TerminalHalf;
            float inner = half.Z - TerminalWall;
            float top = 2 * half.Y - TerminalWall;
            yield return new BoundingBox(new Vector3(-half.X, top, -half.Z), new Vector3(half.X, 2 * half.Y, half.Z));
            yield return new BoundingBox(new Vector3(-half.X, 0, -half.Z), new Vector3(half.X, top, -inner));
            yield return new BoundingBox(new Vector3(-half.X, 0, inner), new Vector3(half.X, top, half.Z));
            foreach (BoundingBox part in AroundDoorway(new BoundingBox(new Vector3(half.X - TerminalWall, 0, -inner), new Vector3(half.X, top, inner)), Entrance))
                yield return part;
            foreach (BoundingBox part in AroundDoorway(new BoundingBox(new Vector3(-half.X, 0, -inner), new Vector3(-half.X + TerminalWall, top, inner)), Gate))
                yield return part;
            foreach (BoundingBox box in Furniture)
                yield return box;
        }

        /// <summary>The drawn terminal: the outer box and the hall seen from inside, joined through the doorways, and the furniture.</summary>
        private static MeshData TerminalMesh()
        {
            Vector3 half = TerminalHalf;
            Opening[] doorways = [Opening.ThroughX(Entrance), Opening.ThroughX(Gate)];
            var hall = new BoundingBox(new Vector3(-half.X + TerminalWall, HallFloor, -half.Z + TerminalWall), new Vector3(half.X - TerminalWall, 2 * half.Y - TerminalWall, half.Z - TerminalWall));
            BoxMesh mesh = new BoxMesh()
                .Box(new BoundingBox(new Vector3(-half.X, 0, -half.Z), new Vector3(half.X, 2 * half.Y, half.Z)), openings: doorways)
                .Box(hall, inward: true, openings: doorways);
            foreach (Opening doorway in doorways)
                mesh.Sides(doorway);
            foreach (BoundingBox box in Furniture)
                mesh.Box(box);
            return mesh.Build();
        }

        /// <summary>
        /// The walker's path: in <paramref name="surface"/>, just inside the entrance, before the gate and just outside it;
        /// then in <paramref name="cabin"/>, the shuttle's frame, 3 m outside the hatch, just inside it, and the place at the
        /// seat. The waypoints on the ship are in its frame, so the path stays right however the shuttle stands.
        /// </summary>
        public static Waypoint[] Path(FrameHandle surface, FrameHandle cabin) =>
        [
            new(surface, new Double3(TerminalCenter.X + 2, EarthRadius + 0.9, 0)),
            new(surface, new Double3(TerminalCenter.X - 2.5, EarthRadius + 0.9, -4)),
            new(surface, new Double3(TerminalCenter.X - 8, EarthRadius + 0.9, -4)),
            new(cabin, Shuttle.OutsideHatch),
            new(cabin, Shuttle.InsideHatch),
            new(cabin, Shuttle.SeatStand),
        ];

        public static SceneInstance Build(World world, IRenderResources resources)
        {
            var scene = new SceneInstance();
            FrameHandle surface = EarthSurface(world, resources, scene, new Vector3(70, 0.5f, 50));
            scene.Add("earth", world.Spawn(new SpawnDesc
            {
                Frame = surface,
                Pose = new DPose(Double3.Zero, DQuaternion.Identity),
                Render = new RenderData
                {
                    Mesh = resources.CreateMesh(Primitives.Sphere((float)(EarthRadius - 1), 256, 128)),
                    Material = resources.CreateMaterial(new MaterialDesc(Vector4.One, SpecularIntensity: 0.1f, Albedo: resources.CreateTexture(EarthGrid()))),
                    BoundingRadius = (float)EarthRadius,
                    Flags = RenderFlags.Visible | RenderFlags.Celestial,
                },
            }));
            scene.Add("ramp", LaunchRamp.Spawn(world, surface, Drawn(resources, LaunchRamp.Mesh(), new Vector4(0.6f, 0.6f, 0.58f, 1), boundingRadius: LaunchRamp.Length + 6)));

            MeshId lampMesh = resources.CreateMesh(Primitives.Box(new Vector3(0.6f, 0.05f, 0.6f)));
            MaterialId lampMaterial = resources.CreateMaterial(new MaterialDesc(new Vector4(0, 0, 0, 1), SpecularIntensity: 0, Emissive: new Vector3(6, 5.4f, 4.5f)));
            SpawnDesc Lamp(FrameHandle frame, Double3 position, float range) => new()
            {
                Frame = frame,
                Pose = new DPose(position, DQuaternion.Identity),
                Render = new RenderData
                {
                    Mesh = lampMesh,
                    Material = lampMaterial,
                    BoundingRadius = 0.85f,
                    Flags = RenderFlags.Visible,
                    Light = new PointLightData(new Pose(new Vector3(0, -0.1f, 0), Quaternion.Identity), new Vector3(1, 0.9f, 0.75f), Intensity: 100, Range: range),
                },
            };

            Vector3 half = TerminalHalf;
            scene.Add("terminal", world.Spawn(new SpawnDesc
            {
                Frame = surface,
                Pose = new DPose(TerminalCenter, DQuaternion.Identity),
                Body = new BodyDesc(new CompoundShapeDesc([.. TerminalShape().Select(box => new CompoundChild(new BoxShapeDesc(box.Extents), new Pose(box.Center, Quaternion.Identity)))]), BodyMotion.Static),
                // The terminal's origin lies on the slab, so its farthest corner is a whole height up.
                Render = Drawn(resources, TerminalMesh(), new Vector4(0.72f, 0.72f, 0.7f, 1), boundingRadius: MathF.Ceiling((half with { Y = 2 * half.Y }).Length())),
            }));
            double lampHeight = EarthRadius + 2 * half.Y - TerminalWall - 0.05;
            world.Spawn(Lamp(surface, new Double3(TerminalCenter.X + 1, lampHeight, -3), range: 12));
            world.Spawn(Lamp(surface, new Double3(TerminalCenter.X + 1, lampHeight, 3), range: 12));

            var padHalf = new Vector3(10, 0.01f, 14);
            scene.Add("pad", world.Spawn(new SpawnDesc
            {
                Frame = surface,
                Pose = new DPose(new Double3(0, EarthRadius + padHalf.Y, 0), DQuaternion.Identity),
                Render = Drawn(resources, Primitives.Box(padHalf), new Vector4(0.25f, 0.25f, 0.24f, 1), boundingRadius: MathF.Ceiling(padHalf.Length())),
            }));
            var towerHalf = new Vector3(1.5f, 8, 1.5f);
            scene.Add("tower", world.Spawn(new SpawnDesc
            {
                Frame = surface,
                Pose = new DPose(new Double3(-11, EarthRadius + towerHalf.Y, -4), DQuaternion.Identity),
                Body = new BodyDesc(new BoxShapeDesc(towerHalf), BodyMotion.Static),
                Render = Drawn(resources, Primitives.Box(towerHalf), new Vector4(0.55f, 0.3f, 0.2f, 1), boundingRadius: towerHalf.Length()),
            }));

            Entity shuttle = Shuttle.Park(world, surface, new DPose(new Double3(0, EarthRadius + Shuttle.Hull.Y, 0), DQuaternion.Identity),
                Drawn(resources, Shuttle.HullMesh(), new Vector4(0.85f, 0.85f, 0.88f, 1), boundingRadius: MathF.Ceiling(Shuttle.Hull.Length())),
                Shuttle.FlightControls(LaunchRamp.Height + ClimbClearance));
            scene.Add("shuttle", shuttle);
            FrameHandle cabin = world.GetOwnedFrame(shuttle);
            Vector3 seatHalf = Shuttle.Seat.Extents;
            scene.Add("seat", world.Spawn(new SpawnDesc
            {
                Frame = cabin,
                Pose = new DPose(Shuttle.Seat.Center, DQuaternion.Identity),
                Render = Drawn(resources, Primitives.Box(seatHalf), new Vector4(0.15f, 0.25f, 0.6f, 1), boundingRadius: seatHalf.Length()),
            }));
            world.Spawn(Lamp(cabin, new Double3(0, Shuttle.Cabin.Y - 0.05, 2), range: 10));
            world.Spawn(Lamp(cabin, new Double3(0, Shuttle.Cabin.Y - 0.05, -4.5), range: 10));

            // The capsule is 1.8 m tall, and its center rests 0.9 m above the slab. Its eye is a view, which the pen in its
            // hand follows.
            var penSpec = new CrateSpec(PenHalf, PenMass, resources.CreateMesh(Primitives.Box(PenHalf)),
                resources.CreateMaterial(MaterialDesc.Color(new Vector4(0.85f, 0.1f, 0.08f, 1))));
            Entity pen = world.Spawn(ClippedPen(penSpec, cabin));
            scene.Add("pen", pen);
            RenderData personRender = Drawn(resources, Primitives.Box(new Vector3(0.3f, 0.9f, 0.3f)), new Vector4(0.2f, 0.45f, 0.8f, 1), boundingRadius: 1);
            personRender.ViewOffset = new Pose(Eye, Quaternion.Identity);
            scene.Add("person", world.Spawn(new SpawnDesc
            {
                Frame = surface,
                Pose = new DPose(PersonStart, DQuaternion.Identity),
                Body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: YardPersonMass),
                Render = personRender,
                Components = [new Walker(Path(surface, cabin)), new Passenger(new Waypoint(cabin, Shuttle.SeatStand), pen)],
            }));

            Entity overview = world.Spawn(new SpawnDesc
            {
                Frame = surface,
                Pose = LookingAt(new Double3(48, EarthRadius + 16, 28), new Double3(12, EarthRadius + 1, -1)),
                Render = new RenderData(),
            });
            scene.Add("overview", overview);
            scene.Add("terminal-camera", world.Spawn(new SpawnDesc
            {
                Frame = surface,
                Pose = LookingAt(new Double3(TerminalCenter.X - 4.2, EarthRadius + 3.6, 7.2), new Double3(TerminalCenter.X + 1, EarthRadius + 1, -1.5)),
                Render = new RenderData(),
            }));
            scene.Add("apron-camera", world.Spawn(new SpawnDesc
            {
                Frame = surface,
                Pose = LookingAt(new Double3(13, EarthRadius + 3, 10), new Double3(5, EarthRadius + 1.2, 0.5)),
                Render = new RenderData(),
            }));
            scene.Add("cabin-camera", world.Spawn(new SpawnDesc
            {
                Frame = cabin,
                Pose = LookingAt(new Double3(-2.3, 1.2, 6.8), new Double3(1, -1.3, -4.5)),
                Render = new RenderData(),
            }));

            scene.Add("ramp-camera", world.Spawn(new SpawnDesc
            {
                Frame = surface,
                Pose = LookingAt(new Double3(140, EarthRadius + 25, -95), new Double3(0, EarthRadius + 55, -110)),
                Render = new RenderData(),
            }));
            scene.Add("aft-camera", world.Spawn(new SpawnDesc
            {
                Frame = cabin,
                Pose = LookingAt(new Double3(2.3, 1.2, -7.3), new Double3(-0.5, -1, 6.5)),
                Render = new RenderData(),
            }));
            scene.Add("chase-camera", world.Spawn(new SpawnDesc
            {
                Frame = cabin,
                Pose = LookingAt(new Double3(0, 10, 45), Double3.Zero),
                Render = new RenderData(),
            }));

            // The sun stands at the distance of the real one, so its light falls the same way everywhere on the flight.
            SunAt(world, scene, surface, new Double3(0, EarthRadius, 0) + new Double3(60, 80, 40).Normalized() * AstronomicalUnit);

            scene.AddView("overview");
            scene.AddView("terminal-camera");
            scene.AddView("apron-camera");
            scene.AddView("cabin-camera");
            scene.AddView("ramp-camera");
            scene.AddView("aft-camera");
            scene.AddView("chase-camera");
            world.Camera = overview;
            return scene;
        }
    }
}
