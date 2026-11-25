using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The shuttle of the Blue Danube demo (docs/plans/blue-danube-demo.md, "The shuttle's tram shape and glass canopy" and
    /// "The fit to the art"): a frame owner whose outside is the Lunar Shuttle's model at <see cref="ArtScale"/>, 13.8 m
    /// wide, 9.3 m tall and 29.1 m long, round a cabin in the tram shape of <see cref="Loft"/>, which fits the box of
    /// <see cref="Tram"/>. In its own axes +Y is up, -Z is the nose and +X is starboard.
    /// <list type="bullet">
    /// <item>In its parent frame the hull is the model's box, <see cref="Hull"/> and <see cref="HullShape"/>, whose bottom is
    /// the plane of the model's three wheels. In its own frame it is hollow: the tram's solid in convex pieces, walls 0.2 m
    /// thick round a cabin that is the box of <see cref="Cabin"/> where the walk, the seat, the pen and the float happen. The
    /// <see cref="Hatch"/> leads through the starboard wall into the <see cref="Porch"/>, out through the model's side, over
    /// the <see cref="Walkway"/>, which starts as a ramp on the cabin floor up to the hatch's sill. The <see cref="Seat"/>, a
    /// static box, stands on the cabin floor. All are part of the interior shape, <see cref="InteriorShape"/>, the static body of the frame's own physics
    /// system.</item>
    /// <item>A glass canopy of 40 panes, <see cref="Glass"/>, runs along both sides from 0.7 m above the cabin floor,
    /// over the roof's bevels and the roof, and across the front above the sill. The glass breaks around the hatch. The
    /// panes have no body: the interior shape is solid at the glass. The cabin's <see cref="Lamps"/> hang on the pillars of
    /// the walls.</item>
    /// <item>The hull's box has one density, so physics puts its center of mass in the box's middle, 0.27 m below and 0.48 m
    /// ahead of the tram's, and gives it the principal axes of <see cref="Inertia"/>, the shuttle's own. The demo reads that
    /// center of mass from the engine's <see cref="BodyFacts.CenterOfMass"/>.</item>
    /// <item>The frame has no gravity field of its own. So its contents feel only the opposite of its proper acceleration
    /// (ARCHITECTURE.md, "Gravity"), which the engine derives from the frame's motion: the weight of the ground while the
    /// shuttle stands parked on it, and nothing once the shuttle falls freely in orbit.</item>
    /// <item>Its bubble is the smallest ellipsoid that holds the hull's box grown by 2 m on every side, as the orbit scene's
    /// ship has, so a person who walks up to the hatch is in the shuttle's frame before it reaches the hull.</item>
    /// </list>
    /// </summary>
    public static class Shuttle
    {
        /// <summary>The thickness of the hull's walls, floor and roof, in meters, in the plane of a cross-section.</summary>
        public const float Wall = 0.2f;

        /// <summary>
        /// The mass of the hull, in kilograms. A parked hull does not use it, but it needs one so that a later
        /// <see cref="BodyHandle.SetMotion"/> can make it dynamic.
        /// </summary>
        public const float Mass = 2e4f;

        /// <summary>
        /// The acceleration that the main engine gives the shuttle at full throttle, in m/s²: three standard gravities,
        /// as a rocket sled pushes.
        /// </summary>
        public const float ThrustAcceleration = 3 * 9.81f;

        /// <summary>
        /// The most force of the translation pairs of the <see cref="ShuttleJetControl"/> along each of the hull's axes, in
        /// newtons: 30,000 N, which gives the shuttle's <see cref="Mass"/> 1.5 m/s². The docking's move out needs up to 1.2
        /// m/s² on one of the hull's axes, to hold the hull on its path against the station's spin. The jets are one piece of
        /// hardware, so every law of the flight gets this limit (docs/plans/blue-danube-demo.md, "The translation thrusters' force").
        /// </summary>
        public const float TranslationThrust = 30000;

        /// <summary>
        /// The radius of the circle that the ascent autopilot flies the shuttle to, in meters, in <c>earth</c>, the frame that does not turn:
        /// <see cref="ParkingOrbitAltitude"/> above Earth's mean radius.
        /// </summary>
        public const double ParkingOrbitRadius = EarthRadius + ParkingOrbitAltitude;

        /// <summary>
        /// How high the parking orbit lies above Earth's mean radius, in meters: 200 km, above
        /// <see cref="AtmosphereTop"/> and below the station's 400 km.
        /// </summary>
        public const double ParkingOrbitAltitude = 200e3;

        /// <summary>The force of the main engine at full throttle, in newtons: <see cref="Mass"/> times
        /// <see cref="ThrustAcceleration"/>.</summary>
        public const float MainEngineThrust = Mass * ThrustAcceleration;

        /// <summary>Where the nose's loft ends and the full cross-section starts, in meters along the shuttle's Z.</summary>
        private const float NoseStart = -5;

        /// <summary>The bay of the hatch, from z = 1 m to 3 m, where the starboard upper side is solid round the doorway.</summary>
        private const int HatchBay = 6;

        /// <summary>
        /// The reflectance of the canopy's glass: that of glass with an anti-reflection coating, which reflects about
        /// 0.5 % of the light that meets it head-on (F0 = 0.16 times its square, ARCHITECTURE.md, "Shading"), where the orbit
        /// scene's glass reflects 16 %. The window films look head-on through the glass, and the cabin's lamps lie in front
        /// of it. With 16 %, each lamp's highlight in the glass is a white blob beside the station. At a grazing angle, glass
        /// reflects much more, whatever its coating, so the highlights on the side and roof glass in the cabin views stay.
        /// </summary>
        private const float AntiReflective = 0.18f;

        /// <summary>
        /// The scale of the Lunar Shuttle's model: the art at 1.6 times its size, the smallest scale of 0.05 steps whose
        /// outside, placed at <see cref="ArtPlace"/>, holds the tram's whole outer skin, so that no wall or pane of the cabin
        /// shows through it. At 1.55 the tram's roof edges and nose show.
        /// </summary>
        public const float ArtScale = 1.6f;

        /// <summary>The half extents of the box that the tram fits, in meters, in the shuttle's axes: its walls, canopy and hatch.</summary>
        public static Vector3 Tram { get; } = new(3, 2, 8);

        /// <summary>
        /// The half extents of the cabin's box inside the walls, in meters, in the shuttle's axes. The bevels along the
        /// cabin's four long edges and the nose cut it.
        /// </summary>
        public static Vector3 Cabin { get; } = Tram - new Vector3(Wall);

        /// <summary>
        /// Where the model's origin stands in the shuttle's axes, in meters: 1 m aft of the tram's middle, which puts the
        /// tram's nose in the model's widest part and its tail in the model's engine section.
        /// </summary>
        public static Vector3 ArtPlace { get; } = new(0, 0, 1);

        /// <summary>
        /// The bounds of the Lunar Shuttle's model in its own axes, in meters, as the converter reports them: its nose wheel
        /// and its two main wheels reach its lowest point, 3.063 m below its origin.
        /// </summary>
        public static BoundingBox ArtBounds { get; } = new(new(-4.325f, -3.063f, -10.013f), new(4.325f, 2.731f, 8.166f));

        /// <summary>
        /// The box that the hull fits, in meters, in the shuttle's axes: <see cref="ArtBounds"/> at <see cref="ArtScale"/>
        /// and <see cref="ArtPlace"/>. Its bottom, 4.90 m below the origin, is the plane of the model's wheels, where the
        /// hull rests on a pad or a deck.
        /// </summary>
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
        /// The box of the cabin's air, in meters, in the hull's axes: the cabin, <see cref="Cabin"/> on each side of the
        /// middle. The person's back jet blows its puffs in it, and they slow and stop in that air. The nose's loft closes in
        /// on the box's front corners, where no puff of the cabin reaches.
        /// </summary>
        public static BoundingBox CabinAir { get; } = new(-Cabin, Cabin);

        /// <summary>
        /// The hatch, the doorway through the starboard wall, in the shuttle's axes: 1.2 m wide, 2.1 m tall, centered 2 m
        /// aft of the shuttle's center, with a sill 0.1 m above the cabin floor. A person on the ground below the hull
        /// steps 0.3 m up onto the sill, within the 0.4 m that a character climbs as a stair.
        /// </summary>
        public static BoundingBox Hatch { get; } =
            new(new(Cabin.X, -Cabin.Y + 0.1f, 1.4f), new(Tram.X, -Cabin.Y + 2.2f, 2.6f));

        /// <summary>
        /// The doorway that the loft cuts through the tram's starboard wall, in the shuttle's axes: the <see cref="Hatch"/>,
        /// 1.5 cm deeper, so that the wall's piece under it lies that far under the <see cref="Walkway"/>'s top, as near
        /// the bevel's inner skin as the loft allows. Flush with the walkway's top, the edge of that piece lifted a person
        /// who walked onto it by 1.3 cm, which edge removal does not stop.
        /// </summary>
        private static BoundingBox Doorway { get; } = new(Hatch.Min - new Vector3(0, 0.015f, 0), Hatch.Max);

        /// <summary>
        /// The walkway, the floor of the way out through the hatch, in the shuttle's axes, as one sharp prism: one end's six
        /// corners round its cross-section, then the other end's in the same order, the porch's width apart. Inside the
        /// cabin it is the threshold, a ramp 0.8 m long at 7 degrees from the cabin floor up to the sill's top, so that a
        /// person walks onto the sill rather than climbing its 0.1 m step, which throws a person up in a weak gravity. It
        /// runs on flush with the sill through the hatch and the porch to the hull's starboard face, and on as the lip, 3 cm
        /// thick, over the gap and the edge of a boarding stair's landing on the pad or the deck (see
        /// <see cref="BoardingStair"/>). The lip is part of the cabin's interior, which no body of the station's frame
        /// touches, so it passes over the landing in the docking. The walkway is one piece, and the tram's wall under the
        /// hatch lies under its top (see <see cref="Doorway"/>), because a character that walks across the edge of a flush
        /// piece of the interior's compound rises 1.3 cm over 0.2 m, which edge removal does not stop. The porch's roof
        /// shortens the follow camera's arm, and that rise moved the camera 7 cm along it.
        /// </summary>
        public static Vector3[] Walkway { get; } =
            [
                .. new[] { Hatch.Min.Z - 0.3f, Hatch.Max.Z + 0.3f }.SelectMany(z =>
                    new Vector3[]
                    {
                        new(Cabin.X - 0.8f, -Cabin.Y, z),
                        new(Hull.Max.X, -Cabin.Y, z),
                        new(Hull.Max.X + BoardingStair.Gap + BoardingStair.LipOverlap, Hatch.Min.Y - 0.03f, z),
                        new(Hull.Max.X + BoardingStair.Gap + BoardingStair.LipOverlap, Hatch.Min.Y, z),
                        new(Cabin.X, Hatch.Min.Y, z),
                        new(Cabin.X - 0.8f, -Cabin.Y + 0.001f, z),
                    }
                ),
            ];

        /// <summary>
        /// The porch: a short passage out from the hatch through the model's side to the hull's box, in the shuttle's axes,
        /// over the <see cref="Walkway"/>, as its two side walls and its roof, each 0.1 m thick. The model's side lies 2.1
        /// to 2.6 m outside the tram's wall at the hatch, so the porch's walls hide the person's way through it. Its open
        /// end lies at the hull's starboard face.
        /// </summary>
        public static IReadOnlyList<BoundingBox> Porch { get; } =
            [
                new(new(Tram.X, Hatch.Min.Y, Hatch.Min.Z - 0.3f), new(Hull.Max.X, Hatch.Max.Y + 0.1f, Hatch.Min.Z - 0.2f)),
                new(new(Tram.X, Hatch.Min.Y, Hatch.Max.Z + 0.2f), new(Hull.Max.X, Hatch.Max.Y + 0.1f, Hatch.Max.Z + 0.3f)),
                new(new(Tram.X, Hatch.Max.Y, Hatch.Min.Z - 0.2f), new(Hull.Max.X, Hatch.Max.Y + 0.1f, Hatch.Max.Z + 0.2f)),
            ];

        /// <summary>
        /// The seat, in the shuttle's axes: a box 0.6 m wide, 0.5 m tall and 0.6 m deep on the cabin floor, centered 1 m to
        /// starboard and 4.5 m ahead of the shuttle's center.
        /// </summary>
        public static BoundingBox Seat { get; } = new(new(0.7f, -Cabin.Y, -4.8f), new(1.3f, -Cabin.Y + 0.5f, -4.2f));

        /// <summary>
        /// The full cross-section of the outer skin, from z = -5 m to the tail, corners 0 to 9 counterclockwise seen from +Z:
        /// the belly, bevels of 0.2 m at 45 degrees up to the sides, the sills at y = -1.2 m, 0.6 m above the cabin floor,
        /// bevels of 0.8 m at 45 degrees up to the roof, and the roof.
        /// </summary>
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

        /// <summary>
        /// The nose's front, at z = -8 m, with the same corners: over the nose's last 3 m the roof falls from y = 2 m to 0.6 m
        /// and the sides close in from x = ±3 m to ±2.4 m, while the belly stays flat. So the front is a flat face 4.8 m wide
        /// and 2.6 m tall, and every edge of it is parallel to the same edge of the full cross-section.
        /// </summary>
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

        /// <summary>
        /// The rib places, in meters along the shuttle's Z: every 1.5 m in the nose and every 2 m in the body. One lies at
        /// z = -1 m, where the pen's clip is, and the hatch's bay runs from 1 m to 3 m.
        /// </summary>
        private static readonly float[] Ribs = [-8, -6.5f, -5, -3, -1, 1, 3, 5, 7];

        /// <summary>
        /// The edges of the cross-sections that hold glass, with the names of their panes: the upper sides, the roof's
        /// bevels and the roof. The panes start 0.1 m above the sill, so the band runs from 0.7 m above the cabin floor
        /// over the roof.
        /// </summary>
        private static readonly (int Edge, string Name)[] GlassEdges =
        [
            (3, "starboard"),
            (4, "starboard-bevel"),
            (5, "roof"),
            (6, "port-bevel"),
            (7, "port"),
        ];

        /// <summary>
        /// The shuttle's hull (docs/plans/blue-danube-demo.md, "The shuttle's tram shape and glass canopy"): the loft of
        /// <see cref="NoseFront"/> at the nose to <see cref="Full"/> at z = -5 m and on to the tail face, with the
        /// <see cref="Hatch"/> through the starboard side in bay 6, and a pocket 1.4 m square and 0.6 m deep for the main
        /// engine's nozzle in the tail face, on the line of thrust through the center of mass. The glass canopy has a pane
        /// in the front above the sill, <c>pane-nose</c>, and one in each cell of the five glass edges in bays 1 to 8, but
        /// the hatch's: 40 panes. Ribs and pillars are 0.2 m wide, mullions 0.12 m, and the glass stays 0.1 m from solid
        /// skin.
        /// </summary>
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
                        .Door(door: Doorway, bay: HatchBay, fromCorner: 2, toCorner: 4)
                        .TailPocket(half: 0.7f, depth: 0.6f, housing: Wall)
                        .Surrounds(rib: 0.1f, fold: 0.06f, solid: 0.1f)
                        .NoseGlass("pane-nose", fromCorner: 3, toCorner: 8),
                    (hull, glass) => hull.Glass($"pane-{glass.Name}-{glass.Bay}", glass.Edge, glass.Bay)
                );

        /// <summary>The hull in its parent frame: the box of <see cref="Hull"/>, with the default convex radius.</summary>
        public static ShapeDesc HullShape { get; } =
            new CompoundShapeDesc([new(new BoxShapeDesc(Hull.Extents), new(Hull.Center, Quaternion.Identity))]);

        /// <summary>
        /// The hull in its own frame: the tram's solid in convex pieces of convex radius 0, solid at the glass and open at
        /// the hatch, the <see cref="Seat"/>, the <see cref="Walkway"/> and the <see cref="Porch"/>. Every piece is sharp,
        /// so physics gives the body edge removal.
        /// </summary>
        public static ShapeDesc InteriorShape { get; } =
            new CompoundShapeDesc([
                .. Loft.Interior(),
                new(new BoxShapeDesc(Seat.Extents, ConvexRadius: 0), new(Seat.Center, Quaternion.Identity)),
                new(new ConvexHullShapeDesc(Walkway, ConvexRadius: 0), Pose.Identity),
                .. Porch.Select(box => new CompoundChild(
                    new BoxShapeDesc(box.Extents, ConvexRadius: 0),
                    new(box.Center, Quaternion.Identity)
                )),
            ]);

        /// <summary>
        /// The hull's moments of inertia about its principal axes, in kg·m², and those axes in the shuttle's axes, as physics
        /// derives them for <see cref="HullShape"/> with <see cref="Mass"/>: a box of one density, whose principal axes are
        /// the shuttle's own. The hull's center of mass is the engine's (<see cref="BodyFacts.CenterOfMass"/>), which every
        /// component reads from the body facts: the box's middle. The user chose one density on 2026-10-20, so no ballast
        /// moves it (docs/plans/blue-danube-demo.md, "Engine changes").
        /// </summary>
        public static PrincipalInertia Inertia { get; } = BoxInertia(Mass, 2 * Hull.Extents);

        /// <summary>The principal inertia of a box of one density, of <paramref name="mass"/> kilograms and <paramref name="size"/> meters.</summary>
        private static PrincipalInertia BoxInertia(float mass, Vector3 size)
        {
            Vector3 square = size * size;
            return new(
                mass / 12 * new Vector3(square.Y + square.Z, square.X + square.Z, square.X + square.Y),
                Quaternion.Identity
            );
        }

        /// <summary>
        /// The main engine's nozzle: a ring from 0.5 m to 0.6 m in radius and 0.5 m long, about its own Y axis, in 32
        /// segments. It is drawn only, so its outside stays 0.1 m from the walls of the tail pocket, which reach 0.7 m from
        /// the pocket's middle.
        /// </summary>
        public static RevolvedSolid Nozzle { get; } =
            RevolvedSolid.Ring(innerRadius: 0.5f, outerRadius: 0.6f, halfWidth: 0.25f, segments: 32);

        /// <summary>
        /// The nozzle's middle along the shuttle's Z, in meters: its aft rim lies 5 cm inside the hull's tail face, the
        /// model's engine section, so it runs from z = 13.57 m to 14.02 m. The main engine's part sits there too (see
        /// <see cref="ShuttleJets.MainEnginePlace"/>), so its plume leaves the model's tail.
        /// </summary>
        public static double NozzleZ { get; } = Hull.Max.Z - 0.05 - 0.25;

        /// <summary>
        /// Where a person 1.8 m tall stands at the seat, in the shuttle's axes: the center of its capsule, 0.9 m above the
        /// cabin floor, on the seat's line aft of it, with 0.15 m between the capsule, 0.3 m in radius, and the seat. A
        /// character has no sitting pose for now, so a person who leaves its seat walks away from this place.
        /// </summary>
        public static Double3 SeatStand { get; } = new(1, -Cabin.Y + 0.9, Seat.Max.Z + 0.45);

        /// <summary>
        /// Where a person 1.8 m tall stands at the porch's open end, in the shuttle's axes: the center of its capsule, 0.9 m
        /// above the porch's floor, 0.6 m in from the hull's face, in line with the hatch. A person who walks from there to
        /// <see cref="InsideHatch"/> walks straight through the middle of the hatch.
        /// </summary>
        public static Double3 OnPorch { get; } = new(Hull.Max.X - 0.6, Hatch.Min.Y + 0.9, Hatch.Center.Z);

        /// <summary>
        /// Where a person 1.8 m tall stands 1.2 m inside the hatch, in the shuttle's axes: the center of its capsule, 0.9 m
        /// above the cabin floor, in line with the hatch, with the whole capsule inside the cabin.
        /// </summary>
        public static Double3 InsideHatch { get; } = new(Cabin.X - 1.2, -Cabin.Y + 0.9, Hatch.Center.Z);

        /// <summary>
        /// The 40 panes of the canopy, from <see cref="Loft"/>, in the shuttle's axes: each pane's scene id, its hole at
        /// both skins, and its slab of glass round its center.
        /// </summary>
        public static IReadOnlyList<HullPane> Glass { get; } = Loft.Panes();

        /// <summary>
        /// The cabin's four lamps, each with its scene id and its glowing panel, in the shuttle's axes: on the inner faces
        /// of the pillars at z = -3 m and 3 m, on both walls, 2.4 m above the cabin floor. A panel is 0.05 m thick, 0.5 m tall
        /// and 0.15 m wide, within its pillar's 0.2 m, so the pillar hides it from outside. The roof is glass, so the lamps
        /// hang on the walls.
        /// </summary>
        public static IReadOnlyList<(string Id, BoundingBox Panel)> Lamps { get; } =
        [
            .. new[] { (Name: "port", Side: -1f), (Name: "starboard", Side: 1f) }.SelectMany(wall =>
                new[] { (Name: "fore", Z: -3f), (Name: "aft", Z: 3f) }.Select(place =>
                {
                    var center = new Vector3(wall.Side * (Cabin.X - LampHalf.X), -Cabin.Y + 2.4f, place.Z);
                    return (
                        Id: $"lamp-{wall.Name}-{place.Name}",
                        Panel: new BoundingBox(center - LampHalf, center + LampHalf)
                    );
                })
            ),
        ];

        /// <summary>The half extents of a lamp's panel, in meters: 0.05 m thick across the wall, 0.5 m tall and 0.15 m wide.</summary>
        private static Vector3 LampHalf => new(0.025f, 0.25f, 0.075f);

        /// <summary>
        /// The light of a lamp on the wall at the side <paramref name="side"/> of X, +1 or −1: its panel's face toward the
        /// cabin's middle, LampHalf's Y by Z twice over.
        /// </summary>
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
        /// The pose of <c>sill-camera</c>, in the shuttle's axes: in the porch, 0.3 m above the walkway against its aft wall
        /// and 0.6 m out from the tram's side, looking forward and in at the hatch's sill. It sees the person's feet cross
        /// the sill from the side, about 1 m away.
        /// </summary>
        public static DPose SillCamera { get; } =
            new(
                new(Tram.X + 0.6, Hatch.Min.Y + 0.3, Hatch.Max.Z + 0.15),
                AttitudeAutopilot.Facing(new Double3(-0.8, 0, -1), Double3.UnitY)
            );

        /// <summary>The radii of the bubble, in meters, in the shuttle's axes.</summary>
        public static Vector3 Bubble { get; } = (Vector3.Max(-Hull.Min, Hull.Max) + new Vector3(2)) * MathF.Sqrt(3);

        /// <summary>
        /// The layers of the shuttle's flight program, in the order in which they run in a tick: the
        /// <see cref="AscentAutopilot"/> at priority 0, the <see cref="RendezvousAutopilot"/> at 1 and the
        /// <see cref="DockingAutopilot"/> at 2, which add the <see cref="Throttle"/>, the <see cref="AttitudeTarget"/> and
        /// the <see cref="Translation"/> of each tick to the shuttle's events. The actuators, which <see cref="FitActuators"/>
        /// adds to the hull once it is spawned, read them after every layer and follow the last layer that speaks. The main
        /// engine pushes <see cref="Mass"/> with <see cref="ThrustAcceleration"/> at full throttle, and both autopilots plan
        /// with that thrust. The ascent autopilot steers only once the shuttle is <paramref name="climbHeight"/> meters higher
        /// than where it launched, clear of its ramp, and it steers into <paramref name="plane"/>, whose frame it reads in.
        /// The rendezvous autopilot flies <see cref="RendezvousPlan.Demo"/> to <paramref name="station"/>, and the docking
        /// autopilot docks in it.
        /// </summary>
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

        /// <summary>
        /// Gives the spawned <paramref name="hull"/> the shuttle's actuators, outside a tick (docs/plans/blue-danube-demo.md,
        /// "The jets as parts"): the <see cref="AttitudeAutopilot"/> at priority 10, which turns the attitude target of the
        /// tick into a <see cref="Torque"/>, and the <see cref="ShuttleJetControl"/> at 20, which spawns the main engine and
        /// the jets as parts of the hull and sends each its throttle by message: the main engine gives
        /// <see cref="MainEngineThrust"/> at full throttle, and the translation pairs <see cref="TranslationThrust"/> on each
        /// axis. Returns the control.
        /// </summary>
        public static ShuttleJetControl FitActuators(World world, Entity hull)
        {
            world.AddComponent(hull, new AttitudeAutopilot(Inertia));
            return FitJets(world, hull);
        }

        /// <summary>Spawns the shuttle's main engine and jets as parts of <paramref name="hull"/> and adds their control, as
        /// <see cref="FitActuators"/> does, without the attitude autopilot.</summary>
        public static ShuttleJetControl FitJets(World world, Entity hull) =>
            ShuttleJetControl.Fit(
                world: world,
                hull: hull,
                mainThrust: MainEngineThrust,
                inertia: Inertia,
                translationThrust: TranslationThrust
            );

        /// <summary>
        /// Where the nozzle lies, in the shuttle's axes, for a hull whose center of mass lies at
        /// <paramref name="centerOfMass"/>: its axis along Z through the center of mass, where the main engine pushes, and
        /// the tail pocket's middle, at <see cref="NozzleZ"/>, turned a quarter turn about X so that the ring's own Y lies
        /// along Z. A nozzle off that line would show a thrust that turns the ship, and the ship does not turn.
        /// </summary>
        public static DPose NozzlePose(Double3 centerOfMass) =>
            new(new(centerOfMass.X, centerOfMass.Y, NozzleZ), DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2));

        /// <summary>
        /// Spawns the nozzle in <paramref name="cabin"/>, the shuttle's frame, at <see cref="NozzlePose"/> for the center of
        /// mass of the frame's owner, the hull, which it reads from the engine's body facts. It is drawn in dark gray, with
        /// no body. Its scene id is <c>nozzle</c>, after <paramref name="prefix"/> and a hyphen when the prefix is not empty,
        /// so that each shuttle of a scene has its own. The nozzle is its own entity, not the main engine's part: a parked
        /// shuttle with no flight controls draws it too, and its mesh turns a quarter turn from the hull's axes, which the
        /// main engine's part keeps.
        /// </summary>
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

        /// <summary>The hull's body with <paramref name="motion"/>: <see cref="HullShape"/>, <see cref="Mass"/>, no damping, and
        /// <see cref="InteriorShape"/>.</summary>
        public static BodyDesc Body(BodyMotion motion) =>
            new(
                Shape: HullShape,
                Motion: motion,
                Mass: Mass,
                LinearDamping: 0,
                AngularDamping: 0,
                InteriorShape: InteriorShape
            );

        /// <summary>
        /// The drawn hull of the gray box, <see cref="Loft"/> whole, in light gray. It casts shadows and is never an
        /// impostor: a flat picture of the hull alone cannot hold the person inside at its depth, and the hull has few
        /// triangles. The scene draws the seat, the panes, the lamps, the porch and the nozzle apart.
        /// </summary>
        public static RenderData DrawnHull(IRenderResources resources) => Tramway(resources, Loft.Mesh());

        /// <summary>
        /// The drawn hull with the Lunar Shuttle's model outside: <see cref="Loft"/> seen from inside, whose outer skin the
        /// model's attached placement (see <see cref="SpawnArt"/>) hides.
        /// </summary>
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
        /// The drawn hull of the shuttle <paramref name="hull"/> as a scene shows it: with <paramref name="art"/>, the
        /// tram seen from inside, and the model, <c>shuttle-art</c> after <paramref name="prefix"/>, attached at
        /// <see cref="ArtPlace"/>; without it, the gray box's whole tram. Either way the porch, <c>porch</c> after the
        /// prefix, drawn in the cabin's frame. The hull's render row must be set by the spawn: pass
        /// <see cref="HullRender"/>.
        /// </summary>
        public static void SpawnArt(
            World world,
            Entity hull,
            SceneInstance scene,
            IRenderResources resources,
            Assets.StoredModel? art,
            string prefix = ""
        )
        {
            string Id(string name) => prefix.Length == 0 ? name : $"{prefix}-{name}";
            BoxMesh walls = new();
            foreach (BoundingBox box in Porch)
                walls.Box(box);
            scene.Add(
                Id("porch"),
                world.Spawn(
                    new()
                    {
                        Frame = world.GetOwnedFrame(hull),
                        Pose = DPose.Identity,
                        Render = Drawn(
                            resources: resources,
                            mesh: Merged(walls.Build(), Prism(Walkway)),
                            color: new(x: 0.42f, y: 0.43f, z: 0.45f, w: 1),
                            boundingRadius: Hull.Max.X + Hatch.Max.Z + 1
                        ) with
                        {
                            Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
                        },
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
        }

        /// <summary>The hull's render row with <paramref name="art"/> or as the gray box (see <see cref="SpawnArt"/>).</summary>
        public static RenderData HullRender(IRenderResources resources, Assets.StoredModel? art) =>
            art is null ? DrawnHull(resources) : DrawnInterior(resources);

        /// <summary>
        /// Spawns each pane of <see cref="Glass"/> in <paramref name="cabin"/>, the shuttle's frame, at its center, with its
        /// scene id after <paramref name="prefix"/> and a hyphen when the prefix is not empty, so that each shuttle of a
        /// scene has its own: the color of the orbit scene's glass, mostly clear with a slight blue tint, with an
        /// anti-reflection coating (see <see cref="AntiReflective"/>). A pane has no body, because the interior shape is
        /// solid at the glass, and it casts no shadow, so sunlight comes in through the holes of the drawn hull. Each pane is
        /// its own instance round its own center, so the renderer sorts it at its real place.
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
                    BaseColor: new(x: 0.45f, y: 0.65f, z: 1, w: 0.2f),
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

        /// <summary>
        /// Spawns the <see cref="Lamps"/> in <paramref name="cabin"/>, the shuttle's frame, with their scene ids: each a
        /// warm glowing panel whose face toward the cabin's middle is a cabin lamp's rectangle light, which casts shadows
        /// within the renderer's budget.
        /// </summary>
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

        /// <summary>
        /// Spawns the shuttle parked at <paramref name="pose"/> in <paramref name="frame"/>, drawn with
        /// <paramref name="render"/>, with the <paramref name="components"/>, such as its <see cref="FlightControls"/>: its
        /// hull is static, so that a later <see cref="BodyHandle.SetMotion"/> unparks it.
        /// The cabin needs no value of its own for its weight. The engine derives every frame's proper acceleration from
        /// how the frame moved in the step (ARCHITECTURE.md, "How a frame moves"): a frame that holds still in a gravity
        /// field feels that gravity reversed, so the person in the parked cabin feels the weight of the ground. A new frame
        /// reads no proper acceleration during its first step, and the derived value from its second step on.
        /// </summary>
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
    }
}
