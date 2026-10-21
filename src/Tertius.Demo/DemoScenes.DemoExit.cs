using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The exit of the Blue Danube demo on its own (docs/plans/blue-danube-demo.md, "The exit", "Sections of the long
    /// script"): the scene <c>demo-exit</c> starts at the docking's end state, so the exit's sections run in a short script
    /// of their own until the docking and the exit merge into the long script.
    /// <list type="bullet">
    /// <item>A root frame with Earth's point gravity, which does not turn, with the drawn <c>earth</c> and the <c>sun</c>
    /// of <c>demo-surface</c>. The <c>station</c> flies its orbit in it (see <see cref="DemoStation"/>), with its chute, its
    /// openings, its lamps and its cameras.</item>
    /// <item>The <c>shuttle</c> parked static at <see cref="DemoStation.ParkedPose"/> in the station's frame, with its
    /// seat, panes, lamps and nozzle, and no flight controls: in the end state every flight control adds nothing.</item>
    /// <item>The <c>person</c> at <see cref="Shuttle.SeatStand"/> in the cabin, at <see cref="StandAtSeat"/>, <see cref="Strapped"/>, with the drawn copy
    /// of the pen in its hand, and its <see cref="Disembark"/> <see cref="Aboard"/>. The <c>follow-camera</c> is attached to
    /// it, behind it toward the nose, and so is <c>slide-camera</c> (see <see cref="StationExit.SpawnSlideCamera"/>).
    /// <c>cabin-camera</c> stands where <c>demo-surface</c> puts it.</item>
    /// </list>
    /// </summary>
    public static class DemoExit
    {
        /// <summary>Where the pen's copy lies in the person's view: at <see cref="Passenger.PenHold"/>, pointing ahead.</summary>
        public static Pose PenGrip { get; } = new(Passenger.PenHold, Quaternion.Identity);

        /// <summary>Half the length of the straight part of the person's capsule, in meters: the capsule is 1.8 m long.</summary>
        public const float CapsuleHalfHeight = 0.6f;

        /// <summary>
        /// The gravity that the person feels at <see cref="Shuttle.SeatStand"/> in the parked cabin, in the cabin's axes, in
        /// m/s²: the spin gravity of the station's steady spin, ω² times the stand's distance from the station's axis, away
        /// from the axis. It is 0.78 m/s² and leans 30 degrees from the cabin floor's down toward port. The scene computes it
        /// from the parked pose, because the frame tree knows the station's spin only from the first tick.
        /// </summary>
        public static Double3 FeltAtSeat
        {
            get
            {
                Double3 at = DemoStation.ParkedPose.TransformPoint(Shuttle.SeatStand);
                return DemoStation.ParkedPose.Rotation.Conjugate().Rotate(new Double3(at.X, 0, at.Z) * (SpinStation.Rate * SpinStation.Rate));
            }
        }

        /// <summary>
        /// The person's pose at the docked seat, in the cabin's axes, as the docking leaves it: its up against
        /// <see cref="FeltAtSeat"/>, and its center above the stand's place on the floor, where its capsule, tipped that way,
        /// stands on the floor with its lower end. So the person stands from the first steps, and the walk starts at once.
        /// </summary>
        public static DPose StandAtSeat
        {
            get
            {
                Double3 up = -FeltAtSeat.Normalized();
                double floor = Shuttle.SeatStand.Y - (CapsuleHalfHeight + StationExit.PersonRadius);
                var center = new Double3(Shuttle.SeatStand.X, floor + StationExit.PersonRadius + CapsuleHalfHeight * up.Y, Shuttle.SeatStand.Z);
                Double3 ahead = -Double3.UnitZ - up * Double3.Dot(-Double3.UnitZ, up);
                return new DPose(center, AttitudeAutopilot.Facing(ahead, up));
            }
        }

        public static SceneInstance Build(World world, IRenderResources resources)
        {
            var scene = new SceneInstance();
            FrameHandle earth = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e8f), GravityField.Point(EarthMu)));
            scene.Add("earth", earth.Entity);
            scene.Add("earth-globe", world.Spawn(new SpawnDesc
            {
                Frame = earth,
                Pose = DPose.Identity,
                Render = new RenderData
                {
                    Mesh = resources.CreateMesh(Primitives.Sphere((float)(EarthRadius - 1), 256, 128)),
                    Material = resources.CreateMaterial(new MaterialDesc(Vector4.One, SpecularIntensity: 0.1f, Albedo: resources.CreateTexture(EarthGrid()))),
                    BoundingRadius = (float)EarthRadius,
                    Flags = RenderFlags.Visible | RenderFlags.Celestial,
                },
            }));

            Entity station = DemoStation.Spawn(world, earth, scene, resources);
            FrameHandle frame = world.GetOwnedFrame(station);
            Entity shuttle = Shuttle.Park(world, frame, DemoStation.ParkedPose, Shuttle.DrawnHull(resources));
            scene.Add("shuttle", shuttle);
            FrameHandle cabin = world.GetOwnedFrame(shuttle);
            Vector3 seatHalf = Shuttle.Seat.Extents;
            scene.Add("seat", world.Spawn(new SpawnDesc
            {
                Frame = cabin,
                Pose = new DPose(Shuttle.Seat.Center, DQuaternion.Identity),
                Render = Drawn(resources, Primitives.Box(seatHalf), new Vector4(0.15f, 0.25f, 0.6f, 1), boundingRadius: seatHalf.Length()),
            }));
            Shuttle.Panes(world, cabin, scene, resources);
            Shuttle.SpawnLamps(world, cabin, scene, resources);
            Shuttle.SpawnNozzle(world, cabin, scene, resources);

            Vector3 personHalf = DemoSurface.PersonHalf;
            RenderData personRender = Drawn(resources, Primitives.Box(personHalf), new Vector4(0.2f, 0.45f, 0.8f, 1), boundingRadius: 1);
            personRender.ViewOffset = new Pose(DemoSurface.Eye, Quaternion.Identity);
            Entity person = world.Spawn(new SpawnDesc
            {
                Frame = cabin,
                Pose = StandAtSeat,
                Body = new BodyDesc(new CapsuleShapeDesc(CapsuleHalfHeight, (float)StationExit.PersonRadius), BodyMotion.Character, Mass: YardPersonMass),
                Render = personRender,
            });
            scene.Add("person", person);
            var penSpec = new CrateSpec(DemoSurface.PenHalf, DemoSurface.PenMass, resources.CreateMesh(Primitives.Box(DemoSurface.PenHalf)),
                resources.CreateMaterial(MaterialDesc.Color(new Vector4(0.85f, 0.1f, 0.08f, 1))));
            Entity copy = world.Spawn(penSpec.HeldCopy(cabin, person, personRender.ViewOffset, PenGrip));
            scene.Add("pen-copy", copy);
            world.AddComponent(person, new Passenger(new Waypoint(cabin, Shuttle.SeatStand), Entity.Null, new Strapped(copy)));
            world.AddComponent(person, StationExit.Disembark(cabin, frame));
            world.AddComponent(person, WheelCoda.Play(scene, frame));
            // The person walks aft first, so the camera starts behind it, toward the nose.
            scene.Add("follow-camera", world.Spawn(new SpawnDesc
            {
                AttachTo = person,
                AttachOffset = new Pose(DemoSurface.Eye + new Vector3(0, (float)DemoSurface.FollowAbove, -(float)DemoSurface.FollowBehind), Quaternion.Identity),
                Render = new RenderData(),
                Components = [new ChaseCamera(DemoSurface.FollowBehind, DemoSurface.FollowAbove, DemoSurface.FollowAhead, personHalf, world.VerticalFov)],
            }));
            scene.Add("slide-camera", StationExit.SpawnSlideCamera(world, person, frame));
            Entity cabinCamera = world.Spawn(new SpawnDesc { Frame = cabin, Pose = DemoSurface.CabinCamera, Render = new RenderData() });
            scene.Add("cabin-camera", cabinCamera);

            SunAt(world, scene, earth, new Double3(0, EarthRadius, 0) + new Double3(60, 80, 40).Normalized() * AstronomicalUnit);

            foreach (string view in new[] { "cabin-camera", "follow-camera", "deck-camera", "spoke-camera", "slide-camera", "wheel-camera", "stack-camera", "spinward-camera", "antispinward-camera", "rim-camera" })
                scene.AddView(view);
            world.Camera = cabinCamera;
            return scene;
        }
    }
}
