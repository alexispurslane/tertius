using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// A camera that follows a person from behind, as a film crew's camera follows an actor. Its entity is attached to the
/// person, the subject, so that it goes with the subject into every frame that the subject enters, and the component
/// writes only its own entity's offset from the subject. The camera's position follows the subject's eye as the
/// attachment places it, with no smoothing of its own; the component smooths only the camera's up, its heading and its
/// arm. It keeps this framing in every tick:
/// <list type="bullet">
/// <item>The camera's up: it turns toward the subject's up with the time constant <see cref="TurnTime"/>, so a person
/// whose up the engine turns at once, as under a ship's thrust, does not swing the camera's view round in one tick.</item>
/// <item>The heading: the way the subject goes, across the camera's up. It swings about that up toward the direction of
/// the subject's velocity across it, as a spring damped critically with the time constant <see cref="SwingTime"/>, while
/// that speed is above <see cref="TurnSpeed"/>, and its swing comes to rest while the subject stands. The swing starts
/// from rest, so after a sudden turn of the subject the arm starts its swing slowly. The heading also turns with the
/// camera's up. It starts opposite to the camera's offset at its spawn, so the scene places the camera behind the
/// subject.</item>
/// <item>The arm: from the eye, <paramref name="behind"/> meters back along the heading and <paramref name="above"/>
/// meters up along the camera's up. The eye is the place of the subject's view offset. A ray along the arm measures the
/// free distance to the nearest body that is not the subject's, and the arm stops <see cref="WallGap"/> short of it: at
/// once when it must shorten, and with the time constant <see cref="ArmTime"/> when it can grow back.</item>
/// <item>The look: along the heading, with the camera's up as the view's up, pitched toward the point
/// <paramref name="ahead"/> meters ahead of the eye along the heading, and lowered as far as the subject's whole
/// body needs to stay in the picture: the pitch is held where every corner of the subject's box, <paramref name="subjectHalf"/>,
/// lies within <see cref="FrameMargin"/> of the half picture's height from the middle of the picture, in the
/// <paramref name="verticalFov"/> of the view. When the arm shortens, the point ahead comes nearer in proportion. When the
/// box is too tall for the picture, as with a wall close behind the subject, the top of the box stays in the picture and
/// its bottom leaves it. When a corner lies beside or behind the camera, the camera pitches toward the point ahead.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>Why the position is not smoothed: the camera reads the subject at the end of the last finished step, and the
/// attachment applies the offset that it writes to the subject's pose at the end of the step that runs beside its tick.
/// So a sudden move of the subject, which no camera can foresee, reaches the camera in the tick of the move, as with a
/// hard attach. A smoothing of the position could act only from the next tick on, and it would move the camera back
/// toward its old course and then forward again: a zigzag, where a hard attach gives one kink. So a turn of the walk
/// and a stair move the camera with the subject, and only the swing of the view after a turn is smooth.</para>
/// <para>The attachment also keeps the offset in the subject's axes, so a sudden turn of the subject's up swings the
/// camera with it in the tick of the turn, by the arm's length times the angle. The engine turns a character's up at once
/// when the gravity round it turns, as at a ship's ignition, by up to 90 degrees. Film a person at such a moment with a
/// fixed camera.</para>
/// <para>The ray's answer arrives in the next tick, so the arm stops short of a wall one tick after the wall comes into
/// it, and in the first tick of the camera it has no answer yet.</para>
/// </remarks>
/// <param name="behind">How far behind the subject's eye the camera keeps, along the heading, in meters.</param>
/// <param name="above">How far above the subject's eye the camera keeps, along the camera's up, in meters.</param>
/// <param name="ahead">How far ahead of the subject's eye the camera looks, along the heading, in meters.</param>
/// <param name="subjectHalf">The half extents of the box that draws the subject, around its center, in its axes, in meters.</param>
/// <param name="verticalFov">The vertical angle of the view through the camera, in radians: the world's <see cref="World.VerticalFov"/>.</param>
public sealed class ChaseCamera(double behind, double above, double ahead, Vector3 subjectHalf, double verticalFov) : IComponent
{
    /// <summary>The time constant with which the camera's up turns toward the subject's up, in seconds.</summary>
    public const double TurnTime = 0.5;

    /// <summary>
    /// The time constant of the heading's swing toward the subject's way, in seconds: the swing is a spring at the angular
    /// frequency 1 / this, damped critically, so a turn of 90 degrees is down to 5 degrees after 1.2 s.
    /// </summary>
    public const double SwingTime = 0.25;

    /// <summary>
    /// The speed of the subject across the camera's up, in m/s, below which the heading holds: a sixth of a walk at
    /// <see cref="PersonControl.WalkSpeed"/>, so the heading holds in the last 0.17 m of a <see cref="Walker"/>'s stop.
    /// </summary>
    public const double TurnSpeed = 0.5;

    /// <summary>How far short of the nearest body along it the arm stops, in meters.</summary>
    public const double WallGap = 0.2;

    /// <summary>The time constant with which a shortened arm grows back, in seconds.</summary>
    public const double ArmTime = 0.5;

    /// <summary>
    /// How far from the middle of the picture the corners of the subject's box may lie, as a share of the half picture's
    /// height: the box keeps 15 percent of the half height to the picture's top and bottom edges.
    /// </summary>
    public const double FrameMargin = 0.85;

    // The tag of the camera's rays, which matches each answer to its question.
    private const int RayTag = 0x43_48_41_53;

    private readonly double behind = behind, above = above, ahead = ahead;
    private readonly Double3 subjectHalf = (Double3)subjectHalf;
    // The largest angle from the view's axis, up or down, at which a corner of the subject's box may lie.
    private readonly double halfFrameAngle = Math.Atan(FrameMargin * Math.Tan(verticalFov / 2));

    // False until the first tick, which takes the heading from the offset at the spawn.
    private bool started;
    // The frame whose axes hold the vectors below: the camera's frame at the last tick.
    private FrameHandle frame;
    // The subject's position at the last tick, in the frame, and its velocity over the step before it, from positions.
    private Double3 lastPosition, subjectVelocity;
    // The camera's up, which follows the subject's up, and the heading, a unit vector across it.
    private Double3 up, heading;
    // How fast the heading swings about the up, in radians per second, positive counterclockwise seen from above.
    private double swingRate;
    // The length of the arm, in meters, and the free distance along it that the last answered ray measured.
    private double arm, free = double.PositiveInfinity;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>().And<RayHit>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        Entity subject = self.AttachedTo;
        Snapshot snapshot = self.Snapshot;
        FrameHandle here = self.Frame;
        if (subject.IsNull || !snapshot.TryGetPoseIn(subject, here, out DPose pose))
            return;
        double dt = self.DeltaTime;
        ReadAnswers(ref events, subject);

        if (!started || (here != frame && !snapshot.Frames.IsAlive(frame)))
            Start(self, snapshot, subject, pose);
        else if (here != frame)
            CarryInto(snapshot.Frames.Relative(frame, here).Rotation, pose.Position, dt);
        frame = here;

        // The subject's velocity over the last finished step, from its positions.
        subjectVelocity = (pose.Position - lastPosition) / dt;
        lastPosition = pose.Position;

        // The camera's up turns toward the subject's up, and the heading turns with it.
        double turn = 1 - Math.Exp(-dt / TurnTime);
        Double3 nextUp = TurnToward(up, pose.Rotation.Rotate(Double3.UnitY), turn, heading);
        heading = Across(Turn(heading, up, nextUp), nextUp) is { Length: > 1e-9 } kept ? kept.Normalized() : heading;
        up = nextUp;
        // The heading swings about the up toward the subject's way, as a spring damped critically: the swing starts from
        // rest, so the arm never starts its swing at full speed. While the subject stands, the swing comes to rest.
        Double3 way = Across(subjectVelocity, up);
        double error = 0;
        if (way.Length > TurnSpeed)
        {
            Double3 target = way.Normalized();
            error = Math.Atan2(Double3.Dot(up, Double3.Cross(heading, target)), Double3.Dot(heading, target));
        }
        double rate = 1 / SwingTime;
        swingRate += (rate * rate * error - 2 * rate * swingRate) * dt;
        heading = DQuaternion.FromAxisAngle(up, swingRate * dt).Rotate(heading).Normalized();

        // The arm, short of the nearest body along it. freeArm is the longest arm that the free distance allows, in meters.
        double length = Math.Sqrt(behind * behind + above * above);
        double freeArm = Math.Clamp(free - WallGap, 0, length);
        arm = freeArm < arm ? freeArm : arm + (freeArm - arm) * (1 - Math.Exp(-dt / ArmTime));
        Double3 back = -heading * behind + up * above;

        // Relative to the subject, in the frame's axes: the eye, then the camera at the end of the arm.
        Double3 eye = pose.Rotation.Rotate((Double3)snapshot.RenderOf(subject).ViewOffset.Position);
        Double3 camera = eye + back * (arm / length);
        double pitch = Pitch(eye + heading * (ahead * arm / length) - camera, pose.Rotation, camera);
        Double3 forward = heading * Math.Cos(pitch) + up * Math.Sin(pitch);
        DQuaternion toSubject = pose.Rotation.Conjugate();
        self.AttachOffset = new Pose(toSubject.Rotate(camera).ToVector3(), View(toSubject.Rotate(forward), toSubject.Rotate(up)));

        // The free distance along the whole arm, from the eye where the step that runs now ends.
        self.Physics.CastRayAll(pose.Position + subjectVelocity * dt + eye, back, length + WallGap, default, RayTag);
    }

    /// <summary>
    /// The pitch of the look above the heading, in radians: the pitch of <paramref name="aim"/>, a way from the camera,
    /// lowered until the lowest corner of the subject's box lies within <see cref="halfFrameAngle"/> of the view's axis, but never
    /// so far that the highest corner lies farther than that above it. The subject's center is at the origin, turned by
    /// <paramref name="rotation"/>, and the camera at <paramref name="camera"/>. When a corner lies beside or behind the
    /// camera, the pitch is that of the aim.
    /// </summary>
    private double Pitch(Double3 aim, DQuaternion rotation, Double3 camera)
    {
        double lowest = double.PositiveInfinity, highest = double.NegativeInfinity;
        bool inFront = true;
        for (int corner = 0; corner < 8; corner++)
        {
            var local = new Double3((corner & 1) == 0 ? -subjectHalf.X : subjectHalf.X, (corner & 2) == 0 ? -subjectHalf.Y : subjectHalf.Y,
                (corner & 4) == 0 ? -subjectHalf.Z : subjectHalf.Z);
            Double3 seen = rotation.Rotate(local) - camera;
            double along = Double3.Dot(seen, heading), pitch = Math.Atan2(Double3.Dot(seen, up), along);
            inFront &= along > 0;
            lowest = Math.Min(lowest, pitch);
            highest = Math.Max(highest, pitch);
        }
        double toward = Math.Atan2(Double3.Dot(aim, up), Double3.Dot(aim, heading));
        return inFront ? Math.Max(Math.Min(toward, lowest + halfFrameAngle), highest - halfFrameAngle) : toward;
    }

    /// <summary>Takes the free distance from this tick's answers to the camera's ray: the nearest hit on a body that is not the subject's.</summary>
    private void ReadAnswers(scoped ref EventList events, Entity subject)
    {
        bool answered = false;
        double nearest = double.PositiveInfinity;
        for (int i = 0; i < events.Count; i++)
        {
            if (!events[i].Is<RayHit>())
                continue;
            RayHit hit = events[i].As<RayHit>();
            if (hit.Tag != RayTag)
                continue;
            answered = true;
            if (hit.IsHit && hit.Entity != subject)
                nearest = Math.Min(nearest, hit.Distance);
        }
        if (answered)
            free = nearest;
    }

    /// <summary>
    /// Starts the framing at the subject's <paramref name="pose"/>: the full arm, a heading at rest opposite to the
    /// camera's offset across the subject's up, or the subject's own -Z when the offset lies along that up.
    /// </summary>
    private void Start(Self self, Snapshot snapshot, Entity subject, DPose pose)
    {
        up = pose.Rotation.Rotate(Double3.UnitY);
        Double3 offset = Across(pose.Rotation.Rotate((Double3)self.AttachOffset.Position), up);
        heading = offset.Length > 1e-9 ? -offset.Normalized() : Across(pose.Rotation.Rotate(-Double3.UnitZ), up).Normalized();
        subjectVelocity = snapshot.TryGetVelocityIn(subject, self.Frame, out Double3 velocity) ? velocity : Double3.Zero;
        lastPosition = pose.Position - subjectVelocity * self.DeltaTime;
        arm = Math.Sqrt(behind * behind + above * above);
        swingRate = 0;
        started = true;
    }

    /// <summary>
    /// Carries the framing into a new frame, whose axes are <paramref name="rotation"/> from the old frame's: its vectors
    /// turn with the axes, and the subject's velocity in the tick of the hand-off counts as the velocity before it, since the
    /// two frames may move apart.
    /// </summary>
    private void CarryInto(DQuaternion rotation, Double3 position, double dt)
    {
        subjectVelocity = rotation.Rotate(subjectVelocity);
        up = rotation.Rotate(up);
        heading = rotation.Rotate(heading);
        lastPosition = position - subjectVelocity * dt;
    }

    /// <summary>The part of <paramref name="v"/> across the unit vector <paramref name="up"/>.</summary>
    private static Double3 Across(Double3 v, Double3 up) => v - up * Double3.Dot(v, up);

    /// <summary>
    /// The unit vector <paramref name="from"/> turned toward the unit vector <paramref name="to"/> by the share
    /// <paramref name="share"/> of the angle between them, about the axis across both. Opposite vectors turn about
    /// <paramref name="across"/>, a unit vector across <paramref name="from"/>.
    /// </summary>
    private static Double3 TurnToward(Double3 from, Double3 to, double share, Double3 across)
    {
        Double3 axis = Double3.Cross(from, to);
        double sine = axis.Length, cosine = Double3.Dot(from, to);
        if (sine < 1e-12 && cosine > 0)
            return from;
        axis = sine < 1e-12 ? across : axis / sine;
        return DQuaternion.FromAxisAngle(axis, Math.Atan2(sine, cosine) * share).Rotate(from).Normalized();
    }

    /// <summary><paramref name="v"/> turned by the shortest turn that takes the unit vector <paramref name="from"/> to the unit vector <paramref name="to"/>.</summary>
    private static Double3 Turn(Double3 v, Double3 from, Double3 to)
    {
        Double3 axis = Double3.Cross(from, to);
        double sine = axis.Length;
        return sine < 1e-12 ? v : DQuaternion.FromAxisAngle(axis / sine, Math.Atan2(sine, Double3.Dot(from, to))).Rotate(v);
    }

    /// <summary>The turn of a view that looks along the unit vector <paramref name="forward"/>, with its up toward <paramref name="up"/>: a view with no turn looks along -Z, with +Y up.</summary>
    private static Quaternion View(Double3 forward, Double3 up)
    {
        Double3 z = -forward;
        Double3 x = Double3.Cross(up, z).Normalized();
        Double3 y = Double3.Cross(z, x);
        // System.Numerics puts the turned axes in the rows.
        var axes = new Matrix4x4((float)x.X, (float)x.Y, (float)x.Z, 0, (float)y.X, (float)y.Y, (float)y.Z, 0, (float)z.X, (float)z.Y, (float)z.Z, 0, 0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(axes));
    }
}
