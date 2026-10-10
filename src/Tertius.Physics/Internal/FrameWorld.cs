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
/// The Jolt physics system of one reference frame. Positions in the system are frame coordinates, so a body's pose
/// in Jolt is its pose in the frame. Gravity is zero: the backend applies the frame's effective gravity as forces.
/// </summary>
internal sealed class FrameWorld : IDisposable
{
    private readonly Lock contactGate = new();
    private readonly List<RawContact> added = new(64);
    private readonly List<ulong> removed = new(64);
    // Body pairs in contact, with the number of touching sub-shape pairs. A pair starts when this goes from 0 to 1.
    private readonly Dictionary<ulong, int> touching = new();
    private readonly List<ulong> purgeScratch = new();

    public FrameWorld(FrameHandle frame, Layers.Tables layers, JoltBackendOptions options)
    {
        Frame = frame;
        System = new PhysicsSystem(new PhysicsSystemSettings
        {
            MaxBodies = options.MaxBodiesPerFrame,
            MaxBodyPairs = options.MaxBodyPairsPerFrame,
            MaxContactConstraints = options.MaxContactConstraintsPerFrame,
            ObjectLayerPairFilter = layers.Pairs,
            BroadPhaseLayerInterface = layers.BroadPhase,
            ObjectVsBroadPhaseLayerFilter = layers.ObjectVsBroadPhase,
        });
        System.Gravity = Vector3.Zero;
        PhysicsSettings settings = System.Settings;
        settings.PenetrationSlop = options.PenetrationSlop;
        System.Settings = settings;
        Bodies = System.BodyInterface;
        System.OnContactAdded += OnContactAdded;
        System.OnContactRemoved += OnContactRemoved;
    }

    public FrameHandle Frame { get; }
    public PhysicsSystem System { get; }
    public BodyInterface Bodies { get; }

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
                if (count == 0)
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
