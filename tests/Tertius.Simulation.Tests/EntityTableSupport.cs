using System.Numerics;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Helpers for the entity table tests that the shared <see cref="TableSupport"/> does not have: entities with a body or
/// components, a transform row at a given x, and a plain list of the entities that a table enumerator yields.
/// </summary>
internal static class EntityTableSupport
{
    /// <summary>A transform row with no frame, at position (<paramref name="x"/>, 0, 0) and no rotation.</summary>
    public static TransformRow RowAt(double x) => new() { Pose = TestWorld.At(x) };

    /// <summary>Reserves a slot and makes it live at x = 0, with this body and these components.</summary>
    public static Entity SpawnLiveWith(this EntityTable table, BodyDesc? body, params IComponent[] components) =>
        table.SpawnLiveAt(0, body, components);

    /// <summary>Reserves a slot and makes it live at position (<paramref name="x"/>, 0, 0), with this body and these components.</summary>
    public static Entity SpawnLiveAt(this EntityTable table, double x, BodyDesc? body = null,
        params IComponent[] components)
    {
        Entity entity = table.Reserve();
        table.Activate(entity: entity, row: RowAt(x), render: new(), components: components,
            wants: [.. components.Select(c => c.Wants)], body: body, bodyCenterOfMass: Vector3.Zero,
            ownedFrame: FrameHandle.Null);
        return entity;
    }

    /// <summary>The entities that an enumerator of live entities yields, in the order it yields them.</summary>
    public static List<Entity> ToList(this EntityTable.LiveEntityEnumerator entities)
    {
        var list = new List<Entity>();
        foreach (Entity entity in entities)
            list.Add(entity);
        return list;
    }
}
