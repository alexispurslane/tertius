using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>
/// A contact that Jolt reported on a worker thread, before it is mapped to entities. <see cref="Touches"/> is true when
/// the bodies really touch in the step (see <see cref="FrameWorld.Touches"/>), not only when Jolt found them within its
/// speculative contact distance. <see cref="Sensor"/> is true when one of the bodies is a sensor, as Jolt's
/// <c>ContactSettings.IsSensor</c> says, so that the touch pushes nothing. Jolt reports such a pair only while the two
/// bodies overlap, so its touch is the overlap.
/// </summary>
internal readonly record struct RawContact(
    BodyID Body1,
    BodyID Body2,
    ulong UserData1,
    ulong UserData2,
    Double3 Point,
    Vector3 Normal,
    float NormalVelocity,
    bool Touches,
    bool Sensor = false)
{
    public ulong PairKey => FrameWorld.PairKey(Body1, Body2);
}

/// <summary>A contact that started and then ended, by the user data of its two bodies, before it is mapped to entities.</summary>
internal readonly record struct RawEnd(ulong UserData1, ulong UserData2);

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
    private readonly float penetrationSlop;
    private readonly Lock contactGate = new();
    private readonly List<RawContact> added = new(64);
    // The contacts that Jolt reported again in the last update for a pair in pending, when they really touch.
    private readonly List<RawContact> persistedTouches = new(16);
    private readonly List<ulong> removed = new(64);
    // Body pairs in contact as Jolt's contact cache holds them, with the number of touching sub-shape pairs. A pair that
    // Jolt adds starts at once if it really touches, unless it is held. Otherwise it waits in pending.
    private readonly Dictionary<ulong, int> touching = new();
    // The pairs of touching that have not started: Jolt found them within its speculative contact distance, but they have
    // not touched yet. A pair leaves at its first real touch, which starts it, or when Jolt drops it, which reports nothing.
    // Only the physics thread changes it, between updates, so Jolt's worker threads read it during an update without the gate.
    private readonly HashSet<ulong> pending = new();
    // Jolt's reports of this frame's contacts arrive through it, from every system of the frame.
    private readonly ContactListener listener;
    // Body pairs that touch as far as the backend knows, but that Jolt's contact cache no longer holds. Jolt finds contacts
    // only for a body that it simulates. So it drops the pair of two bodies that it does not simulate (a body fell asleep
    // or a hull became static, on something static or asleep) while the system keeps stepping. A growth drops every pair,
    // because the new system starts with an empty cache. Jolt reports a held pair as added when it finds it again: the
    // contact continues. A held pair ends as HeldPairEnded says. A pair is never in touching and held at once.
    private readonly HashSet<ulong> held = new();
    // True from an update until the contacts that it reported are drained.
    private bool updatedSinceDrain;
    // The length of the last update, in seconds, by which a contact's approach speed closes its gap (see Touches).
    private float updateDeltaTime;
    // The bodies of this system's characters, and each pair of such a body with a body that the character's own contact
    // list holds, from the last update: the inner body of a character in the walking form, and the body of one in the
    // rigid form. Jolt never simulates an inner body, and the walking form keeps a gap from what it touches as wide as
    // Jolt's speculative contact distance, so for these pairs the character decides. The rigid form gets the same list
    // from a query, so that a switch of form ends no contact (see NoteRigidCharacterContacts).
    private readonly HashSet<uint> characterBodies = new();
    private readonly HashSet<ulong> characterTouches = new();
    // Each body that a character hit in its last update, as a contact of its inner body. For the same reason, Jolt can miss
    // the contact of the inner body, so a hit starts the pair as a held pair (see DrainContacts).
    private readonly List<RawContact> characterHits = new();
    private readonly List<ulong> purgeScratch = new();
    // The pairs that a growth holds, before they take the new IDs, and the held pairs that a drain ends.
    private readonly List<ulong> heldScratch = new();

    public FrameWorld(FrameHandle frame, JoltBackendOptions options)
    {
        Frame = frame;
        penetrationSlop = options.PenetrationSlop;
        Limits = SystemLimits.From(options);
        listener = new ContactListener(this);
        System = CreateSystem(Limits);
        Bodies = System.BodyInterface;
        BodiesNoLock = System.BodyInterfaceNoLock;
    }

    public FrameHandle Frame { get; }
    public PhysicsSystem System { get; private set; }
    public BodyInterface Bodies { get; private set; }

    /// <summary>The body interface that takes no body locks. Use it only on the physics thread, between updates.</summary>
    private BodyInterface BodiesNoLock { get; set; }

    /// <summary>The limits of <see cref="System"/>.</summary>
    public SystemLimits Limits { get; private set; }

    /// <summary>The number of pairs that Jolt found within its speculative contact distance and that have not touched yet.</summary>
    public int PendingPairCount => pending.Count;

    /// <summary>True when <see cref="System"/> holds as many bodies as it can, so that Jolt refuses one more.</summary>
    public bool IsFull => System.BodiesCount >= System.MaxBodies;

    /// <summary>Records whose primary body or character lives in this system.</summary>
    public List<BodyRecord> Members { get; } = new();

    /// <summary>The number of members that are characters, in either form. <see cref="AddMember"/> and <see cref="RemoveMember"/> keep it.</summary>
    public int CharacterCount { get; private set; }

    /// <summary>
    /// The number of members that are characters in the walking form, each with a Jolt CharacterVirtual in this system. The
    /// backend keeps it where it creates and disposes those characters. Such a character moves in its own update, so the
    /// system updates in every step while it has one.
    /// </summary>
    public int WalkingCharacterCount { get; set; }

    /// <summary>Set after the first update that overflowed a Jolt limit, so the warning appears once.</summary>
    public bool ReportedOverflow { get; set; }

    // The frame's gravity state, to wake sleeping bodies when it changes: the field at the last step, and the proper
    // acceleration when the frame's bodies last woke, or when the frame first stepped. A proper acceleration that drifts
    // by a small amount each step still wakes them once it has drifted far enough.
    public bool HasGravityState;
    public Vector3 WakeProperAcceleration;
    public GravityField LastGravity;

    /// <summary>
    /// How fast the origin of <see cref="Frame"/> accelerates relative to its semantic frame in the current step, in
    /// m/s², in frame axes: the step acceleration of a numeric frame, which falls with its semantic frame's gravity, and
    /// zero for a semantic frame. The backend sets it before each update. The minimum rebound reads it (see
    /// <see cref="ForceAcceleration"/>).
    /// </summary>
    public Vector3 HomeAcceleration;

    // Records reported in the last and the current step. A body that falls asleep gets one final report.
    public List<BodyRecord> ReportedLast = new();
    public List<BodyRecord> ReportedNow = new();

    public void AddMember(BodyRecord record)
    {
        record.MemberIndex = Members.Count;
        Members.Add(record);
        if (record.IsCharacter)
            CharacterCount++;
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
        if (record.IsCharacter)
            CharacterCount--;
    }

    // The ghosts in this system: copies of bodies whose own system is another frame's (see GhostBody). It holds the body of
    // every ghost that a record keeps in this system, and nothing else. The backend's AssertInvariants checks that.
    private readonly HashSet<BodyID> ghosts = new();

    /// <summary>The number of ghosts in this system.</summary>
    public int GhostCount => ghosts.Count;

    /// <summary>True when <paramref name="body"/> is a ghost in this system.</summary>
    public bool HasGhost(BodyID body) => ghosts.Contains(body);

    /// <summary>Adds the body of every ghost in this system to <paramref name="into"/>.</summary>
    public void CopyGhosts(List<BodyID> into) => into.AddRange(ghosts);

    // The ghosts in this system whose real body is static, by body ID: the hull of a parked frame, or an anchor in a
    // falling frame. Jolt makes a contact of a static body only with a dynamic body, or with a kinematic body when the
    // static body is a sensor. The ghost of such a body is kinematic unless it is a mesh that holds still, so that a
    // dynamic body that hits it meets a body that moves as the real body does in this frame, and Jolt also makes a contact
    // between it and every sensor. A ghost stands for its real body, so the reports drop the contacts that the real body
    // would not make (see MeetsAsItsRealBody). Only the physics thread changes it, between updates, so Jolt's worker
    // threads read it during an update without the gate.
    private readonly HashSet<uint> ghostsOfStaticBodies = new();

    /// <summary>The number of ghosts in this system whose real body is static (see <see cref="StandsForStaticBody"/>).</summary>
    public int StaticBodyGhostCount => ghostsOfStaticBodies.Count;

    /// <summary>True when <paramref name="ghost"/> is a ghost in this system whose real body is static.</summary>
    public bool StandsForStaticBody(BodyID ghost) => ghostsOfStaticBodies.Contains(ghost.ID);

    /// <summary>
    /// Counts <paramref name="body"/>, a new body of this system, as a ghost, whose real body is static when
    /// <paramref name="ofStaticBody"/> is true.
    /// </summary>
    public void AddGhost(BodyID body, bool ofStaticBody)
    {
        Contract.Requires(!ghosts.Contains(body));
        ghosts.Add(body);
        SetGhostOfStaticBody(body, ofStaticBody);
    }

    /// <summary>
    /// Records whether the real body of <paramref name="ghost"/>, a ghost in this system, is static. Call it when the
    /// motion of the real body changes, so that the ghost meets what the real body meets from the next update on.
    /// </summary>
    public void SetGhostOfStaticBody(BodyID ghost, bool ofStaticBody)
    {
        Contract.Requires(ghosts.Contains(ghost));
        if (ofStaticBody)
            ghostsOfStaticBodies.Add(ghost.ID);
        else
            ghostsOfStaticBodies.Remove(ghost.ID);
    }

    /// <summary>Removes and destroys the ghost <paramref name="body"/>, after it wakes the bodies around it (see <see cref="WakeAround"/>).</summary>
    public void RemoveGhost(BodyID body)
    {
        Contract.Requires(ghosts.Contains(body));
        ghosts.Remove(body);
        ghostsOfStaticBodies.Remove(body.ID);
        RemoveBody(body);
    }

    /// <summary>
    /// True when Jolt can simulate <paramref name="body"/> as a kinematic body: it has Jolt's motion properties, which Jolt
    /// gives a body that is not static or that it created to allow a change of motion, with a finite positive inverse
    /// mass. Jolt would turn a static body without them kinematic, and then crash. False for a body that is not in this
    /// system.
    /// </summary>
    public bool CanMove(BodyID body)
    {
        BodyLockInterface locks = System.BodyLockInterface;
        locks.LockRead(body, out BodyLockRead read);
        bool can = read.Succeeded && read.Body is { MotionProperties: { IsNotNull: true } motion }
                   && float.IsFinite(motion.InverseMassUnchecked) && motion.InverseMassUnchecked > 0;
        locks.UnlockRead(read);
        return can;
    }

    /// <summary>
    /// Moves every ghost in this system by a shift of the frame, like a member: each ghost loses <paramref name="offset"/>
    /// from its position and <paramref name="velocity"/> from its velocity, so that its place and velocity in the frame's
    /// parent stay the same. A static ghost is the ghost of a mesh that held still in this frame (see
    /// <c>JoltPhysicsBackend.GhostMotion</c>). A shift that changes the frame's velocity makes it move, so it turns
    /// kinematic, which the backend created it to allow, and Jolt moves it with its new velocity in the next update.
    /// Numeric frames do not rotate, so the angular velocity stays. Jolt does not simulate the move, and the ghosts move
    /// with the members around them, so nothing wakes. Each ghost moves on its own, so the order of the visits does not
    /// change the result. Costs time in proportion to the number of ghosts in this system.
    /// </summary>
    public void ShiftGhosts(Double3 offset, Double3 velocity)
    {
        BodyInterface bodies = Bodies;
        foreach (BodyID ghost in ghosts)
        {
            bodies.GetRPosition(ghost, out RVector3 position);
            Quaternion rotation = bodies.GetRotation(ghost);
            bodies.SetRPositionAndRotation(ghost, (position.ToDouble3() - offset).ToJolt(), rotation, Activation.DontActivate);
            if (velocity == Double3.Zero)
                continue;
            // Jolt gives a static body no velocity.
            Vector3 linear = ((Double3)bodies.GetLinearVelocity(ghost) - velocity).ToVector3();
            if (linear != Vector3.Zero && bodies.GetMotionType(ghost) == MotionType.Static)
                bodies.SetMotionType(ghost, MotionType.Kinematic, Activation.Activate);
            bodies.SetLinearVelocity(ghost, linear);
        }
    }

    /// <summary>
    /// Creates an empty system with its own filter tables, this frame's settings and the given limits. Its contacts go to
    /// this frame's contact listener from its first update, but only <see cref="System"/> updates, so they arrive only
    /// after <see cref="ReplaceSystem"/> makes it this frame's system.
    /// </summary>
    public PhysicsSystem CreateSystem(SystemLimits limits)
    {
        // The system frees its tables when it is destroyed.
        var layers = new Layers.Tables();
        PhysicsSystem system = NativeOwnership.Owned(new PhysicsSystem(new PhysicsSystemSettings
        {
            MaxBodies = limits.MaxBodies,
            MaxBodyPairs = limits.MaxBodyPairs,
            MaxContactConstraints = limits.MaxContactConstraints,
            ObjectLayerPairFilter = layers.Pairs,
            BroadPhaseLayerInterface = layers.BroadPhase,
            ObjectVsBroadPhaseLayerFilter = layers.ObjectVsBroadPhase,
        }));
        // JoltPhysicsSharp gave the system a listener of its own, which would now call the backend's callbacks with its
        // own user data. Replace it before anything can update the system.
        listener.Attach(system);
        system.Gravity = Vector3.Zero;
        PhysicsSettings settings = system.Settings;
        settings.PenetrationSlop = penetrationSlop;
        settings.PointVelocitySleepThreshold = SleepThreshold;
        system.Settings = settings;
        return system;
    }

    /// <summary>Jolt's own sleep threshold, in m/s: a body whose points move slower than this for half a second sleeps.</summary>
    public const float JoltSleepThreshold = 0.03f;

    /// <summary>The sleep threshold of <see cref="System"/>, in m/s. A system starts with Jolt's own.</summary>
    public float SleepThreshold { get; private set; } = JoltSleepThreshold;

    /// <summary>Gives <see cref="System"/> a new sleep threshold, in m/s. A growth keeps it.</summary>
    public void SetSleepThreshold(float threshold)
    {
        if (threshold == SleepThreshold)
            return;
        SleepThreshold = threshold;
        PhysicsSettings settings = System.Settings;
        settings.PointVelocitySleepThreshold = threshold;
        System.Settings = settings;
    }

    /// <summary>
    /// Makes <paramref name="larger"/>, which already holds every body of the current system, this frame's system, and
    /// disposes the current one. Every pair that started and touches now becomes held: the new system finds it again in
    /// the first update that simulates one of its bodies, and <see cref="DrainContacts"/> does not take that for a new
    /// contact. A pair that was held already stays held, so any number of growths keeps every pair that touched before the
    /// first. A pending pair is forgotten: the new system finds it again, and it starts at its first real touch.
    /// </summary>
    /// <param name="larger">The new system, with <paramref name="limits"/>.</param>
    /// <param name="limits">The limits of <paramref name="larger"/>.</param>
    /// <param name="renamed">
    /// The bodies whose ID changed in the move: by full ID in the current system, their full ID in the new one.
    /// </param>
    public void ReplaceSystem(PhysicsSystem larger, SystemLimits limits, IReadOnlyDictionary<uint, uint> renamed)
    {
        // Contacts are drained after every update, and only an update reports them.
        Contract.Requires(added.Count == 0 && persistedTouches.Count == 0 && removed.Count == 0);
        System.Dispose();
        System = larger;
        Bodies = larger.BodyInterface;
        BodiesNoLock = larger.BodyInterfaceNoLock;
        Limits = limits;
        ReportedOverflow = false;

        lock (contactGate)
        {
            heldScratch.Clear();
            heldScratch.AddRange(held);
            foreach (ulong key in touching.Keys)
                if (!pending.Contains(key))
                    heldScratch.Add(key);
            held.Clear();
            foreach (ulong key in heldScratch)
                held.Add(PairKey(Renamed(First(key), renamed), Renamed(Second(key), renamed)));
            touching.Clear();
            pending.Clear();
        }
    }

    private static BodyID Renamed(uint id, IReadOnlyDictionary<uint, uint> renamed) =>
        new(renamed.TryGetValue(id, out uint newId) ? newId : id);

    /// <summary>How far beyond the bounds of a body <see cref="WakeAround"/> reaches, in meters: more than Jolt's speculative contact distance of 0.02 m.</summary>
    private const float WakeMargin = 0.05f;

    /// <summary>
    /// Wakes every body in or near the bounds of <paramref name="body"/> whose layer collides with the body's layer. Jolt
    /// wakes a sleeping body only when a body that it simulates touches it. So call this before the backend removes or
    /// moves a body without Jolt simulating the move, and after it puts a body that Jolt does not simulate somewhere new.
    /// A body that rested on it, or that it now overlaps, then wakes. Does nothing for a body that is not in the system.
    /// </summary>
    public void WakeAround(BodyID body)
    {
        if (body.IsInvalid)
            return;
        BodyLockInterface locks = System.BodyLockInterface;
        locks.LockRead(body, out BodyLockRead read);
        bool found = read.Succeeded && read.Body is not null;
        JoltPhysicsSharp.BoundingBox bounds = found ? read.Body!.WorldSpaceBounds : default;
        ObjectLayer layer = found ? read.Body!.ObjectLayer : default;
        locks.UnlockRead(read);
        if (!found)
            return;
        // Jolt keeps the bounds in float, so far from the frame origin they are rounded by more than the margin.
        float reach = MathF.Max(MaxAbs(bounds.Min), MaxAbs(bounds.Max));
        var margin = new Vector3(WakeMargin + reach * 1e-6f);
        System.ActivateBodiesInAABox(new JoltPhysicsSharp.BoundingBox(bounds.Min - margin, bounds.Max + margin), layer);
    }

    private static float MaxAbs(Vector3 v) => MathF.Max(MathF.Abs(v.X), MathF.Max(MathF.Abs(v.Y), MathF.Abs(v.Z)));

    /// <summary>Removes and destroys a body, after it wakes the bodies around it (see <see cref="WakeAround"/>).</summary>
    public void RemoveBody(BodyID body)
    {
        WakeAround(body);
        Bodies.RemoveAndDestroyBody(body);
    }

    /// <summary>Runs one update of the system. Call it on the physics thread, with the native gate held.</summary>
    public PhysicsUpdateError Update(float deltaTime, JobSystem jobs)
    {
        updatedSinceDrain = true;
        updateDeltaTime = deltaTime;
        return System.Update(deltaTime, 1, jobs);
    }

    public static ulong PairKey(BodyID a, BodyID b) => PairKey(a.ID, b.ID);

    private static ulong PairKey(uint x, uint y) => x < y ? ((ulong)x << 32) | y : ((ulong)y << 32) | x;

    /// <summary>The body of a pair key with the lower ID.</summary>
    private static BodyID First(ulong key) => new((uint)(key >> 32));

    /// <summary>The body of a pair key with the higher ID.</summary>
    private static BodyID Second(ulong key) => new((uint)key);

    private static bool Involves(ulong key, BodyID body) => (uint)(key >> 32) == body.ID || (uint)key == body.ID;

    /// <summary>Forgets the characters' contacts of the last update. Call it before the characters of the system update.</summary>
    public void ClearCharacterContacts()
    {
        characterBodies.Clear();
        characterTouches.Clear();
        characterHits.Clear();
    }

    /// <summary>
    /// Records what <paramref name="character"/> touches after its update, by its own contact list: every body within its
    /// predictive contact distance, asleep or awake. <see cref="DrainContacts"/> ends a held pair of the character's inner
    /// body only when that list no longer has the other body. It also records each body of the list that the character
    /// hit in the update, which is a real touch and starts a pair. <paramref name="velocity"/> is the velocity that the
    /// character moved with in the update, which gives the speed of the hit.
    /// </summary>
    public void NoteCharacterContacts(CharacterVirtual character, Vector3 velocity)
    {
        BodyID inner = character.InnerBodyID;
        characterBodies.Add(inner.ID);
        int count = character.GetNumActiveContacts();
        for (int i = 0; i < count; i++)
        {
            character.GetActiveContact(i, out CharacterContact contact);
            if (contact.WasDiscarded || !contact.BodyB.IsValid)
                continue;
            characterTouches.Add(PairKey(inner, contact.BodyB));
            // The inner body is no ghost, so a hit of a ghost counts too.
            if (!contact.HadCollision || !IsContactBody(contact.UserData))
                continue;
            // Jolt's contact normal points out of the other body, toward the character. A contact's normal points from
            // its first body, here the inner body, to its second.
            Vector3 normal = -contact.ContactNormal;
            characterHits.Add(new RawContact(inner, contact.BodyB, character.UserData, contact.UserData, contact.RPosition.ToDouble3(),
                normal, Vector3.Dot(contact.LinearVelocity - velocity, normal), Touches: true));
        }
    }

    /// <summary>
    /// Records what a character in the rigid form, whose body is <paramref name="body"/>, touches after the update: each
    /// body of <paramref name="contacts"/>, the result of the query that gives the rigid form the contact list of its
    /// walking form. <see cref="DrainContacts"/> then ends a held pair of the body as it ends one of a walking
    /// character's inner body, only when that list no longer has the other body.
    /// </summary>
    public void NoteRigidCharacterContacts(BodyID body, List<CollideShapeResult> contacts)
    {
        characterBodies.Add(body.ID);
        foreach (CollideShapeResult contact in contacts)
            if (contact.BodyID2.ID != body.ID)
                characterTouches.Add(PairKey(body, contact.BodyID2));
    }

    /// <summary>
    /// True when a held pair has ended. A pair with the body of a character, in either form, ends when the character's
    /// contact list no longer has the other body. Any other pair ends when the last update simulated one of its bodies
    /// and Jolt did not find the pair: Jolt finds the contacts of every body that it simulates, also of one that woke
    /// during the update, while two bodies that Jolt did not simulate did not move apart.
    /// </summary>
    private bool HeldPairEnded(ulong key)
    {
        if (characterBodies.Contains((uint)(key >> 32)) || characterBodies.Contains((uint)key))
            return !characterTouches.Contains(key);
        return IsAwake(First(key)) || IsAwake(Second(key));
    }

    /// <summary>True when Jolt simulates the body: it is awake and not static. False for a body that left the system.</summary>
    private bool IsAwake(BodyID body) => BodiesNoLock.IsActive(body);

    /// <summary>
    /// Moves the contacts that Jolt reported during the last update into the touching-pair table. Appends to
    /// <paramref name="started"/> each body pair that really touched for the first time: a pair that Jolt adds and that
    /// touches, or a pending pair that touches now (see <see cref="Touches"/>). A pair that Jolt adds without a touch
    /// waits in pending. A pair that Jolt drops becomes held if it started, and a held pair that Jolt finds again
    /// continues. A pair that a character hit in its last update starts too, unless it started already: a pending pair
    /// at once, and a pair that Jolt does not hold as a held pair. A held pair ends as <see cref="HeldPairEnded"/> says,
    /// and goes to <paramref name="ended"/>. Call it on the physics thread after the update, with no update running.
    /// Visits every held pair after an update, so it costs O(held pairs) for a system that steps.
    /// </summary>
    public void DrainContacts(List<RawContact> started, List<RawEnd> ended)
    {
        lock (contactGate)
        {
            // Jolt reports from several threads, so sort for a deterministic choice when one pair has many points.
            added.Sort(ByPairAndPoint);
            persistedTouches.Sort(ByPairAndPoint);
            // Additions first: a pair that gains one sub-shape contact and loses another in the same step continues.
            foreach (RawContact contact in added)
            {
                ulong key = contact.PairKey;
                touching.TryGetValue(key, out int count);
                touching[key] = count + 1;
                if (count == 0 && held.Remove(key))
                    continue;
                if (count == 0 && !contact.Touches)
                    pending.Add(key);
                else if (contact.Touches && (count == 0 || pending.Remove(key)))
                    started.Add(contact);
            }
            foreach (RawContact contact in persistedTouches)
                if (pending.Remove(contact.PairKey))
                    started.Add(contact);
            foreach (ulong key in removed)
            {
                if (!touching.TryGetValue(key, out int count))
                    continue;
                if (count > 1)
                {
                    touching[key] = count - 1;
                    continue;
                }
                // A pair that never touched leaves without a report. A pair that started ended, or Jolt dropped it because
                // it simulates neither body. The check below tells which.
                touching.Remove(key);
                if (!pending.Remove(key))
                    held.Add(key);
            }
            added.Clear();
            persistedTouches.Clear();
            removed.Clear();
            // A hit is a real touch. A pending pair starts at once. A pair that Jolt does not hold starts held, because Jolt
            // may never find it, and the character's contact list ends it.
            foreach (RawContact hit in characterHits)
            {
                ulong key = hit.PairKey;
                if (pending.Remove(key) || (!touching.ContainsKey(key) && held.Add(key)))
                    started.Add(hit);
            }

            // Stryker disable once all : the test only saves work; without an update Jolt simulates no body, so no pair ends.
            if (updatedSinceDrain && held.Count > 0)
            {
                heldScratch.Clear();
                foreach (ulong key in held)
                    if (HeldPairEnded(key))
                        heldScratch.Add(key);
                foreach (ulong key in heldScratch)
                {
                    held.Remove(key);
                    ended.Add(EndOf(key));
                }
            }
            updatedSinceDrain = false;
        }
    }

    private static readonly Comparison<RawContact> ByPairAndPoint = static (a, b) =>
    {
        int c = a.PairKey.CompareTo(b.PairKey);
        if (c != 0) return c;
        c = a.Point.X.CompareTo(b.Point.X);
        if (c != 0) return c;
        c = a.Point.Y.CompareTo(b.Point.Y);
        return c != 0 ? c : a.Point.Z.CompareTo(b.Point.Z);
    };

    /// <summary>The end of the pair <paramref name="key"/>, whose two bodies are still in the system.</summary>
    private RawEnd EndOf(ulong key) => new(BodiesNoLock.GetUserData(First(key)), BodiesNoLock.GetUserData(Second(key)));

    /// <summary>
    /// Takes over the contact of two entities whose bodies <paramref name="a"/> and <paramref name="b"/> are in this system,
    /// after a body of the contact left another system: the contact goes on here, and Jolt finding the pair continues it
    /// instead of starting it. A pair that Jolt found without a touch (a pending pair) starts without a report. A pair that
    /// Jolt does not hold becomes held, and it ends as <see cref="HeldPairEnded"/> says. Returns false when this system
    /// holds the pair as started already, so that it takes over nothing.
    /// </summary>
    public bool Carry(BodyID a, BodyID b)
    {
        ulong key = PairKey(a, b);
        lock (contactGate)
            return touching.ContainsKey(key) ? pending.Remove(key) : held.Add(key);
    }

    /// <summary>
    /// True when a body pair of <paramref name="body"/> in this system has started and not ended: a pair of
    /// <c>touching</c> that is not pending, or a held pair. Costs time in proportion to the number of pairs in the system.
    /// </summary>
    public bool HasStartedPair(BodyID body)
    {
        lock (contactGate)
        {
            foreach (ulong key in touching.Keys)
                if (Involves(key, body) && !pending.Contains(key))
                    return true;
            foreach (ulong key in held)
                if (Involves(key, body))
                    return true;
        }
        return false;
    }

    /// <summary>
    /// Appends to <paramref name="pairs"/> each body pair of this system that has started and not ended: the pairs of
    /// <c>touching</c> that are not pending, and the held pairs. Only the backend's check of its invariants reads them.
    /// </summary>
    public void StartedPairs(List<RawEnd> pairs)
    {
        lock (contactGate)
        {
            foreach (ulong key in touching.Keys)
                if (!pending.Contains(key))
                    pairs.Add(EndOf(key));
            foreach (ulong key in held)
                pairs.Add(EndOf(key));
        }
    }

    /// <summary>
    /// Forgets every contact of a body that leaves the system, touching, pending or held, and appends to
    /// <paramref name="ended"/> each one that started: every one but the pending ones. Call it while the body is still
    /// in the system.
    /// </summary>
    public void ForgetContacts(BodyID body, List<RawEnd> ended)
    {
        if (body.IsInvalid)
            return;
        lock (contactGate)
        {
            purgeScratch.Clear();
            foreach (ulong key in touching.Keys)
                if (Involves(key, body))
                    purgeScratch.Add(key);
            foreach (ulong key in held)
                if (Involves(key, body))
                    purgeScratch.Add(key);
            foreach (ulong key in purgeScratch)
            {
                if (!pending.Remove(key))
                    ended.Add(EndOf(key));
                touching.Remove(key);
                held.Remove(key);
            }
        }
    }

    public void Dispose()
    {
        // The system holds a pointer to the listener, so it goes first.
        System.Dispose();
        listener.Dispose();
    }

    /// <summary>
    /// How far apart two bodies can be and still touch, in meters: ten times Jolt's collision tolerance, so that the
    /// rounding of Jolt's contact search hides no touch, and a twentieth of Jolt's speculative contact distance.
    /// </summary>
    public const float TouchTolerance = 0.001f;

    /// <summary>
    /// True when a contact that Jolt found in a step is a real touch: the bodies are at most <see cref="TouchTolerance"/>
    /// apart, or they approach fast enough to close their gap within the step, so that the solver pushes them apart. Jolt
    /// finds a pair as soon as its bodies are within its speculative contact distance of 0.02 m, and such a pair can
    /// touch many steps later, or never.
    /// </summary>
    /// <param name="penetrationDepth">How far the bodies overlap, in meters. Below zero, how far apart they are.</param>
    /// <param name="normalVelocity">How fast the bodies move apart along the contact's normal, in m/s. Below zero, how fast they approach.</param>
    /// <param name="deltaTime">The length of the step, in seconds.</param>
    internal static bool Touches(float penetrationDepth, float normalVelocity, float deltaTime)
    {
        float gap = -penetrationDepth;
        return gap <= TouchTolerance || -normalVelocity * deltaTime >= gap;
    }

    /// <summary>
    /// Jolt's speculative contact distance, in m: Jolt finds the pair of two bodies that are this close, and holds their
    /// contact while they stay this close. It is Jolt's default, which the engine keeps.
    /// </summary>
    public const float JoltSpeculativeContactDistance = 0.02f;

    /// <summary>
    /// Jolt's <c>MinVelocityForRestitution</c>, in m/s: Jolt bounces a contact point only when it approaches faster than
    /// this. It is Jolt's default, which the engine keeps.
    /// </summary>
    public const float JoltMinVelocityForRestitution = 1f;

    /// <summary>
    /// The least height, in m, that a bounce that a contact keeps rises before the pull toward the surface brings it
    /// back: twice <see cref="JoltSpeculativeContactDistance"/>. So every bounce that remains carries the body out of that
    /// distance, and ends the contact, wherever its landing step begins. At 1 g the slowest rebound that remains is
    /// about 0.89 m/s.
    /// </summary>
    /// <remarks>
    /// Jolt finds a pair within its speculative contact distance. When a landing step begins there, Jolt gives a bouncing
    /// body its rebound at once, from where it is, so it rises to about its gap at the start of the step plus the height
    /// of its bounce. Without the minimum, a small bounce from a step that begins near the surface stayed within the
    /// distance, and the same bounce from a step that begins near the edge left it: one drop reported one landing or two
    /// (ter-4kaa). Twice the distance leaves a margin for the rounding of the rise in whole steps.
    /// </remarks>
    public const float MinReboundHeight = 2 * JoltSpeculativeContactDistance;

    /// <summary>
    /// The restitution that a contact keeps: <paramref name="restitution"/> when the bounce that Jolt gives it rises at
    /// least <see cref="MinReboundHeight"/>, or when nothing pulls the bodies back together. Otherwise 0, so that the
    /// contact does not bounce.
    /// </summary>
    /// <remarks>
    /// Jolt's rebound is <c>u = restitution · (approach − a · Δt)</c>, where <c>a</c> is the pull toward the surface, at
    /// least 0: Jolt applies the accumulated forces at the start of the step, so it takes the speed that they added off
    /// the rebound (ContactConstraintManager.cpp). Under a constant pull, a bounce of speed <c>u</c> rises
    /// <c>u² / (2 a)</c>. So the contact keeps its restitution when <c>u² ≥ 2 a · MinReboundHeight</c>. Without a pull,
    /// a bounce never comes back, so every bounce that moves the bodies apart remains. A rebound of 0 or less is none.
    /// </remarks>
    /// <param name="restitution">The contact's combined restitution: the larger of the restitutions of its two bodies.</param>
    /// <param name="approachSpeed">How fast the bodies approach along the contact's normal, in m/s, as Jolt measures it for its rebound. Below zero, how fast they move apart.</param>
    /// <param name="approachAcceleration">How fast the accumulated forces accelerate the bodies toward each other along the normal, in m/s². Below zero, apart.</param>
    /// <param name="deltaTime">The length of the step, in seconds.</param>
    internal static float KeptRestitution(float restitution, float approachSpeed, float approachAcceleration, float deltaTime)
    {
        float pull = MathF.Max(approachAcceleration, 0f);
        float rebound = restitution * (approachSpeed - pull * deltaTime);
        return rebound > 0f && rebound * rebound >= 2f * pull * MinReboundHeight ? restitution : 0f;
    }

    /// <summary>
    /// The restitution that Jolt uses for a manifold in this update: <paramref name="restitution"/>, or 0 when the bounce
    /// that Jolt would give it rises too little (see <see cref="KeptRestitution(float, float, float, float)"/>). It reads
    /// what Jolt simulates, as Jolt does when it bounces a contact: Jolt holds a static body at rest, and only a dynamic
    /// body's forces count, as in the semantic frame (see <see cref="ForceAcceleration"/>). A ghost moves in Jolt as its
    /// real body does, and a ghost of a mesh is static only while it holds still (see
    /// <c>JoltPhysicsBackend.GhostMotion</c>), so the approach that Jolt simulates is the approach of the entities, which
    /// the touch test and the impulse read too (see <see cref="PointVelocity"/>).
    /// </summary>
    private unsafe float KeptRestitution(nint body1, nint body2, nint manifold, float restitution)
    {
        float approach = BounceApproach(body1, body2, manifold, out Vector3 normal);
        if (approach <= 0f)
            return 0f;
        return KeptRestitution(restitution, approach, ApproachAcceleration(body1, body2, normal), updateDeltaTime);
    }

    /// <summary>
    /// The fastest approach speed, in m/s, of a point of the manifold that Jolt can bounce, or 0 when there is none. As
    /// Jolt does, it measures each point at the middle of its two contact points, from the velocities that Jolt simulates
    /// (none for a static body), and counts only a point that approaches faster than
    /// <see cref="JoltMinVelocityForRestitution"/>. So a resting contact reads no forces.
    /// <paramref name="normal"/> is the manifold's normal, from the first body to the second.
    /// </summary>
    /// <remarks>
    /// Jolt decides restitution at each point of a manifold, but a callback sets one restitution for the whole manifold.
    /// The point that Jolt rebounds fastest leaves the surface fastest and carries the body with it, so that point
    /// decides whether the bounce clears the speculative distance. Another point can approach much slower: the center of
    /// the face of a box that spins as it lands flat comes down slower than its leading corner, and a rule that decided
    /// there would remove the bounce that Jolt gives the leading corner. Jolt also skips a point that does not close its
    /// gap within the step. This counts such a point anyway: at 60 Hz it approaches slower than 1.2 m/s, so it can be
    /// the fastest only beside points of about the same speed, and no test can tell the difference.
    /// </remarks>
    private unsafe float BounceApproach(nint body1, nint body2, nint manifold, out Vector3 normal)
    {
        Vector3 direction;
        JoltNative.ManifoldNormal(manifold, &direction);
        normal = direction;
        uint count = JoltNative.ManifoldPointCount(manifold);
        if (count == 0)
            return 0f;

        RVector3 com1, com2;
        Vector3 linear1, angular1, linear2, angular2;
        JoltNative.BodyCenterOfMass(body1, &com1);
        JoltNative.BodyCenterOfMass(body2, &com2);
        JoltNative.BodyLinearVelocity(body1, &linear1);
        JoltNative.BodyAngularVelocity(body1, &angular1);
        JoltNative.BodyLinearVelocity(body2, &linear2);
        JoltNative.BodyAngularVelocity(body2, &angular2);
        Double3 center1 = com1.ToDouble3(), center2 = com2.ToDouble3();

        float fastest = 0f;
        for (uint i = 0; i < count; i++)
        {
            RVector3 p1, p2;
            JoltNative.ContactPointOn1(manifold, i, &p1);
            JoltNative.ContactPointOn2(manifold, i, &p2);
            Double3 middle = 0.5 * (p1.ToDouble3() + p2.ToDouble3());
            Vector3 v1 = linear1 + Vector3.Cross(angular1, (middle - center1).ToVector3());
            Vector3 v2 = linear2 + Vector3.Cross(angular2, (middle - center2).ToVector3());
            float approach = -Vector3.Dot(v2 - v1, direction);
            if (approach > fastest && approach > JoltMinVelocityForRestitution)
                fastest = approach;
        }
        return fastest;
    }

    /// <summary>
    /// How fast the two bodies accelerate toward each other along <paramref name="normal"/>, from the first body to the
    /// second, in m/s², as Jolt reckons it in the semantic frame when it takes their push off a rebound (see
    /// <see cref="ForceAcceleration"/>). Below zero, apart. The backend adds gravity as a force, and keeps the system's
    /// own gravity at zero during an update, so this holds gravity and every Force intent.
    /// </summary>
    private unsafe float ApproachAcceleration(nint body1, nint body2, Vector3 normal) =>
        Vector3.Dot(ForceAcceleration(body1) - ForceAcceleration(body2), normal);

    /// <summary>
    /// The acceleration of a body in this update, in m/s², in frame axes, as Jolt sees it in the semantic frame: a
    /// dynamic body's accumulated force times its inverse mass, and for any other body none in the semantic frame, which
    /// is <see cref="HomeAcceleration"/> backward in this frame.
    /// </summary>
    /// <remarks>
    /// Numeric frames must not change what a body hears. A numeric frame falls with its semantic frame's gravity, so the
    /// backend adds only the gravity that it leaves to a dynamic body there, and a ghost or a kinematic body that holds
    /// still in the semantic frame comes at the body at the frame's acceleration, which Jolt does not see. In the
    /// semantic frame Jolt sees the body's full gravity and the other body at rest, and the difference of the two
    /// accelerations is the same in both frames.
    /// </remarks>
    private unsafe Vector3 ForceAcceleration(nint body)
    {
        if (JoltNative.BodyIsDynamic(body) == 0)
            return -HomeAcceleration;
        Vector3 force;
        JoltNative.BodyAccumulatedForce(body, &force);
        return force * JoltNative.MotionPropertiesInverseMass(JoltNative.BodyMotionProperties(body));
    }

    // ---- Jolt's reports, through the contact listener ----
    //
    // Each runs on a Jolt worker thread during an update, with the bodies locked, so it must not use any locking body
    // interface. Each reads the bodies and the manifold through the pointers that Jolt passes (see ContactListener).
    //
    // Each also applies the minimum rebound (see KeptRestitution) to the contact settings that Jolt passes, which Jolt
    // then uses for the contact in this update. Jolt computes those settings again in every update, the combined
    // restitution included, before it calls the listener, so both reports must apply the minimum every time. Jolt's
    // linear cast of a fast body calls the same two reports, and uses the settings that they leave. The cast does not
    // take the push of the forces off its rebound, so for a cast the rule judges a rebound up to restitution · a · Δt
    // slower than Jolt's, and removes a little more. The cast also measures the spin of the body about its center where
    // the cast hits, which a report cannot know, so for a body that spins fast the two approach speeds differ by the
    // spin times the distance that the center moved before the hit.

    /// <summary>
    /// Jolt's report of a contact that it found in this update and did not hold before. A contact that is not reported here
    /// never enters the pair table, so the other two reports of it change nothing.
    /// </summary>
    public unsafe void OnContactAdded(nint body1, nint body2, nint manifold, ContactSettings* settings)
    {
        if (settings->CombinedRestitution > 0f)
            settings->CombinedRestitution = KeptRestitution(body1, body2, manifold, settings->CombinedRestitution);
        ulong data1 = JoltNative.BodyUserData(body1), data2 = JoltNative.BodyUserData(body2);
        if (!IsReported(data1, data2) || !MeetsAsItsRealBody(body1, data1, body2) || !MeetsAsItsRealBody(body2, data2, body1))
            return;
        RawContact contact = Describe(body1, body2, data1, data2, manifold, settings->IsSensor);
        lock (contactGate)
            added.Add(contact);
    }

    /// <summary>
    /// Jolt's report of a contact that it held before and found again in this update: every contact of every body that it
    /// simulates, in every update. Only a pending pair and a contact that may bounce need it. Nothing changes pending
    /// during an update, and the default restitution is 0, so nearly every call ends at the test of the restitution and of
    /// pending, without a native call and without the gate.
    /// </summary>
    public unsafe void OnContactPersisted(nint body1, nint body2, nint manifold, ContactSettings* settings)
    {
        float restitution = settings->CombinedRestitution;
        if (restitution > 0f)
            settings->CombinedRestitution = KeptRestitution(body1, body2, manifold, restitution);
        if (pending.Count == 0 || !pending.Contains(PairKey(JoltNative.BodyId(body1), JoltNative.BodyId(body2))))
            return;
        RawContact contact = Describe(body1, body2, JoltNative.BodyUserData(body1), JoltNative.BodyUserData(body2), manifold, settings->IsSensor);
        if (!contact.Touches)
            return;
        lock (contactGate)
            persistedTouches.Add(contact);
    }

    /// <summary>Jolt's report that it no longer holds a contact between a sub-shape of each of two bodies.</summary>
    public void OnContactRemoved(BodyID body1, BodyID body2)
    {
        ulong key = PairKey(body1, body2);
        lock (contactGate)
            removed.Add(key);
    }

    /// <summary>
    /// The contact of a manifold that Jolt found in this update, at the mean of its points on the first body.
    /// <paramref name="sensor"/> is true when one of the bodies is a sensor.
    /// </summary>
    private unsafe RawContact Describe(nint body1, nint body2, ulong data1, ulong data2, nint manifold, bool sensor)
    {
        float normalVelocity = NormalVelocity(body1, body2, manifold, out Double3 point, out Vector3 normal);
        return new RawContact(new BodyID(JoltNative.BodyId(body1)), new BodyID(JoltNative.BodyId(body2)), data1, data2, point, normal,
            normalVelocity, Touches(JoltNative.ManifoldPenetrationDepth(manifold), normalVelocity, updateDeltaTime), sensor);
    }

    /// <summary>
    /// How fast the bodies of a manifold that Jolt found in this update move apart along its normal, in m/s, before the
    /// solver runs: below zero, how fast they approach. It is measured at <paramref name="point"/>, the mean of the
    /// manifold's points on the first body, or the first body's center of mass when the manifold has none.
    /// <paramref name="normal"/> is the manifold's normal, from the first body to the second.
    /// </summary>
    private unsafe float NormalVelocity(nint body1, nint body2, nint manifold, out Double3 point, out Vector3 normal)
    {
        uint count = JoltNative.ManifoldPointCount(manifold);
        point = Double3.Zero;
        if (count > 0)
        {
            RVector3 p;
            for (uint i = 0; i < count; i++)
            {
                JoltNative.ContactPointOn1(manifold, i, &p);
                point += p.ToDouble3();
            }
            point /= count;
        }
        else
        {
            RVector3 com;
            JoltNative.BodyCenterOfMass(body1, &com);
            point = com.ToDouble3();
        }

        Vector3 direction;
        JoltNative.ManifoldNormal(manifold, &direction);
        normal = direction;
        Vector3 v1 = PointVelocity(body1, point), v2 = PointVelocity(body2, point);
        return Vector3.Dot(v2 - v1, direction);
    }

    /// <summary>
    /// True when a contact of the two bodies is a contact of their entities: both stand for an entity, and they are not
    /// two ghosts. A ghost stands for its real body, so its contact with any other body counts. The real bodies of two
    /// ghosts are in their own systems, which report their contact if they touch.
    /// </summary>
    private static bool IsReported(ulong data1, ulong data2) =>
        IsContactBody(data1) && IsContactBody(data2) && !(BodyUserData.Kind(data1) == BodyKind.Ghost && BodyUserData.Kind(data2) == BodyKind.Ghost);

    /// <summary>
    /// False when <paramref name="body"/> is the ghost of a static body (see <see cref="StandsForStaticBody"/>), and Jolt
    /// would make no contact between that static body and <paramref name="other"/>: Jolt makes a contact of a static body
    /// only with a dynamic body, or with a kinematic body when the static body is a sensor. The engine never sets
    /// <c>CollideKinematicVsNonDynamic</c>, so this is Jolt's whole rule. Such a ghost is kinematic unless it is a mesh
    /// that holds still, and Jolt also makes a contact between a kinematic body and any sensor, so a kinematic sensor
    /// would detect it, and it would detect a static sensor. A ghost stands for its real body, so it meets only what that
    /// body meets, and numeric frames, whose anchors have such ghosts, change nothing. Reads Jolt's motion of
    /// <paramref name="other"/>, whose body can be a ghost too, only for a ghost of a static body.
    /// </summary>
    private bool MeetsAsItsRealBody(nint body, ulong data, nint other)
    {
        if (BodyUserData.Kind(data) != BodyKind.Ghost || ghostsOfStaticBodies.Count == 0 || !ghostsOfStaticBodies.Contains(JoltNative.BodyId(body)))
            return true;
        return JoltNative.BodyIsDynamic(other) != 0 || (JoltNative.BodyIsKinematic(other) != 0 && JoltNative.BodyIsSensor(body) != 0);
    }

    /// <summary>True for a body that stands for an entity in contacts: a primary body, an interior, a character's inner body or a ghost.</summary>
    private static bool IsContactBody(ulong data)
    {
        BodyKind kind = BodyUserData.Kind(data);
        return kind is BodyKind.Primary or BodyKind.Interior or BodyKind.CharacterInner or BodyKind.Ghost;
    }

    /// <summary>
    /// The velocity of the point <paramref name="point"/> of a body, in m/s: Jolt's velocity of the body. A ghost moves
    /// in Jolt with the velocity of its real body, and a static ghost holds still as its real body does (see
    /// <c>JoltPhysicsBackend.GhostMotion</c>).
    /// </summary>
    private unsafe Vector3 PointVelocity(nint body, Double3 point)
    {
        RVector3 com;
        Vector3 linear, angular;
        JoltNative.BodyCenterOfMass(body, &com);
        JoltNative.BodyLinearVelocity(body, &linear);
        JoltNative.BodyAngularVelocity(body, &angular);
        Vector3 offset = (point - com.ToDouble3()).ToVector3();
        return linear + Vector3.Cross(angular, offset);
    }
}
