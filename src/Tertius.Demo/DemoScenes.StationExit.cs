using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The station's parts for the exit, step 6 of the Blue Danube demo (docs/plans/blue-danube-demo.md, "The exit"): the
    /// chute in the spoke at θ = 180 degrees, its top through the deck, the ceiling's opening where it passes, the lamps
    /// of the spoke and of the wheel round the chute's end, and the still cameras of the exit. Every place is in the
    /// station's axes, as in <see cref="DemoStation"/>: +Y is the axis, the spoke at θ = 180 degrees runs along -X, and
    /// the station turns toward +Z there, the spinward side.
    /// <list type="bullet">
    /// <item>The chute, <c>station-chute</c>: a trough on the spoke's antispinward side, a back plate 0.1 m thick whose
    /// face lies at z = -1 m, and two rails 0.6 m deep whose inner faces lie at y = ±0.6 m. It runs from the hub's outer
    /// face, x = -8.5 m, to 5 cm above the wheel's floor (see <see cref="Chute"/>). It is drawn yellow, and its brake run,
    /// from the ceiling's face toward the axis down, dark orange.</item>
    /// <item>The chute's top, <see cref="DeckHole"/>, a hole through the deck over the trough, whose faces run on into the
    /// trough's. <see cref="DemoStation.HubWalls"/> leaves it open.</item>
    /// <item>The ceiling's opening: the ceiling is three bands turned by half a segment, and the middle band leaves out
    /// the segment under the spoke (see <see cref="CeilingBands"/>).</item>
    /// <item>Seven lamps on the spoke's spinward wall, 12 m apart, and six under the ceiling round the chute's end.</item>
    /// <item>The views <c>deck-camera</c>, on the deck across from the hatch, <c>spoke-camera</c>, in the spoke looking up
    /// at the chute's top, and <c>wheel-camera</c>, on the floor spinward of the chute's end.</item>
    /// </list>
    /// </summary>
    public static class StationExit
    {
        /// <summary>
        /// The person's walk speed on the way out and on the floor, in m/s. The Coriolis force of a walk across the axis,
        /// 2 ω v, is then 0.63 m/s², below the deck's gravity of 0.785 m/s².
        /// </summary>
        public const double WalkSpeed = 1;

        /// <summary>
        /// The speed of the last leg of the way out, antispinward into the chute's top, in m/s. The Coriolis force of an
        /// antispinward walk points toward the axis, 2 ω v. At 1 m/s it takes 0.63 m/s² of the 0.70 m/s² of spin gravity at
        /// the person's center, and a person who walks off the edge at 1 m/s sinks too slowly to pass below the hole's far
        /// edge. At 0.4 m/s it takes 0.25 m/s², and the person sinks into the trough.
        /// </summary>
        public const double StepInSpeed = 0.4;

        /// <summary>The thickness of the chute's back plate and of each rail, in meters.</summary>
        public const float PlateThickness = 0.1f;

        /// <summary>How far the rails stand out from the back plate's face, toward +Z, in meters.</summary>
        public const float RailDepth = 0.6f;

        /// <summary>
        /// The segment of the turned ceiling under the spoke at θ = 180 degrees: segment 127 spans θ from 179.30 to 180.70
        /// degrees once the band is turned by half a segment.
        /// </summary>
        public const int CeilingGap = DemoStation.Segments / 2 - 1;

        /// <summary>The radius of a person's capsule, in meters.</summary>
        public const double PersonRadius = 0.3;

        /// <summary>
        /// The demo's chute: the back plate's face at z = -1 m, the rails' inner faces at y = ±0.6 m, from the deck down to
        /// 5 cm above the wheel's floor. The friction is 1.5 down to the ceiling's face toward the axis and 6 in the brake
        /// run below it, which gives a slide of 8.9 m/s at most and a landing at about 2.6 m/s.
        /// </summary>
        public static Chute Chute { get; } =
            new(
                BackFace: -1.0,
                HalfWidth: 0.6,
                Depth: 0.8,
                Top: DemoStation.Deck,
                BrakeFrom: -DemoStation.CeilingRadius,
                End: -99.95,
                Coefficient: 1.5,
                BrakeCoefficient: 6
            );

        /// <summary>
        /// The chute's top: a hole through the deck's wall, from x = -8.5 to -8 m, y from -0.6 to 0.6 m and z from -1.0 to
        /// -0.2 m, inside <see cref="DemoStation.SpokeMouth"/>. Its antispinward face is the plane of the back plate's face,
        /// and its faces across the axis are the planes of the rails' inner faces. It reaches 0.2 m spinward of the rails, so
        /// that a person who walks into it antispinward loses its footing before its back meets the far face.
        /// </summary>
        public static BoundingBox DeckHole { get; } =
            new(
                new(-DemoStation.HubHalf.X, -(float)Chute.HalfWidth, (float)Chute.BackFace),
                new(DemoStation.Deck, (float)Chute.HalfWidth, (float)(Chute.BackFace + Chute.Depth))
            );

        /// <summary>
        /// The turn of the ceiling's bands about +Y: half a segment, 0.703 degrees, so that the middle of a segment lies under
        /// each spoke.
        /// </summary>
        public static DQuaternion CeilingTurn { get; } =
            DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / DemoStation.Segments);

        /// <summary>The pose of <c>deck-camera</c>: 1.7 m above the deck, forward of the spoke's mouth, looking at the hatch and the chute's top.</summary>
        public static DPose DeckCamera { get; } = Camera(new(-6.3, -4.5, 4.5), new(-7.2, 2.5, -1.0), Double3.UnitX);

        /// <summary>The pose of <c>spoke-camera</c>: in the spoke, 16 m below the deck, looking up the trough at the chute's top.</summary>
        public static DPose SpokeCamera { get; } = Camera(new(-24, 1.2, 1.3), new(-8.5, 0, -0.6), Double3.UnitZ);

        /// <summary>
        /// The pose of <c>wheel-camera</c>: on the floor 9 m spinward of the chute's end, 2.5 m to the side, 1.7 m above the
        /// floor, looking at the chute's end, with its roof toward the axis.
        /// </summary>
        public static DPose WheelCamera { get; } = WheelCameraPose();

        /// <summary>
        /// Where <c>slide-camera</c> stands relative to the person's center, in the station's axes: 3 m up the spoke, behind
        /// the person, which slides feet first, and 1 m toward the spoke's middle. So it stands in the open side of the
        /// trough, 0.7 m above the rails' edges, and 1.4 m from the spoke's spinward wall.
        /// </summary>
        public static Double3 SlideCameraOffset { get; } = new(3.0, 0, 1.0);

        /// <summary>
        /// The way that <c>slide-camera</c> looks, in the station's axes: from <see cref="SlideCameraOffset"/> at a point
        /// 1.5 m down the spoke from the person's center and 0.1 m nearer the back plate. So it looks down the trough past the
        /// person, with the top of the picture toward +Z, the spoke's spinward wall and its lamps.
        /// </summary>
        public static Double3 SlideCameraLook { get; } = new Double3(-1.5, 0, -0.1) - SlideCameraOffset;

        /// <summary>
        /// The ceiling's three bands, each with its scene id, its ring and its pose in the station's frame: the middle band
        /// <c>station-ceiling</c>, from y = -1.75 to 1.75 m, the spoke's inside, which leaves out <see cref="CeilingGap"/>,
        /// and <c>station-ceiling-slot</c> and <c>station-ceiling-far</c> out to y = ±10 m. All three turn by
        /// <see cref="CeilingTurn"/>. The opening spans z from -1.159 to 1.159 m at the ceiling's face toward the axis, inside
        /// the spoke's inside, so the spoke's tube closes it all round.
        /// </summary>
        public static IEnumerable<(string Id, RevolvedSolid Band, DPose Pose)> CeilingBands()
        {
            float inner = DemoStation.CeilingRadius,
                outer = DemoStation.CeilingRadius + DemoStation.CeilingThickness;
            float middle = DemoStation.SpokeHalfWidth - DemoStation.SpokeWall,
                side = (DemoStation.HalfWidth - middle) / 2;
            yield return (
                "station-ceiling",
                RevolvedSolid
                    .Ring(innerRadius: inner, outerRadius: outer, halfWidth: middle, segments: DemoStation.Segments)
                    .LeavingOut(CeilingGap),
                new(Double3.Zero, CeilingTurn)
            );
            var band = RevolvedSolid.Ring(
                innerRadius: inner,
                outerRadius: outer,
                halfWidth: side,
                segments: DemoStation.Segments
            );
            yield return ("station-ceiling-slot", band, new(new(0, middle + side, 0), CeilingTurn));
            yield return ("station-ceiling-far", band, new(new(0, -(middle + side), 0), CeilingTurn));
        }

        /// <summary>
        /// The boxes of the chute's body: the back plate, and the rails toward +Y and toward -Y, from the hub's outer face
        /// to the trough's end.
        /// </summary>
        public static IEnumerable<BoundingBox> ChuteBoxes() => ChuteBoxes((float)Chute.End, -DemoStation.HubHalf.X);

        /// <summary>The boxes of the chute's body from x = <paramref name="low"/> to <paramref name="high"/>.</summary>
        private static IEnumerable<BoundingBox> ChuteBoxes(float low, float high)
        {
            float back = (float)Chute.BackFace,
                half = (float)Chute.HalfWidth;
            yield return new(
                new(low, -half - PlateThickness, back - PlateThickness),
                new(high, half + PlateThickness, back)
            );
            yield return new(new(low, half, back), new(high, half + PlateThickness, back + RailDepth));
            yield return new(new(low, -half - PlateThickness, back), new(high, -half, back + RailDepth));
        }

        /// <summary>
        /// The person's way out, from <see cref="Shuttle.SeatStand"/> to the chute's top: aft through the cabin to
        /// <see cref="Shuttle.InsideHatch"/>, in <paramref name="cabin"/>, the shuttle's frame; then in
        /// <paramref name="station"/>, the station's frame, out of the hatch to 0.6 m spinward of the deck's middle, along the
        /// axis to beside the chute's top, and antispinward into it, to where the person's back meets the back plate's
        /// plane. A person who stands has its center 0.9 m above the deck.
        /// </summary>
        public static Waypoint[] WayOut(FrameHandle cabin, FrameHandle station)
        {
            double x = DemoStation.Deck + 0.9,
                lane = 0.6;
            return
            [
                new(cabin, Shuttle.InsideHatch),
                new(station, new(x, Shuttle.Hatch.Center.Z + DemoStation.ParkedPose.Position.Y, lane)),
                new(station, new(x, 0, lane)),
                new(station, new(x, 0, Chute.BackFace + PersonRadius)),
            ];
        }

        /// <summary>
        /// The person's stroll on the wheel's floor, in <paramref name="station"/>, the station's frame: from where it lands
        /// in the trough, spinward out of the trough's open side to 4.5 m spinward of the spoke's middle, with its center
        /// 0.9 m above the floor, 99.1 m from the axis.
        /// </summary>
        public static Waypoint[] Stroll(FrameHandle station)
        {
            const double z = 4.5,
                radius = DemoStation.FloorRadius - 0.9;
            return [new(station, new(-Math.Sqrt(radius * radius - z * z), 0, z))];
        }

        /// <summary>
        /// The exit component of the person whose seat is in <paramref name="cabin"/>, the docked shuttle's frame, in the
        /// station whose frame is <paramref name="station"/>: the demo's chute, <see cref="WayOut"/>, <see cref="Stroll"/>
        /// <see cref="WalkSpeed"/>, <see cref="StepInSpeed"/> and <see cref="PersonRadius"/>.
        /// </summary>
        public static Disembark Disembark(FrameHandle cabin, FrameHandle station) =>
            new(
                station: station,
                chute: Chute,
                wayOut: WayOut(cabin, station),
                stroll: Stroll(station),
                walkSpeed: WalkSpeed,
                stepInSpeed: StepInSpeed,
                radius: PersonRadius
            );

        private static DPose WheelCameraPose()
        {
            const double z = 9,
                radius = DemoStation.FloorRadius - 1.7;
            var at = new Double3(-Math.Sqrt(radius * radius - z * z), 2.5, z);
            return Camera(at, new(-97, 0, -0.7), -new Double3(at.X, 0, at.Z).Normalized());
        }

        private static DPose Camera(Double3 at, Double3 target, Double3 roof) =>
            new(at, AttitudeAutopilot.Facing(target - at, roof));

        /// <summary>
        /// Spawns <c>slide-camera</c>, attached to <paramref name="person"/>: a <see cref="FrameFollowCamera"/> that keeps
        /// <see cref="SlideCameraOffset"/> and <see cref="SlideCameraLook"/> in the axes of <paramref name="station"/>, the
        /// station's frame, with the top of the picture toward +Z. It films the slide down the spoke from behind and above the
        /// person. The <c>follow-camera</c> cannot: its heading follows the person's way across its up, and in the slide that
        /// way lies along the up, so the heading rests where the deck left it, along the axis, and the arm ends in the rail.
        /// </summary>
        public static Entity SpawnSlideCamera(World world, Entity person, FrameHandle station) =>
            world.Spawn(
                new()
                {
                    AttachTo = person,
                    AttachOffset = new(SlideCameraOffset.ToVector3(), Quaternion.Identity),
                    Render = new RenderData(),
                    Components =
                    [
                        new FrameFollowCamera(
                            reference: station,
                            offset: SlideCameraOffset,
                            forward: SlideCameraLook,
                            up: Double3.UnitZ
                        ),
                    ],
                }
            );

        /// <summary>
        /// Spawns the exit's parts in <paramref name="station"/>, the station's frame: the chute's body and its brake run,
        /// the lamps and the three still cameras, with their scene ids in <paramref name="scene"/>.
        /// </summary>
        public static void Spawn(World world, FrameHandle station, SceneInstance scene, IRenderResources resources)
        {
            SpawnChute(world: world, station: station, scene: scene, resources: resources);
            SpawnLamps(world: world, station: station, resources: resources);
            scene.Add("deck-camera", world.Spawn(ViewIn(station, DeckCamera)));
            scene.Add("spoke-camera", world.Spawn(ViewIn(station, SpokeCamera)));
            scene.Add("wheel-camera", world.Spawn(ViewIn(station, WheelCamera)));
        }

        /// <summary>
        /// Spawns the chute in <paramref name="station"/>: one static body over all of its boxes, drawn as the upper part
        /// and, in another color, the brake run.
        /// </summary>
        private static void SpawnChute(
            World world,
            FrameHandle station,
            SceneInstance scene,
            IRenderResources resources
        )
        {
            float brake = (float)Chute.BrakeFrom;
            BoxMesh upper = new(),
                lower = new();
            foreach (BoundingBox box in ChuteBoxes(brake, -DemoStation.HubHalf.X))
                upper.Box(box);
            foreach (BoundingBox box in ChuteBoxes((float)Chute.End, brake))
                lower.Box(box);
            CompoundShapeDesc shape = new([
                .. ChuteBoxes()
                    .Select(box => new CompoundChild(
                        new BoxShapeDesc(box.Extents),
                        new(box.Center, Quaternion.Identity)
                    )),
            ]);
            float reach = shape.BoundingRadius();
            scene.Add(
                "station-chute",
                world.Spawn(
                    new()
                    {
                        Frame = station,
                        Pose = DPose.Identity,
                        Body = new(Shape: shape, Motion: BodyMotion.Static),
                        Render = Drawn(
                            resources: resources,
                            mesh: upper.Build(),
                            color: new(x: 0.85f, y: 0.7f, z: 0.15f, w: 1),
                            boundingRadius: reach
                        ) with
                        {
                            Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
                        },
                    }
                )
            );
            scene.Add(
                "station-chute-brake",
                world.Spawn(
                    new()
                    {
                        Frame = station,
                        Pose = DPose.Identity,
                        Render = Drawn(
                            resources: resources,
                            mesh: lower.Build(),
                            color: new(x: 0.7f, y: 0.3f, z: 0.08f, w: 1),
                            boundingRadius: reach
                        ) with
                        {
                            Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
                        },
                    }
                )
            );
        }

        /// <summary>Spawns the lamps of the spoke and of the wheel's ceiling in <paramref name="station"/>.</summary>
        private static void SpawnLamps(World world, FrameHandle station, IRenderResources resources)
        {
            // Cabin lamps behind glowing panels 1.2 m square.
            MaterialId lampMaterial = LampPanelMaterial(resources: resources, power: CabinLampPower, area: 1.2f * 1.2f);
            MeshId wallPanel = resources.CreateMesh(Primitives.Box(new(0.6f, 0.6f, 0.05f)));
            MeshId ceilingPanel = resources.CreateMesh(Primitives.Box(new(0.05f, 0.6f, 0.6f)));
            // The spoke lamps, on the spoke's spinward wall, each with its light 0.3 m in front of it.
            float wall = DemoStation.SpokeHalfWidth - DemoStation.SpokeWall;
            foreach (int x in new[] { -14, -26, -38, -50, -62, -74, -86 })
                world.Spawn(
                    Lamp(
                        frame: station,
                        pose: new(new(x, 0, wall - 0.05), DQuaternion.Identity),
                        mesh: wallPanel,
                        material: lampMaterial,
                        light: LampLight(offset: new(0, 0, -0.3f), power: CabinLampPower)
                    )
                );
            // The wheel lamps, flat under the ceiling's face toward the floor, each turned so that its local +X points away
            // from the axis, with its light 0.3 m below it.
            double under = DemoStation.CeilingRadius + DemoStation.CeilingThickness + 0.06;
            foreach (double y in new[] { -5.0, 5.0 })
            foreach (double z in new[] { -6.0, 2.0, 10.0 })
            {
                var at = new Double3(-Math.Sqrt(under * under - z * z), y, z);
                double theta = Math.Atan2(-at.Z, at.X);
                world.Spawn(
                    Lamp(
                        frame: station,
                        pose: new(at, DQuaternion.FromAxisAngle(Double3.UnitY, theta)),
                        mesh: ceilingPanel,
                        material: lampMaterial,
                        light: LampLight(offset: new(0.3f, 0, 0), power: CabinLampPower)
                    )
                );
            }
        }

        private static SpawnDesc Lamp(
            FrameHandle frame,
            DPose pose,
            MeshId mesh,
            MaterialId material,
            PointLightData light
        ) =>
            new()
            {
                Frame = frame,
                Pose = pose,
                Render = new RenderData
                {
                    Mesh = mesh,
                    Material = material,
                    BoundingRadius = 0.9f,
                    Flags = RenderFlags.Visible | RenderFlags.NoImpostor,
                    Light = light,
                },
            };
    }
}
