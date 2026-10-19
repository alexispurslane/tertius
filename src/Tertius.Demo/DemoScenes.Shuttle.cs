using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The shuttle of the Blue Danube demo (docs/plans/blue-danube-demo.md): a frame owner with a box hull 6 m wide, 4 m
    /// tall and 16 m long. In its own axes +Y is up, -Z is the nose and +X is starboard.
    /// <list type="bullet">
    /// <item>In its parent frame the hull is a solid box. In its own frame it is hollow: walls, a floor and a roof 0.2 m
    /// thick round a cabin 5.6 by 3.6 by 15.6 m. The <see cref="Hatch"/> leads through the starboard wall, and the
    /// <see cref="Seat"/>, a static box, stands on the cabin floor. Both are part of the interior shape, the static body
    /// of the frame's own physics system.</item>
    /// <item>The frame has no gravity field of its own. So its contents feel only the opposite of its proper acceleration
    /// (ARCHITECTURE.md, "Gravity"), which the engine derives from the frame's motion: the weight of the ground while the
    /// shuttle stands parked on it, and nothing once the shuttle falls freely in orbit.</item>
    /// <item>Its bubble is the smallest ellipsoid that holds the hull grown by 2 m on every side, as the orbit scene's ship
    /// has, so a person who walks up to the hatch is in the shuttle's frame before it reaches the hull.</item>
    /// </list>
    /// </summary>
    public static class Shuttle
    {
        /// <summary>The half extents of the hull, in meters, in the shuttle's axes.</summary>
        public static Vector3 Hull { get; } = new(3, 2, 8);

        /// <summary>The thickness of the hull's walls, floor and roof, in meters.</summary>
        public const float Wall = 0.2f;

        /// <summary>The half extents of the cabin inside the walls, in meters, in the shuttle's axes.</summary>
        public static Vector3 Cabin { get; } = Hull - new Vector3(Wall);

        /// <summary>
        /// The mass of the hull, in kilograms. A parked hull does not use it, but it needs one so that a later
        /// <see cref="BodyHandle.SetMotion"/> can make it dynamic.
        /// </summary>
        public const float Mass = 2e4f;

        /// <summary>
        /// The moments of inertia of the hull about its own X, Y and Z axes, in kg·m²: those of a solid box of
        /// <see cref="Mass"/> with the hull's size, as physics gives a body whose description has a mass.
        /// </summary>
        public static Vector3 Inertia { get; } = BoxInertia(Mass, 2 * Hull);

        /// <summary>
        /// The acceleration that the main engine gives the shuttle at full throttle, in m/s²: three standard gravities,
        /// as a rocket sled pushes.
        /// </summary>
        public const float ThrustAcceleration = 3 * 9.81f;

        /// <summary>
        /// The radius of the circle that the ascent autopilot flies the shuttle to, in meters, in <c>earth-surface</c>:
        /// <see cref="ParkingOrbitAltitude"/> above Earth's mean radius.
        /// </summary>
        public const double ParkingOrbitRadius = EarthRadius + ParkingOrbitAltitude;

        /// <summary>
        /// How high the parking orbit lies above Earth's mean radius, in meters: 200 km, above
        /// <see cref="AtmosphereTop"/> and below the station's 400 km.
        /// </summary>
        public const double ParkingOrbitAltitude = 200e3;

        /// <summary>
        /// The shuttle's flight controls for the climb to its parking orbit, in the order in which they run in a tick: the
        /// <see cref="AscentAutopilot"/>, which adds the <see cref="Throttle"/> and the <see cref="AttitudeTarget"/> of
        /// each tick to the shuttle's events, then the <see cref="AttitudeAutopilot"/> and the <see cref="MainEngine"/>,
        /// which read them. The engine pushes <see cref="Mass"/> with <see cref="ThrustAcceleration"/> at full throttle,
        /// and the ascent autopilot plans with that thrust. It steers only once the shuttle is
        /// <paramref name="climbHeight"/> meters higher than where it launched, clear of its ramp.
        /// </summary>
        public static IComponent[] FlightControls(double climbHeight)
        {
            const float thrust = Mass * ThrustAcceleration;
            return [new AscentAutopilot(thrust, ParkingOrbitRadius, climbHeight), new AttitudeAutopilot(Inertia), new MainEngine(thrust)];
        }

        private static Vector3 BoxInertia(float mass, Vector3 size) => mass / 12 * new Vector3(
            size.Y * size.Y + size.Z * size.Z, size.X * size.X + size.Z * size.Z, size.X * size.X + size.Y * size.Y);

        /// <summary>
        /// The hatch, the doorway through the starboard wall, in the shuttle's axes: 1.2 m wide, 2.1 m tall, centered 2 m
        /// aft of the shuttle's center, with a sill 0.1 m above the cabin floor. A person on the ground below the hull
        /// steps 0.3 m up onto the sill, within the 0.4 m that a character climbs as a stair.
        /// </summary>
        public static BoundingBox Hatch { get; } = new(new Vector3(Cabin.X, -Cabin.Y + 0.1f, 1.4f), new Vector3(Hull.X, -Cabin.Y + 2.2f, 2.6f));

        /// <summary>
        /// The seat, in the shuttle's axes: a box 0.6 m wide, 0.5 m tall and 0.6 m deep on the cabin floor, centered 1 m to
        /// starboard and 4.5 m ahead of the shuttle's center.
        /// </summary>
        public static BoundingBox Seat { get; } = new(new Vector3(0.7f, -Cabin.Y, -4.8f), new Vector3(1.3f, -Cabin.Y + 0.5f, -4.2f));

        /// <summary>
        /// Where a person 1.8 m tall stands at the seat, in the shuttle's axes: the center of its capsule, 0.9 m above the
        /// cabin floor, on the seat's line aft of it, with 0.15 m between the capsule, 0.3 m in radius, and the seat. A
        /// character has no sitting pose for now, so a person who leaves its seat walks away from this place.
        /// </summary>
        public static Double3 SeatStand { get; } = new(1, -Cabin.Y + 0.9, Seat.Max.Z + 0.45);

        /// <summary>
        /// Where a person 1.8 m tall stands 3 m outside the hatch, in the shuttle's axes: the center of its capsule, 0.9 m
        /// above the ground under the hull, in line with the hatch. A person who walks from there to
        /// <see cref="InsideHatch"/> walks straight through the middle of the hatch.
        /// </summary>
        public static Double3 OutsideHatch { get; } = new(Hull.X + 3, -Hull.Y + 0.9, Hatch.Center.Z);

        /// <summary>
        /// Where a person 1.8 m tall stands 1.2 m inside the hatch, in the shuttle's axes: the center of its capsule, 0.9 m
        /// above the cabin floor, in line with the hatch, with the whole capsule inside the cabin.
        /// </summary>
        public static Double3 InsideHatch { get; } = new(Cabin.X - 1.2, -Cabin.Y + 0.9, Hatch.Center.Z);

        /// <summary>The radii of the bubble, in meters, in the shuttle's axes.</summary>
        public static Vector3 Bubble { get; } = (Hull + new Vector3(2)) * MathF.Sqrt(3);

        /// <summary>
        /// The boxes of the interior shape, in the shuttle's axes, in meters. The floor and the roof span the whole hull, the
        /// side walls stand between them, and the end walls close the cabin between the side walls. The starboard wall
        /// leaves <see cref="Hatch"/> open. The seat stands on the floor. The boxes touch without gaps or overlaps.
        /// </summary>
        public static IEnumerable<BoundingBox> Interior()
        {
            var hull = new BoundingBox(-Hull, Hull);
            yield return hull with { Max = Hull with { Y = -Cabin.Y } };
            yield return hull with { Min = (-Hull) with { Y = Cabin.Y } };
            yield return new BoundingBox(new Vector3(-Hull.X, -Cabin.Y, -Hull.Z), new Vector3(-Cabin.X, Cabin.Y, Hull.Z));
            foreach (BoundingBox part in AroundDoorway(new BoundingBox(new Vector3(Cabin.X, -Cabin.Y, -Hull.Z), new Vector3(Hull.X, Cabin.Y, Hull.Z)), Hatch))
                yield return part;
            yield return new BoundingBox(new Vector3(-Cabin.X, -Cabin.Y, -Hull.Z), new Vector3(Cabin.X, Cabin.Y, -Cabin.Z));
            yield return new BoundingBox(new Vector3(-Cabin.X, -Cabin.Y, Cabin.Z), new Vector3(Cabin.X, Cabin.Y, Hull.Z));
            yield return Seat;
        }

        /// <summary>
        /// The drawn hull, in the shuttle's axes: the outer box and the cabin seen from inside, joined through the hatch.
        /// The scene draws the seat apart, in its own color.
        /// </summary>
        public static MeshData HullMesh()
        {
            var hatch = Opening.ThroughX(Hatch);
            return new BoxMesh()
                .Box(new BoundingBox(-Hull, Hull), openings: hatch)
                .Box(new BoundingBox(-Cabin, Cabin), inward: true, openings: hatch)
                .Sides(hatch)
                .Build();
        }

        /// <summary>
        /// Spawns the shuttle parked at <paramref name="pose"/> in <paramref name="frame"/>, drawn with
        /// <paramref name="render"/>, with the <paramref name="components"/>, such as its <see cref="FlightControls"/>: its
        /// hull is static, so that a later <see cref="BodyHandle.SetMotion"/> unparks it.
        /// The cabin needs no value of its own for its weight. The engine derives every frame's proper acceleration from
        /// how the frame moved in the step (ARCHITECTURE.md, "How a frame moves"): a frame that holds still in a gravity
        /// field feels that gravity reversed, so the person in the parked cabin feels the weight of the ground. A new frame
        /// reads no proper acceleration during its first step, and the derived value from its second step on.
        /// </summary>
        public static Entity Park(World world, FrameHandle frame, DPose pose, RenderData? render = null, IReadOnlyList<IComponent>? components = null) => world.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = pose,
            Body = new BodyDesc(new BoxShapeDesc(Hull), BodyMotion.Static, Mass: Mass, LinearDamping: 0, AngularDamping: 0,
                InteriorShape: new CompoundShapeDesc([.. Interior().Select(box => new CompoundChild(new BoxShapeDesc(box.Extents), new Pose(box.Center, Quaternion.Identity)))])),
            AsFrame = new FrameSpec(new FrameBounds(Bubble)),
            Render = render,
            Components = components ?? [],
        });
    }
}
