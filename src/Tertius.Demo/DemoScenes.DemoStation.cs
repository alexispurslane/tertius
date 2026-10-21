using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The station of the Blue Danube demo (docs/plans/blue-danube-demo.md, "The station"): a wheel 202 m across on a circle
    /// 400 km up, in the launch plane, which turns about its axis at <see cref="SpinStation.Rate"/>, so that its floor, 100 m
    /// from the axis, has one gravity. In its own axes +Y is the axis, about which it turns, and the angle θ about it runs
    /// from +X toward -Z, as <see cref="RevolvedSolid"/> measures it. The docking slot is on the +Y side.
    /// <list type="bullet">
    /// <item>The wheel: the floor, a ring from 100 to 101 m from the axis and 20 m wide, in 256 segments; the ceiling, a ring
    /// from 94.5 to 95 m; and two side walls, rings from 95 to 100 m, 0.3 m thick, at y = ±9.85 m. They close the wheel's
    /// tube, so it looks closed from outside. The floor is the interior shape of the station's own body, as the spin
    /// station's floor is. The ceiling and the side walls are static bodies in the station's frame.</item>
    /// <item>Four spokes, square tubes 4 m on a side outside with walls 0.25 m thick, in the plane y = 0 at θ = 0, 90, 180
    /// and 270 degrees, from the hub's outer face, 8.5 m from the axis, to the ceiling, 94.5 m from it. One static body,
    /// a compound of their 16 wall boxes.</item>
    /// <item>The hub, the docking barrel: a square tube along the axis, 17 by 26 by 17 m outside, with walls 0.5 m thick, so
    /// the bay inside is 16 by 25 by 16 m. The <see cref="Slot"/> leads through its +Y wall. Its -X wall is the
    /// <see cref="Deck"/>, the landing floor of step 5. It is built in its own axes, whose +Z is the station's +Y (see
    /// <see cref="HubTurn"/>), because a box opening runs along X or Z only.</item>
    /// <item>Four glowing panels with point lights, the bay lamps, on the bay's +X, +Z and -Z walls near the slot and on its
    /// far end wall, so the slot glows from inside. They leave the deck clear for the landing.</item>
    /// <item>The view <c>rim-camera</c>, in the station's frame, 2 m off the slot's face of the wheel near its rim, looks
    /// across the axis at a point between the hub and <see cref="HoldPoint"/>, with the axis across the picture. It turns
    /// with the station.</item>
    /// <item>The view <c>bay-camera</c>, in the station's frame, at <see cref="BayCamera"/> near the bay's far wall, looks
    /// at <see cref="BayCameraAim"/>, with its roof away from the deck. It sees the slot, the docking and the parked shuttle
    /// beside the spoke's mouth.</item>
    /// </list>
    /// Every part is drawn with <see cref="RenderFlags.NoImpostor"/>: the spin turns the station 18 degrees a second, so an
    /// impostor's capture would go stale within a few ticks.
    /// </summary>
    public static class DemoStation
    {
        /// <summary>The radius of the wheel's floor, the ring's inner face, in meters, at its joints.</summary>
        public const float FloorRadius = SpinStation.FloorRadius;

        /// <summary>The thickness of the wheel's floor, in meters: it runs from 100 to 101 m from the axis.</summary>
        public const float FloorThickness = 1;

        /// <summary>Half the wheel's width along the axis, in meters.</summary>
        public const float HalfWidth = 10;

        /// <summary>The wheel's segments, and so the facets of its floor: 2.45 m long each.</summary>
        public const int Segments = 256;

        /// <summary>The radius of the ceiling's face toward the axis, in meters: it runs from 94.5 to 95 m, 5 m above the floor.</summary>
        public const float CeilingRadius = 94.5f;

        /// <summary>The thickness of the ceiling, in meters.</summary>
        public const float CeilingThickness = 0.5f;

        /// <summary>The thickness of each side wall along the axis, in meters.</summary>
        public const float SideWallThickness = 0.3f;

        /// <summary>The hub's half extents outside, in meters, in the station's axes: 17 by 26 by 17 m.</summary>
        public static Vector3 HubHalf { get; } = new(8.5f, 13, 8.5f);

        /// <summary>The thickness of the hub's walls, in meters.</summary>
        public const float HubWall = 0.5f;

        /// <summary>
        /// The room that the slot leaves on each side of the shuttle's cross-section, in meters. A spin mismatch of 3 degrees
        /// moves a corner of the shuttle, 3.6 m from its axis, by 0.19 m, which this holds.
        /// </summary>
        public const float SlotClearance = 1;

        /// <summary>Half the outside width of a spoke, in meters: 4 m on a side.</summary>
        public const float SpokeHalfWidth = 2;

        /// <summary>The thickness of a spoke's walls, in meters, so 3.5 m on a side inside.</summary>
        public const float SpokeWall = 0.25f;

        /// <summary>
        /// The radius of the station's bubble, a sphere, in meters: it holds the hull's 101.5 m with 18.5 m to spare, so a
        /// body enters the frame before it reaches the hull.
        /// </summary>
        public const float Bubble = 120;

        /// <summary>The hull's mass, in kilograms, which a kinematic hull does not use.</summary>
        public const float Mass = 1e7f;

        /// <summary>
        /// The world time at which the station passes over the pad, in seconds: 38 s, 18 s after the ignition. It sets the
        /// launch window (docs/plans/blue-danube-demo.md, "The orbits, the window and the times").
        /// </summary>
        public const double OverPadAt = 38;

        /// <summary>
        /// The hold point, in the station's axes: on the axis, 150 m from the station's center, on the slot's side, outside
        /// the bubble. The rendezvous ends there.
        /// </summary>
        public static Double3 HoldPoint { get; } = new(0, 150, 0);

        /// <summary>
        /// The slot through the hub's +Y wall, in the station's axes: 6 m along X by 8 m along Z, on the axis. The shuttle
        /// enters with its belly toward the deck, so its height of 4 m lies along X and its width of 6 m along Z, with
        /// <see cref="SlotClearance"/> on each side.
        /// </summary>
        public static BoundingBox Slot { get; } = new(
            new Vector3(-Shuttle.Hull.Y - SlotClearance, HubHalf.Y - HubWall, -Shuttle.Hull.X - SlotClearance),
            new Vector3(Shuttle.Hull.Y + SlotClearance, HubHalf.Y, Shuttle.Hull.X + SlotClearance));

        /// <summary>
        /// The deck, the bay's wall at x = -8 m, the inner face of the hub's -X wall, in the station's axes: the landing
        /// floor of step 5. Its spin gravity is ω² · 8 m = 0.785 m/s², about 8 percent of a gravity.
        /// </summary>
        public const float Deck = -8;

        /// <summary>
        /// The turn from the hub's own axes to the station's: a quarter turn about X that takes the hub's +Z to the station's
        /// +Y, and keeps X, so the deck stays at x = -8 m.
        /// </summary>
        public static DQuaternion HubTurn { get; } = DQuaternion.FromAxisAngle(Double3.UnitX, -Math.PI / 2);

        /// <summary>
        /// The shuttle's rotation in the station's axes once it is docked (docs/plans/blue-danube-demo.md, "The docking"):
        /// its nose, -Z, along the station's -Y, into the bay, and its roof, +Y, along +X, away from the <see cref="Deck"/>.
        /// So its belly faces the deck, and its starboard side, +X, with the hatch, faces +Z, toward the middle of the deck.
        /// </summary>
        public static DQuaternion DockedRotation { get; } = AttitudeAutopilot.Facing(-Double3.UnitY, Double3.UnitX);

        /// <summary>
        /// How far the parked shuttle's tail lies inside the inner face of the slot's wall, in meters. The entry stops the
        /// shuttle there on the axis, so its tail clears the slot's wall before the hull moves away from the axis.
        /// </summary>
        public const float ParkedTailGap = 1.5f;

        /// <summary>
        /// Where the docked shuttle stands parked, in the station's axes, with <see cref="DockedRotation"/>: belly on the
        /// <see cref="Deck"/> at x = -8 m, port side on the bay's -Z wall at z = -8 m, and tail <see cref="ParkedTailGap"/>
        /// inside the slot's wall. Its center is at (-6, 3, -5) m. It is the only place on the deck where the hull leaves
        /// <see cref="SpokeMouth"/> free: its hull fills z from -8 to -2 m, 0.25 m clear of the mouth. The spin gravity at
        /// its center of mass points away from the axis, into the deck and into the -Z wall, so the corner holds the hull
        /// with no push and no friction. The hatch opens toward +Z, onto the deck beside the mouth.
        /// </summary>
        public static DPose ParkedPose { get; } = new(
            new Double3(Deck + Shuttle.Hull.Y, HubHalf.Y - HubWall - ParkedTailGap - Shuttle.Hull.Z, -(HubHalf.Z - HubWall) + Shuttle.Hull.X),
            DockedRotation);

        /// <summary>
        /// The box of the parked shuttle in the station's axes: the hull's box, <see cref="Shuttle.Hull"/>, at
        /// <see cref="ParkedPose"/>. It runs from x = -8 to -4 m, y = -5 to 11 m and z = -8 to -2 m. The tram's hull fits
        /// it and touches each of its faces.
        /// </summary>
        public static BoundingBox ParkedBounds { get; } = BoxAt(ParkedPose, Shuttle.Hull);

        /// <summary>
        /// The mouth of the spoke at θ = 180 degrees in the deck, in the station's axes: the square of the spoke's inside,
        /// 3.5 m on a side, from y = -1.75 to 1.75 m and z = -1.75 to 1.75 m, through the deck's wall, from the hub's outer
        /// face at x = -8.5 m to the deck at x = -8 m. The exit's chute starts inside it, so a parked shuttle leaves it free.
        /// </summary>
        public static BoundingBox SpokeMouth { get; } = new(
            new Vector3(-HubHalf.X, -(SpokeHalfWidth - SpokeWall), -(SpokeHalfWidth - SpokeWall)),
            new Vector3(Deck, SpokeHalfWidth - SpokeWall, SpokeHalfWidth - SpokeWall));

        /// <summary>
        /// Where <c>bay-camera</c> stands, in the station's axes: (6.5, -11.5, 5.5) m, 1 m from the bay's far wall, in the
        /// corner across from the parked place.
        /// </summary>
        public static Double3 BayCamera { get; } = new(6.5, -11.5, 5.5);

        /// <summary>The point that <c>bay-camera</c> looks at, in the station's axes: (-3, 3, -1.5) m, between the slot and the parked shuttle.</summary>
        public static Double3 BayCameraAim { get; } = new(-3, 3, -1.5);

        /// <summary>The axis-aligned box that holds the box of half extents <paramref name="half"/> at <paramref name="pose"/>.</summary>
        private static BoundingBox BoxAt(DPose pose, Vector3 half)
        {
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (int corner in Enumerable.Range(0, 8))
            {
                var local = new Double3((corner & 1) == 0 ? -half.X : half.X, (corner & 2) == 0 ? -half.Y : half.Y, (corner & 4) == 0 ? -half.Z : half.Z);
                Vector3 at = pose.TransformPoint(local).ToVector3();
                min = Vector3.Min(min, at);
                max = Vector3.Max(max, at);
            }
            return new BoundingBox(min, max);
        }

        /// <summary>
        /// The station's orbit in a frame that does not turn and holds Earth's point gravity (<c>earth-surface</c> of
        /// demo-surface, <c>earth</c> of earth-system): a circle of <see cref="OrbitRadius"/> whose normal is -X, the launch
        /// plane's normal. At mean anomaly 0 the station is at +Z and moves toward +Y. It passes over the pad, at mean
        /// anomaly 90 degrees, at <see cref="OverPadAt"/>, and moves toward -Z there, as the shuttle does. The axial tilt
        /// takes the station's +Y to +X of that frame, the slot's side, and the station spins about that axis at
        /// <see cref="SpinStation.Rate"/>, so the axis stays fixed.
        /// </summary>
        public static KeplerOrbit Orbit { get; } = new(SemiMajorAxis: OrbitRadius, Eccentricity: 0, Inclination: Math.PI / 2, LongitudeOfAscendingNode: -Math.PI / 2,
            ArgumentOfPeriapsis: 0, MeanAnomalyAtEpoch: Math.PI / 2 - Math.Sqrt(EarthMu / (OrbitRadius * OrbitRadius * OrbitRadius)) * OverPadAt, Epoch: 0,
            AxialTilt: DQuaternion.FromAxisAngle(Double3.UnitZ, -Math.PI / 2), SpinRate: SpinStation.Rate, SpinPhaseAtEpoch: 0,
            Attitude: KeplerAttitude.Spin);

        /// <summary>
        /// The station's hull in a frame that does not turn and holds Earth's point gravity (<c>earth-surface</c> of
        /// demo-surface, <c>earth</c> of earth-system), in the station's axes: a compound of three solids. The wheel is a
        /// solid ring from the ceiling, 94.5 m from the axis, to the floor's outer face, 101 m from it, in 256 segment hulls.
        /// The spokes and the hub are solid boxes. Its farthest point, a corner of the wheel's rim, lies 101.5 m from the
        /// center.
        /// </summary>
        public static ShapeDesc HullShape()
        {
            var wheel = (CompoundShapeDesc)RevolvedSolid.Ring(CeilingRadius, FloorRadius + FloorThickness, HalfWidth, Segments).Shape();
            return new CompoundShapeDesc([
                .. wheel.Children,
                .. Enumerable.Range(0, 4).Select(quarter => Box(Turned(SpokeSolid, quarter), Pose.Identity)),
                Box(new BoundingBox(-HubOwnHalf, HubOwnHalf), new Pose(Vector3.Zero, HubTurn.ToQuaternion())),
            ]);
        }

        /// <summary>The hub's half extents in its own axes, whose +Z is the station's +Y.</summary>
        private static Vector3 HubOwnHalf => new(HubHalf.X, HubHalf.Z, HubHalf.Y);

        /// <summary>The slot in the hub's own axes: an opening through its +Z wall.</summary>
        private static Opening HubSlot => Opening.ThroughZ(new BoundingBox(
            new Vector3(Slot.Min.X, Slot.Min.Z, Slot.Min.Y), new Vector3(Slot.Max.X, Slot.Max.Z, Slot.Max.Y)));

        /// <summary>
        /// The boxes of the hub's walls, in its own axes, with the slot open: the far end wall across Z, the slot's end wall
        /// in four parts round the slot, the walls across X between the end walls, and the walls across Y between those.
        /// The boxes touch without gaps or overlaps.
        /// </summary>
        public static IEnumerable<BoundingBox> HubWalls()
        {
            Vector3 outer = HubOwnHalf, inner = outer - new Vector3(HubWall);
            BoundingBox slot = HubSlot.Bounds;
            yield return new BoundingBox(-outer, outer with { Z = -inner.Z });
            var end = new BoundingBox((-outer) with { Z = inner.Z }, outer);
            yield return end with { Max = end.Max with { X = slot.Min.X } };
            yield return end with { Min = end.Min with { X = slot.Max.X } };
            yield return new BoundingBox(new Vector3(slot.Min.X, -outer.Y, inner.Z), new Vector3(slot.Max.X, slot.Min.Y, outer.Z));
            yield return new BoundingBox(new Vector3(slot.Min.X, slot.Max.Y, inner.Z), new Vector3(slot.Max.X, outer.Y, outer.Z));
            yield return new BoundingBox(new Vector3(-outer.X, -outer.Y, -inner.Z), new Vector3(-inner.X, outer.Y, inner.Z));
            yield return new BoundingBox(new Vector3(inner.X, -outer.Y, -inner.Z), new Vector3(outer.X, outer.Y, inner.Z));
            yield return new BoundingBox(new Vector3(-inner.X, -outer.Y, -inner.Z), new Vector3(inner.X, -inner.Y, inner.Z));
            yield return new BoundingBox(new Vector3(-inner.X, inner.Y, -inner.Z), new Vector3(inner.X, outer.Y, inner.Z));
        }

        /// <summary>The spoke at θ = 0 as a solid, along +X from the hub's outer face to the ceiling, in the station's axes.</summary>
        private static BoundingBox SpokeSolid => new(new Vector3(HubHalf.X, -SpokeHalfWidth, -SpokeHalfWidth), new Vector3(CeilingRadius, SpokeHalfWidth, SpokeHalfWidth));

        /// <summary>
        /// The four wall boxes of the spoke at θ = 0, in the station's axes: the walls across Y over the whole width, and
        /// the walls across Z between them. The tube is open at both ends.
        /// </summary>
        private static IEnumerable<BoundingBox> SpokeWalls()
        {
            BoundingBox solid = SpokeSolid;
            float inner = SpokeHalfWidth - SpokeWall;
            yield return solid with { Min = solid.Min with { Y = inner } };
            yield return solid with { Max = solid.Max with { Y = -inner } };
            yield return new BoundingBox(new Vector3(solid.Min.X, -inner, inner), new Vector3(solid.Max.X, inner, SpokeHalfWidth));
            yield return new BoundingBox(new Vector3(solid.Min.X, -inner, -SpokeHalfWidth), new Vector3(solid.Max.X, inner, -inner));
        }

        /// <summary>
        /// The boxes of the four spokes' walls, in the station's axes: those of the spoke at θ = 0, turned by each quarter
        /// turn about the axis.
        /// </summary>
        public static IEnumerable<BoundingBox> Spokes() =>
            Enumerable.Range(0, 4).SelectMany(quarter => SpokeWalls().Select(box => Turned(box, quarter)));

        /// <summary>
        /// <paramref name="box"/> turned by <paramref name="quarter"/> quarter turns about +Y, the way θ grows, from +X
        /// toward -Z: a quarter turn takes +X to -Z and +Z to +X. Quarter turns keep a box aligned with the axes.
        /// </summary>
        private static BoundingBox Turned(BoundingBox box, int quarter)
        {
            Vector3 Turn(Vector3 p) => (quarter % 4) switch
            {
                0 => p,
                1 => new Vector3(p.Z, p.Y, -p.X),
                2 => new Vector3(-p.X, p.Y, -p.Z),
                _ => new Vector3(-p.Z, p.Y, p.X),
            };
            Vector3 a = Turn(box.Min), b = Turn(box.Max);
            return new BoundingBox(Vector3.Min(a, b), Vector3.Max(a, b));
        }

        private static CompoundChild Box(BoundingBox box, Pose pose) =>
            new(new BoxShapeDesc(box.Extents), new Pose(pose.Position + Vector3.Transform(box.Center, pose.Rotation), pose.Rotation));

        private static CompoundShapeDesc Boxes(IEnumerable<BoundingBox> boxes) =>
            new([.. boxes.Select(box => Box(box, Pose.Identity))]);

        private static MeshShapeDesc MeshShape(MeshData mesh) =>
            new([.. mesh.Vertices.Select(v => v.Position)], [.. mesh.Indices.Select(i => (int)i)]);

        /// <summary>A gray-box render row of a station part: it casts shadows, and has no impostor.</summary>
        private static RenderData Part(IRenderResources resources, MeshData mesh, Vector4 color, float boundingRadius) =>
            Drawn(resources, mesh, color, boundingRadius) with { Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor };

        /// <summary>
        /// Spawns the station in <paramref name="parent"/>, a frame that does not turn and holds Earth's point gravity
        /// (<c>earth-surface</c> of demo-surface, <c>earth</c> of earth-system), on <see cref="Orbit"/> at the world's
        /// time, with its parts, its bay lamps, its <c>rim-camera</c> and its <c>bay-camera</c>, and adds them to
        /// <paramref name="scene"/>: the
        /// owner <c>station</c>, <c>station-ceiling</c>, <c>station-wall-slot</c> and <c>station-wall-far</c>, the side walls
        /// on the slot's side and the other, <c>station-spokes</c> and <c>station-hub</c>. Returns the station.
        /// </summary>
        /// <remarks>
        /// The spawn carries the first motion intent of the orbit, as <see cref="Station.Build"/> does, so the frame is on
        /// its rails and turns from its first step. A Kepler frame in a parent that does not turn falls freely, so its proper
        /// acceleration is zero. The hull is kinematic, and its <see cref="KeplerMotion"/> moves it. It moves fast, so it
        /// lives in a following frame of its own (ARCHITECTURE.md, "Numeric frames").
        /// </remarks>
        public static Entity Spawn(World world, FrameHandle parent, SceneInstance scene, IRenderResources resources)
        {
            KeplerOrbit orbit = Orbit;
            (Double3 place, Double3 velocity) = orbit.StateAt(world.Time, EarthMu);
            RevolvedSolid floor = RevolvedSolid.Ring(FloorRadius, FloorRadius + FloorThickness, HalfWidth, Segments);
            MeshData floorMesh = floor.Mesh();
            ShapeDesc hull = HullShape();
            Entity station = world.Spawn(new SpawnDesc
            {
                Frame = parent,
                Pose = new DPose(place, orbit.RotationAt(world.Time, EarthMu)),
                LinearVelocity = velocity,
                AngularVelocity = orbit.AngularVelocityAt(world.Time, EarthMu).ToVector3(),
                FirstMove = new MotionIntent(orbit.Sample(EarthMu, world.TimeAt(world.TickNumber + 1), world.DeltaTime)),
                Components = [new KeplerMotion { Orbit = orbit }],
                Body = new BodyDesc(hull, BodyMotion.Kinematic, Mass: Mass, InteriorShape: MeshShape(floorMesh)),
                AsFrame = new FrameSpec(FrameBounds.Sphere(Bubble)),
                Render = Part(resources, floorMesh, new Vector4(0.62f, 0.63f, 0.66f, 1), floor.Shape().BoundingRadius()),
            });
            scene.Add("station", station);
            FrameHandle frame = world.GetOwnedFrame(station);

            RevolvedSolid ceiling = RevolvedSolid.Ring(CeilingRadius, CeilingRadius + CeilingThickness, HalfWidth, Segments);
            ShapeDesc ceilingShape = ceiling.Shape();
            scene.Add("station-ceiling", world.Spawn(new SpawnDesc
            {
                Frame = frame,
                Pose = DPose.Identity,
                Body = new BodyDesc(ceilingShape, BodyMotion.Static),
                Render = Part(resources, ceiling.Mesh(), new Vector4(0.7f, 0.71f, 0.74f, 1), ceilingShape.BoundingRadius()),
            }));

            RevolvedSolid wall = RevolvedSolid.Ring(CeilingRadius + CeilingThickness, FloorRadius, SideWallThickness / 2, Segments);
            ShapeDesc wallShape = wall.Shape();
            RenderData wallRender = Part(resources, wall.Mesh(), new Vector4(0.55f, 0.57f, 0.62f, 1), wallShape.BoundingRadius());
            foreach ((string id, double y) in new[] { ("station-wall-slot", HalfWidth - SideWallThickness / 2), ("station-wall-far", -(HalfWidth - SideWallThickness / 2)) })
                scene.Add(id, world.Spawn(new SpawnDesc
                {
                    Frame = frame,
                    Pose = new DPose(new Double3(0, y, 0), DQuaternion.Identity),
                    Body = new BodyDesc(wallShape, BodyMotion.Static),
                    Render = wallRender,
                }));

            BoundingBox[] spokes = [.. Spokes()];
            BoxMesh spokeMesh = new();
            foreach (BoundingBox box in spokes)
                spokeMesh.Box(box);
            CompoundShapeDesc spokeShape = Boxes(spokes);
            scene.Add("station-spokes", world.Spawn(new SpawnDesc
            {
                Frame = frame,
                Pose = DPose.Identity,
                Body = new BodyDesc(spokeShape, BodyMotion.Static),
                Render = Part(resources, spokeMesh.Build(), new Vector4(0.5f, 0.52f, 0.58f, 1), spokeShape.BoundingRadius()),
            }));

            Vector3 hubOwn = HubOwnHalf;
            Opening slot = HubSlot;
            CompoundShapeDesc hubShape = Boxes(HubWalls());
            scene.Add("station-hub", world.Spawn(new SpawnDesc
            {
                Frame = frame,
                Pose = new DPose(Double3.Zero, HubTurn),
                Body = new BodyDesc(hubShape, BodyMotion.Static),
                Render = Part(resources, new BoxMesh()
                    .Box(new BoundingBox(-hubOwn, hubOwn), openings: slot)
                    .Box(new BoundingBox(-(hubOwn - new Vector3(HubWall)), hubOwn - new Vector3(HubWall)), inward: true, openings: slot)
                    .Sides(slot)
                    .Build(), new Vector4(0.82f, 0.82f, 0.84f, 1), hubShape.BoundingRadius()),
            }));

            // The bay lamps: glowing panels flat on the bay's walls, each with its light 0.3 m out from the wall, in the
            // station's axes. Three stand near the slot, 8 m in from it, and one in the middle of the far end wall.
            Vector3 bay = HubHalf - new Vector3(HubWall);
            MaterialId lampMaterial = resources.CreateMaterial(new MaterialDesc(new Vector4(0, 0, 0, 1), SpecularIntensity: 0, Emissive: new Vector3(6, 5.4f, 4.5f)));
            (Vector3 Wall, Vector3 Inward)[] lamps =
            [
                (new Vector3(bay.X, bay.Y - 8, 0), -Vector3.UnitX),
                (new Vector3(0, bay.Y - 8, bay.Z), -Vector3.UnitZ),
                (new Vector3(0, bay.Y - 8, -bay.Z), Vector3.UnitZ),
                (new Vector3(0, -bay.Y, 0), Vector3.UnitY),
            ];
            foreach ((Vector3 at, Vector3 inward) in lamps)
            {
                Vector3 half = new Vector3(0.6f) - 0.55f * Vector3.Abs(inward);
                world.Spawn(new SpawnDesc
                {
                    Frame = frame,
                    Pose = new DPose(at + inward * half, DQuaternion.Identity),
                    Render = new RenderData
                    {
                        Mesh = resources.CreateMesh(Primitives.Box(half)),
                        Material = lampMaterial,
                        BoundingRadius = 0.85f,
                        Flags = RenderFlags.Visible | RenderFlags.NoImpostor,
                        Light = new PointLightData(new Pose(inward * 0.3f, Quaternion.Identity), new Vector3(1, 0.9f, 0.75f), Intensity: 100, Range: 20),
                    },
                });
            }

            // The rim camera: at θ = 45 degrees, between two spokes, 98 m from the axis and 2 m off the slot's face of the
            // wheel. It looks at a point on the axis between the hub and the hold point. Its roof lies across the plane of
            // the axis and the camera, so the axis runs across the picture, outward to the right.
            double theta = Math.PI / 4;
            var across = new Double3(Math.Cos(theta), 0, -Math.Sin(theta));
            Double3 rim = across * 98 + new Double3(0, HalfWidth + 2, 0);
            var aim = new Double3(0, 60, 0);
            scene.Add("rim-camera", world.Spawn(new SpawnDesc
            {
                Frame = frame,
                Pose = new DPose(rim, AttitudeAutopilot.Facing(aim - rim, Double3.Cross(across, Double3.UnitY))),
                Render = new RenderData(),
            }));

            // The bay camera: still in the bay, 1 m from its far wall, in the corner across from the parked place. It looks
            // at a point between the slot and the parked shuttle, with its roof away from the deck, so it sees the slot, the
            // nose as it comes through, the move out, the landing in the corner and the spoke's mouth in the deck.
            scene.Add("bay-camera", world.Spawn(new SpawnDesc
            {
                Frame = frame,
                Pose = new DPose(BayCamera, AttitudeAutopilot.Facing(BayCameraAim - BayCamera, Double3.UnitX)),
                Render = new RenderData(),
            }));
            return station;
        }
    }
}
