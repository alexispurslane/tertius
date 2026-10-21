using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;
using Tertius.Physics;

namespace Tertius.Simulation.Tests;

/// <summary>What a <see cref="ChaoticWorkload"/> did, so that a test can show that the workload reached every subsystem.</summary>
internal sealed class WorkloadTally
{
    public int Spawns, AttachedSpawns, BodySpawns, FrameSpawns, Destroys, DestroyMessages, Subscriptions, ComponentChanges, Moves, OffsetChanges;
    public int HandOffs, FrameStates, Contacts, OutsideSpawns, OutsideDestroys, OutsideFrameDestroys;

    /// <summary>Entity-ticks: the sum, over the ticks, of the live entities at the end of each tick.</summary>
    public long EntityTicks;

    public override string ToString() =>
        $"spawns {Spawns} (attached {AttachedSpawns}, with a body {BodySpawns}, as a frame {FrameSpawns}), destroys {Destroys}, destroy messages {DestroyMessages}, " +
        $"subscription changes {Subscriptions}, component changes {ComponentChanges}, moves {Moves}, attach offset changes {OffsetChanges}, hand-offs {HandOffs}, " +
        $"frame states {FrameStates}, contacts {Contacts}, outside-tick spawns {OutsideSpawns}, destroys {OutsideDestroys}, frame destroys {OutsideFrameDestroys}";
}

/// <summary>
/// A seeded workload that churns every subsystem of a <see cref="Simulation.World"/> at once: entities spawn (plain,
/// attached to a parent, with a body, or as a frame) and die, move, change frame (through hand-offs that a physics
/// backend reports, and through the death of the frame they are in), subscribe and unsubscribe, gain and lose
/// components, and send each other messages of every delivery mode. The physics is real Jolt, behind the recording wrapper
/// <see cref="FaultInjectingBackend"/>. Bodies that spawn in a frame whose bubble does not hold them, or beside a child
/// frame, make Jolt hand them off, and bodies that spawn close together make contacts. A shuttle that physics drives sends
/// the state of its frame in every step.
/// </summary>
/// <remarks>
/// The same seed gives the same run on one worker. On more workers the invariants must hold all the same, but the counts
/// in <see cref="Tally"/> may lose updates, since the churning components add to them without a lock. <see cref="Run"/> checks the world after every tick and again after
/// the changes made between ticks, and puts the seed in every failure message.
/// </remarks>
internal sealed class ChaoticWorkload : IDisposable
{
    private static readonly Topic Chatter = Topics.Get("tests.invariants.chatter");

    private readonly Random random;
    private readonly FaultInjectingBackend faults;
    private readonly List<FrameHandle> frameScratch = new();
    private readonly List<Entity> liveScratch = new();
    private readonly CrossCheckHistory history = new();
    private readonly FrameHandle star;
    private readonly Random sampler;

    /// <summary>
    /// The emitters that entities carry when the workload makes puffs: a glow, a lit look, and a glow that lives 0.3 s, so that
    /// puffs die at the end of their lives.
    /// </summary>
    private static readonly PuffEmitter[] Jets =
    [
        new(90, 4, 0.5f, new PuffLook(2, 0.1f, 0.5f, 3, PuffShading.Glow(new Vector3(4, 3, 2)))),
        new(45, 2, 1.2f, new PuffLook(1.5f, 0.2f, 1, 0.75f, PuffShading.Lit(new Vector3(0.6f)))),
        new(120, 9, 0.1f, new PuffLook(0.3f, 0.1f, 0.2f, 2, PuffShading.Glow(new Vector3(1, 2, 3)))),
    ];

    private static readonly MediumField[] Media =
    [
        MediumField.Uniform(1.225f, 2, new BoundingBox(new Vector3(-10), new Vector3(10))),
        MediumField.Planet(5, 1.225f, 8500, 1),
        MediumField.Uniform(0.5f, 0.5f),
        MediumField.None,
    ];

    /// <param name="puffCapacity">
    /// With a capacity, the workload also makes puffs (docs/plans/particles.md): its spawns and churning entities carry and change
    /// emitters, two frames hold media, a medium changes between ticks, and the capacity is the World's cap. Without one, the
    /// run is the same as before puffs existed.
    /// </param>
    public ChaoticWorkload(int seed, int chunkSize = 8, bool debugChecks = true, int initialEntities = 40, int maxDegreeOfParallelism = 1,
        int? puffCapacity = null)
    {
        Seed = seed;
        Puffs = puffCapacity is not null;
        random = new Random(seed);
        sampler = new Random(seed ^ 0x5EED);
        faults = new FaultInjectingBackend(new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 }));
        World = TestWorld.Create(out FrameHandle root, maxDegreeOfParallelism, faults, chunkSize, debugChecks, options =>
        {
            options.DisposePhysics = true;
            if (puffCapacity is { } capacity)
                options.PuffCapacity = capacity;
        });
        Root = root;
        // A frame that loses its parent moves to the next frame up, and a Kepler frame takes its G·M from the point gravity of
        // its parent, so the root has one too. It is so weak that it moves nothing: 1e-3 m³/s² pulls a body at 15 m with 4e-6 m/s².
        World.SetFrameGravity(root, GravityField.Point(1e-3));

        West = SpawnFrame(root, TestWorld.At(-500), new FrameSpec(FrameBounds.Sphere(300)));
        East = SpawnFrame(root, TestWorld.At(500), new FrameSpec(FrameBounds.Sphere(300)));
        FrameHandle bay = SpawnFrame(East, TestWorld.At(10), new FrameSpec(FrameBounds.Sphere(40)));
        // The Kepler frame orbits a star, far from every other frame. The star's gravity is strong, and nothing is spawned in it.
        star = SpawnFrame(root, TestWorld.At(0, 0, -30000), new FrameSpec(FrameBounds.Sphere(10000), GravityField.Point(1e6)));
        KeplerOrbit starOrbit = KeplerOrbit.Circular(2000, spinRate: 0.1);
        FrameHandle orbit = SpawnFrame(star, DPose.Identity,
            new FrameSpec(FrameBounds.Sphere(300)), desc => desc.OnKeplerOrbit(World, starOrbit));

        // The shuttle is a frame whose hull is a body: physics drives the frame, and hands the hull off between frames. It
        // flies at 6 m/s, so that Jolt sends the state of its frame in every step.
        Entity shuttleOwner = World.Spawn(new SpawnDesc
        {
            Frame = root,
            Pose = TestWorld.At(50),
            LinearVelocity = new Vector3(6, 0, 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
            Body = new BodyDesc(new SphereShapeDesc(5), Layer: BodyLayer.FrameHull, LinearDamping: 0, AngularDamping: 0),
            // It refuses Destroy messages, so that the frame that physics drives lives through the run and gets its state every step.
            Components = [new Invulnerable()],
        });
        FrameHandle shuttle = World.GetOwnedFrame(shuttleOwner);
        World.Camera = shuttleOwner;

        if (Puffs)
        {
            World.SetFrameMedium(West, Media[0]);
            World.SetFrameMedium(East, Media[1]);
            World.SetFrameMedium(orbit, Media[2]);
        }

        FrameHandle[] startFrames = [root, West, East, bay, orbit, shuttle];
        for (int i = 0; i < initialEntities; i++)
            SpawnIntoRandomFrame(startFrames);
        World.AssertInvariants();
    }

    public int Seed { get; }
    /// <summary>True when the workload makes puffs (see the constructor).</summary>
    public bool Puffs { get; }
    public World World { get; }
    public FrameHandle Root { get; }
    public FrameHandle West { get; }
    public FrameHandle East { get; }
    public WorkloadTally Tally { get; } = new();

    /// <summary>What the cross-checks have looked at so far.</summary>
    public CrossCheckHistory History => history;

    /// <summary>
    /// Runs this many ticks. After each tick it runs <see cref="World.AssertInvariants"/> and the cross-checks of
    /// <see cref="InvariantCrossChecks"/> on the state the sync point left. Then it makes changes between ticks, as a
    /// host would, and checks again. Every failure names the seed and the tick.
    /// </summary>
    public void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            TickOnce();

            ChangeBetweenTicks();
            Check("after the changes between ticks", afterTick: false);

        }
    }

    /// <summary>
    /// Runs one tick and checks the state the sync point left, with no changes between ticks after it. The inboxes then
    /// still match the topic sets they were routed from, which the cross-checks can verify.
    /// </summary>
    public void TickOnce()
    {
        try
        {
            World.Tick();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"seed {Seed}: tick {World.TickNumber} threw {ex.GetType().Name}: {ex.Message}", ex);
        }
        Tally.EntityTicks += World.Stats.LiveEntities;
        CountPhysicsOutput();
        Check("after the tick", afterTick: true);
    }

    /// <summary>
    /// Adds what Jolt sent in the last tick to the tally, and drops the records of the wrapper, which a long soak would
    /// pile up. A hand-off, a frame state and a contact are counted when Jolt sends them.
    /// </summary>
    private void CountPhysicsOutput()
    {
        foreach (PhysicsStepOutput output in faults.Outputs)
        {
            Tally.HandOffs += output.HandOffs.Count;
            Tally.FrameStates += output.Frames.Count;
            Tally.Contacts += output.Contacts.Count;
        }
        faults.Outputs.Clear();
        faults.AppliedChanges.Clear();
        faults.Steps.Clear();
        faults.Intents.Clear();
    }

    /// <summary>The violations that the cross-checks find in the state as it is now. Empty if it is sound.</summary>
    public List<string> FindViolations(bool afterTick) => InvariantCrossChecks.Find(World, history, afterTick, sampler);

    private void Check(string when, bool afterTick)
    {
        try
        {
            World.AssertInvariants();
        }
        catch (ContractException ex)
        {
            throw new InvalidOperationException($"seed {Seed}, tick {World.TickNumber}, {when}: {ex.Message}", ex);
        }
        FindViolations(afterTick).Should().BeEmpty($"seed {Seed}, tick {World.TickNumber}, {when}");
    }

    public void Dispose() => World.Dispose();

    // ---- The changes between ticks -------------------------------------------------------------------------------------

    private void ChangeBetweenTicks()
    {
        long tick = World.TickNumber;
        liveScratch.Clear();
        World.CopyLiveEntities(liveScratch);
        liveScratch.Remove(Root.Entity);

        if (tick % 10 == 0 && liveScratch.Count > 0)
        {
            World.Destroy(liveScratch[random.Next(liveScratch.Count)]);
            Tally.OutsideDestroys++;
        }
        if (tick % 40 == 0)
        {
            // A frame owner: the frame's contents move to its parent, outside a tick.
            frameScratch.Clear();
            World.Frames.GetAll(frameScratch);
            frameScratch.Remove(Root);
            if (frameScratch.Count > 0)
            {
                World.Destroy(frameScratch[random.Next(frameScratch.Count)].Entity);
                Tally.OutsideFrameDestroys++;
            }
        }
        if (tick % 5 == 0 || liveScratch.Count < 50)
        {
            int count = liveScratch.Count < 50 ? 15 : 4;
            frameScratch.Clear();
            World.Frames.GetAll(frameScratch);
            frameScratch.Remove(star);
            FrameHandle[] frames = [.. frameScratch];
            for (int i = 0; i < count; i++)
                SpawnIntoRandomFrame(frames);
            Tally.OutsideSpawns += count;
        }
        if (liveScratch.Count > 300)
        {
            for (int i = 0; i < 40 && liveScratch.Count > 0; i++)
            {
                int at = random.Next(liveScratch.Count);
                World.Destroy(liveScratch[at]);
                liveScratch.RemoveAt(at);
                Tally.OutsideDestroys++;
            }
        }
        if (Puffs && tick % 25 == 0)
        {
            // A new medium for a frame: its puffs feel it from the next step on.
            frameScratch.Clear();
            World.Frames.GetAll(frameScratch);
            FrameHandle frame = frameScratch[(int)(tick / 25 % frameScratch.Count)];
            if (!World.Frames.IsNumeric(frame))
                World.SetFrameMedium(frame, Media[(int)(tick / 25 % Media.Length)]);
        }
        if (tick % 7 == 0)
        {
            liveScratch.Clear();
            World.CopyLiveEntities(liveScratch);
            if (liveScratch.Count > 0)
            {
                Entity entity = liveScratch[random.Next(liveScratch.Count)];
                World.Subscribe(entity, Chatter);
                World.AddComponent(entity, new Recorder(EventMask.Of<Heal>()));
            }
        }
    }

    private void SpawnIntoRandomFrame(FrameHandle[] frames)
    {
        FrameHandle frame = frames[random.Next(frames.Length)];
        // A frame may have died since the list was made; its handle is then stale, and the spawn would be refused.
        if (!World.Frames.IsAlive(frame))
            frame = Root;
        bool withBody = random.Next(4) == 0;
        // Bodies spawn on a smaller square than the other entities, so that they land close to each other and touch.
        int reach = withBody ? 4 : 15;
        DPose pose = TestWorld.At(random.Next(-reach, reach + 1), 0, random.Next(-reach, reach + 1));
        // The drift is drawn for every entity, so that the random sequence stays the same. Only a body takes it, because
        // motion intents move an entity without one, and a spawn refuses its velocity.
        var drift = new Vector3(random.Next(-1, 2), 0, 0);
        World.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = pose,
            Body = withBody ? TestWorld.Ball : null,
            LinearVelocity = withBody ? drift : Vector3.Zero,
            Render = WithEmitter(random.Next(2) == 0 ? new RenderData { Mesh = new MeshId(1 + random.Next(3)), Flags = RenderFlags.Visible } : null, pose),
            Components = [NewChurn()],
            Subscriptions = random.Next(3) == 0 ? [Chatter] : [],
        });
        if (withBody)
            Tally.BodySpawns++;
    }

    /// <summary>
    /// Spawns a frame without a body in <paramref name="parent"/> at <paramref name="pose"/>, after <paramref name="rails"/>, when
    /// it is given, puts it on its rails (see <see cref="RailsSpawn"/>).
    /// </summary>
    private FrameHandle SpawnFrame(FrameHandle parent, DPose pose, FrameSpec spec, Func<SpawnDesc, SpawnDesc>? rails = null)
    {
        var desc = new SpawnDesc { Frame = parent, Pose = pose, AsFrame = spec };
        return World.GetOwnedFrame(World.Spawn(rails is null ? desc : rails(desc)));
    }

    private Churn NewChurn() => new((ulong)random.NextInt64(), this);

    /// <summary>
    /// When the workload makes puffs, the render row with an emitter that the place picks, with no draw from the generator,
    /// so that the run's sequence stays the same. Otherwise the row as it is.
    /// </summary>
    private RenderData? WithEmitter(RenderData? render, in DPose pose)
    {
        if (!Puffs)
            return render;
        RenderData row = render ?? new RenderData();
        row.Emitter = Jets[(int)Math.Abs(pose.Position.X + 2 * pose.Position.Z) % Jets.Length];
        return row;
    }

    /// <summary>The emitter that a churning entity writes, picked by bits of its generator's state, or the default to switch it off.</summary>
    private static PuffEmitter ChurnEmitter(ulong rng) => (rng & 4) == 0 ? default : Jets[(int)((rng >> 3) % (ulong)Jets.Length)];

    // ---- The entity that does the churning ---------------------------------------------------------------------------

    /// <summary>
    /// Does one random thing per tick from a seeded generator: spawn (plain, attached, with a body, or a frame), destroy
    /// itself or a neighbor, subscribe or unsubscribe, send, change its components, or move.
    /// </summary>
    private sealed class Churn(ulong seed, ChaoticWorkload workload) : IComponent
    {
        private readonly List<Entity> neighbors = new();
        private ulong rng = seed | 1;
        private Recorder? extra;

        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tick>().And<Ping>().And<FrameChanged>().And<Collision>();

        private int Next(int n)
        {
            // xorshift64: deterministic, and cheap enough for a hot loop.
            rng ^= rng << 13;
            rng ^= rng >> 7;
            rng ^= rng << 17;
            return (int)(rng % (ulong)n);
        }

        private Churn Child(ulong salt) => new(rng + salt, workload);

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (!events.Of<Tick>().Any() || self.Frame.IsNull)
                return;
            WorkloadTally tally = workload.Tally;
            DPose here = new(self.PoseInOwnFrame().Position + new Double3(Next(21) - 10, 0, Next(21) - 10), DQuaternion.Identity);
            int roll = Next(100);
            if (roll < 3)
            {
                self.Spawn(new SpawnDesc { Frame = self.Frame, Pose = here, Components = [Child(1)] });
                tally.Spawns++;
            }
            else if (roll < 5 && !self.Body.Exists)
            {
                // The child may itself be a parent later, so chains grow.
                self.Spawn(new SpawnDesc
                {
                    Frame = self.Frame,
                    AttachTo = self.Entity,
                    AttachOffset = new Pose(new Vector3(Next(5) - 2, 0, Next(5) - 2), Quaternion.Identity),
                    Components = [Child(4)],
                });
                tally.AttachedSpawns++;
            }
            else if (roll < 7)
            {
                self.Spawn(new SpawnDesc
                {
                    Frame = self.Frame,
                    Pose = here,
                    Body = TestWorld.Ball,
                    LinearVelocity = new Vector3(Next(3) - 1, 0, 0),
                    Components = [Child(2)],
                });
                tally.BodySpawns++;
            }
            else if (roll < 8)
            {
                self.Spawn(new SpawnDesc
                {
                    Frame = self.Frame,
                    Pose = here,
                    AsFrame = new FrameSpec(FrameBounds.Sphere(20)),
                    Components = [Child(3)],
                    Subscriptions = [Chatter],
                });
                tally.FrameSpawns++;
            }
            else if (roll < 13)
            {
                self.Destroy();
                tally.Destroys++;
            }
            else if (roll < 17)
            {
                self.Snapshot.QueryRadius(self.Frame, self.PoseInOwnFrame().Position, 40, neighbors);
                neighbors.Remove(workload.Root.Entity);
                if (neighbors.Count > 0)
                {
                    outbox.Send(neighbors[Next(neighbors.Count)], new Destroy());
                    tally.DestroyMessages++;
                }
            }
            else if (roll < 24)
            {
                Topic topic = Next(2) == 0 ? Chatter : self.Frame.Topic;
                if (Next(2) == 0)
                    self.Subscribe(topic);
                else
                    self.Unsubscribe(topic);
                tally.Subscriptions++;
            }
            else if (roll < 30)
            {
                outbox.Send(Chatter, new Ping { Value = roll });
                outbox.Send(self.Frame.Topic, new Ping { Value = roll });
                outbox.SendAfter(1 + Next(3), new Broadcast(), new Ping { Value = roll });
            }
            else if (roll < 33)
            {
                if (extra == null)
                {
                    extra = new Recorder(EventMask.Of<Heal>(), priority: Next(3) - 1);
                    self.AddComponent(extra);
                }
                else
                {
                    self.RemoveComponent(extra);
                    extra = null;
                }
                tally.ComponentChanges++;
            }
            else if (roll < 50 && !self.Body.Exists && self.AttachedTo.IsNull)
            {
                // Any entity without a body writes its transform, also the owner of a frame on rails.
                self.SetTransform(here);
                self.Render.Scale = new Vector3(1 + Next(3));
                if (workload.Puffs)
                    self.Render.Emitter = ChurnEmitter(rng);
                tally.Moves++;
            }
            else if (roll < 60 && !self.AttachedTo.IsNull)
            {
                self.AttachOffset = new Pose(new Vector3(Next(5) - 2, 0, Next(5) - 2), Quaternion.Identity);
                tally.OffsetChanges++;
            }
            else if (self.Body.Exists)
            {
                self.Body.AddForce(new Vector3(Next(3) - 1, 0, 0));
                if (workload.Puffs)
                    self.Render.Emitter = ChurnEmitter(rng);
            }
        }
    }
}
