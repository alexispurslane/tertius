using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Host.Scripting;

/// <summary>
/// Takes the state dumps of one run. After each tick that a dump step comes after, the runner calls
/// <see cref="RecordRoutedEvents"/>, which keeps what the sync point routed, and <see cref="Take"/> hands those events to the
/// next dump. So a dump holds every event since the previous dump. After the last dump step, the runner records nothing,
/// because no dump would read it.
/// </summary>
internal sealed class DumpRecorder(World world, SceneInstance scene, EventPayloads payloads)
{
    private List<EventRecord> eventsSinceDump = [];

    /// <summary>Keeps the events that the last sync point routed, in routing order. Call it after every tick that a dump step comes after.</summary>
    public void RecordRoutedEvents()
    {
        foreach (ref readonly Event e in world.RoutedEvents)
        {
            Type type = EventTypeRegistry.TypeOf(e.Type);
            eventsSinceDump.Add(new(
                Tick: world.TickNumber,
                Event: type.Name,
                To: TargetOf(e.Target),
                From: e.Sender.IsNull ? null : scene.IdOf(e.Sender),
                Payload: payloads.Of(type).Box(e)));
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
            Name: name,
            Tick: world.TickNumber,
            Time: world.Time,
            Camera: world.IsAlive(world.Camera) ? scene.IdOf(world.Camera) : null,
            Entities: [.. live.Select(EntityRecordOf)],
            Frames: [.. frames.Select(FrameRecordOf)],
            Events: eventsSinceDump,
            Errors: [.. world.Errors]);
        eventsSinceDump = [];
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
            rootPose = new(scene.IdOf(entity), Double3.Zero, DQuaternion.Identity);
        }
        else
        {
            pose = world.PoseIn(entity, frame);
            velocity = world.VelocityIn(entity, frame);
            angularVelocity = world.AngularVelocityIn(entity, frame);
            FrameHandle root = RootOf(frame);
            DPose inRoot = world.PoseIn(entity, root);
            rootPose = new(scene.IdOf(root.Entity), inRoot.Position, inRoot.Rotation);
        }

        return new(
            Id: scene.IdOf(entity),
            Pose: new(frame.IsNull ? null : scene.IdOf(frame.Entity), pose.Position, pose.Rotation),
            RootPose: rootPose,
            Velocity: velocity,
            AngularVelocity: angularVelocity,
            Standing: world.GetBody(entity) is { Motion: BodyMotion.Character } ? world.Snapshot.IsStanding(entity) : null);
    }

    private FrameRecord FrameRecordOf(FrameHandle frame)
    {
        FrameHandle parent = world.Frames.Parent(frame);
        return new(scene.IdOf(frame.Entity), parent.IsNull ? null : scene.IdOf(parent.Entity), FollowsOf(frame));
    }

    /// <summary>How <paramref name="frame"/> followed its owner at the dump's sync point (see
    /// <see cref="FrameRecord.Follows"/>).</summary>
    private string FollowsOf(FrameHandle frame) => world.Frames.Followed(frame) switch
    {
        FollowKind.Rails => "rails",
        FollowKind.Kinematic => "kinematic",
        FollowKind.Physics => "physics",
        FollowKind.Static => "static"
    };

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
        null => throw new InvalidOperationException("A routed event has a target with no value.")
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
