using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The station's parts for the exit, step 6 of the Blue Danube demo (docs/plans/blue-danube-demo.md, "The exit" and
    /// "The lift"): the lift in the spoke at θ = 180 degrees, its hole through the deck, the ceiling's opening where it
    /// passes, the lamps of the shaft, of the lift and of the wheel round the lift's foot, and the exit's cameras. Every place
    /// is in the station's axes, from <see cref="DemoStation.Layout"/>: +Y is the axis, the lift's spoke runs along -X, and
    /// the station turns toward +Z there, the spinward side.
    /// <list type="bullet">
    /// <item>The lift, <c>lift</c>: a kinematic platform 4 m square, whose floor lies flush with the deck in the lift's hole
    /// at the top. Railings 1.1 m tall close its antispinward, spinward and -Y sides, and it opens toward +Y. Its
    /// <see cref="Lift"/> rides it down the spoke on <see cref="Ride"/> to a step above the wheel's floor.</item>
    /// <item>The ceiling's opening: the ceiling is three bands turned by half a segment, and the middle band leaves out
    /// the segment under the spoke (see <see cref="CeilingBands"/>).</item>
    /// <item>Lamps on the shaft's spinward wall every 150 m, two on the lift's railings, and six under the ceiling round the
    /// lift's foot.</item>
    /// <item>The views <c>deck-camera</c>, on the deck, <c>shaft-camera</c>, in the shaft below the deck,
    /// <c>ride-camera</c>, on the lift, and <c>wheel-camera</c>, on the floor spinward of the lift's foot.</item>
    /// </list>
    /// </summary>
    public static class StationExit
    {
        /// <summary>
        /// The person's walk speed on the way out and on the floor, in m/s. The Coriolis force of a walk across the axis,
        /// 2 ω v, is then 0.14 m/s², below the deck's spin gravity of 0.196 m/s².
        /// </summary>
        public const double WalkSpeed = 1;

        /// <summary>The radius of a person's capsule, in meters.</summary>
        public const double PersonRadius = 0.3;

        /// <summary>How high a person's center stands above the floor under it, in meters.</summary>
        public const double Standing = 0.9;

        /// <summary>The thickness of the lift's floor, in meters.</summary>
        public const float LiftFloorThickness = 0.2f;

        /// <summary>How tall the lift's railings stand above its floor, in meters.</summary>
        public const float RailHeight = 1.1f;

        /// <summary>The thickness of a railing, in meters.</summary>
        public const float RailThickness = 0.1f;

        /// <summary>
        /// The share of the local spin gravity at which the lift's boost accelerates outward. The rider keeps the rest of its
        /// weight: half.
        /// </summary>
        public const double BoostShare = 0.5;

        /// <summary>The lift's cruise speed, in m/s: below 50 m/s, from which a group with a character gets a numeric frame.</summary>
        public const double CruiseSpeed = 20;

        /// <summary>The lift's brake, in m/s², which the rider feels as weight added to the spin gravity.</summary>
        public const double Brake = 1;

        /// <summary>How far apart the shaft's lamps lie, in meters.</summary>
        public const double ShaftLampSpacing = 150;

        private static StationLayout Layout => DemoStation.Layout;

        /// <summary>
        /// The segment of the turned ceiling under the lift's spoke at θ = 180 degrees: once the band is turned by half a
        /// segment, the segment before the half spans the angle of one segment centered on the spoke.
        /// </summary>
        public static int CeilingGap => Layout.Segments / 2 - 1;

        /// <summary>The turn of the ceiling's bands about +Y: half a segment, so that the middle of a segment lies under each spoke.</summary>
        public static DQuaternion CeilingTurn => DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / Layout.Segments);

        /// <summary>The lift's pose at the top: the middle of its floor's top face, flush with the deck, unturned.</summary>
        public static DPose LiftTop => new(new(Layout.Deck, 0, Layout.LiftAcross), DQuaternion.Identity);

        /// <summary>The way that the lift rides, along its spoke away from the axis: -X.</summary>
        public static Double3 LiftOutward => -Double3.UnitX;

        /// <summary>
        /// The lift's ride, from the deck to <see cref="StationLayout.LiftFootStep"/> above the wheel's floor: a boost at
        /// <see cref="BoostShare"/> of the spin gravity to <see cref="CruiseSpeed"/>, and a brake at <see cref="Brake"/>.
        /// </summary>
        public static LiftProfile Ride =>
            LiftProfile.Of(
                rate: Layout.Rate,
                top: Layout.DeckRadius,
                foot: Layout.LiftFoot,
                boostShare: BoostShare,
                cruiseSpeed: CruiseSpeed,
                brake: Brake
            );

        /// <summary>
        /// The ceiling's three bands, each with its scene id, its ring and its pose in the station's frame: the middle band
        /// <c>station-ceiling</c>, as wide as the spoke's inside, which leaves out <see cref="CeilingGap"/>, and
        /// <c>station-ceiling-slot</c> and <c>station-ceiling-far</c> out to the side walls. All three turn by
        /// <see cref="CeilingTurn"/>, so the opening lies inside the spoke's inside, and the spoke's tube closes it all round.
        /// </summary>
        public static IEnumerable<(string Id, RevolvedSolid Band, DPose Pose)> CeilingBands()
        {
            float inner = Layout.CeilingRadius,
                outer = Layout.CeilingRadius + Layout.CeilingThickness;
            float middle = Layout.SpokeHalfWidth - Layout.SpokeWall,
                side = (Layout.HalfWidth - middle) / 2;
            yield return (
                "station-ceiling",
                RevolvedSolid
                    .Ring(innerRadius: inner, outerRadius: outer, halfWidth: middle, segments: Layout.Segments)
                    .LeavingOut(CeilingGap),
                new(Double3.Zero, CeilingTurn)
            );
            var band = RevolvedSolid.Ring(
                innerRadius: inner,
                outerRadius: outer,
                halfWidth: side,
                segments: Layout.Segments
            );
            yield return ("station-ceiling-slot", band, new(new(0, middle + side, 0), CeilingTurn));
            yield return ("station-ceiling-far", band, new(new(0, -(middle + side), 0), CeilingTurn));
        }

        /// <summary>
        /// The boxes of the lift's body, in its own axes, from the middle of its floor's top face: the floor, and the
        /// railings on its antispinward (-Z), spinward (+Z) and -Y sides, on the floor's edges. Its +Y side is open.
        /// </summary>
        public static IEnumerable<BoundingBox> LiftBoxes()
        {
            float half = Layout.LiftHalf,
                inner = half - RailThickness;
            yield return new(new(-LiftFloorThickness, -half, -half), new(0, half, half));
            yield return new(new(0, -half, -half), new(RailHeight, half, -inner));
            yield return new(new(0, -half, inner), new(RailHeight, half, half));
            yield return new(new(0, -half, -inner), new(RailHeight, -inner, inner));
        }

        /// <summary>
        /// Where the deck's boarding ramp stands in the station's frame: the middle of its landing's edge, on its top face,
        /// <see cref="BoardingStair.Gap"/> spinward of the parked hull's starboard face in line with the hatch,
        /// <see cref="BoardingStair.LipDrop"/> under the porch.
        /// </summary>
        public static Double3 StairPlace =>
            Layout.ParkedPose.TransformPoint(
                new Double3(
                    Shuttle.Hull.Max.X + BoardingStair.Gap,
                    Shuttle.Hatch.Min.Y - BoardingStair.LipDrop,
                    Shuttle.Hatch.Center.Z
                )
            );

        /// <summary>
        /// The deck's boarding ramp from the porch down to the deck: its landing reaches spinward (+Z) from the hull, and
        /// its ramp of 10 degrees runs down along the axis toward the lift (-Y), 18 m long, so the walk to the lift needs no
        /// turn back and crosses no Coriolis force.
        /// </summary>
        public static BoardingStair Stair =>
            new(
                Top: Vector3.Zero,
                Up: Vector3.UnitX,
                Out: Vector3.UnitZ,
                Down: -Vector3.UnitY,
                Height: (float)(StairPlace.X - Layout.Deck),
                Ramp: true
            );

        /// <summary>
        /// The person's way out, from <see cref="Shuttle.SeatStand"/> onto the lift: aft through the cabin to
        /// <see cref="Shuttle.InsideHatch"/> and out onto <see cref="Shuttle.OnPorch"/>, in <paramref name="cabin"/>, the
        /// shuttle's frame; then in <paramref name="station"/>, the station's frame, onto the landing of
        /// <see cref="Stair"/>, down its steps to its foot, and along the axis onto the lift through its open +Y side, to
        /// the middle of its floor. A person who stands has its center <see cref="Standing"/> above what it stands on.
        /// </summary>
        public static Waypoint[] WayOut(FrameHandle cabin, FrameHandle station)
        {
            Double3 stair = StairPlace;
            Double3 foot = stair + Stair.AtFoot(above: (float)Standing);
            return
            [
                new(cabin, Shuttle.RampTop),
                new(cabin, Shuttle.RampFoot),
                new(cabin, Shuttle.AftAisle),
                new(cabin, Shuttle.InsideHatch),
                new(cabin, Shuttle.OnPorch),
                new(station, stair + Stair.OnLanding(above: (float)Standing)),
                new(station, foot),
                new(station, new(Layout.Deck + Standing, LiftTop.Position.Y, Layout.LiftAcross)),
            ];
        }

        /// <summary>
        /// The person's stroll on the wheel's floor, in <paramref name="station"/>, the station's frame: off the lift at the
        /// foot through its open +Y side and down its step, to 1.5 m beyond the lift's edge, with its center
        /// <see cref="Standing"/> above the floor.
        /// </summary>
        public static Waypoint[] Stroll(FrameHandle station) =>
            [new(station, OnFloor(across: Layout.LiftAcross, height: Standing, y: Layout.LiftHalf + 1.5))];

        /// <summary>
        /// The exit component of the person whose seat is in <paramref name="cabin"/>, the docked shuttle's frame, in the
        /// station whose frame is <paramref name="station"/> and whose lift is <paramref name="lift"/>: <see cref="WayOut"/>,
        /// <see cref="Stroll"/> and <see cref="WalkSpeed"/>.
        /// </summary>
        public static Disembark Disembark(FrameHandle cabin, FrameHandle station, Entity lift) =>
            new(
                station: station,
                lift: lift,
                wayOut: WayOut(cabin, station),
                stroll: Stroll(station),
                walkSpeed: WalkSpeed
            );

        /// <summary>The place <paramref name="height"/> above the wheel's floor at <paramref name="across"/> along Z on the lift's side of the axis, at <paramref name="y"/>.</summary>
        private static Double3 OnFloor(double across, double height, double y)
        {
            WheelFloor floor = Layout.WheelFloor;
            double theta = Math.PI + Math.Asin(across / floor.Radius);
            return floor.Above(theta, height, y);
        }

        /// <summary>The pose of <c>deck-camera</c>: 1.7 m above the deck, beyond the lift from the hatch, looking at the hatch and the lift.</summary>
        public static DPose DeckCamera
        {
            get
            {
                double hatch = Shuttle.Hatch.Center.Z + Layout.ParkedPose.Position.Y;
                return Camera(
                    new(Layout.Deck + 1.7, LiftTop.Position.Y - 7, Layout.LiftAcross + 4),
                    new(Layout.Deck + 0.9, hatch / 2, Layout.LiftAcross - 2),
                    Double3.UnitX
                );
            }
        }

        /// <summary>The pose of <c>shaft-camera</c>: in the shaft 25 m below the deck, beside the lift's way, looking up at the lift's start.</summary>
        public static DPose ShaftCamera =>
            Camera(
                new(Layout.Deck - 25, LiftTop.Position.Y - 5, Layout.LiftAcross + 4),
                LiftTop.Position,
                Double3.UnitZ
            );

        /// <summary>
        /// The pose of <c>wheel-camera</c>: on the floor 22 m spinward of the lift's lane and 6 m to its +Y side, 2 m above
        /// the floor, looking at the lift's way halfway up to the ceiling, with its roof toward the axis. So it sees the lift
        /// come down through the ceiling's opening and the person step off at the foot.
        /// </summary>
        public static DPose WheelCamera
        {
            get
            {
                Double3 at = OnFloor(across: Layout.LiftAcross + 22, height: 2, y: 6);
                return Camera(
                    at,
                    OnFloor(across: Layout.LiftAcross, height: Layout.CeilingHeight / 2, y: 0),
                    WheelFloor.UpAt(at)
                );
            }
        }

        /// <summary>
        /// The pose of <c>ride-camera</c> on the lift, in the lift's axes: 4 m above the middle of its floor, over its -Y
        /// spinward corner, looking down the shaft past the person, with the top of the picture toward the spinward wall.
        /// </summary>
        public static DPose RideCamera => Camera(new(4, -1.7, 1.7), new(-4, 0, 0), Double3.UnitZ);

        /// <summary>
        /// The pose of <c>docked-camera</c>: 3 m above the deck, 15 m spinward of the axis's plane and 1 m inside the bay's +Y
        /// end, behind the parked hull's tail, looking at the hull's starboard side 6 m ahead of its origin. It sees the
        /// docked shuttle at three quarters from behind, its starboard side with the hatch, and the person who walks out over
        /// the lip and down the ramp away from the camera. The work area's pods stand to the right of its line of sight.
        /// </summary>
        public static DPose DockedCamera =>
            Camera(
                new(Layout.Deck + 3, Layout.Bay.Max.Y - 1, 15),
                new(Layout.Deck + 3, Layout.ParkedPose.Position.Y - 6, 2),
                Double3.UnitX
            );

        /// <summary>
        /// The pose of <c>work-camera</c>: 1.2 m above the deck in the bay's spinward corner near the lift, looking past the
        /// work area's cart and pods toward the parked hull's tail, with the ramp and the person on it beyond them.
        /// </summary>
        public static DPose WorkCamera =>
            Camera(new(Layout.Deck + 1.2, 8, Layout.BayHalfAcross - 1.5), new(Layout.Deck + 1.5, 26, 4), Double3.UnitX);

        /// <summary>
        /// The pose of <c>wheel-terminal-camera</c>: 1.7 m above the wheel's floor, 9 m spinward of the lift's spoke and 10 m to
        /// its +Y side, looking past the stroll's end at the terminal's benches and post, antispinward of the lift's foot.
        /// </summary>
        public static DPose WheelTerminalCamera
        {
            get
            {
                Double3 at = OnFloor(across: 9, height: 1.7, y: 10);
                return Camera(at, OnFloor(across: -3.5, height: 1, y: 0), WheelFloor.UpAt(at));
            }
        }

        /// <summary>
        /// The pose of <c>ride-side-camera</c> on the lift, in the lift's axes: 0.4 m above the middle of its floor's open +Y
        /// edge, looking across the floor along -Y at the person's feet, with its roof toward the axis. It sees the person's
        /// soles and the floor under them side on, 2 m away.
        /// </summary>
        public static DPose RideSideCamera => Camera(new(0.4, Layout.LiftHalf, 0), new(0.4, 0, 0), Double3.UnitX);

        private static DPose Camera(Double3 at, Double3 target, Double3 roof) =>
            new(at, AttitudeAutopilot.Facing(target - at, roof));

        /// <summary>
        /// Spawns the exit's parts in <paramref name="station"/>, the station's frame: the lift with its lamps and its camera,
        /// the shaft's and the wheel's lamps, and the still cameras, with their scene ids in <paramref name="scene"/>.
        /// </summary>
        public static void Spawn(World world, FrameHandle station, SceneInstance scene, IRenderResources resources)
        {
            Entity lift = SpawnLift(world: world, station: station, resources: resources);
            scene.Add("lift", lift);
            SpawnStair(
                world: world,
                frame: station,
                scene: scene,
                resources: resources,
                id: "deck-ramp",
                place: StairPlace,
                stair: Stair
            );
            SpawnLamps(world: world, station: station, lift: lift, resources: resources);
            scene.Add("deck-camera", world.Spawn(ViewIn(station, DeckCamera)));
            scene.Add("shaft-camera", world.Spawn(ViewIn(station, ShaftCamera)));
            scene.Add("wheel-camera", world.Spawn(ViewIn(station, WheelCamera)));
            scene.Add("docked-camera", world.Spawn(ViewIn(station, DockedCamera)));
            scene.Add("work-camera", world.Spawn(ViewIn(station, WorkCamera)));
            scene.Add("wheel-terminal-camera", world.Spawn(ViewIn(station, WheelTerminalCamera)));
            foreach (
                (string id, DPose pose) in new[] { ("ride-camera", RideCamera), ("ride-side-camera", RideSideCamera) }
            )
                scene.Add(
                    id,
                    world.Spawn(
                        new()
                        {
                            AttachTo = lift,
                            AttachOffset = new(pose.Position.ToVector3(), pose.Rotation.ToQuaternion()),
                            Render = new RenderData(),
                        }
                    )
                );
        }

        /// <summary>
        /// Spawns the lift in <paramref name="station"/> at <see cref="LiftTop"/>: a kinematic body over its boxes, drawn
        /// yellow, with its <see cref="Lift"/>.
        /// </summary>
        private static Entity SpawnLift(World world, FrameHandle station, IRenderResources resources)
        {
            BoxMesh mesh = new();
            foreach (BoundingBox box in LiftBoxes())
                mesh.Box(box);
            CompoundShapeDesc shape = new([
                .. LiftBoxes()
                    .Select(box => new CompoundChild(
                        new BoxShapeDesc(box.Extents),
                        new(box.Center, Quaternion.Identity)
                    )),
            ]);
            return world.Spawn(
                new()
                {
                    Frame = station,
                    Pose = LiftTop,
                    Body = new(Shape: shape, Motion: BodyMotion.Kinematic),
                    Render = Drawn(
                        resources: resources,
                        mesh: mesh.Build(),
                        color: new(x: 0.85f, y: 0.7f, z: 0.15f, w: 1),
                        boundingRadius: shape.BoundingRadius()
                    ) with
                    {
                        Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
                    },
                    Components = [new Lift(station: station, top: LiftTop, outward: LiftOutward, profile: Ride)],
                }
            );
        }

        /// <summary>
        /// Spawns the lamps of the shaft and of the wheel's ceiling in <paramref name="station"/>, and two on the railings of
        /// <paramref name="lift"/>: glowing panels 1.2 m square, each of whose face toward the room is a rectangle light.
        /// </summary>
        private static void SpawnLamps(World world, FrameHandle station, Entity lift, IRenderResources resources)
        {
            RectLightData towardMinusZ = PanelLight(
                offset: new(0, 0, -0.05f),
                front: -Vector3.UnitZ,
                across: Vector3.UnitX,
                width: 1.2f,
                height: 1.2f,
                power: CabinLampPower
            );
            MaterialId material = LampPanelMaterial(resources: resources, light: towardMinusZ);
            MeshId acrossZ = resources.CreateMesh(Primitives.Box(new(0.6f, 0.6f, 0.05f)));

            // The shaft lamps, on the spoke's spinward wall, each facing away from it, from 100 m below the deck to 50 m
            // above the ceiling.
            float wall = Layout.SpokeHalfWidth - Layout.SpokeWall;
            for (double depth = Layout.DeckRadius + 100; depth < Layout.CeilingRadius - 50; depth += ShaftLampSpacing)
                world.Spawn(
                    Lamp(
                        frame: station,
                        pose: new(new(-depth, 0, wall - 0.05), DQuaternion.Identity),
                        mesh: acrossZ,
                        material: material,
                        light: towardMinusZ
                    )
                );
            SpawnLiftLamps(world: world, lift: lift, resources: resources, material: material, spinward: towardMinusZ);
            SpawnWheelLamps(world: world, station: station, resources: resources, material: material);
        }

        /// <summary>
        /// Spawns the lift's two lamps, attached to <paramref name="lift"/> on the inner faces of its spinward and -Y railings
        /// at half their height, facing the rider: <paramref name="spinward"/>'s light, and the same toward +Y.
        /// </summary>
        private static void SpawnLiftLamps(
            World world,
            Entity lift,
            IRenderResources resources,
            MaterialId material,
            RectLightData spinward
        )
        {
            RectLightData towardPlusY = PanelLight(
                offset: new(0, 0.05f, 0),
                front: Vector3.UnitY,
                across: Vector3.UnitZ,
                width: 1.2f,
                height: 1.2f,
                power: CabinLampPower
            );
            float inner = Layout.LiftHalf - RailThickness;
            foreach (
                (Vector3 at, Vector3 half, RectLightData light) in new[]
                {
                    (new Vector3(RailHeight / 2, 0, inner - 0.05f), new Vector3(0.6f, 0.6f, 0.05f), spinward),
                    (new Vector3(RailHeight / 2, -inner + 0.05f, 0), new Vector3(0.6f, 0.05f, 0.6f), towardPlusY),
                }
            )
                world.Spawn(
                    new()
                    {
                        AttachTo = lift,
                        AttachOffset = new(at, Quaternion.Identity),
                        Render = LampRender(
                            mesh: resources.CreateMesh(Primitives.Box(half)),
                            material: material,
                            light: light
                        ),
                    }
                );
        }

        /// <summary>
        /// Spawns the wheel's lamps in <paramref name="station"/>, flat under the ceiling's face toward the floor round the
        /// lift's foot and the coda, each turned so that its local +X points away from the axis, toward the floor, with a
        /// floodlight's power, because the floor lies 20 m below.
        /// </summary>
        private static void SpawnWheelLamps(
            World world,
            FrameHandle station,
            IRenderResources resources,
            MaterialId material
        )
        {
            RectLightData ceilingFace = PanelLight(
                offset: new(0.05f, 0, 0),
                front: Vector3.UnitX,
                across: Vector3.UnitZ,
                width: 1.2f,
                height: 1.2f,
                power: FloodlightPower
            );
            MeshId acrossX = resources.CreateMesh(Primitives.Box(new(0.05f, 0.6f, 0.6f)));
            double under = Layout.CeilingRadius + Layout.CeilingThickness + 0.06;
            foreach (double y in new[] { -8.0, 8.0 })
            foreach (double z in new[] { -9.0, 9.0, 21.0 })
            {
                var at = new Double3(-Math.Sqrt(under * under - z * z), y, z);
                double theta = Math.Atan2(-at.Z, at.X);
                world.Spawn(
                    Lamp(
                        frame: station,
                        pose: new(at, DQuaternion.FromAxisAngle(Double3.UnitY, theta)),
                        mesh: acrossX,
                        material: material,
                        light: ceilingFace
                    )
                );
            }
        }

        private static SpawnDesc Lamp(
            FrameHandle frame,
            DPose pose,
            MeshId mesh,
            MaterialId material,
            RectLightData light
        ) =>
            new()
            {
                Frame = frame,
                Pose = pose,
                Render = LampRender(mesh: mesh, material: material, light: light),
            };

        private static RenderData LampRender(MeshId mesh, MaterialId material, RectLightData light) =>
            new()
            {
                Mesh = mesh,
                Material = material,
                BoundingRadius = 0.9f,
                Flags = RenderFlags.Visible | RenderFlags.NoImpostor,
                Light = light,
            };
    }
}
