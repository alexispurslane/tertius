using System.Runtime.CompilerServices;

namespace Tertius.Simulation;

/// <summary>
/// A test of one entity of the snapshot, for <see cref="EntityQuery.Where"/>. Use a static lambda, so that the compiler
/// caches the delegate and nothing allocates. Read the entity through <paramref name="snapshot"/>.
/// </summary>
public delegate bool EntityPredicate(Snapshot snapshot, Entity entity);

/// <summary>
/// A stored query over the entities of the snapshot: a component mask, a collision mask, places and predicates. It holds
/// no source, so a field can keep it, and <see cref="Snapshot.Find"/> runs it. The default value matches every live
/// entity. Each word returns a new query and leaves this one as it was.
/// </summary>
/// <remarks>
/// <para>
/// Inline, with no allocation: the component mask (<see cref="With{T}"/>, <see cref="Without{T}"/>), the collision mask
/// (<see cref="InAll"/>, <see cref="InNone"/>, <see cref="InAny"/>), one <see cref="In"/>, one <c>Near</c> and one
/// <see cref="Where"/>. A second <c>In</c>, <c>Near</c> or <c>Where</c> goes into an immutable heap part. Building the
/// heap part during an entity's run throws, because it would allocate on every run: build such a query once, in a
/// static readonly field of the component. The world runs each component type's class constructor before the type's
/// first run, so such a field is built outside the tick.
/// </para>
/// <para>
/// The filters narrow with each call, except <see cref="InAny"/>, which widens the one any-of set of the collision mask.
/// A query whose <c>With</c> and <c>Without</c> share a type would match nothing, so building it throws. A <c>Near</c>
/// finds the entities in the frame of its center whose position is within the radius.
/// </para>
/// </remarks>
public readonly record struct EntityQuery
{
    /// <summary>The component types that a matching entity must all have and must not have.</summary>
    public ComponentMask Components { get; private init; }

    /// <summary>The filter over the collision groups of a matching entity's body. An entity without a body is in no group.</summary>
    public CollisionMask Collision { get; private init; }

    /// <summary>The frame of the inline <see cref="In"/>, or null.</summary>
    internal FrameHandle InFrame { get; private init; }

    /// <summary>The inline <c>Near</c>, or the empty filter (<see cref="QueryFilter.HasValue"/> is false) when the slot is free.</summary>
    internal QueryFilter NearFilter { get; private init; }

    /// <summary>The predicate of the inline <see cref="Where"/>, or null.</summary>
    internal EntityPredicate? WherePredicate { get; private init; }

    /// <summary>The filters that do not fit inline, or null.</summary>
    internal QueryExtras? Extras { get; private init; }

    /// <summary>Keeps the entities that have a <typeparamref name="T"/> component. Throws if the query refuses
    /// <typeparamref name="T"/>.</summary>
    public EntityQuery With<T>()
        where T : IComponent
    {
        int id = ComponentType<T>.Id;
        return Components.Without.Contains(id)
            ? throw new InvalidOperationException(
                $"The query already refuses {typeof(T).Name}, so With<{typeof(T).Name}> would make it match nothing."
            )
            : this with
            {
                Components = Components.AndWith(id),
            };
    }

    /// <summary>Keeps the entities that have no <typeparamref name="T"/> component. Throws if the query needs
    /// <typeparamref name="T"/>.</summary>
    public EntityQuery Without<T>()
        where T : IComponent
    {
        int id = ComponentType<T>.Id;
        return Components.With.Contains(id)
            ? throw new InvalidOperationException(
                $"The query already needs {typeof(T).Name}, so Without<{typeof(T).Name}> would make it match nothing."
            )
            : this with
            {
                Components = Components.AndWithout(id),
            };
    }

    /// <summary>Keeps the entities whose body is in <paramref name="group"/>.</summary>
    public EntityQuery InAll(CollisionGroup group) => this with { Collision = Collision.InAll(group) };

    /// <summary>Keeps the entities that have no body in <paramref name="group"/>.</summary>
    public EntityQuery InNone(CollisionGroup group) => this with { Collision = Collision.InNone(group) };

    /// <summary>
    /// Adds <paramref name="group"/> to the one any-of set: the query keeps the entities whose body is in at least one
    /// group of the set. Each call widens the set.
    /// </summary>
    public EntityQuery InAny(CollisionGroup group) => this with { Collision = Collision.InAny(group) };

    /// <summary>Keeps the entities in <paramref name="frame"/>. Throws <see cref="ArgumentException"/> for the null frame.</summary>
    public EntityQuery In(FrameHandle frame)
    {
        var filter = QueryFilter.In(frame);
        return InFrame.IsNull ? this with { InFrame = frame } : WithExtra(filter);
    }

    /// <summary>
    /// Keeps the entities in the frame of the entity that runs the query whose position is within
    /// <paramref name="radius"/> meters of it; that entity matches too when it passes the other filters. A run from
    /// <see cref="World.Snapshot"/>, which has no such entity, throws.
    /// </summary>
    public EntityQuery Near(double radius) => WithNear(QueryFilter.Near(radius));

    /// <summary>
    /// Keeps the entities in the frame of <paramref name="other"/> whose position is within <paramref name="radius"/>
    /// meters of its position in the snapshot. When <paramref name="other"/> is not live, the query matches nothing.
    /// </summary>
    public EntityQuery Near(Entity other, double radius) => WithNear(QueryFilter.Near(other, radius));

    /// <summary>
    /// Keeps the entities in <paramref name="frame"/> whose position is within <paramref name="radius"/> meters of
    /// <paramref name="point"/>. When the frame is gone, the query matches nothing.
    /// </summary>
    public EntityQuery Near(FrameHandle frame, Double3 point, double radius) =>
        WithNear(QueryFilter.Near(frame, point, radius));

    /// <summary>Keeps the entities that pass <paramref name="predicate"/>. Use a static lambda, so that it allocates nothing.</summary>
    public EntityQuery Where(EntityPredicate predicate)
    {
        var filter = QueryFilter.Where(predicate);
        return WherePredicate is null ? this with { WherePredicate = predicate } : WithExtra(filter);
    }

    /// <summary>This query with a Near filter: inline while the inline Near is free, else in the heap part.</summary>
    internal EntityQuery WithNear(in QueryFilter filter) =>
        !NearFilter.HasValue ? this with { NearFilter = filter } : WithExtra(filter);

    private EntityQuery WithExtra(in QueryFilter filter) => this with { Extras = QueryExtras.Append(Extras, filter) };

    /// <summary>
    /// True if a live <paramref name="entity"/> passes every filter of the query and every filter of
    /// <paramref name="more"/>, in the front snapshot. The one filter code of every form of the query: the cheap mask
    /// tests come first.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool Matches(in Snapshot snapshot, Entity entity, ReadOnlySpan<QueryFilter> more)
    {
        EntityTable entities = snapshot.World.Entities;
        if (!Components.Matches(entities.FrontComponents(entity)))
            return false;
        if (Collision != CollisionMask.Empty && !Collision.Matches(entities.FrontCollision(entity).Groups))
            return false;
        ref readonly TransformRow row = ref entities.FrontRow(entity);
        if (!InFrame.IsNull && row.Frame != InFrame)
            return false;
        if (!NearFilter.Passes(snapshot, entity, row))
            return false;
        if (WherePredicate != null && !WherePredicate(snapshot, entity))
            return false;
        if (Extras != null)
            foreach (ref readonly QueryFilter filter in Extras.Filters)
                if (!filter.Passes(snapshot, entity, row))
                    return false;
        foreach (ref readonly QueryFilter filter in more)
            if (!filter.Passes(snapshot, entity, row))
                return false;
        return true;
    }
}

/// <summary>
/// The static starts of an <see cref="EntityQuery"/> chain, such as <c>EntityQuery.With&lt;Crew&gt;()</c>. C# cannot give a
/// struct a static and an instance member with one signature, so the starts are static extension members. Each one
/// starts from the default query, which matches every live entity.
/// </summary>
public static class EntityQueryStarts
{
    extension(EntityQuery)
    {
        /// <inheritdoc cref="EntityQuery.With{T}"/>
        public static EntityQuery With<T>()
            where T : IComponent => default(EntityQuery).With<T>();

        /// <inheritdoc cref="EntityQuery.Without{T}"/>
        public static EntityQuery Without<T>()
            where T : IComponent => default(EntityQuery).Without<T>();

        /// <inheritdoc cref="EntityQuery.InAll"/>
        public static EntityQuery InAll(CollisionGroup group) => default(EntityQuery).InAll(group);

        /// <inheritdoc cref="EntityQuery.InNone"/>
        public static EntityQuery InNone(CollisionGroup group) => default(EntityQuery).InNone(group);

        /// <inheritdoc cref="EntityQuery.InAny"/>
        public static EntityQuery InAny(CollisionGroup group) => default(EntityQuery).InAny(group);

        /// <inheritdoc cref="EntityQuery.In"/>
        public static EntityQuery In(FrameHandle frame) => default(EntityQuery).In(frame);

        /// <inheritdoc cref="EntityQuery.Near(double)"/>
        public static EntityQuery Near(double radius) => default(EntityQuery).Near(radius);

        /// <inheritdoc cref="EntityQuery.Near(Entity, double)"/>
        public static EntityQuery Near(Entity other, double radius) => default(EntityQuery).Near(other, radius);

        /// <inheritdoc cref="EntityQuery.Near(FrameHandle, Double3, double)"/>
        public static EntityQuery Near(FrameHandle frame, Double3 point, double radius) =>
            default(EntityQuery).Near(frame, point, radius);

        /// <inheritdoc cref="EntityQuery.Where"/>
        public static EntityQuery Where(EntityPredicate predicate) => default(EntityQuery).Where(predicate);
    }
}

/// <summary>
/// The immutable heap part of an <see cref="EntityQuery"/>: its filters past the inline <c>In</c>, <c>Near</c> and
/// <c>Where</c>, in the order of the calls. Two heap parts are equal when they hold equal filters.
/// </summary>
internal sealed class QueryExtras : IEquatable<QueryExtras>
{
    private readonly QueryFilter[] filters;

    private QueryExtras(QueryFilter[] filters)
    {
        this.filters = filters;
    }

    public ReadOnlySpan<QueryFilter> Filters => filters;

    /// <summary>
    /// A new heap part with the filters of <paramref name="extras"/> and then <paramref name="filter"/>. Throws
    /// <see cref="InvalidOperationException"/> during an entity's run.
    /// </summary>
    public static QueryExtras Append(QueryExtras? extras, in QueryFilter filter) =>
        GameplayThread.InEntityRun
            ? throw new InvalidOperationException(
                "This entity query needs a heap part for its second In, Near or Where, and an entity's run cannot build one: it would allocate on every run. "
                    + "Build the query once, as a static readonly field of the component, or use the chain from Self.Snapshot.Entities, which keeps such filters on the stack."
            )
            : new([.. extras?.filters ?? [], filter]);

    public bool Equals(QueryExtras? other) => other is not null && filters.AsSpan().SequenceEqual(other.filters);

    public override bool Equals(object? obj) => Equals(obj as QueryExtras);

    public override int GetHashCode() => filters.Length;
}
