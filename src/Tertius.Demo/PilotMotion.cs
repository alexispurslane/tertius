using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>The seat belt and pencil that the pilot's body can follow in the next snapshot.</summary>
internal struct PilotAction : IEventPayload
{
    public bool Seated;
    public bool Walking;
    public FrameHandle Cabin;
    public Entity Pencil;
}

/// <summary>Publishes changes to the passenger's action to the attached suit.</summary>
internal sealed class PilotActions(Entity suit) : IComponent
{
    private PilotAction previous;
    public int Priority => 10;
    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        Passenger? passenger = self.GetComponent<Passenger>();
        var action = new PilotAction
        {
            Cabin = passenger?.Cabin ?? FrameHandle.Null,
            Walking = self.Body.Pending.CharacterVelocity.LengthSquared() > 1e-6,
            Seated = passenger?.State.Value is Seated or Strapped,
            Pencil = passenger?.State.Value switch
            {
                Fetching => passenger.LoosePen,
                Holding held => held.Pencil,
                Returning held => held.Pencil,
                Strapped held => held.Pencil,
                Unstrapped held => held.Pencil,
                _ => Entity.Null,
            },
        };
        if (action.Seated == previous.Seated && action.Walking == previous.Walking && action.Pencil == previous.Pencil)
            return;
        outbox.Send(suit, action);
        previous = action;
    }
}

/// <summary>Poses boots and glove in suit space. Each two-bone solve keeps the source segment lengths.</summary>
internal sealed class PilotMotion(Rig rig, Vector3 center) : IComponent
{
    private readonly Pose[] bind = ModelSpace(rig);
    private readonly Pose[] model = new Pose[rig.Joints.Length];
    private readonly Limb left = Limb.Leg(rig, "L");
    private readonly Limb right = Limb.Leg(rig, "R");
    private readonly Limb arm = new(rig: rig, upper: "upper_arm.R", lower: "forearm.R", end: "hand.R");
    private readonly Limb otherArm = new(rig: rig, upper: "upper_arm.L", lower: "forearm.L", end: "hand.L");
    private readonly int palm = Joint(rig, "finger_middle.01.R");
    private readonly int[] fingers =
    [
        .. rig
            .Joints.Index()
            .Where(pair =>
                pair.Item.Name.StartsWith("finger_", StringComparison.Ordinal)
                && pair.Item.Name.EndsWith(".R", StringComparison.Ordinal)
            )
            .Select(pair => pair.Index),
    ];
    private PilotAction action;
    private float cycle,
        walking,
        sitting,
        reaching;
    private Vector3 lastPencil;
    private Quaternion lastTurn = Quaternion.Identity;

    public int Priority => 0;
    public EventMask Wants => EventMask.Of<Tick>().And<PilotAction>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        foreach (PilotAction change in events.Of<PilotAction>())
            action = change;
        Entity person = self.AttachedTo;
        if (self.Snapshot.PoseIn(person, self.Frame) is not { } personPose)
            return;
        float dt = (float)self.DeltaTime;
        Pose view = self.Snapshot.RenderOf(person).ViewOffset;
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)WalkHeading.Yaw(view.Rotation));
        float speed = 0;
        if (
            action.Walking
            && !action.Seated
            && self.Snapshot.IsStanding(person)
            && self.Snapshot.VelocityIn(person, self.Frame) is { } v
        )
        {
            Vector3 local = personPose.InverseTransformDirection(v).ToVector3();
            speed = new Vector2(local.X, local.Z).Length();
        }
        const float step = 0.56f;
        cycle = (cycle + speed * dt / (2 * step)) % 1;
        walking += (Math.Min(speed * 4, 1) - walking) * (1 - MathF.Exp(-12 * dt));
        sitting = Approach(sitting, action.Seated ? 1 : 0, 1.2f * dt);
        // SeatStand is the capsule beside the cushion. Put the hips above the cushion and keep the boots on the floor.
        float seatDrop =
            bind[left.Upper].Position.Y - (DemoScenes.Shuttle.SeatCushionY - DemoScenes.Shuttle.CockpitFloorY + 0.09f);
        float drop = 0.055f * walking + seatDrop * sitting;
        Pose standing = new(Vector3.Transform(center - Vector3.UnitY * (0.055f * walking), turn), turn);
        self.AttachOffset = standing;
        if (sitting > 0 && self.Snapshot.PoseIn(person, action.Cabin) is { } inCabin)
        {
            Vector3 origin = DemoScenes.Shuttle.Seat.Center + new Vector3(center.X, 0, center.Z);
            origin.Y = DemoScenes.Shuttle.CockpitFloorY - seatDrop;
            Pose onSeat = DPose.Compose(inCabin.Inverse(), new(origin, DQuaternion.Identity)).ToPose();
            Pose offset = Pose.Lerp(standing, onSeat, sitting);
            // The backrest lies between the boarding capsule and the cushion. Pass the armrest on the right.
            offset.Position += onSeat.TransformDirection(Vector3.UnitX) * (0.52f * MathF.Sin(MathF.PI * sitting));
            self.AttachOffset = offset;
        }
        Span<Pose> joints = self.RigPose.Joints;
        rig.Rest.Joints.AsSpan().CopyTo(joints);
        bind.CopyTo(model, 0);
        PlaceBoot(left, cycle, joints);
        PlaceBoot(right, (cycle + 0.5f) % 1, joints);
        float swing = 0.18f * MathF.Cos(2 * MathF.PI * cycle) * walking;
        PlaceArm(limb: otherArm, stride: -swing, side: -Vector3.UnitX, pose: joints);
        PlaceArm(limb: arm, stride: swing, side: Vector3.UnitX, pose: joints);
        PoseGlove(self: self, personPose: personPose, dt: dt, joints: joints);

        void PlaceArm(Limb limb, float stride, Vector3 side, Span<Pose> pose)
        {
            Vector3 goal = bind[limb.End].Position + new Vector3(0, sitting * 0.24f, stride - sitting * 0.25f);
            Solve(joints: pose, limb: limb, goal: goal, pole: side);
        }

        void PlaceBoot(Limb leg, float phase, Span<Pose> pose)
        {
            // The planted ankle travels backwards one step relative to the moving hips.
            float t = phase < 0.5f ? phase * 2 : (phase - 0.5f) * 2;
            float z = phase < 0.5f ? t - 0.5f : 0.5f - t * t * (3 - 2 * t);
            float rise = phase < 0.5f ? 0 : 0.13f * MathF.Sin(MathF.PI * t);
            Vector3 offset = new(0, rise * walking + drop, step * z * walking - 0.42f * sitting);
            Solve(joints: pose, limb: leg, goal: bind[leg.End].Position + offset, pole: -Vector3.UnitZ);
        }
    }

    private void PoseGlove(Self self, DPose personPose, float dt, Span<Pose> joints)
    {
        if (!action.Pencil.IsNull && self.Snapshot.PoseIn(action.Pencil, self.Frame) is { } pen)
        {
            Vector3 point = personPose.InverseTransformPoint(pen.Position).ToVector3();
            point = self.AttachOffset.InverseTransformPoint(point);
            Vector3 penAxis = self.AttachOffset.InverseTransformDirection(
                personPose.InverseTransformDirection(pen.Rotation.Rotate(Double3.UnitZ)).ToVector3()
            );
            lastPencil = point;
            lastTurn = Align(Vector3.UnitZ, penAxis);
            reaching = Math.Min(1, reaching + dt * 6);
        }
        else
            reaching = Math.Max(0, reaching - dt * 5);
        if (reaching > 0)
        {
            Vector3 point = lastPencil;
            Quaternion wristTurn = lastTurn;
            Vector3 wristToPalm = bind[palm].Position - bind[arm.End].Position + new Vector3(-0.017f, -0.018f, 0);
            Vector3 wrist = point - Vector3.Transform(wristToPalm, wristTurn);
            float distance = Vector3.Distance(point, bind[arm.Upper].Position);
            float reach = Math.Clamp((1.35f - distance) / 0.65f, 0, 1) * reaching;
            Solve(
                joints: joints,
                limb: arm,
                goal: Vector3.Lerp(bind[arm.End].Position, wrist, reach),
                pole: Vector3.UnitX
            );
            Rotate(joints, arm.End, Quaternion.Slerp(Quaternion.Identity, wristTurn, reach) * bind[arm.End].Rotation);
            float close = Math.Clamp((0.61f - distance) / 0.09f, 0, 1) * reaching;
            foreach (int finger in fingers)
            {
                Quaternion curl = Quaternion.CreateFromAxisAngle(
                    bind[finger].InverseTransformDirection(Vector3.UnitZ),
                    -1.08f * close
                );
                joints[finger] = joints[finger] with { Rotation = joints[finger].Rotation * curl };
            }
        }
    }

    private static float Approach(float value, float target, float limit) =>
        value + Math.Clamp(target - value, -limit, limit);

    private static Pose[] ModelSpace(Rig rig)
    {
        var poses = new Pose[rig.Joints.Length];
        for (int i = 0; i < poses.Length; i++)
        {
            RigJoint joint = rig.Joints[i];
            poses[i] = joint.Parent is int p ? Pose.Compose(poses[p], joint.Rest) : joint.Rest;
        }
        return poses;
    }

    private void Solve(Span<Pose> joints, Limb limb, Vector3 goal, Vector3 pole)
    {
        Vector3 start = bind[limb.Upper].Position;
        Vector3 first = bind[limb.Lower].Position - start;
        Vector3 second = bind[limb.End].Position - bind[limb.Lower].Position;
        Vector3 line = goal - start;
        if (line.LengthSquared() < 1e-10f)
            return;
        Vector3 direction = Vector3.Normalize(line);
        float distance = Math.Clamp(
            line.Length(),
            MathF.Abs(first.Length() - second.Length()) + 0.0001f,
            first.Length() + second.Length() - 0.0001f
        );
        float along = (first.LengthSquared() - second.LengthSquared() + distance * distance) / (2 * distance);
        Vector3 across = pole - direction * Vector3.Dot(direction, pole);
        if (across.LengthSquared() < 1e-8f)
            across = Vector3.Cross(direction, Vector3.UnitY);
        Vector3 bend =
            direction * along
            + Vector3.Normalize(across) * MathF.Sqrt(Math.Max(0, first.LengthSquared() - along * along));
        Rotate(joints, limb.Upper, Align(first, bend) * bind[limb.Upper].Rotation);
        Rotate(joints, limb.Lower, Align(second, direction * distance - bend) * bind[limb.Lower].Rotation);
        Rotate(joints, limb.End, bind[limb.End].Rotation);
    }

    private void Rotate(Span<Pose> joints, int joint, Quaternion rotation)
    {
        Quaternion parent = rig.Joints[joint].Parent is int p ? model[p].Rotation : Quaternion.Identity;
        joints[joint] = joints[joint] with { Rotation = Quaternion.Normalize(Quaternion.Conjugate(parent) * rotation) };
        model[joint].Rotation = rotation;
    }

    private static Quaternion Align(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        float dot = Vector3.Dot(from, to);
        if (dot < -0.99999f)
        {
            Vector3 axis = MathF.Abs(from.X) < 0.8f ? Vector3.UnitX : Vector3.UnitY;
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(from, axis)), MathF.PI);
        }
        return Quaternion.Normalize(new Quaternion(Vector3.Cross(from, to), 1 + dot));
    }

    private static int Joint(Rig rig, string name) =>
        rig.JointNamed(name) ?? throw new ArgumentException($"The pilot suit requires joint {name}.", nameof(rig));

    private readonly record struct Limb(int Upper, int Lower, int End)
    {
        public Limb(Rig rig, string upper, string lower, string end)
            : this(Joint(rig, upper), Joint(rig, lower), Joint(rig, end)) { }

        public static Limb Leg(Rig rig, string side) =>
            new(rig: rig, upper: $"thigh.{side}", lower: $"shin.{side}", end: $"ankle.{side}");
    }
}
