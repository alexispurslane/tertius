using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <remarks>
/// <para>
/// Contacts are kept by body pair in each system (<see cref="FrameWorld"/>), and reported by entity pair. A contact
/// through a ghost is a contact of the two entities, and it can start through the ghost. (User) That holds for the body of
/// one entity and the ghost of another entity's body, and for the ghosts of two entities (the first half of ter-3mwi):
/// Jolt makes a contact of two ghosts only when one of them is a sensor, because no ghost is dynamic in Jolt. So a
/// sensor and a body that stick out of two sibling frames, and overlap only in the parent, outside both bubbles, touch
/// through their two stick-out ghosts there. A ghost meets what its real body meets: the contact filter judges each pair
/// with a ghost by the contact motions of both bodies, the motions of their real bodies (see
/// <see cref="FrameWorld.CanTouch"/>). The same pair of entities can be in contact in several systems at once. For
/// example, a body that sticks out of a child frame has a ghost in the parent, where a body of the parent touches the
/// ghost, while that body touches the child's bubble and has a ghost in the child, which the first body touches. Each
/// system then holds a mirror pair.
/// </para>
/// <para>
/// The contact ledger, <see cref="contactCounts"/>, keeps the reports right. For each pair of entities in contact, it
/// counts the body pairs that hold the contact as started, in every system, the loose contacts of the pair (see
/// <see cref="looseContacts"/>), and its waiting contacts (see <see cref="waitingContacts"/>). Each system holds at most
/// one body pair of an entity pair, because an entity has at most one body in each system that meets the other entity's
/// bodies: its primary body or a ghost, never both, and an interior only in its own frame, where the entity has no ghost.
/// A contact starts when its count goes from 0 to 1, and ends when the count goes back to 0. So the two entities hear one
/// Collision and one CollisionEnded, however many systems hold the contact in between.
/// </para>
/// <para>Invariants, which <see cref="AssertContactInvariants"/> checks:</para>
/// <list type="bullet">
/// <item>The count of each entity pair is the number of body pairs that hold its contact as started in all systems
/// (see <see cref="FrameWorld.StartedPairs"/>), plus the number of its loose contacts, plus the number of its waiting
/// contacts. An entity pair with no such body pair, no loose contact and no waiting contact has no count.</item>
/// <item>Every count is at least 1.</item>
/// <item>Outside a step, no entity is marked as handed off (see <see cref="handedOff"/>), so a mark never makes a contact
/// of a later step wait.</item>
/// </list>
/// </remarks>
public sealed partial class JoltPhysicsBackend
{
    /// <summary>
    /// The order of <see cref="bodyPairStarts"/>: by entities, then by frame index. One system holds at most one body pair
    /// of an entity pair, so the frame index tells the starts of one pair apart. The point tells them apart too, so that
    /// the order is total.
    /// </summary>
    private static readonly Comparison<ContactStarted> ByEntitiesThenFrame = static (x, y) =>
    {
        int c = x.A.CompareTo(y.A);
        if (c != 0) return c;
        c = x.B.CompareTo(y.B);
        if (c != 0) return c;
        c = x.Frame.Index.CompareTo(y.Frame.Index);
        if (c != 0) return c;
        c = x.Point.X.CompareTo(y.Point.X);
        if (c != 0) return c;
        c = x.Point.Y.CompareTo(y.Point.Y);
        return c != 0 ? c : x.Point.Z.CompareTo(y.Point.Z);
    };

    private readonly List<RawContact> rawContacts = [];

    private readonly List<RawEnd> rawEnds = [];

    // The body pairs that started in this step, in every system, as contacts of their entities, before the ledger decides
    // which of them the step reports.
    private readonly List<ContactStarted> bodyPairStarts = [];

    // The contacts that ended since the last step's report, until WriteEndedContacts reports them.
    private readonly List<ContactEnded> endedContacts = [];

    // The started contacts of the bodies that left their systems since the last ResolveLooseContacts, by entities in
    // canonical order, once for each body pair. A ghost that goes leaves its system too.
    private readonly List<(Entity A, Entity B)> looseContacts = [];

    // The entities whose body a hand-off of this step moved to another frame. ResolveLooseContacts reads and clears them at
    // the end of the step.
    private readonly HashSet<Entity> handedOff = [];

    // The loose contacts that the last ResolveLooseContacts let wait for the next one, by entities in canonical order:
    // contacts of an entity that a hand-off moved, with a body or a ghost in a numeric frame, which no system took over
    // (see ResolveLooseContacts). Each keeps its count in the ledger until then.
    private readonly List<(Entity A, Entity B)> waitingContacts = [];

    // The contact ledger: for each pair of entities in contact, in canonical order, the number of body pairs that hold the
    // contact as started in every system, plus its loose and its waiting contacts. See the remarks of this file.
    private readonly Dictionary<(Entity A, Entity B), int> contactCounts = [];

    // Scratch for AssertContactInvariants.
    private readonly Dictionary<(Entity A, Entity B), int> countScratch = [];

    private readonly List<RawEnd> pairScratch = [];

    // Scratch for EndPairsThatCannotTouch: the other bodies of the started pairs of one body.
    private readonly List<BodyID> partnerScratch = [];

    // ---- Contacts ----

    /// <summary>
    /// Reports each pair of entities whose contact started during the step, once, with entities in canonical order. A
    /// contact starts when the first system starts a body pair of the two entities (see the remarks of this file). When
    /// several systems start one in the same step, the point, the normal and the frame come from the system of the frame
    /// with the lowest index, so that the report does not depend on the order of the systems. Every body pair that the
    /// drain ended counts down after the starts of the step, so a contact whose body pair ends in one system while another
    /// system starts one goes on. The contacts that end go to <see cref="endedContacts"/>, which
    /// <see cref="WriteEndedContacts"/> reports.
    /// </summary>
    private void CollectContacts(PhysicsStepOutput output)
    {
        bodyPairStarts.Clear();
        rawEnds.Clear();
        foreach (FrameWorld world in worldList)
        {
            rawContacts.Clear();
            world.DrainContacts(rawContacts, rawEnds);
            foreach (RawContact raw in rawContacts)
            {
                if (ContactEntity(raw.UserData1, out float inverseMass1) is not { } a ||
                    ContactEntity(raw.UserData2, out float inverseMass2) is not { } b ||
                    a == b)
                    continue;
                // Relative normal speed times the reduced mass: the impulse that stops the approach. A sensor stops nothing.
                float inverseMassSum = inverseMass1 + inverseMass2;
                float reducedMass = inverseMassSum > 0 && !raw.Sensor ? 1f / inverseMassSum : 0f;
                float impulse = MathF.Max(0f, -raw.NormalVelocity) * reducedMass;
                // Jolt's normal points from body 1 to body 2. Keep it pointing from A to B.
                Vector3 normal = raw.Normal;
                if (b.CompareTo(a) < 0)
                {
                    (a, b) = (b, a);
                    normal = -normal;
                }

                bodyPairStarts.Add(new(A: a, B: b, Frame: world.Frame, Point: raw.Point, Normal: normal,
                    Impulse: impulse));
            }
        }

        // The body pairs of one entity pair lie next to each other, the one of the lowest frame index first.
        bodyPairStarts.Sort(ByEntitiesThenFrame);
        foreach (ContactStarted start in bodyPairStarts)
        {
            ref int count = ref CollectionsMarshal.GetValueRefOrAddDefault(contactCounts, (start.A, start.B), out _);
            if (count++ == 0)
                output.Contacts.Add(start);
        }

        foreach (RawEnd raw in rawEnds)
            if (EntityPair(raw) is { } pair)
                Recount(pair, -1);
        rawEnds.Clear();
    }

    /// <summary>
    /// Changes the count of <paramref name="pair"/> in the ledger by <paramref name="change"/>, and ends the contact when
    /// the count reaches 0. Only a pair that has a count can end or loosen, so a pair without one comes only from a bug.
    /// </summary>
    private void Recount((Entity A, Entity B) pair, int change)
    {
        ref int count = ref CollectionsMarshal.GetValueRefOrNullRef(contactCounts, pair);
        if (Unsafe.IsNullRef(ref count))
        {
            ReportBug(
                $"Physics: the contact of {pair.A} and {pair.B} ended, but it had not started. Only a bug ends such a contact, so the end was skipped.");
            return;
        }

        count += change;
        Contract.Assert(count >= 0);
        if (count > 0)
            return;
        contactCounts.Remove(pair);
        endedContacts.Add(new(pair.A, pair.B));
    }

    /// <summary>
    /// Reports every contact that ended since the last step, in entity order: the ones whose last body pair the drain
    /// ended, and the loose ones that no system takes over (see <see cref="ResolveLooseContacts"/>). Call it after the
    /// hand-offs and the ghosts of the step.
    /// </summary>
    private void WriteEndedContacts(PhysicsStepOutput output)
    {
        ResolveLooseContacts();
        endedContacts.Sort(static (x, y) =>
        {
            int c = x.A.CompareTo(y.A);
            return c != 0 ? c : x.B.CompareTo(y.B);
        });
        output.ContactsEnded.AddRange(endedContacts);
        endedContacts.Clear();
    }

    /// <summary>
    /// Takes the started contacts of a body that leaves its system, for <see cref="ResolveLooseContacts"/>. Call it for
    /// every body that the backend takes out of a system, a ghost too, while its record still exists.
    /// </summary>
    private void LoosenContacts(FrameWorld world, BodyID body)
    {
        rawEnds.Clear();
        world.ForgetContacts(body, rawEnds);
        foreach (RawEnd raw in rawEnds)
            if (EntityPair(raw) is { } pair)
                looseContacts.Add(pair);
        rawEnds.Clear();
    }

    /// <summary>
    /// The check after a change of motion (case 3b of docs/plans/complete-ghosts.md): ends each started pair of
    /// <paramref name="body"/> in <paramref name="world"/> that the pair test refuses now (see
    /// <see cref="FrameWorld.CanTouch"/>). The body is a hull or an interior whose motion changed, which keeps its body
    /// through the change. Jolt drops a pair that it can no longer make only when it simulates one of the two bodies, so
    /// without the check a pair of two bodies that Jolt does not simulate, such as a parked hull and a static sensor,
    /// would outlive the change, and the filter never sees a pair that Jolt holds already. Each pair that ends counts down
    /// in the ledger, and a contact whose count reaches 0 ends at the end of the step (see <see cref="Recount"/>). A pair
    /// that the new motion allows goes on. A started pair that is no contact of two entities comes only from a bug, since
    /// only the contacts of two entities enter the pair tables (see <see cref="ReportBug"/>), and a Release build ends it.
    /// </summary>
    private void EndPairsThatCannotTouch(FrameWorld world, BodyID body)
    {
        partnerScratch.Clear();
        world.StartedPairsOf(body, partnerScratch);
        BodyInterface bodies = world.Bodies;
        foreach (BodyID other in partnerScratch)
        {
            (Entity A, Entity B)? pair = EntityPair(new(bodies.GetUserData(body), bodies.GetUserData(other)));
            if (pair is not null && world.CanTouch(body, other))
                continue;
            world.DropPair(body, other);
            if (pair is { } ended)
                Recount(ended, -1);
            else
                ReportBug(
                    $"Physics: the system of {world.Frame} held a started pair of {body} and {other}, which is no contact of two entities. " +
                    "Only a bug starts such a pair, so it was dropped.");
        }

        partnerScratch.Clear();
    }

    /// <summary>
    /// Settles each contact whose body left its system since the last call. Call it after the ghosts of the step, so that
    /// the new ghost of a body that left can carry its contacts. A contact goes on in every system that holds a body of each
    /// entity that can touch the other one there (see <see cref="CarryContact"/>): the system takes it over as a held
    /// pair, which ends as a held pair does, when Jolt does not find it again. So a contact goes on when two touching
    /// bodies move together, by a hand-off or a move between numeric frames, and when one of them moves on while the
    /// other touches its ghost, or the other way round. A move between numeric frames happens at the sync point, and the
    /// ghost pass puts the moved body's ghosts at its new state only at the end of the next step, and makes the ones that
    /// its new place wants then, so its contacts wait until then.
    /// Each loose contact counts down in the ledger, and each system that takes it over counts up. A contact that no
    /// system holds then ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A hand-off happens in the step, after the numeric update of the last sync point, which chose the numeric frames for
    /// the place of each body before the step. So after a hand-off, a contact of the body with a body in a numeric frame
    /// can be in no system for one step, where without numeric frames one system takes it over:
    /// </para>
    /// <list type="bullet">
    /// <item>A body that enters a frame while it touches a body of the semantic parent has its ghost among the frames of
    /// the semantic parent only in the frame's tree parent, the home of the frame's hull, which can be a numeric frame. The
    /// update linked the other body to the body that entered, not to the hull, so the other body can live in another frame
    /// of the semantic parent: a numeric frame, or the semantic parent's own system while the ghost is in a numeric frame.
    /// Without numeric frames, the ghost in the semantic parent's own system takes the contact over.</item>
    /// <item>A body that leaves a frame whose hull lives apart from its group, in a following frame of its own, leaves into
    /// that following frame, and drops its ghost in the home of the group (see <see cref="WantGhostsBesideHull"/>), where the
    /// bodies that the merge test linked to the hull live. Without numeric frames, those bodies lie in the semantic
    /// parent's own system, where the body goes, and it takes the contact over there.</item>
    /// </list>
    /// <para>
    /// The next sync point's numeric update links the touching bodies, which puts them in one system, also a static body
    /// and a body that falls, or names the home of their group for the ghost of the anchor of a following frame, whose ghost the next ghost
    /// pass makes. So the rule is: a contact that no system takes over waits for the next call with its count, as the
    /// contacts of a move between numeric frames do, when a hand-off of the step moved a body of it, and a body of it or a
    /// ghost of one is in a numeric frame (see <see cref="InNumericFrame"/>). That call settles it for good. A contact that
    /// really ended at the hand-off then ends one step late. Without numeric frames no body and no ghost is in a numeric
    /// frame, so no contact waits. Before the review of ter-4psc, the rule asked for a body in a numeric frame. A crate that
    /// rests against a rock in space and enters a ship that falls in a numeric frame then lost the contact: its ghost went
    /// into that frame, the rock stayed in space until the next update, and gameplay heard an end and a second start.
    /// </para>
    /// <para>
    /// An interior never takes over a contact of its owner's hull. The hull of a frame lies inside the frame's bubble, so
    /// a body moves into the frame before it can touch the hull, and it leaves the frame only far outside the bubble.
    /// </para>
    /// </remarks>
    private void ResolveLooseContacts()
    {
        // The contacts that waited since the last call, now that a sync point and a step have passed.
        foreach ((Entity A, Entity B) pair in waitingContacts)
            Recount(pair,
                (Find(pair.A) is { } first && Find(pair.B) is { } second ? CarryContact(first, second) : 0) - 1);
        waitingContacts.Clear();
        foreach ((Entity A, Entity B) pair in looseContacts)
        {
            if (Find(pair.A) is not { } first || Find(pair.B) is not { } second)
            {
                Recount(pair, -1);
                continue;
            }

            int taken = CarryContact(first, second);
            if (taken == 0 && (handedOff.Contains(pair.A) || handedOff.Contains(pair.B)) &&
                (InNumericFrame(first) || InNumericFrame(second)))
                waitingContacts.Add(pair);
            else
                Recount(pair, taken - 1);
        }

        looseContacts.Clear();
        handedOff.Clear();
    }

    /// <summary>
    /// True when the system of a numeric frame holds the body of the record (see <see cref="BodyRecord.Frame"/>) or one of
    /// its ghosts.
    /// </summary>
    private bool InNumericFrame(BodyRecord record)
    {
        if (!record.Frame.IsNull && frames.IsNumeric(record.Frame))
            return true;
        if (record.Ghosts is { } ghosts)
            foreach (GhostBody ghost in ghosts)
                if (frames.IsNumeric(ghost.Frame))
                    return true;
        return false;
    }

    /// <summary>
    /// Lets every system that holds a body of each of the two records carry their contact, and returns how many of them
    /// took it over: the ones that did not hold it as started already (see <see cref="FrameWorld.Carry"/>). The bodies are
    /// the primary body or a ghost of each: two primary bodies, a primary body and a ghost, or two ghosts. They must pass
    /// the pair test (see <see cref="FrameWorld.CanTouch"/>), because Jolt would never find a pair that cannot touch, and a
    /// held pair that Jolt never finds ends a step late, or never while both its bodies sleep. Of two ghosts, only a pair
    /// with a sensor passes it.
    /// </summary>
    private static int CarryContact(BodyRecord first, BodyRecord second)
    {
        int taken = 0;
        if (first.World is { } home)
            taken += CarryIn(home, PrimaryId(first), second);
        if (first.Ghosts is { } ghosts)
            foreach (GhostBody ghost in ghosts)
                taken += CarryIn(ghost.World, ghost.Body, second);
        return taken;
    }

    /// <summary>
    /// Lets <paramref name="world"/> carry the contact of <paramref name="body"/>, a body of one record there, with the body
    /// of <paramref name="other"/> there: its primary body, or its ghost. Returns 1 when the system took the contact over,
    /// and 0 when it holds no body of <paramref name="other"/>, when the two cannot touch, or when it holds the contact as
    /// started already.
    /// </summary>
    private static int CarryIn(FrameWorld world, BodyID body, BodyRecord other)
    {
        BodyID otherBody = other.World == world ? PrimaryId(other) : GhostIn(other, world).Body;
        return otherBody.IsValid && world.CanTouch(body, otherBody) && world.Carry(body, otherBody) ? 1 : 0;
    }

    /// <summary>The ghost of the record's body in <paramref name="world"/>, or one with an invalid ID when it has none there.</summary>
    private static GhostBody GhostIn(BodyRecord record, FrameWorld world)
    {
        if (record.Ghosts is { } ghosts)
            foreach (GhostBody ghost in ghosts)
                if (ghost.World == world)
                    return ghost;
        return new() { Body = BodyID.Invalid };
    }

    /// <summary>
    /// The contact motion of a ghost of the record's body, which decides what the ghost meets (see
    /// <see cref="FrameWorld.GhostContactMotion"/>): the motion of the real body, with a character in either form as
    /// dynamic, since a character counts as dynamic in Jolt's rule. For a hull it is the hull's motion.
    /// </summary>
    private static MotionType GhostContactMotion(BodyRecord record) => ToMotionType(record.Motion);

    /// <summary>The two entities of a contact, in canonical order, or null when the contact is not reported.</summary>
    private (Entity A, Entity B)? EntityPair(in RawEnd raw) =>
        ContactEntity(raw.UserData1, out _) is not { } a || ContactEntity(raw.UserData2, out _) is not { } b ||
        a == b
            ? null
            : b.CompareTo(a) < 0
                ? (b, a)
                : (a, b);

    /// <summary>
    /// The entity behind a body in a contact, and the inverse mass to use for the impulse estimate. A ghost stands for its
    /// real body, so it has the real body's mass, and the impulse does not depend on which system reports the contact.
    /// </summary>
    private Entity? ContactEntity(ulong userData, out float inverseMass)
    {
        inverseMass = 0;
        if (RecordFor(userData) is not { } record)
            return null;
        float bodyInverseMass = record.Desc.Mass > 0 ? 1f / record.Desc.Mass : 0f;
        (Entity? entity, inverseMass) = BodyUserData.Kind(userData) switch
        {
            BodyKind.Primary or BodyKind.Ghost when record.Motion is BodyMotion.Dynamic or BodyMotion.Character =>
                (record.Entity, bodyInverseMass),
            BodyKind.CharacterInner => (record.Entity, bodyInverseMass),
            BodyKind.Primary or BodyKind.Interior or BodyKind.Ghost => (record.Entity, 0f),
            BodyKind.None => ((Entity?)null, 0f)
        };
        return entity;
    }

    /// <summary>
    /// Checks the invariants of the contact ledger (see the remarks of this file). Visits every started body pair of every
    /// system, so it costs O(started pairs + loose contacts + waiting contacts).
    /// </summary>
    private void AssertContactInvariants()
    {
        Contract.Invariant(handedOff.Count == 0);
        countScratch.Clear();
        foreach (FrameWorld world in worldList)
        {
            pairScratch.Clear();
            world.StartedPairs(pairScratch);
            foreach (RawEnd raw in pairScratch)
                if (EntityPair(raw) is { } pair)
                    CollectionsMarshal.GetValueRefOrAddDefault(countScratch, pair, out _)++;
        }

        foreach ((Entity A, Entity B) pair in looseContacts)
            CollectionsMarshal.GetValueRefOrAddDefault(countScratch, pair, out _)++;
        foreach ((Entity A, Entity B) pair in waitingContacts)
            CollectionsMarshal.GetValueRefOrAddDefault(countScratch, pair, out _)++;
        Contract.Invariant(countScratch.Count == contactCounts.Count);
        foreach (((Entity A, Entity B) pair, int count) in contactCounts)
            Contract.Invariant(count >= 1 && countScratch.TryGetValue(pair, out int expected) && expected == count);
    }
}
