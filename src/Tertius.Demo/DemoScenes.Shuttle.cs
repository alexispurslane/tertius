using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The shuttle of the Blue Danube demo (docs/plans/blue-danube-fidelity.md, ter-tiei): a frame owner whose outside and
    /// fitted interior are the Lunar Shuttle's model at <see cref="ArtScale"/>, 13.8 m wide, 9.3 m tall and 29.1 m long,
    /// with 21 real window openings (5 cockpit + 16 side cabin) and a starboard boarding doorway fitted directly to
    /// Pioneer's fuselage. In its own axes +Y is up, -Z is the nose and +X is starboard.
    /// <list type="bullet">
    /// <item>In its parent frame the hull is the model's box, <see cref="Hull"/> and <see cref="HullShape"/>, whose bottom is
    /// the plane of the model's three wheels. In its own frame it is hollow: a watertight interior collision shell,
    /// <see cref="InteriorShape"/>, matching the fitted aft floor, 10-degree center ramp, raised cockpit floor, tapered walls,
    /// aft bulkhead, ceiling, solid glass panes, <see cref="Seat"/> and smooth <see cref="Walkway"/> through the starboard
    /// <see cref="Hatch"/>.</item>
    /// <item>The 21 panes of <see cref="Glass"/> sit recessed inside the real cut window openings using clear dielectric
    /// glazing (<see cref="GlassAlpha"/> with zero diffuse opacity and anti-reflective coating <see cref="AntiReflective"/>).
    /// Each pane has no physics body because <see cref="InteriorShape"/> stays solid across every window opening.</item>
    /// <item>When parked (<see cref="BodyMotion.Static"/>), the stowable <c>shuttle-hatch-door</c> opens and the
    /// <c>boarding-bridge</c> deploys across the gap to the boarding stair's landing. In flight
    /// (<see cref="BodyMotion.Dynamic"/>), the bridge retracts and the hatch seals flush with the hull.</item>
    /// <item>The hull's box has one density, putting its center of mass in the box's middle with principal axes
    /// <see cref="Inertia"/>. The frame has no gravity field of its own, deriving felt weight from its proper acceleration.</item>
    /// <item>Its bubble is the smallest ellipsoid holding the hull's box grown by 2 m on every side.</item>
    /// </list>
    /// </summary>
    public static class Shuttle
    {
        /// <summary>The thickness of the gray-box loft wall, in meters, in the plane of a cross-section.</summary>
        public const float Wall = 0.2f;

        /// <summary>The mass of the hull, in kilograms.</summary>
        public const float Mass = 2e4f;

        /// <summary>The main engine acceleration at full throttle, in m/s²: three standard gravities.</summary>
        public const float ThrustAcceleration = 3 * 9.81f;

        /// <summary>The maximum force of each translation thruster pair along a hull axis, in newtons.</summary>
        public const float TranslationThrust = 30000;

        /// <summary>The radius of the parking orbit in earth's frame, in meters.</summary>
        public const double ParkingOrbitRadius = EarthRadius + ParkingOrbitAltitude;

        /// <summary>The altitude of the parking orbit above Earth's mean radius, in meters.</summary>
        public const double ParkingOrbitAltitude = 200e3;

        /// <summary>The force of the main engine at full throttle, in newtons.</summary>
        public const float MainEngineThrust = Mass * ThrustAcceleration;

        /// <summary>The aft passenger cabin floor height, in meters, in the shuttle's axes.</summary>
        public const float AftFloorY = -1.50f;

        /// <summary>The raised cockpit floor height, in meters, in the shuttle's axes.</summary>
        public const float CockpitFloorY = -0.50f;

        /// <summary>Where the 10-degree center cabin ramp starts rising from the aft floor, in meters along Z.</summary>
        public const float RampStartZ = -3.00f;

        /// <summary>Where the 10-degree center cabin ramp meets the cockpit floor, in meters along Z.</summary>
        public const float RampEndZ = -8.70f;

        /// <summary>The pressurized aft engine bulkhead plane, in meters along Z.</summary>
        public const float AftBulkheadZ = 4.00f;

        /// <summary>The flat ceiling height over the passenger cabin, in meters along Y.</summary>
        public const float CeilingY = 3.10f;

        /// <summary>Where the gray-box loft's nose ends and its full cross-section starts, in meters along Z.</summary>
        private const float NoseStart = -5;

        /// <summary>The bay of the hatch on the legacy lofted hull.</summary>
        private const int HatchBay = 6;

        /// <summary>
        /// Anti-reflective dielectric coating reflectance (~0.5 % normal incidence, keeping cabin lamp reflections soft while
        /// preserving grazing Fresnel highlights).
        /// </summary>
        private const float AntiReflective = 0.18f;

        /// <summary>Scale of the Lunar Shuttle model in shuttle axes.</summary>
        public const float ArtScale = 1.6f;

        /// <summary>The half extents of the legacy gray-box tram, kept for <see cref="Loft"/> and jet placement.</summary>
        public static Vector3 Tram { get; } = new(3, 2, 8);

        /// <summary>The legacy tram interior half extents, kept for compatibility with gray-box tests.</summary>
        public static Vector3 Cabin { get; } = Tram - new Vector3(Wall);

        /// <summary>Where the model's origin stands in the shuttle's axes, in meters.</summary>
        public static Vector3 ArtPlace { get; } = new(0, 0, 1);

        /// <summary>The bounds of the Lunar Shuttle model in its own axes, in meters.</summary>
        public static BoundingBox ArtBounds { get; } =
            new(new(-4.325f, -3.063f, -10.013f), new(4.325f, 2.731f, 8.166f));

        /// <summary>The box that the hull fits, in meters, in the shuttle's axes.</summary>
        public static BoundingBox Hull { get; } =
            new(ArtBounds.Min * ArtScale + ArtPlace, ArtBounds.Max * ArtScale + ArtPlace);

        /// <summary>The eight corners of <see cref="Hull"/>, in the shuttle's axes.</summary>
        public static IReadOnlyList<Vector3> HullCorners { get; } =
        [
            .. Enumerable
                .Range(0, 8)
                .Select(corner => new Vector3(
                    (corner & 1) == 0 ? Hull.Min.X : Hull.Max.X,
                    (corner & 2) == 0 ? Hull.Min.Y : Hull.Max.Y,
                    (corner & 4) == 0 ? Hull.Min.Z : Hull.Max.Z
                )),
        ];

        /// <summary>How far the hull's farthest corner lies from the shuttle's origin, in meters.</summary>
        public static float Reach { get; } = Vector3.Max(-Hull.Min, Hull.Max).Length();

        /// <summary>
        /// The box of the cabin's air, in meters, in the shuttle's axes: spans the fitted pressurized cabin from the nose
        /// console to the aft engine bulkhead.
        /// </summary>
        public static BoundingBox CabinAir { get; } =
            new(new(-5.6f, AftFloorY, -13.6f), new(5.6f, CeilingY, AftBulkheadZ));

        /// <summary>
        /// The starboard main side wall plane normal in shuttle axes (outward, measured from <c>lunar_shuttle_hi.dae</c>).
        /// </summary>
        public static Vector3 StarboardWallNormal { get; } = Vector3.Normalize(new(0.94478f, 0.24727f, -0.21504f));

        /// <summary>The outer starboard hull wall X at (<paramref name="y"/>, <paramref name="z"/>) in shuttle axes.</summary>
        public static float StarboardOuterX(float y, float z) => (4.52231f - 0.24727f * y + 0.21504f * z) / 0.94478f;

        /// <summary>The walkable deck floor height Y along the fitted cabin length <paramref name="z"/>, in shuttle axes.</summary>
        public static float CabinFloorY(float z)
        {
            if (z >= RampStartZ)
                return AftFloorY;
            if (z <= RampEndZ)
                return CockpitFloorY;
            float t = (RampStartZ - z) / (RampStartZ - RampEndZ);
            return AftFloorY + t * (CockpitFloorY - AftFloorY);
        }

        /// <summary>The interior half-width |X| of the fitted cabin at (<paramref name="z"/>, <paramref name="y"/>), in shuttle axes.</summary>
        public static float CabinHalfWidth(float z, float y)
        {
            if (z >= -5.0f)
            {
                float outer = StarboardOuterX(y, z);
                if (y > 1.65f)
                {
                    float crease = StarboardOuterX(1.65f, z);
                    outer = crease - 0.88f * (y - 1.65f);
                }

                return MathF.Max(0.35f, outer - 0.30f);
            }

            if (z >= RampEndZ)
            {
                float t = (-5.0f - z) / 3.7f;
                float baseAtM5 = StarboardOuterX(MathF.Min(y, 1.65f), -5.0f) - 0.30f;
                float baseAtM87 = 2.60f - 0.30f * MathF.Max(0.0f, y - CockpitFloorY);
                float w = baseAtM5 * (1.0f - t) + baseAtM87 * t;
                if (y > 1.55f)
                    w -= 0.72f * (y - 1.55f);
                return MathF.Max(0.35f, w);
            }

            float tn = Math.Clamp((RampEndZ - z) / 4.8f, 0.0f, 1.0f);
            float floorW = 2.55f * (1.0f - tn) + 1.35f * tn;
            if (y > 0.0f)
                floorW -= (0.45f + 0.25f * tn) * y;
            return MathF.Max(0.30f, floorW);
        }

        /// <summary>The fitted cabin ceiling height Y at <paramref name="z"/>, in shuttle axes.</summary>
        public static float CabinCeilingY(float z)
        {
            if (z >= -7.0f)
                return CeilingY;
            float t = Math.Clamp((-7.0f - z) / 4.05f, 0.0f, 1.0f);
            return CeilingY + t * (1.98f - CeilingY);
        }

        /// <summary>
        /// True when <paramref name="position"/>, in shuttle axes, lies strictly inside the fitted pressurized cabin with at
        /// least <paramref name="margin"/> meters of clearance from walls, floor and ceiling.
        /// </summary>
        public static bool InsideCabin(Double3 position, double margin = 0.1)
        {
            float z = (float)position.Z;
            float y = (float)position.Y;
            if (z < -13.4f + margin || z > AftBulkheadZ - margin)
                return false;
            float floor = CabinFloorY(z);
            float ceil = CabinCeilingY(z);
            if (y < floor + margin || y > ceil - margin)
                return false;
            return Math.Abs(position.X) < CabinHalfWidth(z, y) - margin;
        }

        /// <summary>
        /// The starboard boarding doorway cut cleanly through the flat starboard hull wall, in shuttle axes: 1.20 m wide
        /// (Z from 2.20 to 3.40 m) and 2.20 m tall (Y from -1.40 to +0.80 m), with its sill 0.10 m above the aft cabin floor.
        /// </summary>
        public static BoundingBox Hatch { get; } = new(new(4.85f, -1.40f, 2.20f), new(5.95f, 0.80f, 3.40f));

        /// <summary>Legacy doorway cut for <see cref="Loft"/> (keeps <c>LoftedHullTests</c> intact).</summary>
        private static BoundingBox LegacyDoorway { get; } =
            new(new(Cabin.X, -Cabin.Y + 0.085f, 1.4f), new(Tram.X, -Cabin.Y + 2.2f, 2.6f));

        /// <summary>
        /// The walkway through the starboard doorway and across the retractable boarding bridge, in shuttle axes, as one
        /// sharp prism: starts as a 7-degree threshold ramp inside the aft cabin (from X = 4.65 m at floor level up to
        /// X = 5.45 m at the sill Y = -1.40 m), runs flush through the doorway to <see cref="Hull"/>'s starboard face, and
        /// extends as a 3 cm lip over <see cref="BoardingStair.Gap"/> and <see cref="BoardingStair.LipOverlap"/> with 5 mm
        /// of air above the stair landing.
        /// </summary>
        public static Vector3[] Walkway { get; } =
        [
            .. new[] { Hatch.Min.Z - 0.3f, Hatch.Max.Z + 0.3f }.SelectMany(z =>
                new Vector3[]
                {
                    new(4.65f, AftFloorY, z),
                    new(Hull.Max.X, AftFloorY, z),
                    new(Hull.Max.X + BoardingStair.Gap + BoardingStair.LipOverlap, Hatch.Min.Y - 0.03f, z),
                    new(Hull.Max.X + BoardingStair.Gap + BoardingStair.LipOverlap, Hatch.Min.Y, z),
                    new(5.45f, Hatch.Min.Y, z),
                    new(4.65f, AftFloorY + 0.001f, z),
                }
            ),
        ];

        /// <summary>
        /// Open aerospace tubular safety handrails (top bar, mid bar and three vertical stanchions per side) flanking the
        /// retractable boarding bridge outside the starboard doorway, from the outer hull sill to the stair landing edge.
        /// Drawn only when parked.
        /// </summary>
        public static IReadOnlyList<BoundingBox> Porch { get; } = BuildBridgeHandrails();

        private static BoundingBox[] BuildBridgeHandrails()
        {
            const float xStart = 5.88f;
            float xEnd = Hull.Max.X + BoardingStair.Gap;
            float xMid = (xStart + xEnd) * 0.5f;
            float y0 = Hatch.Min.Y;
            var boxes = new List<BoundingBox>();
            foreach (float z in new[] { Hatch.Min.Z - 0.22f, Hatch.Max.Z + 0.22f })
            {
                // Top and mid tubular rail bars (4 cm square).
                boxes.Add(new(new(xStart, y0 + 0.96f, z - 0.02f), new(xEnd, y0 + 1.00f, z + 0.02f)));
                boxes.Add(new(new(xStart, y0 + 0.48f, z - 0.018f), new(xEnd, y0 + 0.516f, z + 0.018f)));
                // Low toe-kick curb along the deck edge (8 cm tall).
                boxes.Add(new(new(xStart, y0, z - 0.025f), new(xEnd, y0 + 0.08f, z + 0.025f)));
                // Three slim vertical stanchions.
                foreach (float xp in new[] { xStart + 0.03f, xMid, xEnd - 0.03f })
                    boxes.Add(new(new(xp - 0.02f, y0, z - 0.02f), new(xp + 0.02f, y0 + 0.98f, z + 0.02f)));
            }

            return [.. boxes];
        }

        /// <summary>
        /// The pilot's command seat collider on the raised cockpit floor, in shuttle axes: 0.60 m wide, 0.50 m tall and
        /// 0.60 m deep, centered on the centerline directly facing <c>pane-nose</c>.
        /// </summary>
        public static BoundingBox Seat { get; } =
            new(new(-0.30f, CockpitFloorY, -11.20f), new(0.30f, CockpitFloorY + 0.50f, -10.60f));

        // Legacy LoftedHull tables, preserved for DemoShuttle, MirrorBalls and LoftedHullTests.
        private static readonly Vector2[] Full =
        [
            new(-2.8f, -2),
            new(2.8f, -2),
            new(3, -1.8f),
            new(3, -1.2f),
            new(3, 1.2f),
            new(2.2f, 2),
            new(-2.2f, 2),
            new(-3, 1.2f),
            new(-3, -1.2f),
            new(-3, -1.8f),
        ];

        private static readonly Vector2[] NoseFront =
        [
            new(-2.2f, -2),
            new(2.2f, -2),
            new(2.4f, -1.8f),
            new(2.4f, -1.2f),
            new(2.4f, -0.2f),
            new(1.6f, 0.6f),
            new(-1.6f, 0.6f),
            new(-2.4f, -0.2f),
            new(-2.4f, -1.2f),
            new(-2.4f, -1.8f),
        ];

        private static readonly float[] Ribs = [-8, -6.5f, -5, -3, -1, 1, 3, 5, 7];

        private static readonly (int Edge, string Name)[] GlassEdges =
        [
            (3, "starboard"),
            (4, "starboard-bevel"),
            (5, "roof"),
            (6, "port-bevel"),
            (7, "port"),
        ];

        /// <summary>The legacy lofted tram hull, retained for <c>demo-shuttle</c>, <c>mirror-balls</c> and <see cref="LoftedHull"/> tests.</summary>
        public static LoftedHull Loft { get; } =
            Enumerable
                .Range(1, Ribs.Length - 1)
                .SelectMany(bay =>
                    GlassEdges
                        .Where(glass => !(glass.Edge == 3 && bay == HatchBay))
                        .Select(glass => (glass.Edge, glass.Name, Bay: bay))
                )
                .Aggregate(
                    new LoftedHull([new(-Tram.Z, NoseFront), new(NoseStart, Full), new(Tram.Z, Full)], Wall, Ribs)
                        .Door(door: LegacyDoorway, bay: HatchBay, fromCorner: 2, toCorner: 4)
                        .TailPocket(half: 0.7f, depth: 0.6f, housing: Wall)
                        .Surrounds(rib: 0.1f, fold: 0.06f, solid: 0.1f)
                        .NoseGlass("pane-nose", fromCorner: 3, toCorner: 8),
                    (hull, glass) => hull.Glass($"pane-{glass.Name}-{glass.Bay}", glass.Edge, glass.Bay)
                );

        /// <summary>The hull in its parent frame: the box of <see cref="Hull"/>, with the default convex radius.</summary>
        public static ShapeDesc HullShape { get; } =
            new CompoundShapeDesc([new(new BoxShapeDesc(Hull.Extents), new(Hull.Center, Quaternion.Identity))]);

        /// <summary>
        /// The 21 real window panes of the fitted Lunar Shuttle (5 cockpit panes plus 8 port and 8 starboard cabin shoulder
        /// windows), measured directly from Pioneer's <c>lunar_shuttle_hi.dae</c> at <see cref="ArtScale"/> and
        /// <see cref="ArtPlace"/>.
        /// </summary>
        public static IReadOnlyList<HullPane> Glass { get; } = BuildFittedGlass();

        /// <summary>
        /// The fitted cabin interior physics shape in the shuttle's own frame: walkable aft floor, 10-degree center ramp,
        /// raised cockpit floor, solid cabin enclosure with an open starboard doorway at <see cref="Hatch"/>, solid convex
        /// slabs across all 21 window panes (so physics keeps glass solid), the pilot <see cref="Seat"/>, and the smooth
        /// <see cref="Walkway"/> prism. Every piece uses convex radius 0 for clean edge removal.
        /// </summary>
        public static ShapeDesc InteriorShape { get; } = BuildFittedInteriorShape();

        /// <summary>The hull's principal moments of inertia for <see cref="HullShape"/> with <see cref="Mass"/>.</summary>
        public static PrincipalInertia Inertia { get; } = BoxInertia(Mass, 2 * Hull.Extents);

        private static PrincipalInertia BoxInertia(float mass, Vector3 size)
        {
            Vector3 square = size * size;
            return new(
                mass / 12 * new Vector3(square.Y + square.Z, square.X + square.Z, square.X + square.Y),
                Quaternion.Identity
            );
        }

        /// <summary>The main engine's nozzle ring.</summary>
        public static RevolvedSolid Nozzle { get; } =
            RevolvedSolid.Ring(innerRadius: 0.5f, outerRadius: 0.6f, halfWidth: 0.25f, segments: 32);

        /// <summary>The nozzle's middle along the shuttle's Z, in meters.</summary>
        public static double NozzleZ { get; } = Hull.Max.Z - 0.05 - 0.25;

        /// <summary>
        /// Where a person 1.8 m tall stands at the pilot's command seat, in shuttle axes: centered behind <see cref="Seat"/>
        /// on the cockpit floor, facing <c>pane-nose</c>.
        /// </summary>
        public static Double3 SeatStand { get; } = new(0, CockpitFloorY + 0.9, Seat.Max.Z + 0.45);

        /// <summary>
        /// Where a person 1.8 m tall stands at the outer end of the boarding bridge, in shuttle axes, 0.6 m inside the hull's
        /// starboard face, aligned with <see cref="Hatch"/>.
        /// </summary>
        public static Double3 OnPorch { get; } = new(Hull.Max.X - 0.6, Hatch.Min.Y + 0.9, Hatch.Center.Z);

        /// <summary>
        /// Where a person 1.8 m tall stands inside the aft cabin just inward of the doorway threshold ramp, in shuttle axes.
        /// </summary>
        public static Double3 InsideHatch { get; } = new(3.95, AftFloorY + 0.9, Hatch.Center.Z);

        /// <summary>
        /// Mid-cabin aisle waypoint at the foot of the center ramp, in shuttle axes, for smooth walking between the aft
        /// doorway and the raised cockpit.
        /// </summary>
        public static Double3 AftAisle { get; } = new(0, AftFloorY + 0.9, Hatch.Center.Z);

        /// <summary>Waypoint at the bottom of the 10-degree center ramp, in shuttle axes.</summary>
        public static Double3 RampFoot { get; } = new(0, AftFloorY + 0.9, RampStartZ);

        /// <summary>Waypoint at the top of the 10-degree center ramp on the cockpit floor, in shuttle axes.</summary>
        public static Double3 RampTop { get; } = new(0, CockpitFloorY + 0.9, RampEndZ);

        /// <summary>
        /// Four warm cabin wall lamps mounted on interior structural ribs at Z = -5.0 m (forward ramp ribs) and Z = +0.2 m
        /// (aft cabin ribs), at Y = 1.25 m on the solid lower walls cleanly below all 21 window openings.
        /// </summary>
        public static IReadOnlyList<(string Id, BoundingBox Panel)> Lamps { get; } =
        [
            .. new[] { (Name: "port", Side: -1f), (Name: "starboard", Side: 1f) }.SelectMany(wall =>
                new[] { (Name: "fore", Z: -5.0f), (Name: "aft", Z: 0.2f) }.Select(place =>
                {
                    float wallX = CabinHalfWidth(place.Z, 1.25f) - 0.08f;
                    var center = new Vector3(wall.Side * (wallX - LampHalf.X), 1.25f, place.Z);
                    return (
                        Id: $"lamp-{wall.Name}-{place.Name}",
                        Panel: new BoundingBox(center - LampHalf, center + LampHalf)
                    );
                })
            ),
        ];

        private static Vector3 LampHalf => new(0.025f, 0.25f, 0.075f);

        private static RectLightData LampFace(float side) =>
            PanelLight(
                offset: new(-side * LampHalf.X, 0, 0),
                front: new(-side, 0, 0),
                across: Vector3.UnitZ,
                width: 2 * LampHalf.Z,
                height: 2 * LampHalf.Y,
                power: CabinLampPower
            );

        /// <summary>
        /// The pose of <c>sill-camera</c> in shuttle axes: just outside the aft doorway jamb at ankle height looking forward
        /// across the doorway sill and boarding bridge lip.
        /// </summary>
        public static DPose SillCamera { get; } =
            new(
                new(6.18, Hatch.Min.Y + 0.42, Hatch.Max.Z + 0.12),
                AttitudeAutopilot.Facing(new Double3(-0.12, -0.32, -1), Double3.UnitY)
            );

        /// <summary>The radii of the bubble, in meters, in the shuttle's axes.</summary>
        public static Vector3 Bubble { get; } = (Vector3.Max(-Hull.Min, Hull.Max) + new Vector3(2)) * MathF.Sqrt(3);

        /// <summary>The shuttle's flight autopilot layers: ascent, rendezvous and docking.</summary>
        public static IComponent[] FlightControls(double climbHeight, LaunchPlane plane, Entity station) =>
            [
                new AscentAutopilot(
                    maxThrust: MainEngineThrust,
                    orbitRadius: ParkingOrbitRadius,
                    climbHeight: climbHeight,
                    plane: plane
                ),
                new RendezvousAutopilot(station, RendezvousPlan.Demo, MainEngineThrust),
                new DockingAutopilot(station),
            ];

        /// <summary>Adds attitude control and reaction jet actuators to <paramref name="hull"/>.</summary>
        public static ShuttleJetControl FitActuators(World world, Entity hull)
        {
            world.AddComponent(hull, new AttitudeAutopilot(Inertia));
            return FitJets(world, hull);
        }

        /// <summary>Spawns the shuttle's main engine and translation jets as parts of <paramref name="hull"/>.</summary>
        public static ShuttleJetControl FitJets(World world, Entity hull) =>
            ShuttleJetControl.Fit(
                world: world,
                hull: hull,
                mainThrust: MainEngineThrust,
                inertia: Inertia,
                translationThrust: TranslationThrust
            );

        /// <summary>Where the nozzle lies on the thrust line through <paramref name="centerOfMass"/>.</summary>
        public static DPose NozzlePose(Double3 centerOfMass) =>
            new(new(centerOfMass.X, centerOfMass.Y, NozzleZ), DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2));

        /// <summary>
        /// Spawns the cabin contents of <paramref name="hull"/> in its own frame: the <c>seat</c> entity (drawn only in
        /// gray-box mode when <paramref name="hasArt"/> is false, since the fitted model carries the sculpted command chair),
        /// the 21 clear dielectric panes of <see cref="Glass"/>, the four cabin <see cref="Lamps"/> and the tail nozzle.
        /// </summary>
        public static FrameHandle SpawnCabin(
            World world,
            Entity hull,
            SceneInstance scene,
            IRenderResources resources,
            bool hasArt = false
        )
        {
            FrameHandle cabin = world.GetOwnedFrame(hull);
            Vector3 seatHalf = Seat.Extents;
            scene.Add(
                "seat",
                world.Spawn(
                    new()
                    {
                        Frame = cabin,
                        Pose = new(Seat.Center, DQuaternion.Identity),
                        Render = hasArt
                            ? null
                            : Drawn(
                                resources: resources,
                                mesh: Primitives.Box(seatHalf),
                                color: new(x: 0.15f, y: 0.25f, z: 0.6f, w: 1),
                                boundingRadius: seatHalf.Length()
                            ),
                    }
                )
            );
            Panes(world: world, cabin: cabin, scene: scene, resources: resources);
            SpawnLamps(world: world, cabin: cabin, scene: scene, resources: resources);
            SpawnNozzle(world: world, cabin: cabin, scene: scene, resources: resources);
            return cabin;
        }

        /// <summary>Spawns the nozzle in <paramref name="cabin"/> at <see cref="NozzlePose"/>.</summary>
        public static Entity SpawnNozzle(
            World world,
            FrameHandle cabin,
            SceneInstance scene,
            IRenderResources resources,
            string prefix = ""
        )
        {
            if (world.Snapshot.BodyOf(cabin.Entity) is not { } hull)
                throw new ArgumentException(
                    $"The frame {cabin} has no owner with a body, so it has no hull for the nozzle.",
                    nameof(cabin)
                );
            Entity nozzle = world.Spawn(
                new()
                {
                    Frame = cabin,
                    Pose = NozzlePose(hull.CenterOfMass),
                    Render = Drawn(
                        resources: resources,
                        mesh: Nozzle.Mesh(),
                        color: new(x: 0.18f, y: 0.18f, z: 0.2f, w: 1),
                        boundingRadius: 0.7f
                    ),
                }
            );
            scene.Add(prefix.Length == 0 ? "nozzle" : $"{prefix}-nozzle", nozzle);
            return nozzle;
        }

        /// <summary>The hull's body with <paramref name="motion"/>: <see cref="HullShape"/>, <see cref="Mass"/> and <see cref="InteriorShape"/>.</summary>
        public static BodyDesc Body(BodyMotion motion) =>
            new(
                Shape: HullShape,
                Motion: motion,
                Mass: Mass,
                LinearDamping: 0,
                AngularDamping: 0,
                InteriorShape: InteriorShape
            );

        /// <summary>The drawn hull of the legacy gray-box loft (<see cref="Loft"/>), used by <c>demo-shuttle</c> and <c>mirror-balls</c>.</summary>
        public static RenderData DrawnHull(IRenderResources resources) => Tramway(resources, Loft.Mesh());

        /// <summary>The legacy loft interior mesh.</summary>
        public static RenderData DrawnInterior(IRenderResources resources) => Tramway(resources, Loft.InteriorMesh());

        private static RenderData Tramway(IRenderResources resources, MeshData mesh)
        {
            RenderData render = Drawn(
                resources: resources,
                mesh: mesh,
                color: new(x: 0.85f, y: 0.85f, z: 0.88f, w: 1),
                boundingRadius: MathF.Ceiling(Tram.Length())
            );
            render.Flags |= RenderFlags.NoImpostor;
            return render;
        }

        /// <summary>
        /// Spawns the shuttle's art (<c>shuttle-art</c>, plus optional stowable <c>shuttle-hatch-door</c>) and the
        /// retractable boarding bridge (<c>porch</c>) that deploys when parked and retracts in flight.
        /// </summary>
        public static void SpawnArt(
            World world,
            Entity hull,
            SceneInstance scene,
            IRenderResources resources,
            Assets.StoredModel? art,
            string prefix = "",
            Assets.StoredModel? hatchDoor = null
        )
        {
            string Id(string name) => prefix.Length == 0 ? name : $"{prefix}-{name}";
            FrameHandle cabin = world.GetOwnedFrame(hull);
            bool parked = world.Frames.HullMotion(cabin) == BodyMotion.Static;

            BoxMesh rails = new();
            foreach (BoundingBox box in Porch)
                rails.Box(box);
            // Exterior bridge deck slab from the outer hull sill edge (X = 5.93 m, clear of the interior amber sill plate)
            // out across the stair lip (X = 7.52 m).
            Vector3[] bridgeDeck =
            [
                .. new[] { Hatch.Min.Z - 0.25f, Hatch.Max.Z + 0.25f }.SelectMany(z =>
                    new Vector3[]
                    {
                        new(5.93f, Hatch.Min.Y - 0.08f, z),
                        new(Hull.Max.X, Hatch.Min.Y - 0.08f, z),
                        new(Hull.Max.X + BoardingStair.Gap + BoardingStair.LipOverlap, Hatch.Min.Y - 0.03f, z),
                        new(Hull.Max.X + BoardingStair.Gap + BoardingStair.LipOverlap, Hatch.Min.Y - 0.002f, z),
                        new(5.93f, Hatch.Min.Y - 0.002f, z),
                    }
                ),
            ];
            scene.Add(
                Id("porch"),
                world.Spawn(
                    new()
                    {
                        Frame = cabin,
                        Pose = DPose.Identity,
                        Render = Drawn(
                            resources: resources,
                            mesh: Merged(rails.Build(), Prism(bridgeDeck)),
                            color: new(x: 0.34f, y: 0.36f, z: 0.39f, w: 1),
                            boundingRadius: Hull.Max.X + Hatch.Max.Z + 1
                        ) with
                        {
                            Flags = parked
                                ? RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor
                                : RenderFlags.NoImpostor,
                        },
                        Components = [new ShuttleHatchControl(cabin, showWhenDynamic: false)],
                    }
                )
            );
            if (art is null)
                return;
            scene.Add(
                Id("shuttle-art"),
                world.Spawn(
                    new()
                    {
                        AttachTo = hull,
                        AttachOffset = new(ArtPlace, Quaternion.Identity),
                        Render = new RenderData
                        {
                            Model = art.Id,
                            Scale = new(ArtScale),
                            BoundingRadius = art.BoundingRadius * ArtScale,
                            Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
                        },
                    }
                )
            );
            if (hatchDoor is null)
                return;
            scene.Add(
                Id("shuttle-hatch-door"),
                world.Spawn(
                    new()
                    {
                        AttachTo = hull,
                        AttachOffset = new(ArtPlace, Quaternion.Identity),
                        Render = new RenderData
                        {
                            Model = hatchDoor.Id,
                            Scale = new(ArtScale),
                            BoundingRadius = hatchDoor.BoundingRadius * ArtScale,
                            Flags = parked
                                ? RenderFlags.NoImpostor
                                : RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
                        },
                        Components = [new ShuttleHatchControl(cabin, showWhenDynamic: true)],
                    }
                )
            );
        }

        /// <summary>
        /// The hull's render row: with <paramref name="art"/>, returns null so only the fitted <c>shuttle-art</c> (which
        /// draws both the exterior hull and the fitted cabin interior) is rendered; without art, draws the gray-box hull.
        /// </summary>
        public static RenderData? HullRender(IRenderResources resources, Assets.StoredModel? art) =>
            art is null ? DrawnHull(resources) : null;

        /// <summary>
        /// Places each pane at its window opening in the shuttle frame. Each pane has its own center for glass sorting.
        /// The coating reflects light. Zero diffuse opacity lets the pilot see through the pane.
        /// </summary>
        public static void Panes(
            World world,
            FrameHandle cabin,
            SceneInstance scene,
            IRenderResources resources,
            string prefix = ""
        )
        {
            MaterialId glass = resources.CreateMaterial(
                new(
                    BaseColor: new(x: 1, y: 1, z: 1, w: 0),
                    Roughness: GlassRoughness,
                    Reflectance: AntiReflective,
                    Alpha: new GlassAlpha()
                )
            );
            foreach (HullPane pane in Glass)
            {
                Entity entity = world.Spawn(
                    new()
                    {
                        Frame = cabin,
                        Pose = new(pane.Center, DQuaternion.Identity),
                        Render = new RenderData
                        {
                            Mesh = resources.CreateMesh(pane.Mesh),
                            Material = glass,
                            BoundingRadius = pane.Mesh.Vertices.Max(vertex => vertex.Position.Length()),
                            Flags = RenderFlags.Visible,
                        },
                    }
                );
                scene.Add(prefix.Length == 0 ? pane.Id : $"{prefix}-{pane.Id}", entity);
            }
        }

        /// <summary>Spawns the four cabin wall <see cref="Lamps"/> in <paramref name="cabin"/>.</summary>
        public static void SpawnLamps(World world, FrameHandle cabin, SceneInstance scene, IRenderResources resources)
        {
            MeshId mesh = resources.CreateMesh(Primitives.Box(LampHalf));
            MaterialId material = LampPanelMaterial(resources: resources, light: LampFace(side: 1));
            foreach ((string id, BoundingBox panel) in Lamps)
                scene.Add(
                    id,
                    world.Spawn(
                        new()
                        {
                            Frame = cabin,
                            Pose = new(panel.Center, DQuaternion.Identity),
                            Render = new RenderData
                            {
                                Mesh = mesh,
                                Material = material,
                                BoundingRadius = LampHalf.Length(),
                                Flags = RenderFlags.Visible,
                                Light = LampFace(side: MathF.Sign(panel.Center.X)),
                            },
                        }
                    )
                );
        }

        /// <summary>Spawns the shuttle parked static at <paramref name="pose"/> in <paramref name="frame"/>.</summary>
        public static Entity Park(
            World world,
            FrameHandle frame,
            DPose pose,
            RenderData? render = null,
            IReadOnlyList<IComponent>? components = null
        ) =>
            world.Spawn(
                new()
                {
                    Frame = frame,
                    Pose = pose,
                    Body = Body(BodyMotion.Static),
                    AsFrame = new(new(Bubble), Medium: Air.Filling(CabinAir)),
                    Render = render,
                    Components = components ?? [],
                }
            );

        private static HullPane[] BuildFittedGlass()
        {
            // Exact 21 window polygons measured from Pioneer's lunar_shuttle_hi.dae in shuttle axes (ArtScale=1.6, ArtPlace=(0,0,1)).
            (string Id, Vector3 Normal, float Depth, Vector3[] Outer)[] specs =
            [
                (
                    "pane-nose",
                    new(0.0f, 0.7841f, -0.6207f),
                    0.14f,
                    [
                        new(-1.2644f, -0.0721f, -13.6911f),
                        new(-0.3780f, 1.9621f, -11.1262f),
                        new(0.3780f, 1.9621f, -11.1262f),
                        new(1.2644f, -0.0721f, -13.6911f),
                    ]
                ),
                (
                    "pane-cockpit-port-fore",
                    new(-0.8338f, 0.5345f, -0.1379f),
                    0.14f,
                    [
                        new(-1.3458f, 0.2318f, -12.4250f),
                        new(-0.9965f, 1.1000f, -11.2142f),
                        new(-0.5111f, 1.8645f, -11.1437f),
                        new(-1.3066f, 0.0384f, -13.4540f),
                    ]
                ),
                (
                    "pane-cockpit-starboard-fore",
                    new(0.8338f, 0.5345f, -0.1379f),
                    0.14f,
                    [
                        new(0.5111f, 1.8645f, -11.1437f),
                        new(0.9965f, 1.1000f, -11.2142f),
                        new(1.3458f, 0.2318f, -12.4250f),
                        new(1.3066f, 0.0384f, -13.4540f),
                    ]
                ),
                (
                    "pane-cockpit-port-aft",
                    new(-0.8507f, 0.5080f, -0.1351f),
                    0.14f,
                    [
                        new(-0.9754f, 1.1720f, -11.1053f),
                        new(-0.7338f, 1.8046f, -9.9304f),
                        new(-0.4480f, 2.2624f, -10.3259f),
                        new(-0.4746f, 1.9466f, -11.0288f),
                    ]
                ),
                (
                    "pane-cockpit-starboard-aft",
                    new(0.8507f, 0.5080f, -0.1351f),
                    0.14f,
                    [
                        new(0.4480f, 2.2624f, -10.3259f),
                        new(0.7338f, 1.8046f, -9.9304f),
                        new(0.9754f, 1.1720f, -11.1053f),
                        new(0.4746f, 1.9466f, -11.0288f),
                    ]
                ),
                (
                    "pane-port-1",
                    new(-0.6358f, 0.7549f, -0.1611f),
                    0.24f,
                    [
                        new(-2.4164f, 2.1182f, -5.4854f),
                        new(-2.0335f, 2.4387f, -5.4853f),
                        new(-1.8999f, 2.4390f, -6.0206f),
                        new(-2.2788f, 2.1179f, -6.0205f),
                    ]
                ),
                (
                    "pane-port-2",
                    new(-0.6374f, 0.7560f, -0.1487f),
                    0.24f,
                    [
                        new(-2.7373f, 2.1175f, -4.1350f),
                        new(-2.3589f, 2.4390f, -4.1351f),
                        new(-2.2528f, 2.4385f, -4.5792f),
                        new(-2.6361f, 2.1179f, -4.5793f),
                    ]
                ),
                (
                    "pane-port-3",
                    new(-0.6415f, 0.7524f, -0.1497f),
                    0.24f,
                    [
                        new(-3.0403f, 2.1184f, -2.8319f),
                        new(-2.6670f, 2.4391f, -2.8320f),
                        new(-2.5610f, 2.4387f, -3.2761f),
                        new(-2.9390f, 2.1188f, -3.2762f),
                    ]
                ),
                (
                    "pane-port-4",
                    new(-0.6456f, 0.7486f, -0.1506f),
                    0.24f,
                    [
                        new(-3.3392f, 2.1189f, -1.5287f),
                        new(-2.9712f, 2.4388f, -1.5288f),
                        new(-2.8651f, 2.4384f, -1.9729f),
                        new(-3.2380f, 2.1193f, -1.9730f),
                    ]
                ),
                (
                    "pane-port-5",
                    new(-0.6498f, 0.7449f, -0.1516f),
                    0.24f,
                    [
                        new(-3.6422f, 2.1200f, -0.2256f),
                        new(-3.2793f, 2.4392f, -0.2257f),
                        new(-3.1732f, 2.4388f, -0.6699f),
                        new(-3.5410f, 2.1204f, -0.6699f),
                    ]
                ),
                (
                    "pane-port-6",
                    new(-0.6539f, 0.7410f, -0.1526f),
                    0.24f,
                    [
                        new(-3.9412f, 2.1205f, 1.0776f),
                        new(-3.5834f, 2.4389f, 1.0775f),
                        new(-3.4773f, 2.4385f, 0.6333f),
                        new(-3.8400f, 2.1209f, 0.6332f),
                    ]
                ),
                (
                    "pane-port-7",
                    new(-0.6581f, 0.7371f, -0.1536f),
                    0.24f,
                    [
                        new(-4.2422f, 2.1213f, 2.3807f),
                        new(-3.8895f, 2.4389f, 2.3807f),
                        new(-3.7834f, 2.4385f, 1.9364f),
                        new(-4.1410f, 2.1218f, 1.9364f),
                    ]
                ),
                (
                    "pane-port-8",
                    new(-0.6624f, 0.7331f, -0.1545f),
                    0.24f,
                    [
                        new(-4.5432f, 2.1222f, 3.6839f),
                        new(-4.1956f, 2.4389f, 3.6838f),
                        new(-4.0895f, 2.4385f, 3.2396f),
                        new(-4.4420f, 2.1226f, 3.2395f),
                    ]
                ),
                (
                    "pane-starboard-1",
                    new(0.6358f, 0.7549f, -0.1611f),
                    0.24f,
                    [
                        new(1.8999f, 2.4390f, -6.0206f),
                        new(2.0335f, 2.4387f, -5.4853f),
                        new(2.4164f, 2.1182f, -5.4854f),
                        new(2.2788f, 2.1179f, -6.0205f),
                    ]
                ),
                (
                    "pane-starboard-2",
                    new(0.6374f, 0.7560f, -0.1487f),
                    0.24f,
                    [
                        new(2.2528f, 2.4385f, -4.5792f),
                        new(2.3589f, 2.4390f, -4.1351f),
                        new(2.7373f, 2.1175f, -4.1350f),
                        new(2.6361f, 2.1179f, -4.5793f),
                    ]
                ),
                (
                    "pane-starboard-3",
                    new(0.6415f, 0.7524f, -0.1497f),
                    0.24f,
                    [
                        new(2.5610f, 2.4387f, -3.2761f),
                        new(2.6670f, 2.4391f, -2.8320f),
                        new(3.0403f, 2.1184f, -2.8319f),
                        new(2.9390f, 2.1188f, -3.2762f),
                    ]
                ),
                (
                    "pane-starboard-4",
                    new(0.6456f, 0.7486f, -0.1506f),
                    0.24f,
                    [
                        new(2.8651f, 2.4384f, -1.9729f),
                        new(2.9712f, 2.4388f, -1.5288f),
                        new(3.3392f, 2.1189f, -1.5287f),
                        new(3.2380f, 2.1193f, -1.9730f),
                    ]
                ),
                (
                    "pane-starboard-5",
                    new(0.6498f, 0.7449f, -0.1516f),
                    0.24f,
                    [
                        new(3.1732f, 2.4388f, -0.6699f),
                        new(3.2793f, 2.4392f, -0.2257f),
                        new(3.6422f, 2.1200f, -0.2256f),
                        new(3.5410f, 2.1204f, -0.6699f),
                    ]
                ),
                (
                    "pane-starboard-6",
                    new(0.6539f, 0.7410f, -0.1526f),
                    0.24f,
                    [
                        new(3.4773f, 2.4385f, 0.6333f),
                        new(3.5834f, 2.4389f, 1.0775f),
                        new(3.9412f, 2.1205f, 1.0776f),
                        new(3.8400f, 2.1209f, 0.6332f),
                    ]
                ),
                (
                    "pane-starboard-7",
                    new(0.6581f, 0.7371f, -0.1536f),
                    0.24f,
                    [
                        new(3.7834f, 2.4385f, 1.9364f),
                        new(3.8895f, 2.4389f, 2.3807f),
                        new(4.2422f, 2.1213f, 2.3807f),
                        new(4.1410f, 2.1218f, 1.9364f),
                    ]
                ),
                (
                    "pane-starboard-8",
                    new(0.6624f, 0.7331f, -0.1545f),
                    0.24f,
                    [
                        new(4.0895f, 2.4385f, 3.2396f),
                        new(4.1956f, 2.4389f, 3.6838f),
                        new(4.5432f, 2.1222f, 3.6839f),
                        new(4.4420f, 2.1226f, 3.2395f),
                    ]
                ),
            ];
            return
            [
                .. specs.Select(spec =>
                    BuildPane(id: spec.Id, rawOuter: spec.Outer, outward: spec.Normal, revealDepth: spec.Depth)
                ),
            ];
        }

        private static HullPane BuildPane(string id, Vector3[] rawOuter, Vector3 outward, float revealDepth)
        {
            Vector3 normal = Vector3.Normalize(outward);
            Vector3 mid = Vector3.Zero;
            foreach (Vector3 p in rawOuter)
                mid += p;
            mid /= rawOuter.Length;

            // Project onto exact plane (mid, normal) and ensure counterclockwise winding viewed from +normal.
            Vector3[] outer = [.. rawOuter.Select(p => p - normal * Vector3.Dot(p - mid, normal))];
            Vector3 wound = Vector3.Cross(outer[1] - outer[0], outer[2] - outer[0]);
            if (Vector3.Dot(wound, normal) < 0)
                Array.Reverse(outer);

            Vector3[] inner = [.. outer.Select(p => p - normal * revealDepth)];
            // Glass slab sits recessed 2.5 cm inside the outer bevel, 2 cm thick, grown 1 cm into the frame to seal edges.
            Vector3 center = mid - normal * 0.025f;
            const float halfThick = 0.01f;
            const float growth = 0.01f;
            Vector3[] front =
            [
                .. outer.Select(p =>
                {
                    Vector3 radial = p - mid;
                    float len = radial.Length();
                    Vector3 grown = len > 1e-6f ? p + radial * (growth / len) : p;
                    return grown - center - normal * (0.025f - halfThick);
                }),
            ];
            Vector3[] back = [.. front.Select(p => p - normal * (2 * halfThick))];

            var verts = new List<Vertex>();
            var indices = new List<uint>();

            void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
            {
                var tangent = new Vector4(Vector3.Normalize(b - a), 1);
                uint start = (uint)verts.Count;
                verts.Add(
                    new Vertex
                    {
                        Position = a,
                        Normal = n,
                        Tangent = tangent,
                        TexCoord = Vector2.Zero,
                    }
                );
                verts.Add(
                    new Vertex
                    {
                        Position = b,
                        Normal = n,
                        Tangent = tangent,
                        TexCoord = Vector2.Zero,
                    }
                );
                verts.Add(
                    new Vertex
                    {
                        Position = c,
                        Normal = n,
                        Tangent = tangent,
                        TexCoord = Vector2.Zero,
                    }
                );
                verts.Add(
                    new Vertex
                    {
                        Position = d,
                        Normal = n,
                        Tangent = tangent,
                        TexCoord = Vector2.Zero,
                    }
                );
                indices.Add(start);
                indices.Add(start + 1);
                indices.Add(start + 2);
                indices.Add(start);
                indices.Add(start + 2);
                indices.Add(start + 3);
            }

            AddQuad(a: front[0], b: front[1], c: front[2], d: front[3], n: normal);
            AddQuad(a: back[3], b: back[2], c: back[1], d: back[0], n: -normal);
            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                Vector3 edgeNormal = Vector3.Normalize(Vector3.Cross(front[next] - front[i], normal));
                AddQuad(a: front[i], b: back[i], c: back[next], d: front[next], n: edgeNormal);
            }

            return new(Id: id, Center: center, Outer: outer, Inner: inner, Mesh: new([.. verts], [.. indices]));
        }

        private static CompoundShapeDesc BuildFittedInteriorShape()
        {
            var children = new List<CompoundChild>();

            void AddBox(Vector3 min, Vector3 max)
            {
                var box = new BoundingBox(min, max);
                children.Add(new(new BoxShapeDesc(box.Extents, ConvexRadius: 0), new(box.Center, Quaternion.Identity)));
            }

            // 1. Walkable aft cabin floor (Y = -1.50, Z = -3.0..4.0), cockpit floor (Y = -0.50, Z = -13.6..-8.7), and 10 deg ramp.
            AddBox(new(-5.70f, AftFloorY - 0.30f, RampStartZ), new(5.70f, AftFloorY, AftBulkheadZ));
            AddBox(new(-3.20f, CockpitFloorY - 0.30f, -13.60f), new(3.20f, CockpitFloorY, RampEndZ));
            Vector3[] rampHull =
            [
                new(-4.50f, AftFloorY - 0.30f, RampStartZ),
                new(4.50f, AftFloorY - 0.30f, RampStartZ),
                new(4.50f, AftFloorY, RampStartZ),
                new(-4.50f, AftFloorY, RampStartZ),
                new(-3.20f, AftFloorY - 0.30f, RampEndZ),
                new(3.20f, AftFloorY - 0.30f, RampEndZ),
                new(3.20f, CockpitFloorY, RampEndZ),
                new(-3.20f, CockpitFloorY, RampEndZ),
            ];
            children.Add(new(new ConvexHullShapeDesc(rampHull, ConvexRadius: 0), Pose.Identity));

            // 2. Aft engine bulkhead (Z = 4.0..4.3) and flat cabin ceiling (Y = 3.10..3.40, Z = -7.0..4.0).
            AddBox(new(-5.70f, AftFloorY - 0.30f, AftBulkheadZ), new(5.70f, CeilingY + 0.30f, AftBulkheadZ + 0.30f));
            AddBox(new(-4.50f, CeilingY, -7.0f), new(4.50f, CeilingY + 0.30f, AftBulkheadZ + 0.30f));

            // 3. Sloped cockpit ceiling behind the front windshield (Z = -11.05..-7.0).
            Vector3[] cockpitRoof =
            [
                new(-1.60f, 1.98f, -11.05f),
                new(1.60f, 1.98f, -11.05f),
                new(1.60f, 2.28f, -11.05f),
                new(-1.60f, 2.28f, -11.05f),
                new(-2.40f, CeilingY, -7.0f),
                new(2.40f, CeilingY, -7.0f),
                new(2.40f, CeilingY + 0.30f, -7.0f),
                new(-2.40f, CeilingY + 0.30f, -7.0f),
            ];
            children.Add(new(new ConvexHullShapeDesc(cockpitRoof, ConvexRadius: 0), Pose.Identity));

            // 4. Forward nose console & bulkhead below pane-nose (Z = -13.9..-12.15, Y = -0.50..-0.10).
            AddBox(new(-1.50f, CockpitFloorY, -13.90f), new(1.50f, -0.10f, -12.15f));

            // 5. Tapered lower side walls on port (full length) and starboard (open at Hatch Z = [2.20, 3.40], Y = [-1.40, 0.80]).
            void AddWallSlab(float sign, float z0, float z1, float yBot, float yTop)
            {
                float x0Bot = sign * CabinHalfWidth(z0, yBot);
                float x1Bot = sign * CabinHalfWidth(z1, yBot);
                float x0Top = sign * CabinHalfWidth(z0, yTop);
                float x1Top = sign * CabinHalfWidth(z1, yTop);
                float thick = sign * 0.28f;
                Vector3[] pts =
                [
                    new(x0Bot, yBot, z0),
                    new(x1Bot, yBot, z1),
                    new(x1Top, yTop, z1),
                    new(x0Top, yTop, z0),
                    new(x0Bot + thick, yBot, z0),
                    new(x1Bot + thick, yBot, z1),
                    new(x1Top + thick, yTop, z1),
                    new(x0Top + thick, yTop, z0),
                ];
                children.Add(new(new ConvexHullShapeDesc(pts, ConvexRadius: 0), Pose.Identity));
            }

            // Port solid walls from cockpit nose to aft bulkhead.
            AddWallSlab(sign: -1, z0: -13.5f, z1: RampEndZ, yBot: CockpitFloorY, yTop: 0.02f);
            AddWallSlab(sign: -1, z0: RampEndZ, z1: -5.0f, yBot: AftFloorY, yTop: 1.65f);
            AddWallSlab(sign: -1, z0: -5.0f, z1: AftBulkheadZ, yBot: AftFloorY, yTop: 1.65f);
            AddWallSlab(sign: -1, z0: -6.5f, z1: AftBulkheadZ, yBot: 1.65f, yTop: CeilingY);

            // Starboard solid walls with clean doorway gap at Hatch (Z in [2.20, 3.40], Y in [-1.40, 0.80]).
            AddWallSlab(sign: 1, z0: -13.5f, z1: RampEndZ, yBot: CockpitFloorY, yTop: 0.02f);
            AddWallSlab(sign: 1, z0: RampEndZ, z1: -5.0f, yBot: AftFloorY, yTop: 1.65f);
            AddWallSlab(sign: 1, z0: -5.0f, z1: Hatch.Min.Z, yBot: AftFloorY, yTop: 1.65f);
            AddWallSlab(sign: 1, z0: Hatch.Max.Z, z1: AftBulkheadZ, yBot: AftFloorY, yTop: 1.65f);
            AddWallSlab(sign: 1, z0: Hatch.Min.Z, z1: Hatch.Max.Z, yBot: Hatch.Max.Y, yTop: 1.65f);
            AddWallSlab(sign: 1, z0: -6.5f, z1: AftBulkheadZ, yBot: 1.65f, yTop: CeilingY);

            // 6. Solid convex slabs backing all 21 glass panes so physics keeps every window airtight and solid.
            foreach (HullPane pane in Glass)
            {
                Vector3 n = Vector3.Normalize(
                    Vector3.Cross(pane.Outer[1] - pane.Outer[0], pane.Outer[2] - pane.Outer[0])
                );
                Vector3 outerMid = (pane.Outer[0] + pane.Outer[1] + pane.Outer[2] + pane.Outer[3]) * 0.25f;
                Vector3 innerMid = (pane.Inner[0] + pane.Inner[1] + pane.Inner[2] + pane.Inner[3]) * 0.25f;
                if (Vector3.Dot(n, outerMid - innerMid) < 0)
                    n = -n;
                // Slightly expand each pane collider in-plane by 0.15 m so seams between adjacent cockpit panes stay airtight.
                Vector3[] slab = new Vector3[8];
                for (int i = 0; i < 4; i++)
                {
                    Vector3 radial = pane.Inner[i] - innerMid;
                    float len = radial.Length();
                    Vector3 grown = len > 1e-6f ? pane.Inner[i] + radial * (0.15f / len) : pane.Inner[i];
                    slab[i] = grown;
                    slab[i + 4] = grown + n * 0.22f;
                }

                children.Add(new(new ConvexHullShapeDesc(slab, ConvexRadius: 0), Pose.Identity));
            }

            // 7. Pilot command seat and smooth walkway prism through the doorway across the stair lip.
            children.Add(new(new BoxShapeDesc(Seat.Extents, ConvexRadius: 0), new(Seat.Center, Quaternion.Identity)));
            children.Add(new(new ConvexHullShapeDesc(Walkway, ConvexRadius: 0), Pose.Identity));

            return new CompoundShapeDesc([.. children]);
        }
    }
}
