using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>A contact that Jolt reported on a worker thread, before it is mapped to entities.</summary>
internal readonly record struct RawContact(
    BodyID Body1,
    BodyID Body2,
    ulong UserData1,
    ulong UserData2,
    Double3 Point,
    Vector3 Normal,
    float NormalVelocity)
{
    public ulong PairKey => FrameWorld.PairKey(Body1, Body2);
}

/// <summary>
/// The limits by which Jolt sizes the buffers of a physics system when it creates the system. A system never holds more
/// than <see cref="MaxBodies"/> bodies.
/// </summary>
internal readonly record struct SystemLimits(int MaxBodies, int MaxBodyPairs, int MaxContactConstraints)
{
    public static SystemLimits From(JoltBackendOptions options) =>
        new(options.MaxBodiesPerFrame, options.MaxBodyPairsPerFrame, options.MaxContactConstraintsPerFrame);

    /// <summary>
    /// Twice every limit. The number of bodies stops at <see cref="BodyID.MaxBodyIndex"/>, the most that a body ID can
    /// count, and the other limits stop at <see cref="int.MaxValue"/>.
    /// </summary>
    public SystemLimits Doubled() => new(
        (int)Math.Min(2L * MaxBodies, BodyID.MaxBodyIndex),
        (int)Math.Min(2L * MaxBodyPairs, int.MaxValue),
        (int)Math.Min(2L * MaxContactConstraints, int.MaxValue));
}

/// <summary>
/// The Jolt physics system of one reference frame. Positions in the system are frame coordinates, so a body's pose
/// in Jolt is its pose in the frame. Gravity is zero: the backend applies the frame's effective gravity as forces.
/// </summary>
/// <remarks>
/// A system that is full can be replaced by a larger one (<see cref="ReplaceSystem"/>). The <see cref="FrameWorld"/>
/// stays the same object, with the same members, so every reference to it stays valid.
/// </remarks>
internal sealed class FrameWorld : IDisposable
{
    private readonly Layers.Tables layers;
    private readonly float penetrationSlop;
    private readonly Lock contactGate = new();
    private readonly List<RawContact> added = new(64);
    private readonly List<ulong> removed = new(64);
    // Body pairs in contact as Jolt's contact cache holds them, with the number of touching sub-shape pairs. A pair starts
    // when this goes from 0 to 1, unless it is held.
    private readonly Dictionary<ulong, int> touching = new();
    // Body pairs that touch as far as the backend knows, but that Jolt's contact cache no longer holds. Jolt finds contacts
    // only for a body that it simulates. So it drops the pair of two bodies that it does not simulate (a body fell asleep
    // or a hull became static, on something static or asleep) while the system keeps stepping. A growth drops every pair,
    // because the new system starts with an empty cache. Jolt reports a held pair as added when it finds it again: the
    // contact continues. A held pair ends at the first update that simulates one of its bodies and does not find it.
    private readonly HashSet<ulong> held = new();
    // True from an update until the contacts that it reported are drained.
    private bool updatedSinceDrain;
    private readonly List<ulong> purgeScratch = new();
    // The pairs that a growth holds, before they take the new IDs, and the held pairs that a drain ends.
    private readonly List<ulong> heldScratch = new();

    public FrameWorld(FrameHandle frame, Layers.Tables layers, JoltBackendOptions options)
    {
        Frame = frame;
        this.layers = layers;
        penetrationSlop = options.PenetrationSlop;
        Limits = SystemLimits.From(options);
        System = CreateSystem(Limits);
        Bodies = System.BodyInterface;
        BodiesNoLock = System.BodyInterfaceNoLock;
        Listen(System);
    }

    public FrameHandle Frame { get; }
    public PhysicsSystem System { get; private set; }
    public BodyInterface Bodies { get; private set; }

    /// <summary>The body interface that takes no body locks. Use it only on the physics thread, between updates.</summary>
    private BodyInterface BodiesNoLock { get; set; }

    /// <summary>The limits of <see cref="System"/>.</summary>
    public SystemLimits Limits { get; private set; }

    /// <summary>True when <see cref="System"/> holds as many bodies as it can, so that Jolt refuses one more.</summary>
    public bool IsFull => System.BodiesCount >= System.MaxBodies;

    /// <summary>Records whose primary body or character lives in this system.</summary>
    public List<BodyRecord> Members { get; } = new();

    public int CharacterCount { get; set; }

    /// <summary>Set after the first update that overflowed a Jolt limit, so the warning appears once.</summary>
    public bool ReportedOverflow { get; set; }

    // The frame's gravity state, to wake sleeping bodies when it changes: the field at the last step, and the proper
    // acceleration when the frame's bodies last woke, or when the frame first stepped. A proper acceleration that drifts
    // by a small amount each step still wakes them once it has drifted far enough.
    public bool HasGravityState;
    public Vector3 WakeProperAcceleration;
    public GravityField LastGravity;

    // Records reported in the last and the current step. A body that falls asleep gets one final report.
    public List<BodyRecord> ReportedLast = new();
    public List<BodyRecord> ReportedNow = new();

    public void AddMember(BodyRecord record)
    {
        record.MemberIndex = Members.Count;
        Members.Add(record);
    }

    public void RemoveMember(BodyRecord record)
    {
        int index = record.MemberIndex;
        int last = Members.Count - 1;
        BodyRecord moved = Members[last];
        Members[index] = moved;
        moved.MemberIndex = index;
        Members.RemoveAt(last);
        record.MemberIndex = -1;
    }

    /// <summary>
    /// Creates an empty system with this frame's filters and settings and the given limits. Nothing listens to its
    /// contacts until <see cref="ReplaceSystem"/> makes it this frame's system.
    /// </summary>
    public PhysicsSystem CreateSystem(SystemLimits limits)
    {
        var system = new PhysicsSystem(new PhysicsSystemSettings
        {
            MaxBodies = limits.MaxBodies,
            MaxBodyPairs = limits.MaxBodyPairs,
            MaxContactConstraints = limits.MaxContactConstraints,
            ObjectLayerPairFilter = layers.Pairs,
            BroadPhaseLayerInterface = layers.BroadPhase,
            ObjectVsBroadPhaseLayerFilter = layers.ObjectVsBroadPhase,
        });
        system.Gravity = Vector3.Zero;
        PhysicsSettings settings = system.Settings;
        settings.PenetrationSlop = penetrationSlop;
        system.Settings = settings;
        return system;
    }

    /// <summary>
    /// Makes <paramref name="larger"/>, which already holds every body of the current system, this frame's system, and
    /// disposes the current one. Every pair that touches now becomes held: the new system finds it again in the first
    /// update that simulates one of its bodies, and <see cref="DrainContacts"/> does not take that for a new contact. A
    /// pair that was held already stays held, so any number of growths keeps every pair that touched before the first.
    /// </summary>
    /// <param name="larger">The new system, with <paramref name="limits"/>.</param>
    /// <param name="limits">The limits of <paramref name="larger"/>.</param>
    /// <param name="renamed">
    /// The bodies whose ID changed in the move: by full ID in the current system, their full ID in the new one.
    /// </param>
    public void ReplaceSystem(PhysicsSystem larger, SystemLimits limits, IReadOnlyDictionary<uint, uint> renamed)
    {
        // Contacts are drained after every update, and only an update reports them.
        Contract.Requires(added.Count == 0 && removed.Count == 0);
        System.OnContactAdded -= OnContactAdded;
        System.OnContactRemoved -= OnContactRemoved;
        System.Dispose();
        System = larger;
        Bodies = larger.BodyInterface;
        BodiesNoLock = larger.BodyInterfaceNoLock;
        Limits = limits;
        Listen(larger);
        ReportedOverflow = false;

        lock (contactGate)
        {
            heldScratch.Clear();
            heldScratch.AddRange(held);
            heldScratch.AddRange(touching.Keys);
            held.Clear();
            foreach (ulong key in heldScratch)
                held.Add(PairKey(Renamed(First(key), renamed), Renamed(Second(key), renamed)));
            touching.Clear();
        }
    }

    private static BodyID Renamed(uint id, IReadOnlyDictionary<uint, uint> renamed) =>
        new(renamed.TryGetValue(id, out uint newId) ? newId : id);

    /// <summary>Runs one update of the system. Call it on the physics thread, with the native gate held.</summary>
    public PhysicsUpdateError Update(float deltaTime, JobSystem jobs)
    {
        updatedSinceDrain = true;
        return System.Update(deltaTime, 1, jobs);
    }

    private void Listen(PhysicsSystem system)
    {
        system.OnContactAdded += OnContactAdded;
        system.OnContactRemoved += OnContactRemoved;
    }

    public static ulong PairKey(BodyID a, BodyID b)
    {
        uint x = a.ID, y = b.ID;
        return x < y ? ((ulong)x << 32) | y : ((ulong)y << 32) | x;
    }

    /// <summary>The body of a pair key with the lower ID.</summary>
    private static BodyID First(ulong key) => new((uint)(key >> 32));

    /// <summary>The body of a pair key with the higher ID.</summary>
    private static BodyID Second(ulong key) => new((uint)key);

    private static bool Involves(ulong key, BodyID body) => (uint)(key >> 32) == body.ID || (uint)key == body.ID;

    /// <summary>True when Jolt simulates the body: it is awake and not static. False for a body that left the system.</summary>
    private bool IsAwake(BodyID body) => BodiesNoLock.IsActive(body);

    /// <summary>
    /// Moves the contacts that Jolt reported during the last update into the touching-pair table, and appends the
    /// ones that started a new body pair contact to <paramref name="started"/>. A pair that Jolt drops becomes held, and a
    /// held pair that Jolt finds again continues. A held pair ends when the update simulated one of its bodies and did not
    /// find it. Call it on the physics thread after the update, with no update running. Visits every held pair after an
    /// update, so it costs O(held pairs) for a system that steps.
    /// </summary>
    public void DrainContacts(List<RawContact> started)
    {
        lock (contactGate)
        {
            // Jolt reports from several threads, so sort for a deterministic choice when one pair has many points.
            added.Sort(static (a, b) =>
            {
                int c = a.PairKey.CompareTo(b.PairKey);
                if (c != 0) return c;
                c = a.Point.X.CompareTo(b.Point.X);
                if (c != 0) return c;
                c = a.Point.Y.CompareTo(b.Point.Y);
                return c != 0 ? c : a.Point.Z.CompareTo(b.Point.Z);
            });
            // Additions first: a pair that gains one sub-shape contact and loses another in the same step continues.
            foreach (RawContact contact in added)
            {
                ulong key = contact.PairKey;
                touching.TryGetValue(key, out int count);
                touching[key] = count + 1;
                if (count == 0 && !held.Remove(key))
                    started.Add(contact);
            }
            foreach (ulong key in removed)
            {
                if (!touching.TryGetValue(key, out int count))
                    continue;
                if (count > 1)
                {
                    touching[key] = count - 1;
                    continue;
                }
                // The pair ended, or Jolt dropped it because it simulates neither body. The check below tells which.
                touching.Remove(key);
                held.Add(key);
            }
            added.Clear();
            removed.Clear();

            // Jolt finds the contacts of every body that it simulates, also of one that woke during the update. So a held
            // pair with a simulated body that Jolt did not find has ended. Two bodies that Jolt did not simulate did not
            // move apart.
            // Stryker disable once all : the test only saves work; without an update Jolt simulates no body, so no pair ends.
            if (updatedSinceDrain && held.Count > 0)
            {
                heldScratch.Clear();
                foreach (ulong key in held)
                    if (IsAwake(First(key)) || IsAwake(Second(key)))
                        heldScratch.Add(key);
                foreach (ulong key in heldScratch)
                    held.Remove(key);
            }
            updatedSinceDrain = false;
        }
    }

    /// <summary>Forgets every contact of a body that leaves the system, touching or held.</summary>
    public void ForgetContacts(BodyID body)
    {
        if (body.IsInvalid)
            return;
        lock (contactGate)
        {
            purgeScratch.Clear();
            foreach (ulong key in touching.Keys)
                if (Involves(key, body))
                    purgeScratch.Add(key);
            foreach (ulong key in purgeScratch)
                touching.Remove(key);
            if (held.Count > 0)
                held.RemoveWhere(key => Involves(key, body));
        }
    }

    public void Dispose()
    {
        System.OnContactAdded -= OnContactAdded;
        System.OnContactRemoved -= OnContactRemoved;
        System.Dispose();
    }

    // Runs on Jolt worker threads with the bodies locked. It must not use any locking body interface.
    private unsafe void OnContactAdded(PhysicsSystem system, in Body body1, in Body body2, in ContactManifold manifold, ref ContactSettings settings)
    {
        ulong data1 = body1.GetUserData(), data2 = body2.GetUserData();
        if (!IsReportable(data1) || !IsReportable(data2))
            return;

        uint count = manifold.PointCount;
        Double3 point = Double3.Zero;
        if (count > 0)
        {
            RVector3 p;
            for (uint i = 0; i < count; i++)
            {
                JoltNative.ContactPointOn1(manifold.Handle, i, &p);
                point += p.ToDouble3();
            }
            point /= count;
        }
        else
        {
            body1.GetRCenterOfMassPosition(out RVector3 com);
            point = com.ToDouble3();
        }

        Vector3 normal = manifold.WorldSpaceNormal;
        Vector3 v1 = PointVelocity(body1, point), v2 = PointVelocity(body2, point);
        var contact = new RawContact(body1.ID, body2.ID, data1, data2, point, normal, Vector3.Dot(v2 - v1, normal));
        lock (contactGate)
            added.Add(contact);
    }

    private void OnContactRemoved(PhysicsSystem system, ref SubShapeIDPair pair)
    {
        ulong key = PairKey(pair.Body1ID, pair.Body2ID);
        lock (contactGate)
            removed.Add(key);
    }

    private static bool IsReportable(ulong data)
    {
        BodyKind kind = BodyUserData.Kind(data);
        return kind is BodyKind.Primary or BodyKind.Interior or BodyKind.CharacterInner;
    }

    private static Vector3 PointVelocity(Body body, Double3 point)
    {
        body.GetRCenterOfMassPosition(out RVector3 com);
        Vector3 offset = (point - com.ToDouble3()).ToVector3();
        return body.GetLinearVelocity() + Vector3.Cross(body.GetAngularVelocity(), offset);
    }
}
