using System.Numerics;
using System.Runtime.InteropServices;

namespace Tertius.Rendering;

/// <summary>A spot light's tile looks along its cone.</summary>
internal readonly record struct ConeAim;

/// <summary>One of a point light's six tiles looks along a cube face.</summary>
internal readonly record struct FaceAim(CubeFace Face);

/// <summary>Where a tile looks. A generated union; its zero value is <see cref="ConeAim"/>.</summary>
[GeneratedUnion(typeof(ConeAim), typeof(FaceAim), ZeroCase = typeof(ConeAim))]
internal readonly partial struct TileAim;

/// <summary>One tile of a frame: its layer, the index of its light in PreparedScene.Lights, and its aim.</summary>
internal readonly record struct TileView(int Layer, int Light, TileAim Aim);

/// <summary>A tile view to draw this frame, with its casters: CasterIndices[First] to CasterIndices[First + Count − 1].</summary>
internal readonly record struct TileDraw(int View, int First, int Count);

/// <summary>
/// The CPU side of the local shadow atlas (docs/plans/local-light-shadows.md, "The budget", "The cache"): which light holds
/// which layer, and what each layer holds, as of the last submitted frame. <see cref="Update"/> chooses a frame's layers
/// and draws; <see cref="Drew"/> keeps them once the frame is submitted. Invariants, checked by AssertInvariants after Drew:
/// <list type="bullet">
/// <item>A layer belongs to at most one light.</item>
/// <item>A light holds 1 or 6 layers.</item>
/// <item>A layer's record names the entity of the light whose view holds it.</item>
/// <item>No two views of a frame share a layer.</item>
/// <item>A layer without a view of the last submitted frame holds no record.</item>
/// <item>A record's casters' pose ranges follow one another in caster order from 0 and end at the end of its pose rows.</item>
/// </list>
/// </summary>
internal sealed class ShadowTiles
{
    public const int MarginTexels = ShadowCascades.MarginTexels;
    public const float StaleTexels = 0.25f;
    public const float WidestCone = MathF.PI / 3;

    /// <summary>
    /// How many times as strong as its light a holder, a light whose entity holds layers in <see cref="records"/>,
    /// ranks for tiles: a challenger, a light that holds none, takes the layers only with more than this many times the
    /// holder's rank irradiance in the holder's tier, or with exactly this many times when it comes first in the list,
    /// or from a higher tier than the holder reaches even at <see cref="HolderReachGrowth"/> times its reach
    /// (docs/plans/local-light-shadows.md, settled fork 2). The margin spans tiers only inside the light list, and only
    /// inside the main view's tiers. A holder that the batcher drops from the list, because its own reach misses the
    /// view and no drawn face keeps it (SceneBatcher.Reaches), loses its layers at once, and a holder that only a face
    /// keeps ranks at <see cref="MainViewTier.FaceOnly"/>. So today the margin spans a change of tier only for a spot
    /// lamp between <see cref="MainViewTier.AxisMissesView"/> and <see cref="MainViewTier.InView"/>, and a point lamp
    /// gets it inside its tier only. NOTE: if a scene shows a point lamp's shadows that pop at the view's edge, the
    /// known follow-up is design B of the fork in ter-4xw0: keep a holder in the light list while
    /// √<see cref="HolderMargin"/> times its reach meets the view.
    /// </summary>
    public const float HolderMargin = 2;

    /// <summary>The value of a layer of no light in <see cref="layerLights"/>.</summary>
    private const int NoLight = -1;

    /// <summary>
    /// How far float rounding of a caster's camera-relative pose can move it, as a share of its largest input: 8 × 2⁻²³
    /// (docs/plans/local-light-shadows.md, "The cache").
    /// </summary>
    private const float RoundingShare = 8f / (1 << 23);

    /// <summary>
    /// How many times its reach a holder's tier is computed at: √<see cref="HolderMargin"/>, because the reach is
    /// √(peak intensity / cutoff irradiance) (<see cref="LightUnits.Reach"/>), so a light <see cref="HolderMargin"/> times as
    /// strong reaches √<see cref="HolderMargin"/> times as far.
    /// </summary>
    private static readonly float HolderReachGrowth = MathF.Sqrt(HolderMargin);

    /// <summary>What each layer holds, from the submitted frame that drew it, or null for a free layer.</summary>
    private readonly TileRecord?[] records;

    /// <summary>Records of freed layers, kept so that a layer taken again allocates nothing.</summary>
    private readonly Stack<TileRecord> spareRecords = new();

    private readonly List<int> casterIndices = [];

    // The scratch of one Update: none of it outlives the call.
    private readonly List<Candidate> candidates = [];
    private readonly List<int> tileCounts = [];
    private readonly List<int> reachable = [];
    private readonly List<int> tileCasters = [];
    private readonly List<(int Mesh, int Caster)> meshOrder = [];

    // The frame in flight: what the last Update chose, which Drew keeps once the frame is submitted, and the next Update
    // drops when no Drew came (docs/plans/local-light-shadows.md, "The cache").

    /// <summary>The light of each layer in the frame in flight, an index into its lights, or <see cref="NoLight"/>.</summary>
    private readonly int[] layerLights;

    /// <summary>The records of the views that the frame in flight draws, by layer.</summary>
    private readonly List<(int Layer, TileRecord Record)> drawnRecords = [];

    /// <summary>The layer of each view of the frame in flight and its light's entity.</summary>
    private readonly List<(int Layer, uint LightEntity)> frameTiles = [];

    /// <summary>The layer of each view of the last submitted frame and its light's entity, for AssertInvariants.</summary>
    private readonly List<(int Layer, uint LightEntity)> drawnTiles = [];

    public ShadowTiles(int layers, int tileSize)
    {
        Contract.Requires(layers > 0 && tileSize > 2 * MarginTexels);
        Layers = layers;
        TileSize = tileSize;
        records = new TileRecord?[layers];
        layerLights = new int[layers];
        Array.Fill(layerLights, NoLight);
    }

    public int Layers { get; }
    public int TileSize { get; }

    /// <summary>The casters of the frame's draws, as indices into its placed instances, in mesh order within each draw.</summary>
    public IReadOnlyList<int> CasterIndices => casterIndices;

    /// <summary>
    /// The choke point of a frame: ranks the near lights that cast shadows, gives the top ones layers within the budget,
    /// writes their AtlasTiles slots, fills <paramref name="views"/> in slot order, and adds a draw for each view whose
    /// record is stale, with its casters. It only reads the records and the layers' owners: <see cref="Drew"/> keeps the
    /// frame's.
    /// </summary>
    /// <param name="camera">The frame's camera, with which the batcher computed each light's tier.</param>
    /// <param name="placed">The frame's placed instances, whose casters are those that cast shadows, in the snapshot's order.</param>
    /// <param name="deformed">The frame meshes that the casters name, which tell a posed caster's static mesh and pose rows.</param>
    public void Update(
        Span<PreparedLight> lights,
        in CameraView camera,
        ReadOnlySpan<PreparedInstance> placed,
        DeformedMeshes deformed,
        long originFrameId,
        List<TileView> views,
        List<TileDraw> draws
    )
    {
        views.Clear();
        draws.Clear();
        casterIndices.Clear();
        frameTiles.Clear();
        // A frame that was never submitted drew nothing, so its records go back unused.
        foreach ((int _, TileRecord record) in drawnRecords)
            spareRecords.Push(record);
        drawnRecords.Clear();
        Grant(lights: lights, camera: camera);
        Allot(lights);

        // The views follow the list, the snapshot's order, which every run shares.
        for (int light = 0; light < lights.Length; light++)
        {
            if (tileCounts[light] == 0)
                continue;
            lights[light].Shadow = new(new AtlasTiles(First: views.Count, Count: tileCounts[light]));
            CollectReachable(light: lights[light], placed: placed);
            AddViews(
                light: light,
                lights: lights,
                placed: placed,
                deformed: deformed,
                originFrameId: originFrameId,
                views: views,
                draws: draws
            );
        }
    }

    /// <summary>
    /// Keeps what the frame of the last <see cref="Update"/> chose, once that frame is submitted: the layers of its lights,
    /// and the records of the views that it drew. A layer of no light of the frame becomes free.
    /// </summary>
    public void Drew()
    {
        for (int layer = 0; layer < Layers; layer++)
        {
            if (layerLights[layer] != NoLight || records[layer] is not { } freed)
                continue;
            spareRecords.Push(freed);
            records[layer] = null;
        }

        foreach ((int layer, TileRecord record) in drawnRecords)
        {
            if (records[layer] is { } replaced)
                spareRecords.Push(replaced);
            records[layer] = record;
        }

        drawnRecords.Clear();
        drawnTiles.Clear();
        drawnTiles.AddRange(frameTiles);
    }

    /// <summary>The near plane of every tile of <paramref name="light"/>, in m from it: max(radius, <see cref="DepthRanges.NearPlane"/>).</summary>
    internal static float NearPlane(in PreparedLight light) => MathF.Max(light.Radius, DepthRanges.NearPlane);

    /// <summary>The tiles that a near light takes: 1 for a spot light of outer half-angle up to WidestCone, else 6.</summary>
    internal static int TilesOf(in PreparedLight light) =>
        light.Spread switch
        {
            Cone { OuterAngle: <= WidestCone } => 1,
            Cone or EveryDirection => CubeFaces.Count,
        };

    /// <summary>Camera-relative world space to the tile's clip space, row-vector form: reversed-Z, infinite far plane.</summary>
    /// <exception cref="ArgumentException">A cone aim for a point light or a spot light wider than WidestCone.</exception>
    internal static Matrix4x4 ViewProjection(in PreparedLight light, TileAim aim, int tileSize)
    {
        var frustum = TileFrustum.Of(light: light, aim: aim, tileSize: tileSize);
        LightBasis basis = frustum.Basis;
        // View space looks down −Z, along the tile's forward axis, as CubeFaces.View does.
        var view = new Matrix4x4(
            m11: basis.X.X,
            m12: basis.Y.X,
            m13: basis.Z.X,
            m14: 0,
            m21: basis.X.Y,
            m22: basis.Y.Y,
            m23: basis.Z.Y,
            m24: 0,
            m31: basis.X.Z,
            m32: basis.Y.Z,
            m33: basis.Z.Z,
            m34: 0,
            m41: 0,
            m42: 0,
            m43: 0,
            m44: 1
        );
        Matrix4x4 projection = DepthRanges.Projection(
            verticalFov: 2 * MathF.Atan(frustum.TanHalf),
            aspect: 1,
            near: frustum.Near,
            far: float.PositiveInfinity
        );
        return Matrix4x4.CreateTranslation(-light.Position) * view * projection;
    }

    /// <summary>The world size of one texel at 1 m from the light along the view axis: 2 tan(half-angle) / tileSize.</summary>
    /// <exception cref="ArgumentException">A cone aim for a point light or a spot light wider than WidestCone.</exception>
    internal static float TexelPerMeter(in PreparedLight light, TileAim aim, int tileSize) =>
        TileFrustum.Of(light: light, aim: aim, tileSize: tileSize).TexelPerMeter(tileSize);

    internal void AssertInvariants()
    {
        // One record per layer, so a layer belongs to at most one light by the form of the array.
        foreach (IGrouping<uint, TileRecord> held in records.OfType<TileRecord>().GroupBy(record => record.LightEntity))
            Contract.Invariant(held.Count() is 1 or CubeFaces.Count);

        Contract.Invariant(drawnTiles.Select(tile => tile.Layer).Distinct().Count() == drawnTiles.Count);
        foreach ((int layer, uint lightEntity) in drawnTiles)
            Contract.Invariant(records[layer]?.LightEntity == lightEntity);

        // Each view's layer holds a record, so as many records as views leave none on a layer without a view.
        Contract.Invariant(drawnTiles.Count == records.Count(record => record is not null));

        foreach (TileRecord record in records.OfType<TileRecord>())
            Contract.Invariant(PoseRangesTile(record));
    }

    /// <summary>Whether the record's casters' pose ranges lie end to end from 0 to the end of its pose rows, in caster order.</summary>
    private static bool PoseRangesTile(TileRecord record)
    {
        int next = 0;
        foreach (RecordedCaster caster in record.Casters)
        {
            if (caster.PoseStart != next || caster.PoseCount < 0)
                return false;
            next += caster.PoseCount;
        }

        return next == record.Poses.Count;
    }

    /// <summary>How far the farthest axis of one basis lies from its counterpart in the other: the chord, about the angle.</summary>
    private static float Turn(in LightBasis from, in LightBasis to) =>
        MathF.Max(
            MathF.Max(Vector3.Distance(from.X, to.X), Vector3.Distance(from.Y, to.Y)),
            Vector3.Distance(from.Z, to.Z)
        );

    /// <summary>
    /// The farthest that a change of a caster's pose moves a point of its bounding sphere: by its offset from the light,
    /// by its turn at its radius, or by its scale.
    /// </summary>
    private static float Movement(in RecordedCaster was, in PreparedInstance now, Vector3 offset)
    {
        float shift = Vector3.Distance(was.Offset, offset);
        // The vector part of the relative rotation is sin(θ / 2), so the chord at the radius is 2 r sin(θ / 2).
        Quaternion relative = now.Rotation * Quaternion.Conjugate(was.Rotation);
        float turn = 2 * now.Radius * new Vector3(relative.X, relative.Y, relative.Z).Length();
        float growth = MathF.Max(
            MathF.Max(Growth(was.Scale.X, now.Scale.X), Growth(was.Scale.Y, now.Scale.Y)),
            Growth(was.Scale.Z, now.Scale.Z)
        );
        return MathF.Max(MathF.Max(shift, turn), now.Radius * growth);
    }

    /// <summary>
    /// The change of a scale factor as a share of the old one, which is never 0: the batcher drops a caster without
    /// volume (SceneBatcher.HasVolume).
    /// </summary>
    private static float Growth(float was, float now) => MathF.Abs(now - was) / MathF.Abs(was);

    /// <summary>
    /// Ranks the near lights that cast shadows by tier, each tier by <see cref="LightUnits.RankIrradiance"/>, ties in
    /// list order, and gives each its tiles while they fit. A challenger ranks at its <see cref="PreparedLight.Tier"/>,
    /// and a holder as if its light were <see cref="HolderMargin"/> times as strong (<see cref="Candidate.Holder"/>),
    /// so a ranking that swings by less than the margin keeps one owner. So a challenger spot lamp whose axis misses
    /// the view takes only the layers that the lights whose light falls in view leave free, and a light that only a
    /// face keeps, holder or challenger, only the layers that the main view's lights leave free
    /// (docs/plans/environment-map.md, decision 20). A holder that the batcher dropped from <paramref name="lights"/>
    /// is not ranked, so it loses its layers at once (<see cref="HolderMargin"/>). Clears every near light's slot.
    /// </summary>
    private void Grant(Span<PreparedLight> lights, in CameraView camera)
    {
        candidates.Clear();
        tileCounts.Clear();
        for (int i = 0; i < lights.Length; i++)
        {
            tileCounts.Add(0);
            ref PreparedLight light = ref lights[i];
            if (LightUnits.IsFar(light.Distance))
                continue;
            light.Shadow = default;
            if (!light.CastsShadow)
                continue;
            // The holders come from the records, the layers of the last submitted frame.
            candidates.Add(
                Held(light.EntityIndex) > 0
                    ? Candidate.Holder(light: i, prepared: light, camera: camera)
                    : Candidate.Challenger(light: i, prepared: light)
            );
        }

        candidates.Sort(Candidate.Ranked);
        int free = Layers;
        foreach (Candidate candidate in candidates)
        {
            int count = TilesOf(lights[candidate.Light]);
            // A light that does not fit is skipped, so that a later one that fits still gets its tiles.
            if (count > free)
                continue;
            tileCounts[candidate.Light] = count;
            free -= count;
        }
    }

    /// <summary>
    /// Gives each light with tiles its layers: a light keeps the layers that it held with the same count, and the others
    /// take the lowest layers that no light of the frame holds.
    /// </summary>
    private void Allot(ReadOnlySpan<PreparedLight> lights)
    {
        Array.Fill(layerLights, NoLight);
        for (int light = 0; light < lights.Length; light++)
            if (tileCounts[light] > 0)
                Keep(light: light, entity: lights[light].EntityIndex);

        // New lights take layers in list order, the snapshot's order, which every run shares.
        for (int light = 0; light < lights.Length; light++)
            if (tileCounts[light] > 0 && Array.IndexOf(layerLights, light) < 0)
                Take(light);
    }

    /// <summary>Gives <paramref name="light"/> the layers that its entity held, when they are as many as its tiles now.</summary>
    private void Keep(int light, uint entity)
    {
        if (Held(entity) != tileCounts[light])
            return;
        for (int layer = 0; layer < Layers; layer++)
            if (records[layer]?.LightEntity == entity)
                layerLights[layer] = light;
    }

    /// <summary>How many layers the light of <paramref name="entity"/> holds in <see cref="records"/>.</summary>
    private int Held(uint entity)
    {
        int held = 0;
        foreach (TileRecord? record in records)
            if (record?.LightEntity == entity)
                held++;
        return held;
    }

    /// <summary>
    /// Gives <paramref name="light"/> the lowest layers that no light of the frame holds. Such a layer can still hold the
    /// record of a light that lost its tiles, which <see cref="IsStale"/> tells apart by its entity and aim.
    /// </summary>
    private void Take(int light)
    {
        int taken = 0;
        for (int layer = 0; layer < Layers && taken < tileCounts[light]; layer++)
        {
            if (layerLights[layer] != NoLight)
                continue;
            layerLights[layer] = light;
            taken++;
        }

        Contract.Assert(taken == tileCounts[light]);
    }

    /// <summary>The casters whose bounding spheres come within the light's reach, in list order, into <see cref="reachable"/>.</summary>
    private void CollectReachable(in PreparedLight light, ReadOnlySpan<PreparedInstance> placed)
    {
        reachable.Clear();
        for (int i = 0; i < placed.Length; i++)
        {
            ref readonly PreparedInstance caster = ref placed[i];
            if (caster.CastsShadow && Vector3.Distance(caster.Position, light.Position) - caster.Radius < light.Reach)
                reachable.Add(i);
        }
    }

    /// <summary>Adds a view for each layer of <paramref name="light"/>, in layer order, and a draw for each stale one.</summary>
    private void AddViews(
        int light,
        ReadOnlySpan<PreparedLight> lights,
        ReadOnlySpan<PreparedInstance> placed,
        DeformedMeshes deformed,
        long originFrameId,
        List<TileView> views,
        List<TileDraw> draws
    )
    {
        bool cone = tileCounts[light] == 1;
        int face = 0;
        for (int layer = 0; layer < Layers; layer++)
        {
            if (layerLights[layer] != light)
                continue;
            // The faces take the light's layers in ascending order, so a light that keeps its layers keeps its faces.
            TileAim aim = cone ? new(new ConeAim()) : new(new FaceAim((CubeFace)face++));
            views.Add(new(Layer: layer, Light: light, Aim: aim));
            frameTiles.Add((layer, LightEntity: lights[light].EntityIndex));
            var frustum = TileFrustum.Of(light: lights[light], aim: aim, tileSize: TileSize);
            CollectSeen(frustum: frustum, light: lights[light], placed: placed);
            ReadOnlySpan<int> seen = CollectionsMarshal.AsSpan(tileCasters);
            bool cached =
                records[layer] is { } record
                && !IsStale(
                    record: record,
                    frustum: frustum,
                    light: lights[light],
                    placed: placed,
                    deformed: deformed,
                    seen: seen,
                    tileSize: TileSize,
                    originFrameId: originFrameId
                );
            if (cached)
                continue;
            Record(
                layer: layer,
                frustum: frustum,
                light: lights[light],
                placed: placed,
                deformed: deformed,
                seen: seen,
                originFrameId: originFrameId
            );
            draws.Add(new(View: views.Count - 1, First: casterIndices.Count, Count: seen.Length));
            AppendInMeshOrder(placed: placed, seen: seen);
        }
    }

    /// <summary>The reachable casters that the tile's frustum holds, in list order, into <see cref="tileCasters"/>.</summary>
    private void CollectSeen(in TileFrustum frustum, in PreparedLight light, ReadOnlySpan<PreparedInstance> placed)
    {
        tileCasters.Clear();
        foreach (int index in reachable)
            if (frustum.Holds(offset: placed[index].Position - light.Position, radius: placed[index].Radius))
                tileCasters.Add(index);
    }

    /// <summary>
    /// True when the record no longer matches the tile (docs/plans/local-light-shadows.md, decision 34, "The cache"):
    /// another light or aim, another camera frame, another projection, a turn of the aim, other casters or materials, or a
    /// caster that moved by a quarter of a texel where it shows, or by more than float rounding of its pose, or a caster of
    /// another static mesh or other pose rows (docs/plans/skinning-and-morphs.md, fork 1).
    /// </summary>
    private static bool IsStale(
        TileRecord record,
        in TileFrustum frustum,
        in PreparedLight light,
        ReadOnlySpan<PreparedInstance> placed,
        DeformedMeshes deformed,
        ReadOnlySpan<int> seen,
        int tileSize,
        long originFrameId
    )
    {
        bool sameTile = record.LightEntity == light.EntityIndex && record.Frustum.Aim == frustum.Aim;
        bool sameProjection = record.Frustum.Near == frustum.Near && record.Frustum.TanHalf == frustum.TanHalf;
        if (!sameTile || record.OriginFrameId != originFrameId || !sameProjection)
            return true;

        // A quarter of a texel at 1 m from the light along the axis, which is also a quarter of a texel's angle there.
        float axisQuarter = StaleTexels * frustum.TexelPerMeter(tileSize);
        // A turn of the aim moves the image at the tile's edge the most.
        bool turned = Turn(record.Frustum.Basis, frustum.Basis) >= axisQuarter * frustum.EdgeTexelShare;
        if (turned || record.Casters.Count != seen.Length)
            return true;

        for (int i = 0; i < seen.Length; i++)
        {
            RecordedCaster was = record.Casters[i];
            ref readonly PreparedInstance now = ref placed[seen[i]];
            // A deformed copy's frame mesh changes with the jobs before it, so the record names its static mesh. The
            // material decides which texels cast: a cutout casts only its core.
            bool sameCaster =
                was.EntityIndex == now.EntityIndex
                && was.Part == now.Part
                && was.Mesh == deformed.SourceOf(now.Mesh)
                && was.Material == now.Material;
            if (!sameCaster || !SamePose(record: record, was: was, now: deformed.PoseOf(now.Mesh)))
                return true;
            Vector3 offset = now.Position - light.Position;
            float nearest = MathF.Max(offset.Length() - now.Radius, frustum.Near);
            float quarter = axisQuarter * nearest * frustum.TexelShareOver(offset: offset, radius: now.Radius);
            float largestInput = MathF.Max(MathF.Max(now.Position.Length(), light.Position.Length()), now.Radius);
            float rounding = RoundingShare * largestInput;
            if (Movement(was: was, now: now, offset: offset) >= MathF.Max(quarter, rounding))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a caster's pose rows <paramref name="now"/> are the recorded caster's, bit for bit: an empty span for a
    /// static mesh on both sides, or the same rows of a still rig (docs/plans/skinning-and-morphs.md, decision 21).
    /// </summary>
    private static bool SamePose(TileRecord record, in RecordedCaster was, ReadOnlySpan<Vector4> now)
    {
        ReadOnlySpan<Vector4> recorded = CollectionsMarshal.AsSpan(record.Poses).Slice(was.PoseStart, was.PoseCount);
        // Bits, not values: a NaN equals itself here, and −0 differs from +0.
        return MemoryMarshal.Cast<Vector4, uint>(recorded).SequenceEqual(MemoryMarshal.Cast<Vector4, uint>(now));
    }

    /// <summary>
    /// Writes the record of the layer's tile in this frame, with its <paramref name="seen"/> casters and the pose rows of
    /// the posed ones, into <see cref="drawnRecords"/>, which <see cref="Drew"/> keeps.
    /// </summary>
    private void Record(
        int layer,
        in TileFrustum frustum,
        in PreparedLight light,
        ReadOnlySpan<PreparedInstance> placed,
        DeformedMeshes deformed,
        ReadOnlySpan<int> seen,
        long originFrameId
    )
    {
        TileRecord record = spareRecords.TryPop(out TileRecord? spare) ? spare : new();
        record.LightEntity = light.EntityIndex;
        record.OriginFrameId = originFrameId;
        record.Frustum = frustum;
        record.Casters.Clear();
        record.Poses.Clear();
        foreach (int index in seen)
        {
            ref readonly PreparedInstance caster = ref placed[index];
            ReadOnlySpan<Vector4> pose = deformed.PoseOf(caster.Mesh);
            record.Casters.Add(
                new(
                    EntityIndex: caster.EntityIndex,
                    Part: caster.Part,
                    Mesh: deformed.SourceOf(caster.Mesh),
                    Material: caster.Material,
                    Offset: caster.Position - light.Position,
                    Rotation: caster.Rotation,
                    Scale: caster.Scale,
                    PoseStart: record.Poses.Count,
                    PoseCount: pose.Length
                )
            );
            record.Poses.AddRange(pose);
        }

        drawnRecords.Add((layer, record));
    }

    /// <summary>Appends the <paramref name="seen"/> casters to <see cref="casterIndices"/> by mesh, and in list order within a mesh.</summary>
    private void AppendInMeshOrder(ReadOnlySpan<PreparedInstance> placed, ReadOnlySpan<int> seen)
    {
        meshOrder.Clear();
        foreach (int index in seen)
            meshOrder.Add((placed[index].Mesh, Caster: index));
        // A tuple sorts by mesh, then by index, so the order is total and the same in every run.
        meshOrder.Sort();
        foreach ((int _, int caster) in meshOrder)
            casterIndices.Add(caster);
    }

    /// <summary>A near light that casts shadows, the tier and the rank irradiance, in W/m², at which it ranks for tiles.</summary>
    private readonly record struct Candidate(int Light, MainViewTier Tier, float Irradiance)
    {
        /// <summary>A light that holds no layers: it ranks at its own tier and rank irradiance.</summary>
        public static Candidate Challenger(int light, in PreparedLight prepared) =>
            new(Light: light, Tier: prepared.Tier, Irradiance: LightUnits.RankIrradiance(prepared));

        /// <summary>
        /// A light that holds layers: it ranks as if its light were <see cref="HolderMargin"/> times as strong, at
        /// <see cref="HolderMargin"/> times its rank irradiance, in the tier that the one tier rule
        /// (<see cref="CameraView.TierAndRankDistance"/>) gives at <see cref="HolderReachGrowth"/> times its reach. A
        /// holder that only a face keeps (<see cref="MainViewTier.FaceOnly"/>) stays in that tier, so that the main
        /// view's shadow slots never depend on the faces that a map draws (docs/plans/environment-map.md, decision 20).
        /// The grown tier is an input of this ranking only: <see cref="PreparedLight.Tier"/>, which decides the direct
        /// pass's lights, stays as the batcher set it.
        /// </summary>
        public static Candidate Holder(int light, in PreparedLight prepared, in CameraView camera)
        {
            MainViewTier grown =
                prepared.Tier is MainViewTier.FaceOnly
                    ? MainViewTier.FaceOnly
                    : camera.TierAndRankDistance(light: prepared, reach: prepared.Reach * HolderReachGrowth).Tier;
            return new(Light: light, Tier: grown, Irradiance: HolderMargin * LightUnits.RankIrradiance(prepared));
        }

        /// <summary>
        /// The higher tier first (<see cref="MainViewTier"/>), then the brighter first, and of equal ones the first in the
        /// list (docs/plans/local-light-shadows.md, decision 24).
        /// </summary>
        public static readonly Comparison<Candidate> Ranked = static (a, b) =>
        {
            // NOTE: not Tier.CompareTo, which is Enum.CompareTo(object) and boxes both tiers on every comparison.
            if (a.Tier != b.Tier)
                return a.Tier > b.Tier ? -1 : 1;
            return a.Irradiance != b.Irradiance ? b.Irradiance.CompareTo(a.Irradiance) : a.Light.CompareTo(b.Light);
        };
    }

    /// <summary>
    /// A caster as a tile record holds it: its static mesh, its material, which decides which texels cast, its position
    /// relative to the light, and its pose rows, TileRecord.Poses from PoseStart, PoseCount of them, none for a static mesh.
    /// </summary>
    private readonly record struct RecordedCaster(
        uint EntityIndex,
        ushort Part,
        int Mesh,
        uint Material,
        Vector3 Offset,
        Quaternion Rotation,
        Vector3 Scale,
        int PoseStart,
        int PoseCount
    );

    /// <summary>
    /// The lens of a tile: its axes, with <see cref="LightBasis.Z"/> pointing back toward the light, its near plane, the
    /// tangent of its half-angle widened by the margin, and its aim, which says whether it holds a cone or a square.
    /// </summary>
    private readonly record struct TileFrustum(LightBasis Basis, float Near, float TanHalf, TileAim Aim)
    {
        /// <summary>The frustum of the tile of <paramref name="light"/> that looks along <paramref name="aim"/>.</summary>
        /// <exception cref="ArgumentException">A cone aim for a point light or a spot light wider than WidestCone.</exception>
        public static TileFrustum Of(in PreparedLight light, TileAim aim, int tileSize)
        {
            float widening = (float)tileSize / (tileSize - 2 * MarginTexels);
            float near = NearPlane(light);
            return (aim, light.Spread) switch
            {
                (FaceAim(var face), _) => new(Basis: FaceBasisOf(face), Near: near, TanHalf: widening, Aim: aim),
                (ConeAim, Cone { OuterAngle: <= WidestCone } cone) => new(
                    Basis: LightBasis.FromDirection(-cone.Axis),
                    Near: near,
                    TanHalf: MathF.Tan(cone.OuterAngle) * widening,
                    Aim: aim
                ),
                (ConeAim, Cone) => throw new ArgumentException(
                    "A spot light wider than WidestCone takes six faces, not a cone.",
                    nameof(aim)
                ),
                (ConeAim, EveryDirection) => throw new ArgumentException("A point light has no cone.", nameof(aim)),
            };
        }

        /// <summary>The tangent of the widest angle off the axis that the tile shows: its cone's rim, or a face's corner.</summary>
        public float EdgeTan =>
            Aim switch
            {
                ConeAim => TanHalf,
                FaceAim => MathF.Sqrt(2) * TanHalf,
            };

        /// <summary>The share of a texel on the axis that a texel at the tile's edge spans at the same distance: cos² of the edge.</summary>
        public float EdgeTexelShare => 1 / (1 + EdgeTan * EdgeTan);

        /// <summary>The world size of one texel at 1 m from the light along the axis.</summary>
        public float TexelPerMeter(int tileSize) => 2 * TanHalf / tileSize;

        /// <summary>
        /// The share of a texel on the axis that a texel spans at the same distance, cos² of the angle off the axis, at
        /// the widest angle at which the sphere at <paramref name="offset"/> from the light shows in the tile.
        /// </summary>
        public float TexelShareOver(Vector3 offset, float radius)
        {
            float distance = offset.Length();
            // A sphere that holds the light shows out to the tile's edge.
            if (radius >= distance)
                return EdgeTexelShare;
            float depth = -Vector3.Dot(offset, Basis.Z);
            float center = MathF.Acos(Math.Clamp(depth / distance, -1, 1));
            float widest = MathF.Min(center + MathF.Asin(radius / distance), MathF.Atan(EdgeTan));
            float cos = MathF.Cos(widest);
            return cos * cos;
        }

        /// <summary>
        /// False when the sphere at <paramref name="offset"/> from the light lies wholly behind the light, or outside the
        /// widened cone or square. Depth clamping draws a sphere nearer than the near plane, so it holds one.
        /// </summary>
        public bool Holds(Vector3 offset, float radius)
        {
            float depth = -Vector3.Dot(offset, Basis.Z);
            if (depth + radius <= 0)
                return false;
            float cos = 1 / MathF.Sqrt(1 + TanHalf * TanHalf);
            return Aim switch
            {
                ConeAim => ConeHolds(offset: offset, radius: radius, depth: depth, cos: cos),
                FaceAim => SquareHolds(offset: offset, radius: radius, depth: depth, cos: cos),
            };
        }

        /// <summary>A cube face's axes as a light basis: X right, Y up, Z back toward the light.</summary>
        private static LightBasis FaceBasisOf(CubeFace face)
        {
            FaceBasis basis = CubeFaces.Basis(face);
            return new(X: basis.Right, Y: basis.Up, Z: -basis.Forward);
        }

        /// <summary>True when the sphere comes within the cone of half-angle atan(TanHalf), whose cosine is <paramref name="cos"/>.</summary>
        private bool ConeHolds(Vector3 offset, float radius, float depth, float cos)
        {
            // The distance from the center to the cone's side. Behind the apex it is shorter than the distance to the
            // apex, so the test holds more there, never less.
            float across = MathF.Sqrt(MathF.Max(offset.LengthSquared() - depth * depth, 0));
            return cos * (across - TanHalf * depth) <= radius;
        }

        /// <summary>True when the sphere is inside or touches each of the four side planes |side| = TanHalf · depth.</summary>
        private bool SquareHolds(Vector3 offset, float radius, float depth, float cos)
        {
            // The distance to a side plane is (TanHalf · depth − |side|) · cos, so the sphere's radius allows radius / cos.
            float room = TanHalf * depth + radius / cos;
            return MathF.Abs(Vector3.Dot(offset, Basis.X)) <= room && MathF.Abs(Vector3.Dot(offset, Basis.Y)) <= room;
        }
    }

    /// <summary>What a layer holds: the tile of the frame that drew it, and its casters in list order.</summary>
    private sealed class TileRecord
    {
        public uint LightEntity;
        public long OriginFrameId;
        public TileFrustum Frustum;
        public readonly List<RecordedCaster> Casters = [];

        /// <summary>The pose rows of the posed casters, which they name.</summary>
        public readonly List<Vector4> Poses = [];
    }
}
