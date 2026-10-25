using System.Numerics;

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
/// which layer, and what each layer holds. Invariants, checked by AssertInvariants:
/// <list type="bullet">
/// <item>A layer belongs to at most one light.</item>
/// <item>A light holds 1 or 6 layers.</item>
/// <item>A layer's record names the light that holds it.</item>
/// <item>No two views of a frame share a layer.</item>
/// </list>
/// </summary>
internal sealed class ShadowTiles
{
    public const int MarginTexels = ShadowCascades.MarginTexels;
    public const float StaleTexels = 0.25f;
    public const float WidestCone = MathF.PI / 3;

    /// <summary>The value of a layer of no light in <see cref="layerLights"/>.</summary>
    private const int NoLight = -1;

    /// <summary>What each layer holds, from the frame that drew it, or null for a free layer.</summary>
    private readonly TileRecord?[] records;

    /// <summary>Records of freed layers, kept so that a layer taken again allocates nothing.</summary>
    private readonly Stack<TileRecord> spareRecords = new();

    private readonly List<int> casterIndices = [];

    // The scratch of one Update: none of it outlives the call.
    private readonly List<Candidate> candidates = [];
    private readonly List<int> tileCounts = [];
    private readonly int[] layerLights;
    private readonly List<int> reachable = [];
    private readonly List<int> seen = [];
    private readonly List<(int Mesh, int Caster)> meshOrder = [];

    /// <summary>The views of the last Update, for AssertInvariants.</summary>
    private List<TileView> frameViews = [];

    public ShadowTiles(int layers, int tileSize)
    {
        Contract.Requires(layers > 0 && tileSize > 2 * MarginTexels);
        Layers = layers;
        TileSize = tileSize;
        records = new TileRecord?[layers];
        layerLights = new int[layers];
    }

    public int Layers { get; }
    public int TileSize { get; }

    /// <summary>The caster indices of the frame's draws, in mesh order within each draw.</summary>
    public IReadOnlyList<int> CasterIndices => casterIndices;

    /// <summary>
    /// The choke point of a frame: ranks the near lights that cast shadows, gives the top ones layers within the budget,
    /// writes their AtlasTiles slots, fills <paramref name="views"/> in slot order, and adds a draw for each view whose
    /// record is stale, with its casters, replacing that record.
    /// </summary>
    public void Update(
        Span<PreparedLight> lights,
        ReadOnlySpan<ShadowCaster> casters,
        long originFrameId,
        List<TileView> views,
        List<TileDraw> draws
    )
    {
        views.Clear();
        draws.Clear();
        casterIndices.Clear();
        frameViews = views;
        Grant(lights);
        Allot(lights);

        // The views follow the list, the snapshot's order, which every run shares.
        for (int light = 0; light < lights.Length; light++)
        {
            if (tileCounts[light] == 0)
                continue;
            lights[light].Shadow = new(new AtlasTiles(First: views.Count, Count: tileCounts[light]));
            CollectReachable(light: lights[light], casters: casters);
            AddViews(
                light: light,
                lights: lights,
                casters: casters,
                originFrameId: originFrameId,
                views: views,
                draws: draws
            );
        }
    }

    /// <summary>The tiles that a near light takes: 1 for a spot light of outer half-angle up to WidestCone, else 6.</summary>
    internal static int TilesOf(in PreparedLight light) =>
        light.Spread switch
        {
            Cone { OuterAngle: <= WidestCone } => 1,
            Cone or EveryDirection => CubeFaces.Count,
        };

    /// <summary>Camera-relative world space to the tile's clip space, row-vector form: reversed-Z, infinite far plane.</summary>
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

    /// <summary>The world size of one texel at 1 m from the light: 2 tan(half-angle) / tileSize.</summary>
    internal static float TexelPerMeter(in PreparedLight light, TileAim aim, int tileSize) =>
        TileFrustum.Of(light: light, aim: aim, tileSize: tileSize).TexelPerMeter(tileSize);

    internal void AssertInvariants()
    {
        // One record per layer, so a layer belongs to at most one light by the form of the array.
        foreach (IGrouping<uint, TileRecord> held in records.OfType<TileRecord>().GroupBy(record => record.Light))
            Contract.Invariant(held.Count() is 1 or CubeFaces.Count);

        Contract.Invariant(frameViews.Select(view => view.Layer).Distinct().Count() == frameViews.Count);
        Contract.Invariant(frameViews.Count == records.Count(record => record is not null));

        // The views of one light find one entity in their records, and two lights two entities.
        var entities = new Dictionary<int, uint>();
        foreach (TileView view in frameViews)
        {
            Contract.Invariant(records[view.Layer] is not null);
            uint entity = records[view.Layer]?.Light ?? 0;
            Contract.Invariant(entities.TryAdd(view.Light, entity) || entities[view.Light] == entity);
        }

        Contract.Invariant(entities.Values.Distinct().Count() == entities.Count);
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
    private static float Movement(in RecordedCaster was, in ShadowCaster now, Vector3 offset)
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
    /// Ranks the near lights that cast shadows by their irradiance at the eye, ties in list order, and gives each its
    /// tiles while they fit. Clears every near light's slot.
    /// </summary>
    private void Grant(Span<PreparedLight> lights)
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
            if (light.CastsShadow)
                candidates.Add(new(Light: i, Irradiance: LightUnits.EyeIrradiance(light)));
        }

        candidates.Sort(Candidate.Brighter);
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
    /// Gives each light with tiles its layers: a light keeps the layers that it held with the same count, layers of no
    /// light are freed, and the others take the lowest free layers.
    /// </summary>
    private void Allot(ReadOnlySpan<PreparedLight> lights)
    {
        Array.Fill(layerLights, NoLight);
        for (int light = 0; light < lights.Length; light++)
            if (tileCounts[light] > 0)
                Keep(light: light, entity: lights[light].EntityIndex);

        for (int layer = 0; layer < Layers; layer++)
        {
            if (layerLights[layer] != NoLight || records[layer] is not { } record)
                continue;
            spareRecords.Push(record);
            records[layer] = null;
        }

        // New lights take layers in list order, the snapshot's order, which every run shares.
        for (int light = 0; light < lights.Length; light++)
            if (tileCounts[light] > 0 && Array.IndexOf(layerLights, light) < 0)
                Take(light);
    }

    /// <summary>Gives <paramref name="light"/> the layers that its entity held, when they are as many as its tiles now.</summary>
    private void Keep(int light, uint entity)
    {
        int held = 0;
        foreach (TileRecord? record in records)
            if (record?.Light == entity)
                held++;
        if (held != tileCounts[light])
            return;
        for (int layer = 0; layer < Layers; layer++)
            if (records[layer]?.Light == entity)
                layerLights[layer] = light;
    }

    /// <summary>Gives <paramref name="light"/> the lowest free layers, which hold no record.</summary>
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
    private void CollectReachable(in PreparedLight light, ReadOnlySpan<ShadowCaster> casters)
    {
        reachable.Clear();
        for (int i = 0; i < casters.Length; i++)
            if (Vector3.Distance(casters[i].Position, light.Position) - casters[i].Radius < light.Reach)
                reachable.Add(i);
    }

    /// <summary>Adds a view for each layer of <paramref name="light"/>, in layer order, and a draw for each stale one.</summary>
    private void AddViews(
        int light,
        ReadOnlySpan<PreparedLight> lights,
        ReadOnlySpan<ShadowCaster> casters,
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
            var frustum = TileFrustum.Of(light: lights[light], aim: aim, tileSize: TileSize);
            CollectSeen(frustum: frustum, light: lights[light], casters: casters);
            bool cached =
                records[layer] is { } record
                && !IsStale(
                    record: record,
                    frustum: frustum,
                    light: lights[light],
                    casters: casters,
                    originFrameId: originFrameId
                );
            if (cached)
                continue;
            Record(
                layer: layer,
                frustum: frustum,
                light: lights[light],
                casters: casters,
                originFrameId: originFrameId
            );
            draws.Add(new(View: views.Count - 1, First: casterIndices.Count, Count: seen.Count));
            AppendInMeshOrder(casters);
        }
    }

    /// <summary>The reachable casters that the tile's frustum holds, in list order, into <see cref="seen"/>.</summary>
    private void CollectSeen(in TileFrustum frustum, in PreparedLight light, ReadOnlySpan<ShadowCaster> casters)
    {
        seen.Clear();
        foreach (int index in reachable)
            if (frustum.Holds(offset: casters[index].Position - light.Position, radius: casters[index].Radius))
                seen.Add(index);
    }

    /// <summary>
    /// True when the record no longer matches the tile (docs/plans/local-light-shadows.md, decision 34): another camera
    /// frame, another projection, a turn of the aim, other casters, or a caster that moved by a quarter of a texel.
    /// </summary>
    private bool IsStale(
        TileRecord record,
        in TileFrustum frustum,
        in PreparedLight light,
        ReadOnlySpan<ShadowCaster> casters,
        long originFrameId
    )
    {
        bool sameProjection = record.Frustum.Near == frustum.Near && record.Frustum.TanHalf == frustum.TanHalf;
        if (record.OriginFrameId != originFrameId || !sameProjection)
            return true;

        // A quarter of a texel at 1 m from the light, which is also a quarter of a texel's angle.
        float quarter = StaleTexels * frustum.TexelPerMeter(TileSize);
        if (Turn(record.Frustum.Basis, frustum.Basis) >= quarter || record.Casters.Count != seen.Count)
            return true;

        for (int i = 0; i < seen.Count; i++)
        {
            RecordedCaster was = record.Casters[i];
            ref readonly ShadowCaster now = ref casters[seen[i]];
            bool sameCaster = was.EntityIndex == now.EntityIndex && was.Part == now.Part && was.Mesh == now.Mesh;
            if (!sameCaster)
                return true;
            Vector3 offset = now.Position - light.Position;
            float nearest = MathF.Max(offset.Length() - now.Radius, frustum.Near);
            if (Movement(was: was, now: now, offset: offset) >= quarter * nearest)
                return true;
        }

        return false;
    }

    /// <summary>Replaces the layer's record with the tile of this frame and its seen casters.</summary>
    private void Record(
        int layer,
        in TileFrustum frustum,
        in PreparedLight light,
        ReadOnlySpan<ShadowCaster> casters,
        long originFrameId
    )
    {
        TileRecord record = records[layer] ?? (spareRecords.TryPop(out TileRecord? spare) ? spare : new());
        record.Light = light.EntityIndex;
        record.OriginFrameId = originFrameId;
        record.Frustum = frustum;
        record.Casters.Clear();
        foreach (int index in seen)
        {
            ref readonly ShadowCaster caster = ref casters[index];
            record.Casters.Add(
                new(
                    EntityIndex: caster.EntityIndex,
                    Part: caster.Part,
                    Mesh: caster.Mesh,
                    Offset: caster.Position - light.Position,
                    Rotation: caster.Rotation,
                    Scale: caster.Scale
                )
            );
        }

        records[layer] = record;
    }

    /// <summary>Appends the seen casters to <see cref="casterIndices"/> by mesh, and in list order within a mesh.</summary>
    private void AppendInMeshOrder(ReadOnlySpan<ShadowCaster> casters)
    {
        meshOrder.Clear();
        foreach (int index in seen)
            meshOrder.Add((casters[index].Mesh, Caster: index));
        // A tuple sorts by mesh, then by index, so the order is total and the same in every run.
        meshOrder.Sort();
        foreach ((int _, int caster) in meshOrder)
            casterIndices.Add(caster);
    }

    /// <summary>A near light that casts shadows, with its irradiance at the eye, in W/m².</summary>
    private readonly record struct Candidate(int Light, float Irradiance)
    {
        /// <summary>The brighter first, and of equal ones the first in the list (docs/plans/local-light-shadows.md, decision 24).</summary>
        public static readonly Comparison<Candidate> Brighter = static (a, b) =>
            a.Irradiance != b.Irradiance ? b.Irradiance.CompareTo(a.Irradiance) : a.Light.CompareTo(b.Light);
    }

    /// <summary>A caster as a tile record holds it: its position relative to the light.</summary>
    private readonly record struct RecordedCaster(
        uint EntityIndex,
        ushort Part,
        int Mesh,
        Vector3 Offset,
        Quaternion Rotation,
        Vector3 Scale
    );

    /// <summary>
    /// The lens of a tile: its axes, with <see cref="LightBasis.Z"/> pointing back toward the light, its near plane, the
    /// tangent of its half-angle widened by the margin, and its aim, which says whether it holds a cone or a square.
    /// </summary>
    private readonly record struct TileFrustum(LightBasis Basis, float Near, float TanHalf, TileAim Aim)
    {
        /// <summary>The frustum of the tile of <paramref name="light"/> that looks along <paramref name="aim"/>.</summary>
        public static TileFrustum Of(in PreparedLight light, TileAim aim, int tileSize)
        {
            float widening = (float)tileSize / (tileSize - 2 * MarginTexels);
            float near = MathF.Max(light.Radius, DepthRanges.NearPlane);
            return (aim, light.Spread) switch
            {
                (FaceAim(var face), _) => new(Basis: FaceBasisOf(face), Near: near, TanHalf: widening, Aim: aim),
                (ConeAim, Cone cone) => new(
                    Basis: LightBasis.FromSunDirection(-cone.Axis),
                    Near: near,
                    TanHalf: MathF.Tan(cone.OuterAngle) * widening,
                    Aim: aim
                ),
                (ConeAim, EveryDirection) => throw new ArgumentException("A point light has no cone.", nameof(aim)),
            };
        }

        /// <summary>The world size of one texel at 1 m from the light.</summary>
        public float TexelPerMeter(int tileSize) => 2 * TanHalf / tileSize;

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
        public uint Light;
        public long OriginFrameId;
        public TileFrustum Frustum;
        public readonly List<RecordedCaster> Casters = [];
    }
}
