using System.Numerics;
using System.Runtime.InteropServices;

namespace Tertius.Rendering;

/// <summary>
/// Identifies one impostor: the entity and its render part (<see cref="RenderInstance.Part"/>), and the mesh and the
/// resolved material slot it is drawn with. Two parts of one entity are separate impostors even when they share a
/// mesh and a material. A part that changes its mesh or material gets a new key, so it never draws an old capture.
/// </summary>
internal readonly record struct ImpostorKey(uint EntityIndex, ushort Part, int Mesh, uint Material)
{
    public static ImpostorKey Of(in PreparedInstance instance) =>
        new(instance.EntityIndex, instance.Part, instance.Mesh, instance.Material);
}

/// <summary>A capture that <see cref="ImpostorPass"/> renders this frame: one object alone, into one atlas tile.</summary>
internal struct ImpostorCapture
{
    /// <summary>The atlas tile (layer) the capture replaces.</summary>
    public int Tile;

    public int Mesh;
    public uint Material;

    /// <summary>The object's interpolated, camera-relative transform this frame.</summary>
    public Vector3 Position;

    public Quaternion Rotation;
    public Vector3 Scale;

    /// <summary>The capture's axes in camera-relative world space. <see cref="Toward"/> points from the object to the eye.</summary>
    public Vector3 Right;

    public Vector3 Up;
    public Vector3 Toward;

    /// <summary>The distance from the eye to the center of the bounding sphere.</summary>
    public float Distance;

    /// <summary>The bounding radius.</summary>
    public float Radius;

    /// <summary>The tangent of the capture frustum's half angle: the sphere's tangent cone plus the margin.</summary>
    public float TanHalfAngle;

    /// <summary>Which way is up for the object's hemisphere ambient light, as the capture bakes it.</summary>
    public HemisphereUp Hemisphere;
}

/// <summary>An object drawn as an impostor this frame.</summary>
internal struct ImpostorDraw
{
    /// <summary>The camera-relative center of the bounding sphere.</summary>
    public Vector3 Center;

    /// <summary>
    /// The billboard's axes in camera-relative world space: <see cref="Right"/> and <see cref="Up"/> span half the
    /// captured square in the plane of the center, and <see cref="Toward"/> reaches from the center to the eye the
    /// capture was taken from, as the object has turned and scaled since.
    /// </summary>
    public Vector3 Right;

    public Vector3 Up;
    public Vector3 Toward;

    /// <summary>The atlas tile (layer) that holds the capture.</summary>
    public int Tile;

    /// <summary>The depth ranges to draw it in, as a bit mask over <see cref="DepthRanges"/>.</summary>
    public int Ranges;
}

/// <summary>What the manager knows about one key. A test and tooling view.</summary>
/// <param name="Impostor">The hysteresis state: drawn as an impostor once a capture exists.</param>
/// <param name="Tile">The atlas tile holding the key's capture, or -1.</param>
/// <param name="CapturedFrame">The manager frame of the last capture, or -1.</param>
/// <param name="ProjectedHeight">The projected height in pixels when the key was last seen.</param>
internal readonly record struct ImpostorStatus(bool Impostor, int Tile, long CapturedFrame, float ProjectedHeight);

/// <summary>
/// Decides which objects are drawn as impostors and which atlas captures happen this frame (ARCHITECTURE.md,
/// Impostors). It runs on the render thread as an <see cref="IInstanceProcessor"/>, after culling and before
/// batching, and allocates nothing in a steady state.
/// </summary>
/// <remarks>
/// <para>Projected height h: the diameter in pixels of the bounding sphere's image at the center of the screen,
/// H · tan(asin(r / d)) / tan(fov / 2), where H is the output height and d the distance to the eye. It does not
/// change when the camera only turns. An eye inside the sphere gives an infinite height.</para>
/// <para>Hysteresis, per key (entity index, part, mesh and resolved material; see <see cref="ImpostorKey"/>): an
/// object becomes an impostor when h drops below <see cref="EnterHeight"/> and returns to geometry when h rises
/// above <see cref="ExitHeight"/>.
/// <see cref="RenderFlags.NoImpostor"/>, <see cref="RenderFlags.Celestial"/> and <see cref="RenderFlags.Transparent"/>
/// objects never become impostors. If two visible instances share a key in one frame (the snapshot repeats a part),
/// only the first can be an impostor; the others draw as geometry.</para>
/// <para>Captures: an impostor needs a capture in an atlas tile; until it has one it keeps drawing as geometry. A
/// capture is a perspective view from the eye, fitted to the bounding sphere's tangent cone (see
/// <see cref="ImpostorPass"/>). It records the view direction (from the object to the eye), the sun direction, and
/// the capture's own axes, all in object space, so the object's rotation counts, and r / d, the bounding radius over
/// the eye's distance. Its error later is the largest of: the angle between the current and captured view directions; the same
/// for the sun direction; the change of r / d, the parallax between the sphere's front and back, which moves the
/// silhouette by about (h / 2) · Δ(r / d) pixels as a turn of Δθ moves it by (h / 2) · Δθ; the relative change of the
/// light that reaches the object, against the total (sun radiance times the analytic eclipse visibility at its
/// center, the ambient colors, and the irradiance of the point and spot lights at its center with its direction in
/// object space); and how far the scale has become non-uniform relative to the captured scale.
/// A capture with error × h above <see cref="RecaptureError"/> (the 2 / h rule, about one pixel) is a recapture
/// candidate with priority error × h.
/// A first capture has priority π × h, the error of the worst possible view. Each frame the
/// <see cref="CapturesPerFrame"/> candidates of highest priority are captured, from two binary heaps (recaptures
/// and first captures) merged by priority. A first capture needs a tile: a free one, or else the least recently
/// used tile whose key was not seen this frame or is back to geometry. Without one, first captures wait.</para>
/// <para>A capture made this frame is drawn this frame: <see cref="ImpostorPass"/> renders captures before the
/// G-buffer pass. An impostor that was drawn last frame and waits for a recapture keeps drawing with its previous
/// capture. A key that holds a stale capture but was not drawn last frame (it was geometry, or out of view) draws
/// as geometry until it is recaptured, with the priority of a first capture.</para>
/// <para>States of keys that were not seen for <see cref="StateLifetime"/> frames are dropped, unless they hold a
/// tile; those live until the tile is evicted, so that an object coming back into view can reuse its capture.</para>
/// </remarks>
internal sealed class ImpostorManager : IInstanceProcessor
{
    /// <summary>Below this projected height, in pixels, an object becomes an impostor.</summary>
    public const float EnterHeight = 200f;

    /// <summary>Above this projected height, in pixels, an impostor returns to geometry.</summary>
    public const float ExitHeight = 300f;

    /// <summary>The capture budget per frame.</summary>
    public const int CapturesPerFrame = 4;

    /// <summary>A capture is stale when its error (radians, or a relative change) times h exceeds this.</summary>
    public const float RecaptureError = 2f;

    /// <summary>The side of one atlas tile in texels.</summary>
    public const int TileSize = 512;

    /// <summary>
    /// The capture frustum's half-angle tangent over the tangent cone's: two texels of margin on every side, so that
    /// the silhouette never touches the tile's edge.
    /// </summary>
    public const float MarginScale = TileSize / (TileSize - 4f);

    /// <summary>
    /// An eye closer than this many bounding radii to the center never sees an impostor: the capture frustum's near
    /// plane would cut into the sphere.
    /// </summary>
    public const float MinimumDistance = 1.05f;

    /// <summary>Frames a key without a tile is remembered after it was last seen.</summary>
    public const int StateLifetime = 120;

    private const RenderFlags Excluded = RenderFlags.NoImpostor | RenderFlags.Celestial | RenderFlags.Transparent;

    private readonly Dictionary<ImpostorKey, int> lookup = [];
    private readonly int[] tileOwner;
    private readonly long[] tileUsed;
    private readonly CandidateHeap recaptures = new();
    private readonly CandidateHeap firstCaptures = new();
    private readonly ImpostorCapture[] captures = new ImpostorCapture[CapturesPerFrame];
    private State[] states = new State[64];
    private int stateSlots;
    private int[] freeStates = new int[16];
    private int freeCount;
    private int[] decisions = new int[256];
    private ImpostorDraw[] draws = new ImpostorDraw[64];
    private int drawCount;
    private int captureCount;

    /// <param name="tiles">The number of atlas tiles. At least 1.</param>
    public ImpostorManager(int tiles)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tiles, 1);
        tileOwner = new int[tiles];
        tileUsed = new long[tiles];
        Array.Fill(tileOwner, -1);
        Array.Fill(tileUsed, -1);
    }

    /// <summary>Counts <see cref="Process"/> calls. Captures and tile use are stamped with it.</summary>
    public long Frame { get; private set; }

    /// <summary>Atlas tiles.</summary>
    public int TileCount => tileOwner.Length;

    /// <summary>Tiles that hold a capture.</summary>
    public int TilesInUse { get; private set; }

    /// <summary>
    /// Objects that wanted to be impostors this frame but drew as geometry: they wait for a first capture, or for the
    /// recapture of a stale one after a spell as geometry or out of view.
    /// </summary>
    public int Waiting { get; private set; }

    /// <summary>The captures to render this frame, in priority order.</summary>
    public ReadOnlySpan<ImpostorCapture> Captures => captures.AsSpan(0, captureCount);

    /// <summary>The objects drawn as impostors this frame.</summary>
    public ReadOnlySpan<ImpostorDraw> Draws => draws.AsSpan(0, drawCount);

    /// <summary>The key whose capture a tile holds, if any.</summary>
    public ImpostorKey? TileOwner(int tile)
    {
        int owner = tileOwner[tile];
        return owner >= 0 ? states[owner].Key : null;
    }

    /// <summary>The state of a key, if the manager remembers it.</summary>
    public bool TryGetStatus(ImpostorKey key, out ImpostorStatus status)
    {
        if (!lookup.TryGetValue(key, out int slot))
        {
            status = default;
            return false;
        }

        ref State state = ref states[slot];
        status = new(state.Impostor, state.Tile, state.CapturedFrame, state.Height);
        return true;
    }

    /// <summary>
    /// The projected height in pixels of a bounding sphere of <paramref name="radius"/> at <paramref name="distance"/>
    /// from the eye: the diameter of its image at the center of the screen. Infinite when the eye is inside it.
    /// </summary>
    public static float ProjectedHeight(float distance, float radius, in CameraView camera)
    {
        if (distance <= radius)
            return float.PositiveInfinity;
        float tangent = radius / MathF.Sqrt((distance - radius) * (distance + radius));
        return camera.Height * tangent / camera.TanHalfY;
    }

    public void Process(PreparedScene scene)
    {
        Frame++;
        captureCount = 0;
        drawCount = 0;
        recaptures.Clear();
        firstCaptures.Clear();
        ExpireStates();

        CameraView camera = scene.Camera;
        var light = new SceneLight(scene);
        Span<PreparedInstance> visible = scene.VisibleSpan;
        if (decisions.Length < visible.Length)
            decisions = new int[Math.Max(visible.Length, decisions.Length * 2)];

        for (int i = 0; i < visible.Length; i++)
        {
            decisions[i] = -1;
            ref PreparedInstance instance = ref visible[i];
            if ((instance.Flags & Excluded) != 0 || !(instance.Radius > 0f) ||
                instance.Distance < MinimumDistance * instance.Radius)
                continue;
            float height = ProjectedHeight(instance.Distance, instance.Radius, camera);
            ImpostorKey key = ImpostorKey.Of(instance);
            ref int slot = ref CollectionsMarshal.GetValueRefOrAddDefault(lookup, key, out bool exists);
            if (!exists)
                slot = AllocateState(key);
            ref State state = ref states[slot];
            if (state.LastSeen == Frame)
                continue;
            state.LastSeen = Frame;
            state.Height = height;
            if (state.Impostor ? height > ExitHeight : height < EnterHeight)
                state.Impostor = !state.Impostor;
            if (!state.Impostor)
                continue;

            decisions[i] = slot;
            if (state.Tile >= 0)
            {
                float error = CaptureError(state, instance, light) * height;
                bool current = error <= RecaptureError;
                // A stale capture is drawn only by an impostor that was drawn last frame, until its recapture. An
                // object that just became an impostor again, or came back into view, waits as geometry instead.
                state.Drawable = current || state.DrawnFrame == Frame - 1;
                if (!current)
                    recaptures.Push(state.Drawable ? error : MathF.PI * height, i);
            }
            else
            {
                state.Drawable = false;
                firstCaptures.Push(MathF.PI * height, i);
            }
        }

        SelectCaptures(visible, light, camera);
        EmitDraws(scene, visible);
    }

    /// <summary>Takes the highest-priority candidates, up to the budget, and records their captures.</summary>
    private void SelectCaptures(ReadOnlySpan<PreparedInstance> visible, in SceneLight light, in CameraView camera)
    {
        int available = AvailableTiles();
        recaptures.Build();
        firstCaptures.Build();
        while (captureCount < CapturesPerFrame)
        {
            bool recapture = recaptures.Count > 0;
            bool first = firstCaptures.Count > 0 && available > 0;
            if (!recapture && !first)
                break;
            bool takeFirst = first && (!recapture || firstCaptures.Top.Priority > recaptures.Top.Priority);
            int index = takeFirst ? firstCaptures.Pop() : recaptures.Pop();
            int slot = decisions[index];
            if (takeFirst)
            {
                AcquireTile(slot);
                available--;
            }

            RecordCapture(slot, visible[index], light, camera);
        }
    }

    /// <summary>Moves every object with a capture from the visible list to the impostor draws.</summary>
    private void EmitDraws(PreparedScene scene, Span<PreparedInstance> visible)
    {
        int kept = 0, waiting = 0;
        for (int i = 0; i < visible.Length; i++)
        {
            int slot = decisions[i];
            if (slot >= 0 && states[slot].Drawable)
            {
                AddDraw(ref states[slot], visible[i]);
                continue;
            }

            if (slot >= 0)
                waiting++;
            if (kept != i)
                visible[kept] = visible[i];
            kept++;
        }

        scene.VisibleCount = kept;
        Waiting = waiting;
    }

    private void AddDraw(ref State state, in PreparedInstance instance)
    {
        if (drawCount == draws.Length)
            Array.Resize(ref draws, draws.Length * 2);
        Quaternion rotation = instance.Rotation;
        // The capture follows the object's scale, not its bounding radius, which may be given independently.
        float scale = UniformScale(instance.Scale, state.Scale);
        float extent = state.Extent * scale;
        int containing = DepthRanges.Containing(instance.Depth);
        draws[drawCount++] = new()
        {
            Center = instance.Position,
            Right = Vector3.Transform(state.Right, rotation) * extent,
            Up = Vector3.Transform(state.Up, rotation) * extent,
            Toward = Vector3.Transform(state.View, rotation) * (state.EyeDistance * scale),
            Tile = state.Tile,
            // The range of the center and any nearer range the sphere reaches: the billboard lies in the plane of
            // the center, so a farther range would clip it at its near plane.
            Ranges = DepthRanges.OpaqueMask(instance.Depth, instance.Radius) & ~((1 << containing) - 1)
        };
        tileUsed[state.Tile] = Frame;
        state.DrawnFrame = Frame;
    }

    private void RecordCapture(int slot, in PreparedInstance instance, in SceneLight light, in CameraView camera)
    {
        ref State state = ref states[slot];
        Vector3 toward = -instance.Position / instance.Distance;
        // Upright on screen: the camera's up axis made perpendicular to the view direction.
        Vector3 right = Vector3.Cross(camera.Up, toward);
        if (right.LengthSquared() < 1e-8f)
            right = Vector3.Cross(camera.Forward, toward);
        right = Vector3.Normalize(right);
        Vector3 up = Vector3.Cross(toward, right);

        Quaternion inverse = Quaternion.Conjugate(instance.Rotation);
        state.View = Vector3.Transform(toward, inverse);
        state.Right = Vector3.Transform(right, inverse);
        state.Up = Vector3.Transform(up, inverse);
        state.Sun = light.HasSun ? Vector3.Transform(light.SunDirection, inverse) : Vector3.Zero;
        state.Scale = instance.Scale;
        float distance = instance.Distance, radius = instance.Radius;
        float tanHalfAngle = radius / MathF.Sqrt((distance - radius) * (distance + radius)) * MarginScale;
        state.EyeDistance = distance;
        state.Extent = distance * tanHalfAngle;
        state.Parallax = radius / distance;
        state.Light = light.At(instance);
        state.CapturedFrame = Frame;
        state.Drawable = true;
        tileUsed[state.Tile] = Frame;

        captures[captureCount++] = new()
        {
            Tile = state.Tile,
            Mesh = instance.Mesh,
            Material = instance.Material,
            Position = instance.Position,
            Rotation = instance.Rotation,
            Scale = instance.Scale,
            Right = right,
            Up = up,
            Toward = toward,
            Distance = distance,
            Radius = radius,
            TanHalfAngle = tanHalfAngle,
            Hemisphere = instance.Hemisphere
        };
    }

    /// <summary>How far a capture is from what the object would look like now: an angle in radians, or a relative change.</summary>
    private static float CaptureError(in State state, in PreparedInstance instance, in SceneLight light)
    {
        Quaternion inverse = Quaternion.Conjugate(instance.Rotation);
        Vector3 toward = Vector3.Transform(-instance.Position / instance.Distance, inverse);
        float error = Angle(toward, state.View);
        error = MathF.Max(error, MathF.Abs(instance.Radius / instance.Distance - state.Parallax));
        if (light.HasSun)
            error = MathF.Max(error, Angle(Vector3.Transform(light.SunDirection, inverse), state.Sun));
        error = MathF.Max(error, LightSignature.Change(state.Light, light.At(instance)));
        return MathF.Max(error, 1f - 1f / NonUniformity(instance.Scale, state.Scale));
    }

    /// <summary>The angle between two directions; precise for small angles, unlike acos of the dot product.</summary>
    internal static float Angle(Vector3 a, Vector3 b) => MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b));

    /// <summary>The largest over the smallest per-axis ratio of two scales: 1 when one is a uniform multiple of the other.</summary>
    private static float NonUniformity(Vector3 scale, Vector3 captured)
    {
        Vector3 ratio = Vector3.Abs(scale) / Vector3.Abs(captured);
        float max = MathF.Max(ratio.X, MathF.Max(ratio.Y, ratio.Z));
        float min = MathF.Min(ratio.X, MathF.Min(ratio.Y, ratio.Z));
        return min > 0f ? max / min : float.PositiveInfinity;
    }

    /// <summary>The mean per-axis ratio of the current scale to the captured one.</summary>
    private static float UniformScale(Vector3 scale, Vector3 captured)
    {
        Vector3 ratio = Vector3.Abs(scale) / Vector3.Abs(captured);
        return (ratio.X + ratio.Y + ratio.Z) / 3f;
    }

    private int AvailableTiles()
    {
        int available = 0;
        for (int tile = 0; tile < tileOwner.Length; tile++)
        {
            int owner = tileOwner[tile];
            if (owner < 0 || Evictable(owner))
                available++;
        }

        return available;
    }

    /// <summary>A tile may be taken from a key that was not seen this frame or is back to geometry.</summary>
    private bool Evictable(int owner) => states[owner].LastSeen != Frame || !states[owner].Impostor;

    /// <summary>Gives the state a free tile, or else evicts the least recently used evictable one.</summary>
    private void AcquireTile(int slot)
    {
        int chosen = -1;
        for (int tile = 0; tile < tileOwner.Length; tile++)
            if (tileOwner[tile] < 0)
            {
                chosen = tile;
                break;
            }

        if (chosen < 0)
        {
            for (int tile = 0; tile < tileOwner.Length; tile++)
                if (Evictable(tileOwner[tile]) && (chosen < 0 || tileUsed[tile] < tileUsed[chosen]))
                    chosen = tile;
            if (chosen < 0)
                throw new InvalidOperationException("No atlas tile is available; the caller counts them first.");
            states[tileOwner[chosen]].Tile = -1;
            TilesInUse--;
        }

        tileOwner[chosen] = slot;
        tileUsed[chosen] = Frame;
        states[slot].Tile = chosen;
        TilesInUse++;
    }

    private int AllocateState(ImpostorKey key)
    {
        int slot;
        if (freeCount > 0)
        {
            slot = freeStates[--freeCount];
        }
        else
        {
            slot = stateSlots++;
            if (slot == states.Length)
                Array.Resize(ref states, states.Length * 2);
        }

        states[slot] = new() { Key = key, Live = true, Tile = -1, LastSeen = -1, CapturedFrame = -1, DrawnFrame = -1 };
        return slot;
    }

    /// <summary>Forgets keys that have not been seen for <see cref="StateLifetime"/> frames and hold no tile.</summary>
    private void ExpireStates()
    {
        for (int slot = 0; slot < stateSlots; slot++)
        {
            ref State state = ref states[slot];
            if (!state.Live || state.Tile >= 0 || Frame - state.LastSeen <= StateLifetime)
                continue;
            lookup.Remove(state.Key);
            state = default;
            if (freeCount == freeStates.Length)
                Array.Resize(ref freeStates, freeStates.Length * 2);
            freeStates[freeCount++] = slot;
        }
    }

    private struct State
    {
        public ImpostorKey Key;
        public bool Live;

        /// <summary>The hysteresis state.</summary>
        public bool Impostor;

        /// <summary>The atlas tile with this key's capture, or -1. A tile always holds a capture.</summary>
        public int Tile;

        /// <summary>This frame: the capture may be drawn (it is current, or it is an impostor's that waits for a recapture).</summary>
        public bool Drawable;

        public long LastSeen;

        /// <summary>The last frame the key was drawn as an impostor.</summary>
        public long DrawnFrame;

        public float Height;

        public long CapturedFrame;

        // The capture, in object space: the direction to the eye, the sun direction, and the capture's own axes.
        public Vector3 View;
        public Vector3 Sun;
        public Vector3 Right;
        public Vector3 Up;
        public Vector3 Scale;

        /// <summary>The eye's distance from the center at capture, in meters, at <see cref="Scale"/>.</summary>
        public float EyeDistance;

        /// <summary>Half the side of the captured square in the plane of the center, in meters, at <see cref="Scale"/>.</summary>
        public float Extent;

        /// <summary>r / d at capture: how much perspective the capture has.</summary>
        public float Parallax;

        public LightSignature Light;
    }

    /// <summary>
    /// The light a capture bakes in, at the object: what reaches it from the sun, the two ambient colors, the up of the
    /// hemisphere ambient light at its center in object space, weighted by how much the sky and the ground differ, and
    /// the irradiance from the point and spot lights, with that irradiance's direction in object space.
    /// </summary>
    private readonly record struct LightSignature(
        Vector3 Sun,
        Vector3 Sky,
        Vector3 Ground,
        Vector3 Up,
        Vector3 Local,
        Vector3 LocalDirection)
    {
        private float Total => Sun.Length() + Sky.Length() + Ground.Length() + Local.Length();

        /// <summary>The change from <paramref name="a"/> to <paramref name="b"/> as a fraction of the larger total light.</summary>
        public static float Change(in LightSignature a, in LightSignature b)
        {
            float difference = (a.Sun - b.Sun).Length() + (a.Sky - b.Sky).Length() + (a.Ground - b.Ground).Length() +
                               (a.Up - b.Up).Length() +
                               (a.Local - b.Local).Length() + (a.LocalDirection - b.LocalDirection).Length();
            return difference / MathF.Max(MathF.Max(a.Total, b.Total), 1e-6f);
        }
    }

    /// <summary>The frame's sun, ambient light, occluders and local lights, as the lighting shader sees them.</summary>
    private readonly ref struct SceneLight
    {
        private readonly ReadOnlySpan<Vector4> occluders;
        private readonly ReadOnlySpan<GpuLight> lights;
        private readonly Vector3 sunRadiance;
        private readonly Vector3 sky;
        private readonly Vector3 ground;
        private readonly float sunAngularRadius;

        public SceneLight(PreparedScene scene)
        {
            RenderSun sun = scene.Sun;
            HasSun = SceneBatcher.HasSun(sun);
            SunDirection = HasSun ? Vector3.Normalize(sun.Direction) : Vector3.Zero;
            sunRadiance = HasSun ? sun.Color * sun.Intensity : Vector3.Zero;
            sunAngularRadius = MathF.Max(sun.AngularRadius, 0f);
            sky = scene.Ambient.SkyColor * scene.Ambient.Intensity;
            ground = scene.Ambient.GroundColor * scene.Ambient.Intensity;
            occluders = scene.Occluders.AsSpan(0, scene.OccluderCount);
            lights = scene.Lights.AsSpan(0, scene.LightCount);
        }

        public bool HasSun { get; }
        public Vector3 SunDirection { get; }

        public LightSignature At(in PreparedInstance instance)
        {
            Vector3 position = instance.Position;
            Vector3 sun = HasSun
                ? sunRadiance * Eclipse.SunVisibility(position, SunDirection, sunAngularRadius, occluders)
                : Vector3.Zero;
            Vector3 local = Vector3.Zero, direction = Vector3.Zero;
            Quaternion inverse = Quaternion.Conjugate(instance.Rotation);
            HemisphereUp hemisphere = instance.Hemisphere;
            // A center inside the object, such as a planet's own, gives every side its own up, so there is no one up to
            // compare.
            Vector3 up = hemisphere switch
            {
                UpAlong along => along.Direction,
                UpAwayFrom away when away.Center.Length() > instance.Radius => Vector3.Normalize(-away.Center),
                UpAwayFrom or null => Vector3.Zero
            };
            Vector3 upInObject = Vector3.Transform(up, inverse) * (sky - ground).Length();
            if (!lights.IsEmpty)
            {
                float radius = instance.Radius;
                foreach (ref readonly GpuLight light in lights)
                {
                    // The lighting shader's range window and spot cone, at the center, with the window measured to
                    // the nearest point of the bounding sphere so that a light grazing the object counts.
                    Vector3 toLight = new Vector3(light.PositionRange.X, light.PositionRange.Y, light.PositionRange.Z) -
                                      position;
                    float distance = toLight.Length();
                    float range = light.PositionRange.W;
                    float nearest = MathF.Max(distance - radius, 0f);
                    if (nearest >= range || distance < 1e-6f)
                        continue;
                    float ratio = nearest * nearest / (range * range);
                    float window = Math.Clamp(1f - ratio * ratio, 0f, 1f);
                    float attenuation = window * window / MathF.Max(distance * distance, radius * radius);
                    Vector3 unit = toLight / distance;
                    if (light.RadianceKind.W > 0.5f)
                    {
                        float cosAngle = Vector3.Dot(-unit,
                            new(light.DirectionCosOuter.X, light.DirectionCosOuter.Y, light.DirectionCosOuter.Z));
                        attenuation *= SmoothStep(light.DirectionCosOuter.W, light.Spot.X, cosAngle);
                    }

                    Vector3 irradiance = new Vector3(light.RadianceKind.X, light.RadianceKind.Y, light.RadianceKind.Z) *
                                         attenuation;
                    local += irradiance;
                    direction += Vector3.Transform(unit, inverse) * irradiance.Length();
                }
            }

            return new(sun, sky, ground, upInObject, local, direction);
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }

    private readonly record struct Candidate(float Priority, int Index);

    /// <summary>A binary max-heap of capture candidates, built in place. Equal priorities pop in visible order.</summary>
    private sealed class CandidateHeap
    {
        private Candidate[] items = new Candidate[64];

        public int Count { get; private set; }
        public Candidate Top => items[0];

        public void Clear() => Count = 0;

        public void Push(float priority, int index)
        {
            if (Count == items.Length)
                Array.Resize(ref items, items.Length * 2);
            items[Count++] = new(priority, index);
        }

        /// <summary>Turns the pushed items into a heap in linear time.</summary>
        public void Build()
        {
            for (int i = Count / 2 - 1; i >= 0; i--)
                SiftDown(i);
        }

        public int Pop()
        {
            int index = items[0].Index;
            items[0] = items[--Count];
            if (Count > 0)
                SiftDown(0);
            return index;
        }

        private static bool Before(in Candidate a, in Candidate b) =>
            a.Priority > b.Priority || (a.Priority == b.Priority && a.Index < b.Index);

        private void SiftDown(int i)
        {
            while (true)
            {
                int best = i, left = 2 * i + 1, right = left + 1;
                if (left < Count && Before(items[left], items[best]))
                    best = left;
                if (right < Count && Before(items[right], items[best]))
                    best = right;
                if (best == i)
                    return;
                (items[i], items[best]) = (items[best], items[i]);
                i = best;
            }
        }
    }
}

/// <summary>
/// The analytic eclipse test of Shaders/lighting.glsl on the CPU, so that impostor captures notice when an occluder
/// starts or stops shading the object.
/// </summary>
internal static class Eclipse
{
    /// <summary>
    /// The visible fraction of the sun's disc at a camera-relative position, given far shadows: xyz the center, w the radius
    /// of a caster's bounding sphere. A sphere shadows only points outside it, so a caster never shadows its own surface;
    /// a planet's ground faces away from the sun at night anyway. Impostor captures get no shadow-map shadows, so every
    /// sphere counts here, also those of casters that the maps draw.
    /// </summary>
    public static float SunVisibility(Vector3 position, Vector3 sunDirection, float sunAngularRadius,
        ReadOnlySpan<Vector4> occluders)
    {
        float sunRadius = MathF.Max(sunAngularRadius, 1e-5f);
        float visibility = 1f;
        foreach (Vector4 occluder in occluders)
        {
            Vector3 toCenter = new Vector3(occluder.X, occluder.Y, occluder.Z) - position;
            float distance = toCenter.Length();
            if (distance <= occluder.W)
                continue;
            float occluderRadius = MathF.Asin(occluder.W / distance);
            float separation = MathF.Atan2(Vector3.Cross(toCenter, sunDirection).Length(),
                Vector3.Dot(toCenter, sunDirection));
            visibility *= 1f - DiscCoverage(sunRadius, occluderRadius, separation);
        }

        return visibility;
    }

    /// <summary>The fraction of the sun's disc hidden by an occluder's disc whose center is <paramref name="separation"/>
    /// radians away.</summary>
    public static float DiscCoverage(float sunRadius, float occluderRadius, float separation)
    {
        if (separation >= sunRadius + occluderRadius)
            return 0f;
        if (occluderRadius > 4f * sunRadius)
        {
            float x = Math.Clamp((separation - occluderRadius) / sunRadius, -1f, 1f);
            return (MathF.Acos(x) - x * MathF.Sqrt(1f - x * x)) / MathF.PI;
        }

        if (separation <= MathF.Abs(occluderRadius - sunRadius))
            return occluderRadius >= sunRadius ? 1f : occluderRadius * occluderRadius / (sunRadius * sunRadius);
        float d = separation, r1 = sunRadius, r2 = occluderRadius;
        float d2 = d * d, r1s = r1 * r1, r2s = r2 * r2;
        float a1 = r1s * MathF.Acos(Math.Clamp((d2 + r1s - r2s) / (2f * d * r1), -1f, 1f));
        float a2 = r2s * MathF.Acos(Math.Clamp((d2 + r2s - r1s) / (2f * d * r2), -1f, 1f));
        float k = 0.5f * MathF.Sqrt(MathF.Max((-d + r1 + r2) * (d + r1 - r2) * (d - r1 + r2) * (d + r1 + r2), 0f));
        return Math.Clamp((a1 + a2 - k) / (MathF.PI * r1s), 0f, 1f);
    }
}
