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
    // Body pairs in contact, with the number of touching sub-shape pairs. A pair starts when this goes from 0 to 1.
    private readonly Dictionary<ulong, int> touching = new();
    // The body pairs that touched before the growths since the last update. The new system has no contact cache, so its
    // first update reports each pair that still touches as added. Such a pair continues: it did not start.
    private readonly HashSet<ulong> carried = new();
    // False from a growth until the first update of the new system, which reports every pair that still touches.
    private bool updatedSinceGrowth = true;
    private readonly List<ulong> purgeScratch = new();
    // The pairs that a growth carries over, before they take the new IDs.
    private readonly List<ulong> carryScratch = new();

    public FrameWorld(FrameHandle frame, Layers.Tables layers, JoltBackendOptions options)
    {
        Frame = frame;
        this.layers = layers;
        penetrationSlop = options.PenetrationSlop;
        Limits = SystemLimits.From(options);
        System = CreateSystem(Limits);
        Bodies = System.BodyInterface;
        Listen(System);
    }

    public FrameHandle Frame { get; }
    public PhysicsSystem System { get; private set; }
    public BodyInterface Bodies { get; private set; }

    /// <summary>The limits of <see cref="System"/>.</summary>
    public SystemLimits Limits { get; private set; }

    /// <summary>True when <see cref="System"/> holds as many bodies as it can, so that Jolt refuses one more.</summary>
    public bool IsFull => System.BodiesCount >= System.MaxBodies;

    /// <summary>Records whose primary body or character lives in this system.</summary>
    public List<BodyRecord> Members { get; } = new();

    public int CharacterCount { get; set; }

    /// <summary>Set after the first update that overflowed a Jolt limit, so the warning appears once.</summary>
    public bool ReportedOverflow { get; set; }

    // The frame's gravity state at the last step, to wake sleeping bodies when it changes.
    public bool HasGravityState;
    public Vector3 LastProperAcceleration;
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
    /// disposes the current one. The pairs that touch now are carried over: the first update of the new system reports
    /// each of them again, and <see cref="DrainContacts"/> does not take that for a new contact. Until that update, the
    /// touching-pair table is empty, so a second growth before it carries over the pairs of the first one again. Any
    /// number of growths between two updates keeps every pair that touched before the first of them.
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
        Limits = limits;
        Listen(larger);
        ReportedOverflow = false;

        lock (contactGate)
        {
            // The pairs that an earlier growth carried and that no update has reported yet still touch, as far as the
            // backend knows. They take the new IDs too.
            carryScratch.Clear();
            carryScratch.AddRange(carried);
            carryScratch.AddRange(touching.Keys);
            carried.Clear();
            foreach (ulong key in carryScratch)
                carried.Add(PairKey(Renamed((uint)(key >> 32), renamed), Renamed((uint)key, renamed)));
            touching.Clear();
            updatedSinceGrowth = false;
        }
    }

    private static BodyID Renamed(uint id, IReadOnlyDictionary<uint, uint> renamed) =>
        new(renamed.TryGetValue(id, out uint newId) ? newId : id);

    /// <summary>Runs one update of the system. Call it on the physics thread, with the native gate held.</summary>
    public PhysicsUpdateError Update(float deltaTime, JobSystem jobs)
    {
        updatedSinceGrowth = true;
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

    /// <summary>
    /// Moves the contacts that Jolt reported during the last update into the touching-pair table, and appends the
    /// ones that started a new body pair contact to <paramref name="started"/>. Call it on the physics thread after
    /// the update.
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
                if (count == 0 && !carried.Contains(key))
                    started.Add(contact);
            }
            foreach (ulong key in removed)
            {
                if (!touching.TryGetValue(key, out int count))
                    continue;
                if (count <= 1)
                    touching.Remove(key);
                else
                    touching[key] = count - 1;
            }
            added.Clear();
            removed.Clear();
            // The first update after a growth has reported every carried pair that still touches.
            if (updatedSinceGrowth)
                carried.Clear();
        }
    }

    /// <summary>Forgets every contact of a body that leaves the system.</summary>
    public void ForgetContacts(BodyID body)
    {
        if (body.IsInvalid)
            return;
        lock (contactGate)
        {
            purgeScratch.Clear();
            foreach (ulong key in touching.Keys)
                if ((uint)(key >> 32) == body.ID || (uint)key == body.ID)
                    purgeScratch.Add(key);
            foreach (ulong key in purgeScratch)
                touching.Remove(key);
            if (carried.Count > 0)
                carried.RemoveWhere(key => (uint)(key >> 32) == body.ID || (uint)key == body.ID);
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
