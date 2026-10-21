using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>A place on a <see cref="Walker"/>'s path: <paramref name="Position"/>, in meters, in <paramref name="Frame"/>.</summary>
public readonly record struct Waypoint(FrameHandle Frame, Double3 Position);

/// <summary>
/// Walks a character along a path of waypoints, in order, with the character controls, as a scripted passenger does: on
/// every tick it asks for a walk at <see cref="PersonControl.WalkSpeed"/> toward the next waypoint, across the character's
/// up. A waypoint counts as reached when the character's center is within <see cref="ArrivalRadius"/> of it across that
/// up, and the walker turns toward the next one. In the last meter before the last waypoint it slows, and within
/// <see cref="StopRadius"/> of it it stops for good. Its entity needs a character body.
/// </summary>
/// <remarks>
/// <para>Each waypoint is a position in a frame of its own, so a path can lead from the ground into a ship. The walker reads
/// its own position in that frame on every tick, and turns the way to the waypoint into the axes of its own frame, in
/// which the walk is written. So the waypoints stay fixed in their frames however those frames move, and a hand-off
/// that moves the character into another frame changes nothing for the path. When the frame of the next waypoint dies,
/// the way is gone, and the walker stops where it is for good.</para>
/// <para>The walk moves the character only while physics stands it on walkable ground (see
/// <see cref="BodyHandle.MoveCharacter"/>), which also climbs steps up to 0.4 m. The walker reads the pose at the end of
/// the last finished step, and the walk that it writes applies only in the step after the one that runs beside its tick.
/// So after the pose that it reads as a waypoint reached, the character walks one more step on its old walk: 0.05 m at
/// 3 m/s. The slowdown before the last waypoint keeps that step short there, so the character stops on it. Physics keeps the last walk that a character asked for, so the
/// walker asks for no walk once when it stops, and nothing after that. The path has no jumps, because no path of the
/// demo needs one.</para>
/// </remarks>
/// <param name="path">The waypoints, in the order that the walker visits them.</param>
public sealed class Walker(IReadOnlyList<Waypoint> path) : IComponent
{
    /// <summary>How near the character's center comes to a waypoint before the last across its up, in meters, for it to count as reached.</summary>
    public const double ArrivalRadius = 0.2;

    /// <summary>How near the character's center comes to the last waypoint across its up, in meters, before the walker stops.</summary>
    public const double StopRadius = 0.05;

    // Within the last speed / StopRate before the last waypoint, 1 m at PersonControl.WalkSpeed, the speed of the walk is
    // this rate times the distance, in 1/s, so the distance falls by e every 1/3 s, and the step of lag moves the
    // character only 1/20 of the distance.
    private const double StopRate = 3;

    private readonly Waypoint[] path = [.. path];
    // The index of the waypoint that the walker walks toward, or path.Length once it has stopped for good.
    private int next;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        next = Step(self, path, next, PersonControl.WalkSpeed);

    /// <summary>
    /// Walks the character of <paramref name="self"/> one tick along <paramref name="path"/>, toward the waypoint at index
    /// <paramref name="next"/>, at <paramref name="speed"/> in m/s, and returns the index of the waypoint that it walks
    /// toward now. It skips each waypoint that the character has reached, slows within the last meter before the last
    /// waypoint, and once the character is within <see cref="StopRadius"/> of it asks for no walk and returns the path's
    /// length. When the frame of the next waypoint is gone, it logs that, asks for no walk and returns the path's length.
    /// For an index of the path's length it writes nothing, so a walker that stopped asks for no walk only once.
    /// </summary>
    public static int Step(Self self, ReadOnlySpan<Waypoint> path, int next, double speed)
    {
        if (next >= path.Length)
            return path.Length;
        for (; next < path.Length; next++)
        {
            Waypoint target = path[next];
            FrameTree frames = self.Snapshot.Frames;
            if (!frames.IsAlive(target.Frame))
            {
                self.Log($"Walker stops: the frame {target.Frame} of waypoint {next} is gone.");
                next = path.Length;
                break;
            }
            // The way to the waypoint, in its frame's axes, then in the axes of the character's frame, without its part
            // along the character's up.
            Double3 way = frames.Relative(target.Frame, self.Frame).Rotation.Rotate(target.Position - self.PoseIn(target.Frame).Position);
            Double3 up = self.PoseIn(self.Frame).Rotation.Rotate(Double3.UnitY);
            Double3 across = way - Double3.Dot(way, up) * up;
            double distance = across.Length;
            bool last = next == path.Length - 1;
            if (distance > (last ? StopRadius : ArrivalRadius))
            {
                double pace = last ? Math.Min(speed, StopRate * distance) : speed;
                self.Body.MoveCharacter((across * (pace / distance)).ToVector3());
                return next;
            }
        }
        // The path is done, or the way to its next waypoint is gone.
        self.Body.MoveCharacter(default);
        return path.Length;
    }
}
