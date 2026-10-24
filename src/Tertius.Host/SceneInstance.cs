using Tertius.Simulation;

namespace Tertius.Host;

/// <summary>
/// The scene ids of a built scene: a map between ids such as <c>"box"</c> and entity handles. Scripts and state dumps
/// name entities by these ids. The content formats design spawns scenes from files into the same kind of map.
/// </summary>
/// <remarks>
/// A scene id follows the grammar of the content formats design: lowercase letters, digits, <c>_</c>, <c>.</c> and
/// <c>-</c>, starting with a letter or a digit, at most 64 characters. So an id is also a safe file name, and it never
/// looks like the debug form <c>#index.generation</c> of an entity without an id.
/// </remarks>
public sealed class SceneInstance
{
    private readonly Dictionary<string, Entity> entities = [with(StringComparer.Ordinal)];
    private readonly Dictionary<Entity, string> ids = [];
    private readonly List<string> views = [];

    /// <summary>The entity with the scene id. Throws <see cref="KeyNotFoundException"/> for an unknown id.</summary>
    public Entity this[string id] =>
        entities.TryGetValue(id, out Entity entity)
            ? entity
            : throw new KeyNotFoundException($"The scene has no entity with the id '{id}'.");

    /// <summary>The scene ids of the entities that the window's view key cycles through as the camera, in order.</summary>
    public IReadOnlyList<string> Views => views;

    /// <summary>
    /// Gives <paramref name="entity"/> the scene id <paramref name="id"/>. Throws <see cref="ArgumentException"/> for an id
    /// outside the grammar, an id that another entity has, an entity that has an id already, or the null entity.
    /// </summary>
    public void Add(string id, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!IsSceneId(id))
            throw new ArgumentException(
                $"'{id}' is not a scene id. Use lowercase letters, digits, '_', '.' and '-', start with a letter or a digit, and write at most 64 characters.",
                nameof(id)
            );
        if (entity.IsNull)
            throw new ArgumentException($"The null entity cannot have the scene id '{id}'.", nameof(entity));
        if (entities.TryGetValue(id, out Entity holder))
            throw new ArgumentException($"The scene id '{id}' belongs to {holder} already.", nameof(id));
        if (ids.TryGetValue(entity, out string? other))
            throw new ArgumentException($"{entity} has the scene id '{other}' already.", nameof(entity));
        entities.Add(id, entity);
        ids.Add(entity, id);
    }

    /// <summary>The entity with the scene id, or false for an unknown id.</summary>
    public bool TryGetEntity(string id, out Entity entity) => entities.TryGetValue(id, out entity);

    /// <summary>The scene id of the entity, or its debug form <c>#index.generation</c> if it has none.</summary>
    public string IdOf(Entity entity) =>
        ids.TryGetValue(entity, out string? id) ? id : $"#{entity.Index}.{entity.Generation}";

    /// <summary>
    /// Makes the entity with the scene id one of the views. Throws <see cref="KeyNotFoundException"/> for an unknown id,
    /// and <see cref="ArgumentException"/> for an id that is a view already.
    /// </summary>
    public void AddView(string id)
    {
        _ = this[id];
        if (views.Contains(id))
            throw new ArgumentException($"The scene id '{id}' is a view already.", nameof(id));
        views.Add(id);
    }

    /// <summary>
    /// The view after <paramref name="camera"/>, in the order that the views were added, from the last back to the first.
    /// The first view if the camera is none of them. The camera itself if the scene has no views.
    /// </summary>
    public Entity NextView(Entity camera)
    {
        if (views.Count == 0)
            return camera;
        int current = ids.TryGetValue(camera, out string? id) ? views.IndexOf(id) : -1;
        return entities[views[(current + 1) % views.Count]];
    }

    /// <summary>True if <paramref name="text"/> follows the grammar of a scene id.</summary>
    public static bool IsSceneId(string text)
    {
        if (text.Length is 0 or > 64 || !IsLowercaseLetterOrDigit(text[0]))
            return false;
        foreach (char c in text)
            if (!IsLowercaseLetterOrDigit(c) && c is not ('_' or '.' or '-'))
                return false;
        return true;
    }

    private static bool IsLowercaseLetterOrDigit(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9';
}

/// <summary>
/// A named function that fills a new world and returns the scene ids of what it made. It makes its meshes and materials
/// through the render resources, which are <see cref="RenderResources.None"/> in a run with no renderer.
/// </summary>
public sealed record Scene(string Name, Func<World, IRenderResources, SceneInstance> Build);

/// <summary>Render resources for a run with no renderer.</summary>
public static class RenderResources
{
    /// <summary>Resources with no renderer behind them. Every mesh, texture and material they make is none, so nothing draws.</summary>
    public static IRenderResources None { get; } = new NoResources();

    private sealed class NoResources : IRenderResources
    {
        public MeshId CreateMesh(MeshData mesh) => MeshId.None;

        public TextureId CreateTexture(TextureData texture) => TextureId.None;

        public MaterialId CreateMaterial(MaterialDesc material) => MaterialId.None;
    }
}
