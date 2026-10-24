using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The places of the coda on the wheel's floor, after the exit (docs/plans/blue-danube-demo.md, "The coda"): a stack of
    /// loose boxes that the person walks into and knocks over, two loose boxes beside a stand from which the person throws
    /// them the same way, one spinward and one antispinward, the floor marks where a flat field of the floor's gravity
    /// would land each throw, and three still cameras. Every place is in the station's axes, in the lit stretch of the floor
    /// round the chute's end, on the antispinward side of the axis's middle, away from the chute's trough. The stack and the
    /// stand lie on facet <see cref="Facet"/>, the facet where the person's stroll ends, 3.7 m spinward of the chute's end:
    /// the stack at y = 2.5 m and the stand at y = -2 m, across the trough's line from it.
    /// <list type="bullet">
    /// <item>The stack, <c>stack-0</c> to <c>stack-5</c>: six boxes in a wall of three, two and one across the facet, its
    /// face toward the person's arrival at y = 0.</item>
    /// <item>The boxes to throw, <c>throw-box-0</c>, green, for the spinward throw, and <c>throw-box-1</c>, orange, for the
    /// antispinward one: on the floor 0.8 m in front of the stand, toward -Y, the way that the person faces there.</item>
    /// <item>The marks, <c>flat-mark-0</c> and <c>flat-mark-1</c>, in the colors of their boxes: thin plates across the throws'
    /// lane where a flat field would land each throw from the stand.</item>
    /// <item>The views <c>stack-camera</c>, beside the stack, and <c>spinward-camera</c> and <c>antispinward-camera</c>, low
    /// on the floor near the side wall at y = -9.3 m, each looking across the lane at one throw.</item>
    /// </list>
    /// Every box and mark is drawn with <see cref="RenderFlags.NoImpostor"/>, as every part of the station is. The boxes are
    /// dynamic bodies in the station's frame. They spawn at rest on the floor with the scene, so in the long run they rest
    /// asleep on the floor for the whole flight before the coda.
    /// </summary>
    public static class WheelCoda
    {
        /// <summary>The facet of the wheel's floor on which the stack and the stand lie: from 2.45 to 4.9 m spinward of the chute's end.</summary>
        public const int Facet = DemoStation.Segments / 2 + 1;

        /// <summary>Half the size of every loose box, in meters: a box 0.5 m on a side, taller than the 0.4 m step that a character climbs.</summary>
        public const float BoxHalf = 0.25f;

        /// <summary>The mass of every loose box, in kilograms.</summary>
        public const float BoxMass = 5;

        /// <summary>The speed of each throw relative to the person, in m/s.</summary>
        public const float ThrowSpeed = 8;

        /// <summary>How far above the floor each throw aims, in radians: 30 degrees.</summary>
        public const double ThrowAngle = Math.PI / 6;

        /// <summary>Where the stack stands across the axis, in meters.</summary>
        public const double StackY = 2.5;

        /// <summary>Where the stand lies across the axis, in meters: the lane of both throws.</summary>
        public const double LaneY = -2;

        /// <summary>The person's center above the floor when it stands, in meters.</summary>
        private const double Standing = 0.9;

        private static WheelFloor Floor => DemoStation.WheelFloor;

        /// <summary>The place of the stand: the person's center there, over the middle of <see cref="Facet"/> in the lane.</summary>
        public static Double3 Stand { get; } = Floor.OnFacet(facet: Facet, height: Standing, y: LaneY);

        /// <summary>The person's capsule, 1.8 m tall and 0.3 m in radius, which a thrown box starts clear of.</summary>
        public static CapsuleShapeDesc PersonBody { get; } = new(0.6f, (float)StationExit.PersonRadius);

        /// <summary>The pose of <c>stack-camera</c>: beside the stack, spinward of it, looking at it and at the person's way into it.</summary>
        public static DPose StackCamera { get; } = Camera(OnFloor(3.5, 1.5, -1.0), OnFloor(0, 0.7, 1.6));

        /// <summary>
        /// The pose of <c>spinward-camera</c>: near the side wall at y = -9 m, 2 m above the floor and 11 m spinward of the
        /// stand, looking back across the lane at a point 4 m spinward of the stand. It sees the stand, the throw, its landing
        /// and its mark, and the fallen stack behind the lane.
        /// </summary>
        public static DPose SpinwardCamera { get; } = Camera(OnFloor(11, 2, -9), OnFloor(4, 1.3, LaneY));

        /// <summary>
        /// The pose of <c>antispinward-camera</c>: near the side wall at y = -9 m, 2 m above the floor and 14 m antispinward of
        /// the stand, looking back across the lane at a point 5.5 m antispinward of the stand. The chute's trough lies beyond
        /// the lane from it, so it hides nothing of the throw.
        /// </summary>
        public static DPose AntispinwardCamera { get; } = Camera(OnFloor(-14, 2, -9), OnFloor(-5.5, 1.3, LaneY));

        /// <summary>
        /// The six boxes of the stack: three on the floor, 0.62 m apart along the facet, so that a person 0.6 m wide walks
        /// into the middle one and not the others, two on them, and one on top, each with its scene id.
        /// </summary>
        public static IEnumerable<(string Id, DPose Pose)> Stack()
        {
            (double Along, double Height)[] boxes = [(-0.62, 1), (0, 1), (0.62, 1), (-0.31, 3), (0.31, 3), (0, 5)];
            for (int i = 0; i < boxes.Length; i++)
                yield return ($"stack-{i}",
                    new(
                        Floor.OnFacet(facet: Facet, height: boxes[i].Height * BoxHalf + 0.002 * (boxes[i].Height - 1),
                            y: StackY,
                            along: boxes[i].Along), Floor.FacetTurn(Facet)));
        }

        /// <summary>The scene id of the box of throw <paramref name="index"/>.</summary>
        public static string ThrowBoxId(int index) => $"throw-box-{index}";

        /// <summary>Where the box of throw <paramref name="index"/> rests before the person takes it: in front of the stand, on the side of its throw.</summary>
        public static DPose ThrowBoxPose(int index) =>
            new(Floor.OnFacet(facet: Facet, height: BoxHalf, y: LaneY - 0.8, along: index == 0 ? 0.45 : -0.45),
                Floor.FacetTurn(Facet));

        /// <summary>The unit direction of throw <paramref name="index"/> at the stand: 30 degrees above the floor, spinward for 0 and antispinward for 1.</summary>
        public static Double3 Aim(int index) =>
            WheelFloor.SpinwardAt(Stand) * ((index == 0 ? 1 : -1) * Math.Cos(ThrowAngle)) +
            WheelFloor.UpAt(Stand) * Math.Sin(ThrowAngle);

        /// <summary>
        /// The walk into the stack and through it, from where the stroll ends: to the stack's line across the floor, and
        /// along +Y into the stack's middle, 1.15 m past its near face.
        /// </summary>
        public static Waypoint[] Shove(FrameHandle station) =>
        [
            new(station, Floor.OnFacet(facet: Facet, height: Standing, y: 1)),
            new(station, Floor.OnFacet(facet: Facet, height: Standing, y: StackY + 0.9))
        ];

        /// <summary>The walk to the stand, straight back along -Y.</summary>
        public static Waypoint[] ToStand(FrameHandle station) => [new(station, Stand)];

        /// <summary>
        /// The release of throw <paramref name="index"/> that the design expects: from where <see cref="Carry.ThrowStart"/>
        /// puts a box for a person who stands still at the stand, upright on the floor, with its eye at
        /// <see cref="DemoSurface.Eye"/>, at the throw speed along the throw's aim. The capsule is round about its axis, so
        /// the start does not depend on the way that the person faces.
        /// </summary>
        public static ThrowRelease DesignRelease(int index, CapsuleShapeDesc body)
        {
            var box = new CrateSpec(HalfExtents: new(BoxHalf), Mass: BoxMass, Mesh: default, Material: default);
            Vector3 eye = DemoSurface.Eye;
            // In axes with +Y along the person's up and +X along the throw's part across it.
            var aim = new Vector3((float)Math.Cos(ThrowAngle), (float)Math.Sin(ThrowAngle), 0);
            Vector3 start = Carry.ThrowStart(body: body, carried: box, eye: eye, aim: aim);
            Double3 up = WheelFloor.UpAt(Stand), across = Aim(index) - up * Math.Sin(ThrowAngle);
            Double3 place = Stand + up * start.Y + across.Normalized() * start.X;
            return new(place, Aim(index) * ThrowSpeed);
        }

        /// <summary>Where a flat field of the floor's gravity, ω² times the floor's radius, lands throw <paramref name="index"/>
        /// of the design: on the floor in the lane.</summary>
        public static Double3 FlatMark(int index, CapsuleShapeDesc body)
        {
            ThrowRelease release = DesignRelease(index, body);
            double gravity = SpinStation.Rate * SpinStation.Rate * Floor.Radius;
            return Floor.Above(
                WheelFloor.Theta(release.Place) + release.FlatLanding(gravity, Floor, BoxHalf).Spinward / Floor.Radius,
                0, LaneY);
        }

        /// <summary>The play of the person in <paramref name="station"/>, the station's frame, with the boxes of
        /// <paramref name="scene"/>.</summary>
        public static BoxPlay Play(SceneInstance scene, FrameHandle station) => new(station: station,
            shove: Shove(station),
            toStand: ToStand(station),
            throws: [new(scene[ThrowBoxId(0)], Aim(0)), new(scene[ThrowBoxId(1)], Aim(1))],
            walkSpeed: StationExit.WalkSpeed, throwSpeed: ThrowSpeed, body: PersonBody, floor: Floor,
            rate: SpinStation.Rate);

        /// <summary>The place <paramref name="height"/> above the floor, <paramref name="along"/> meters spinward of the middle of
        /// <see cref="Facet"/> along the floor, at <paramref name="y"/>.</summary>
        private static Double3 OnFloor(double along, double height, double y) =>
            Floor.Above((Facet + 0.5) * Floor.Facet + along / Floor.Radius, height, y);

        private static DPose Camera(Double3 at, Double3 target) =>
            new(at, AttitudeAutopilot.Facing(target - at, WheelFloor.UpAt(at)));

        /// <summary>
        /// Spawns the stack, the boxes to throw, the marks and the cameras in <paramref name="station"/>, the station's frame,
        /// with their scene ids in <paramref name="scene"/>.
        /// </summary>
        public static void Spawn(World world, FrameHandle station, SceneInstance scene, IRenderResources resources)
        {
            MeshId mesh = resources.CreateMesh(Primitives.Box(new(BoxHalf)));

            CrateSpec Box(Vector4 color) => new(HalfExtents: new(BoxHalf), Mass: BoxMass, Mesh: mesh,
                Material: resources.CreateMaterial(MaterialDesc.Color(color)), NoImpostor: true);

            CrateSpec stackBox = Box(new(x: 0.72f, y: 0.55f, z: 0.32f, w: 1));
            foreach ((string id, DPose pose) in Stack())
                scene.Add(id, world.Spawn(stackBox.Spawn(station, pose, Double3.Zero)));
            Vector4[] colors = [new(x: 0.2f, y: 0.7f, z: 0.3f, w: 1), new(x: 0.95f, y: 0.5f, z: 0.1f, w: 1)];
            for (int i = 0; i < BoxPlayState.Throws; i++)
            {
                scene.Add(ThrowBoxId(i), world.Spawn(Box(colors[i]).Spawn(station, ThrowBoxPose(i), Double3.Zero)));
                Double3 mark = FlatMark(i, PersonBody);
                var plate = new Vector3(0.004f, 0.75f, 0.1f);
                scene.Add($"flat-mark-{i}", world.Spawn(new()
                {
                    Frame = station,
                    Pose = new(mark - Floor.Normal(mark) * plate.X, Floor.FacetTurn(Floor.FacetIndex(mark))),
                    Render = Drawn(resources: resources, mesh: Primitives.Box(plate), color: colors[i],
                            boundingRadius: plate.Length()) with
                        {
                            Flags = RenderFlags.Visible | RenderFlags.NoImpostor
                        }
                }));
            }

            scene.Add("stack-camera",
                world.Spawn(new() { Frame = station, Pose = StackCamera, Render = new RenderData() }));
            scene.Add("spinward-camera",
                world.Spawn(new() { Frame = station, Pose = SpinwardCamera, Render = new RenderData() }));
            scene.Add("antispinward-camera",
                world.Spawn(new() { Frame = station, Pose = AntispinwardCamera, Render = new RenderData() }));
        }
    }
}
