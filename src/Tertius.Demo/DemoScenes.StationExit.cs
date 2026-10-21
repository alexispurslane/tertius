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
        /// The demo's chute: the back plate's face at z = -1 m, the rails' inner faces at y = ±0.6 m, from the deck down to
        /// 5 cm above the wheel's floor. The friction is 1.5 down to the ceiling's face toward the axis and 6 in the brake
        /// run below it, which gives a slide of 8.9 m/s at most and a landing at about 2.6 m/s.
        /// </summary>
        public static Chute Chute { get; } = new(BackFace: -1.0, HalfWidth: 0.6, Top: DemoStation.Deck, BrakeFrom: -DemoStation.CeilingRadius, End: -99.95,
            Friction: 1.5, BrakeFriction: 6);

        /// <summary>The thickness of the chute's back plate and of each rail, in meters.</summary>
        public const float PlateThickness = 0.1f;

        /// <summary>How far the rails stand out from the back plate's face, toward +Z, in meters.</summary>
        public const float RailDepth = 0.6f;

        /// <summary>
        /// The chute's top: a hole through the deck's wall, from x = -8.5 to -8 m, y from -0.6 to 0.6 m and z from -1.0 to
        /// -0.2 m, inside <see cref="DemoStation.SpokeMouth"/>. Its antispinward face is the plane of the back plate's face,
        /// and its faces across the axis are the planes of the rails' inner faces. It reaches 0.2 m spinward of the rails, so
        /// that a person who walks into it antispinward loses its footing before its back meets the far face.
        /// </summary>
        public static BoundingBox DeckHole { get; } = new(new Vector3(-DemoStation.HubHalf.X, -(float)Chute.HalfWidth, (float)Chute.BackFace),
            new Vector3(DemoStation.Deck, (float)Chute.HalfWidth, -0.2f));

        /// <summary>
        /// The turn of the ceiling's bands about +Y: half a segment, 0.703 degrees, so that the middle of a segment lies under
        /// each spoke.
        /// </summary>
        public static DQuaternion CeilingTurn { get; } = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / DemoStation.Segments);

        /// <summary>
        /// The segment of the turned ceiling under the spoke at θ = 180 degrees: segment 127 spans θ from 179.30 to 180.70
        /// degrees once the band is turned by half a segment.
        /// </summary>
        public const int CeilingGap = DemoStation.Segments / 2 - 1;

        /// <summary>
        /// The ceiling's three bands, each with its scene id, its ring and its pose in the station's frame: the middle band
        /// <c>station-ceiling</c>, from y = -1.75 to 1.75 m, the spoke's inside, which leaves out <see cref="CeilingGap"/>,
        /// and <c>station-ceiling-slot</c> and <c>station-ceiling-far</c> out to y = ±10 m. All three turn by
        /// <see cref="CeilingTurn"/>. The opening spans z from -1.159 to 1.159 m at the ceiling's face toward the axis, inside
        /// the spoke's inside, so the spoke's tube closes it all round.
        /// </summary>
        public static IEnumerable<(string Id, RevolvedSolid Band, DPose Pose)> CeilingBands()
        {
            float inner = DemoStation.CeilingRadius, outer = DemoStation.CeilingRadius + DemoStation.CeilingThickness;
            float middle = DemoStation.SpokeHalfWidth - DemoStation.SpokeWall, side = (DemoStation.HalfWidth - middle) / 2;
            yield return ("station-ceiling", RevolvedSolid.Ring(inner, outer, middle, DemoStation.Segments).LeavingOut(CeilingGap), new DPose(Double3.Zero, CeilingTurn));
            RevolvedSolid band = RevolvedSolid.Ring(inner, outer, side, DemoStation.Segments);
            yield return ("station-ceiling-slot", band, new DPose(new Double3(0, middle + side, 0), CeilingTurn));
            yield return ("station-ceiling-far", band, new DPose(new Double3(0, -(middle + side), 0), CeilingTurn));
        }

        /// <summary>
        /// The boxes of the chute's body: the back plate, and the rails toward +Y and toward -Y, from the hub's outer face
        /// to the trough's end.
        /// </summary>
        public static IEnumerable<BoundingBox> ChuteBoxes() => ChuteBoxes((float)Chute.End, -DemoStation.HubHalf.X);

        /// <summary>The boxes of the chute's body from x = <paramref name="low"/> to <paramref name="high"/>.</summary>
        private static IEnumerable<BoundingBox> ChuteBoxes(float low, float high)
        {
            float back = (float)Chute.BackFace, half = (float)Chute.HalfWidth;
            yield return new BoundingBox(new Vector3(low, -half - PlateThickness, back - PlateThickness), new Vector3(high, half + PlateThickness, back));
            yield return new BoundingBox(new Vector3(low, half, back), new Vector3(high, half + PlateThickness, back + RailDepth));
            yield return new BoundingBox(new Vector3(low, -half - PlateThickness, back), new Vector3(high, -half, back + RailDepth));
        }

        /// <summary>The pose of <c>deck-camera</c>: 1.7 m above the deck, forward of the spoke's mouth, looking at the hatch and the chute's top.</summary>
        public static DPose DeckCamera { get; } = Camera(new Double3(-6.3, -4.5, 4.5), new Double3(-7.2, 2.5, -1.0), Double3.UnitX);

        /// <summary>The pose of <c>spoke-camera</c>: in the spoke, 16 m below the deck, looking up the trough at the chute's top.</summary>
        public static DPose SpokeCamera { get; } = Camera(new Double3(-24, 1.2, 1.3), new Double3(-8.5, 0, -0.6), Double3.UnitZ);

        /// <summary>
        /// The pose of <c>wheel-camera</c>: on the floor 9 m spinward of the chute's end, 2.5 m to the side, 1.7 m above the
        /// floor, looking at the chute's end, with its roof toward the axis.
        /// </summary>
        public static DPose WheelCamera { get; } = WheelCameraPose();

        private static DPose WheelCameraPose()
        {
            const double z = 9, radius = DemoStation.FloorRadius - 1.7;
            var at = new Double3(-Math.Sqrt(radius * radius - z * z), 2.5, z);
            return Camera(at, new Double3(-97, 0, -0.7), -new Double3(at.X, 0, at.Z).Normalized());
        }

        private static DPose Camera(Double3 at, Double3 target, Double3 roof) => new(at, AttitudeAutopilot.Facing(target - at, roof));

        /// <summary>
        /// Spawns the exit's parts in <paramref name="station"/>, the station's frame: the chute's body and its brake run,
        /// the lamps and the three still cameras, with their scene ids in <paramref name="scene"/>.
        /// </summary>
        public static void Spawn(World world, FrameHandle station, SceneInstance scene, IRenderResources resources)
        {
            float brake = (float)Chute.BrakeFrom;
            BoxMesh upper = new(), lower = new();
            foreach (BoundingBox box in ChuteBoxes(brake, -DemoStation.HubHalf.X))
                upper.Box(box);
            foreach (BoundingBox box in ChuteBoxes((float)Chute.End, brake))
                lower.Box(box);
            CompoundShapeDesc shape = new([.. ChuteBoxes().Select(box => new CompoundChild(new BoxShapeDesc(box.Extents), new Pose(box.Center, Quaternion.Identity)))]);
            float reach = shape.BoundingRadius();
            scene.Add("station-chute", world.Spawn(new SpawnDesc
            {
                Frame = station,
                Pose = DPose.Identity,
                Body = new BodyDesc(shape, BodyMotion.Static),
                Render = Drawn(resources, upper.Build(), new Vector4(0.85f, 0.7f, 0.15f, 1), reach) with { Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor },
            }));
            scene.Add("station-chute-brake", world.Spawn(new SpawnDesc
            {
                Frame = station,
                Pose = DPose.Identity,
                Render = Drawn(resources, lower.Build(), new Vector4(0.7f, 0.3f, 0.08f, 1), reach) with { Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor },
            }));

            MaterialId lampMaterial = resources.CreateMaterial(new MaterialDesc(new Vector4(0, 0, 0, 1), SpecularIntensity: 0, Emissive: new Vector3(6, 5.4f, 4.5f)));
            MeshId wallPanel = resources.CreateMesh(Primitives.Box(new Vector3(0.6f, 0.6f, 0.05f)));
            MeshId ceilingPanel = resources.CreateMesh(Primitives.Box(new Vector3(0.05f, 0.6f, 0.6f)));
            // The spoke lamps, on the spoke's spinward wall, each with its light 0.3 m in front of it.
            float wall = DemoStation.SpokeHalfWidth - DemoStation.SpokeWall;
            foreach (int x in new[] { -14, -26, -38, -50, -62, -74, -86 })
                world.Spawn(Lamp(station, new DPose(new Double3(x, 0, wall - 0.05), DQuaternion.Identity), wallPanel, lampMaterial,
                    new PointLightData(new Pose(new Vector3(0, 0, -0.3f), Quaternion.Identity), new Vector3(1, 0.9f, 0.75f), Intensity: 60, Range: 14)));
            // The wheel lamps, flat under the ceiling's face toward the floor, each turned so that its local +X points away
            // from the axis, with its light 0.3 m below it.
            double under = DemoStation.CeilingRadius + DemoStation.CeilingThickness + 0.06;
            foreach (double y in new[] { -5.0, 5.0 })
                foreach (double z in new[] { -6.0, 2.0, 10.0 })
                {
                    var at = new Double3(-Math.Sqrt(under * under - z * z), y, z);
                    double theta = Math.Atan2(-at.Z, at.X);
                    world.Spawn(Lamp(station, new DPose(at, DQuaternion.FromAxisAngle(Double3.UnitY, theta)), ceilingPanel, lampMaterial,
                        new PointLightData(new Pose(new Vector3(0.3f, 0, 0), Quaternion.Identity), new Vector3(1, 0.9f, 0.75f), Intensity: 100, Range: 20)));
                }

            scene.Add("deck-camera", world.Spawn(new SpawnDesc { Frame = station, Pose = DeckCamera, Render = new RenderData() }));
            scene.Add("spoke-camera", world.Spawn(new SpawnDesc { Frame = station, Pose = SpokeCamera, Render = new RenderData() }));
            scene.Add("wheel-camera", world.Spawn(new SpawnDesc { Frame = station, Pose = WheelCamera, Render = new RenderData() }));
        }

        private static SpawnDesc Lamp(FrameHandle frame, DPose pose, MeshId mesh, MaterialId material, PointLightData light) => new()
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
