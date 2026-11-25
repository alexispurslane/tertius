using System.Numerics;
using Tertius.Assets;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

public sealed class PilotMotionTests : IDisposable
{
    private readonly World world = NewWorld(backend: out _);
    private readonly Rig rig = SuitRig();

    public void Dispose() => world.Dispose();

    [Fact]
    public void EachBootStaysOnTheFloorWhileTheOtherBootSwings()
    {
        FrameHandle room = Ground(world, 9.81f);
        Floor(world, room);
        Entity person = Character(room, new(0, 0.9, 0), new SteadyWalk(new(0, 0, -1), ticks: 260));
        Entity suit = Draw(person);
        Tick(world, 120);

        int leftSwings = 0,
            rightSwings = 0;
        for (int tick = 0; tick < 90; tick++)
        {
            world.Tick();
            Pose[] joints = ModelPose();
            double left = JointInRoom(world.PoseIn(suit, room), joints, "ankle.L").Y - 0.1;
            double right = JointInRoom(world.PoseIn(suit, room), joints, "ankle.R").Y - 0.1;
            Math.Min(left, right).Should().BeApproximately(0, 0.003);
            if (left > 0.08)
                leftSwings++;
            if (right > 0.08)
                rightSwings++;
        }

        leftSwings.Should().BeGreaterThan(8);
        rightSwings.Should().BeGreaterThan(8);
    }

    [Fact]
    public void ClosingTheSeatBeltPlacesTheHipsOverTheCushionAndTheBootsOnTheCockpitFloor()
    {
        FrameHandle cabin = Ground(world, 0);
        Entity person = Character(cabin, Shuttle.SeatStand, new Passenger(new(cabin, Shuttle.SeatStand), Entity.Null));
        Entity suit = Draw(person);
        Tick(world, 80);

        Pose[] joints = ModelPose();
        Double3 hip = JointInRoom(world.PoseIn(suit, cabin), joints, "thigh.L");
        hip.Y.Should().BeInRange(Shuttle.SeatCushionY + 0.06, Shuttle.SeatCushionY + 0.12);
        hip.Z.Should().BeInRange(Shuttle.Seat.Min.Z, Shuttle.Seat.Max.Z);
        Double3 knee = JointInRoom(world.PoseIn(suit, cabin), joints, "shin.L");
        knee.Z.Should().BeLessThan(hip.Z - 0.32);
        (JointInRoom(world.PoseIn(suit, cabin), joints, "ankle.L").Y - 0.1)
            .Should()
            .BeApproximately(Shuttle.CockpitFloorY, 0.003);
    }

    [Fact]
    public void TheGloveClosesAtThePhysicalPencilAndOpensWhenThePilotLetsGo()
    {
        FrameHandle room = Ground(world, 0);
        Vector3 grip = DemoSurface.Eye + Passenger.PenHold;
        Entity pen = world.Spawn(
            new()
            {
                Frame = room,
                Pose = new(grip, DQuaternion.Identity),
                Body = new(Shape: new BoxShapeDesc(DemoSurface.PenHalf), Mass: DemoSurface.PenMass),
            }
        );
        Entity person = Character(room, Double3.Zero, new Passenger(new(room, Double3.Zero), pen, new Unstrapped(pen)));
        Entity suit = Draw(person);
        Tick(world, 40);

        Pose[] grasp = ModelPose();
        Double3 knuckle = JointInRoom(world.PoseIn(suit, room), grasp, "finger_middle.01.R");
        Double3.Distance(knuckle, world.PoseIn(pen, room).Position).Should().BeLessThan(0.035);
        int finger = rig.JointNamed("finger_middle.01.R")!.Value;
        int wrist = rig.JointNamed("hand.R")!.Value;
        var curl = Pose.Compose(grasp[wrist].Inverse(), grasp[finger]);
        MathF.Abs(curl.Rotation.W).Should().BeLessThan(0.96f);

        world.Post(Event.Create(target: person, sender: Entity.Null, payload: new PlayWithBoxes()));
        Tick(world, 25);

        Pose[] open = ModelPose();
        var released = Pose.Compose(open[wrist].Inverse(), open[finger]);
        MathF.Abs(released.Rotation.W).Should().BeApproximately(1, 1e-5f);
        Double3
            .Distance(
                JointInRoom(world.PoseIn(suit, room), open, "finger_middle.01.R"),
                world.PoseIn(pen, room).Position
            )
            .Should()
            .BeGreaterThan(0.15);
    }

    private Entity Character(FrameHandle frame, Double3 place, IComponent behavior) =>
        world.Spawn(
            new()
            {
                Frame = frame,
                Pose = new(place, DQuaternion.Identity),
                Body = new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character, Mass: 70),
                Render = new RenderData { ViewOffset = new(DemoSurface.Eye, Quaternion.Identity) },
                Components = [behavior],
            }
        );

    private Entity Draw(Entity person)
    {
        world.Camera = person;
        var suit = new StoredModel(new(0), new(new(-0.3f, 0, -0.2f), new(0.3f, 1.8f, 0.2f)), 2) { Rig = rig };
        return DemoSurface.SpawnDrawnBody(
            world: world,
            scene: new(),
            person: person,
            resources: RenderResources.None,
            suit: suit
        );
    }

    private Pose[] ModelPose()
    {
        RenderSnapshot snapshot = world.RenderSnapshots.AcquireLatest() ?? world.RenderSnapshots.ReadBuffer;
        Pose[] placed = snapshot.JointsOf(snapshot.RigPoses.Single()).ToArray();
        for (int i = 0; i < placed.Length; i++)
            if (rig.Joints[i].Parent is int parent)
                placed[i] = Pose.Compose(placed[parent], placed[i]);
        return placed;
    }

    private Double3 JointInRoom(DPose suit, Pose[] joints, string name) =>
        suit.TransformPoint(joints[rig.JointNamed(name)!.Value].Position);

    private static Rig SuitRig()
    {
        List<RigJoint> joints = [];
        foreach (string side in new[] { "L", "R" })
        {
            float sign = side == "L" ? -1 : 1;
            int leg = joints.Count;
            Add($"thigh.{side}", null, new(sign * 0.1f, 0.97f, 0));
            Add($"shin.{side}", leg, new(0, -0.46f, -0.07f));
            Add($"ankle.{side}", leg + 1, new(0, -0.41f, 0.10f));
            int arm = joints.Count;
            Add($"upper_arm.{side}", null, new(sign * 0.2f, 1.44f, 0));
            Add($"forearm.{side}", arm, new(0, -0.27f, 0));
            Add($"hand.{side}", arm + 1, new(0, -0.23f, -0.07f));
            Add($"finger_middle.01.{side}", arm + 2, new(0, -0.1f, -0.05f));
        }
        return new([.. joints], []);
        void Add(string name, int? parent, Vector3 offset) =>
            joints.Add(new(name, parent, new(offset, Quaternion.Identity)));
    }
}
