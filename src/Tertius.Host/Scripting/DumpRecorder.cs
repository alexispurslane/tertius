using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Host.Scripting;

/// <summary>
/// Takes the state dumps of one run. After each tick, <see cref="RecordRoutedEvents"/> keeps what the sync point routed,
/// and <see cref="Take"/> hands those events to the next dump. So a dump holds every event since the previous dump.
/// </summary>
internal sealed class DumpRecorder(World world, SceneInstance scene, EventPayloads payloads)
{
    private List<EventRecord> eventsSinceDump = new();

    /// <summary>Keeps the events that the last sync point routed, in routing order. Call it after every tick.</summary>
    public void RecordRoutedEvents()
    {
        foreach (ref readonly Event e in world.RoutedEvents)
        {
            Type type = EventTypeRegistry.TypeOf(e.Type);
            eventsSinceDump.Add(new EventRecord(
                world.TickNumber,
                type.Name,
                TargetOf(e.Target),
                e.Sender.IsNull ? null : scene.IdOf(e.Sender),
                payloads.Of(type).Box(e)));
        }
    }

    /// <summary>The state of the world now, with the events since the previous dump.</summary>
    public StateDump Take(string name)
    {
        var live = new List<Entity>();
        world.CopyLiveEntities(live);
        var frames = new List<FrameHandle>();
        world.Frames.GetAll(frames);

        var dump = new StateDump(
            name,
            world.TickNumber,
            world.Time,
            world.IsAlive(world.Camera) ? scene.IdOf(world.Camera) : null,
            live.Select(EntityRecordOf).ToArray(),
            frames.Select(FrameRecordOf).ToArray(),
            eventsSinceDump,
            world.Errors.ToArray());
        eventsSinceDump = new List<EventRecord>();
        return dump;
    }

    private EntityRecord EntityRecordOf(Entity entity)
    {
        FrameHandle frame = world.GetFrame(entity);
        DPose pose;
        Double3 velocity;
        Vector3 angularVelocity;
        FramePose rootPose;
        if (frame.IsNull)
        {
            // The owner of a root frame is in no frame. It is its own root frame, at that frame's origin, and its pose
            // and velocities are the frame's own.
            FrameHandle own = world.GetOwnedFrame(entity);
            pose = world.Frames.LocalPose(own);
            velocity = world.Frames.LinearVelocity(own);
            angularVelocity = world.Frames.AngularVelocity(own);
            rootPose = new FramePose(scene.IdOf(entity), Double3.Zero, DQuaternion.Identity);
        }
        else
        {
            pose = world.PoseIn(entity, frame);
            velocity = world.VelocityIn(entity, frame);
            angularVelocity = world.AngularVelocityIn(entity, frame);
            FrameHandle root = RootOf(frame);
            DPose inRoot = world.PoseIn(entity, root);
            rootPose = new FramePose(scene.IdOf(root.Entity), inRoot.Position, inRoot.Rotation);
        }
        return new EntityRecord(
            scene.IdOf(entity),
            new FramePose(frame.IsNull ? null : scene.IdOf(frame.Entity), pose.Position, pose.Rotation),
            rootPose,
            velocity,
            angularVelocity,
            world.GetBody(entity) is { Motion: BodyMotion.Character } ? world.Snapshot.IsStanding(entity) : null);
    }

    private FrameRecord FrameRecordOf(FrameHandle frame)
    {
        FrameHandle parent = world.Frames.Parent(frame);
        return new FrameRecord(scene.IdOf(frame.Entity), parent.IsNull ? null : scene.IdOf(parent.Entity), FollowsOf(frame));
    }

    /// <summary>
    /// What moved <paramref name="frame"/> in the tick that the dump closes (see <see cref="FrameRecord.Follows"/>). The
    /// samples that the frames follow also hold the records of physics, teleports and writes of a transform, so only the
    /// buffer of the samples that the owners wrote in the tick tells rails apart.
    /// </summary>
    private string FollowsOf(FrameHandle frame) =>
        world.Frames.HullMotion(frame) == BodyMotion.Dynamic ? "physics"
        : world.Frames.TryGetAppliedSample(frame, out _) ? "rails"
        : "static";

    private FrameHandle RootOf(FrameHandle frame)
    {
        for (FrameHandle parent = world.Frames.Parent(frame); !parent.IsNull; parent = world.Frames.Parent(frame))
            frame = parent;
        return frame;
    }

    private MessageTarget TargetOf(Target target) => target switch
    {
        Entity entity => new ToEntity(scene.IdOf(entity)),
        Topic { IsFrameTopic: true } topic => new ToFrame(FrameOwnerId(topic)),
        Topic topic => new ToTopic(Topics.NameOf(topic)),
        Broadcast => new ToEveryone(),
        _ => throw new InvalidOperationException("A routed event has a target with no value."),
    };

    /// <summary>The scene id of the owner of a frame topic's frame, or the topic's name if that frame is gone.</summary>
    private string FrameOwnerId(Topic topic)
    {
        var frames = new List<FrameHandle>();
        world.Frames.GetAll(frames);
        foreach (FrameHandle frame in frames)
            if (frame.Index == topic.FrameIndex && frame.Generation == topic.Generation)
                return scene.IdOf(frame.Entity);
        return $"#{Topics.NameOf(topic)}";
    }
}
