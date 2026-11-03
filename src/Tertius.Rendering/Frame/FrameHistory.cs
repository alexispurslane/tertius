using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The identity of an instance from frame to frame: its entity's slot and generation, and its render part
/// (docs/plans/temporal-renderer.md, "The key of an object").
/// </summary>
internal readonly record struct PlacementKey(uint Slot, uint Generation, ushort Part)
{
    /// <summary>
    /// The key's place in the order of the snapshot's instances: by slot, then by part. The generation takes no part,
    /// because one slot holds one entity at a time.
    /// </summary>
    public ulong Order => ((ulong)Slot << 16) | Part;
}

/// <summary>
/// Where a drawn frame wrote an instance's deformed copy: its first vertex in the kept positions. The copy's vertex count
/// is its static mesh's, which <see cref="KeptPlacement.Mesh"/> names.
/// </summary>
internal readonly record struct KeptCopy(int FirstVertex);

/// <summary>
/// One placed instance of a drawn frame, as the placement history keeps it (docs/plans/temporal-renderer.md, "The
/// placement history").
/// </summary>
/// <param name="Row0">The first row of the 3 × 4 model matrix that the frame drew, relative to that frame's eye.</param>
/// <param name="Mesh">The static mesh of the instance's frame mesh.</param>
/// <param name="Copy">Where the frame kept the instance's deformed copy, or null when it drew the static mesh.</param>
internal readonly record struct KeptPlacement(
    PlacementKey Key,
    Vector4 Row0,
    Vector4 Row1,
    Vector4 Row2,
    int Mesh,
    KeptCopy? Copy
);

/// <summary>
/// The identity of a light from frame to frame: its entity's slot and generation (docs/plans/temporal-renderer.md, "The
/// reservoirs"). An entity carries at most one light.
/// </summary>
internal readonly record struct LightKey(uint Slot, uint Generation);

/// <summary>A light of a drawn frame, as the history keeps it: its key and its index in that frame's light list.</summary>
internal readonly record struct KeptLight(LightKey Key, int Index);

/// <summary>
/// Which headless draws are stills, and how many warm-up draws a still takes (docs/plans/temporal-renderer.md, decision 32):
/// the input of <see cref="FrameHistory"/>'s still rule.
/// </summary>
/// <param name="Headless">Whether the renderer draws headless: a window never draws a still.</param>
/// <param name="WarmUpDraws">The warm-up draws before a still's shown draw.</param>
internal readonly record struct StillRule(bool Headless, int WarmUpDraws)
{
    /// <summary>A window's rule: no draw is a still.</summary>
    public static StillRule Window => new(Headless: false, WarmUpDraws: 0);

    /// <summary>
    /// The rule of a renderer of <paramref name="options"/>: <see cref="RendererOptions.WarmUpFrames"/> where some history
    /// holds more than one frame, else 0, and at least 1 under <see cref="AutoExposure"/>, so that the exposure has a draw to
    /// adapt in (section 10, "The still rule").
    /// </summary>
    public static StillRule For(RendererOptions options, bool headless)
    {
        int warmUp = SignalParameters.Direct(options).Cap > 1 ? options.WarmUpFrames : 0;
        if (options.Exposure is AutoExposure)
            warmUp = Math.Max(warmUp, 1);
        return new(Headless: headless, WarmUpDraws: warmUp);
    }
}

/// <summary>The camera of a drawn frame, as the batch interpolated it, which the next frame's motion vectors need.</summary>
/// <param name="EyeInFrame">The eye's position in the camera's semantic frame, in m: the render origin plus the eye's offset.</param>
/// <param name="FrameId">The id of the camera's frame, <see cref="RenderSnapshot.OriginFrameId"/>.</param>
/// <param name="Rotation">The camera's axes in the axes of its frame.</param>
/// <param name="TanHalfX">tan of the horizontal half field of view, with no jitter.</param>
/// <param name="TanHalfY">tan of the vertical half field of view, with no jitter.</param>
/// <param name="RootRotation">The axes of the root of the camera's frame tree in the axes of the camera's frame.</param>
internal readonly record struct DrawnCamera(
    Double3 EyeInFrame,
    long FrameId,
    Quaternion Rotation,
    float TanHalfX,
    float TanHalfY,
    Quaternion RootRotation
);

/// <summary>The last drawn frame's camera relative to this draw's eye, which the frame constants hold.</summary>
/// <param name="Eye">Where the last frame's eye was, relative to this draw's eye, in m, in the axes of the camera's frame.</param>
/// <param name="RenderSize">The last frame's render size, the size of every history that it wrote.</param>
internal readonly record struct PreviousView(
    Vector3 Eye,
    Quaternion Rotation,
    float TanHalfX,
    float TanHalfY,
    Quaternion RootRotation,
    Extent2D RenderSize
);

/// <summary>A drawn frame, as the history records it.</summary>
/// <param name="LightCount">The count of the frame's light list, which the remap of the next frame covers.</param>
internal readonly record struct DrawnFrame(
    DrawnCamera Camera,
    Extent2D RenderSize,
    Extent2D OutputSize,
    long CameraCuts,
    int LightCount
);

/// <summary>
/// The record of the last drawn frame, and the rules that relate each draw to it (docs/plans/temporal-renderer.md, "The
/// frame history"). DrawsOf gives the draws of a snapshot at a clock step: 1, or for a headless still also its warm-up
/// draws. Begin derives one draw's pending state: whether it resets every history, its draw index since the last reset,
/// its exposure step, whether it refreshes every face of the map, the parity of the images that alternate, and the record
/// relative to this draw's eye. The batch writes the draw's camera, placements and light keys into the pending state.
/// Drew commits it after the submission. A draw that throws before Drew leaves the record, the count of drawn frames and
/// so every parity as they were. No other owner keeps a copy of these values.
/// </summary>
/// <remarks>
/// Invariants, checked by <see cref="AssertInvariants"/>:
/// <list type="bullet">
/// <item>A record exists exactly when at least one frame was drawn.</item>
/// <item>The draw index of the last drawn frame is below the count of drawn frames.</item>
/// <item>The keys of the placement history, and of the pending placements, rise strictly.</item>
/// <item>The keys of the kept lights, and of the pending lights, rise strictly by slot.</item>
/// <item>Every kept light's index lies below the light count of the drawn frame.</item>
/// </list>
/// </remarks>
/// <example>
/// One draw, as the renderer runs it:
/// <code>
/// for (int draw = 0; draw &lt; history.DrawsOf(step); draw++)
/// PendingFrame pending = history.Begin(snapshot, step, draw, outputSize, renderSize);
/// batcher.Prepare(..., frame: pending, ...);   // writes the camera, the placements and the light keys
/// graph.Record(context);                       // the passes read pending.Reset and pending.Parity
/// output.SubmitAndPresent(frame);
/// history.Drew(pending);
/// </code>
/// </example>
internal sealed class FrameHistory
{
    /// <summary>
    /// The shortest clock step, in s, after which a headless draw is a still (docs/plans/auto-exposure.md, "A headless still
    /// after a long step"). It is 5.4 ticks at 60 Hz, so that no step of whole ticks lies on it.
    /// </summary>
    public const float MeteringStepSeconds = 0.09f;

    private readonly StillRule rule;
    private readonly PendingFrame pending;
    private List<KeptPlacement> drawnPlacements = [];
    private List<KeptLight> drawnLights = [];
    private DrawnFrame? drawn;
    private long lastDrawIndex;

    // The pending state of the draw that the last Begin armed, which only Begin, Drew and the pending frame's own writes
    // change.
    private Derivation derived;
    private List<KeptPlacement> pendingPlacements = [];
    private List<KeptLight> pendingLights = [];
    private int pendingLightCount;
    private DrawnCamera? pendingCamera;
    private bool armed;

    public FrameHistory(StillRule rule)
    {
        Contract.Requires(rule.WarmUpDraws >= 0);
        this.rule = rule;
        pending = new(this);
    }

    /// <summary>
    /// The frames drawn so far: the number of the frame that the renderer draws next. Each owner of two images that
    /// alternate takes its parity from it, through <see cref="PendingFrame.Parity"/>.
    /// </summary>
    public long DrawnFrames { get; private set; }

    /// <summary>The placement history: the last drawn frame's placed instances, keys rising.</summary>
    internal ReadOnlySpan<KeptPlacement> DrawnPlacements => CollectionsMarshal.AsSpan(drawnPlacements);

    /// <summary>
    /// The draws of a snapshot after a clock step of <paramref name="step"/> s: the warm-up draws and the shown draw of a
    /// still, or 1. A headless draw is a still when its step is 0, endless, or at least <see cref="MeteringStepSeconds"/>.
    /// </summary>
    public int DrawsOf(float step) => IsStill(step) ? rule.WarmUpDraws + 1 : 1;

    /// <summary>
    /// The one choke point of the resets and the still rule: derives the pending state of draw <paramref name="draw"/> of
    /// <paramref name="snapshot"/> after a clock step of <paramref name="step"/> s, at <paramref name="outputSize"/> and
    /// <paramref name="renderSize"/>, and changes no record. The draw resets when it is the first drawn frame, when the
    /// snapshot's count of camera cuts differs from the drawn frame's, when the output size changed, or when it is the
    /// first draw of a still. A change of the render size resets nothing.
    /// </summary>
    /// <param name="draw">The draw of the snapshot, from 0 to <see cref="DrawsOf"/> − 1. The last one is the shown draw.</param>
    /// <returns>The history's one pending state, armed for this draw. The next Begin arms it again.</returns>
    public PendingFrame Begin(RenderSnapshot snapshot, float step, int draw, Extent2D outputSize, Extent2D renderSize)
    {
        Contract.Requires(draw >= 0 && draw < DrawsOf(step));
        bool still = IsStill(step);
        bool reset =
            drawn is not { } last
            || last.CameraCuts != snapshot.CameraCuts
            || last.OutputSize.Width != outputSize.Width
            || last.OutputSize.Height != outputSize.Height
            || (still && draw == 0);
        derived = new(
            Reset: reset,
            DrawIndex: reset ? 0 : lastDrawIndex + 1,
            ExposureStep: still ? StillExposureStep(step, draw) : step,
            RefreshesEveryFace: still && draw == 0,
            DrawnFrames: DrawnFrames,
            RenderSize: renderSize,
            OutputSize: outputSize,
            CameraCuts: snapshot.CameraCuts,
            // A position in another frame's axes means nothing in this one's, so the record then serves no motion.
            Continued: drawn is { } record && record.Camera.FrameId == snapshot.OriginFrameId ? record : null
        );
        pendingCamera = null;
        pendingPlacements.Clear();
        pendingLights.Clear();
        pendingLightCount = 0;
        armed = true;
        return pending;
    }

    /// <summary>Whether a draw after a clock step of <paramref name="step"/> s is a still: the rule of decision 32.</summary>
    private bool IsStill(float step) => rule.Headless && (step == 0 || step >= MeteringStepSeconds);

    /// <summary>
    /// The exposure step of draw <paramref name="draw"/> of a still after <paramref name="step"/> s (section 10, "The still
    /// rule"): each warm-up draw adapts by its share of the step, or by an endless step after a step of 0 or an endless one,
    /// and the shown draw by none. A still without warm-up draws adapts by its step in its one draw.
    /// </summary>
    private float StillExposureStep(float step, int draw)
    {
        int warmUp = rule.WarmUpDraws;
        if (warmUp == 0)
            return step;
        if (draw == warmUp)
            return 0;
        return step == 0 || float.IsPositiveInfinity(step) ? float.PositiveInfinity : step / warmUp;
    }

    /// <summary>
    /// Commits <paramref name="frame"/>, the pending state of the last <see cref="Begin"/>, after its submission: its
    /// camera, render and output size, cut count and placements become the record, and the count of drawn frames grows by 1.
    /// </summary>
    public void Drew(PendingFrame frame)
    {
        Contract.Requires(armed && ReferenceEquals(frame, pending));
        DrawnCamera camera =
            pendingCamera ?? throw new InvalidOperationException("The batch wrote no camera into the pending frame.");
        long framesBefore = DrawnFrames;
        drawn = new(
            Camera: camera,
            RenderSize: derived.RenderSize,
            OutputSize: derived.OutputSize,
            CameraCuts: derived.CameraCuts,
            LightCount: pendingLightCount
        );
        // The lists change places, so a draw copies no placement or light and allocates none.
        (drawnPlacements, pendingPlacements) = (pendingPlacements, drawnPlacements);
        pendingPlacements.Clear();
        (drawnLights, pendingLights) = (pendingLights, drawnLights);
        pendingLights.Clear();
        lastDrawIndex = derived.DrawIndex;
        DrawnFrames++;
        armed = false;
        Contract.Ensures(DrawnFrames == framesBefore + 1);
    }

    /// <summary>Checks the invariants in the class comment. O(placements).</summary>
    internal void AssertInvariants()
    {
        Contract.Invariant(drawn.HasValue == DrawnFrames > 0);
        Contract.Invariant(DrawnFrames == 0 || lastDrawIndex < DrawnFrames);
        Contract.Invariant(KeysRise(DrawnPlacements));
        Contract.Invariant(KeysRise(CollectionsMarshal.AsSpan(pendingPlacements)));
        Contract.Invariant(KeysRise(CollectionsMarshal.AsSpan(drawnLights)));
        Contract.Invariant(KeysRise(CollectionsMarshal.AsSpan(pendingLights)));
        int lightCount = drawn?.LightCount ?? 0;
        foreach (KeptLight light in drawnLights)
            Contract.Invariant(light.Index >= 0 && light.Index < lightCount);
    }

    private static bool KeysRise(ReadOnlySpan<KeptLight> lights)
    {
        for (int i = 1; i < lights.Length; i++)
            if (lights[i - 1].Key.Slot >= lights[i].Key.Slot)
                return false;
        return true;
    }

    private static bool KeysRise(ReadOnlySpan<KeptPlacement> placements)
    {
        for (int i = 1; i < placements.Length; i++)
            if (placements[i - 1].Key.Order >= placements[i].Key.Order)
                return false;
        return true;
    }

    /// <summary>What <see cref="Begin"/> derived for the pending draw.</summary>
    /// <param name="DrawnFrames">The frames drawn before the draw, which stays the draw's own after <see cref="Drew"/>.</param>
    /// <param name="Continued">The drawn frame that the draw continues, or null where it continues none.</param>
    /// <param name="ExposureStep">The step in s by which the draw's adapt step moves the exposure.</param>
    /// <param name="RefreshesEveryFace">Whether the draw refreshes every face of the environment map.</param>
    private readonly record struct Derivation(
        bool Reset,
        long DrawIndex,
        float ExposureStep,
        bool RefreshesEveryFace,
        long DrawnFrames,
        Extent2D RenderSize,
        Extent2D OutputSize,
        long CameraCuts,
        DrawnFrame? Continued
    );

    /// <summary>
    /// One draw's pending state: a derived read of what <see cref="Begin"/> found, and the writes of the camera and the
    /// placements that <see cref="Drew"/> commits. It holds no state of its own: the history keeps it, so only Begin and
    /// Drew arm and commit it. The history has one, which each Begin arms again, so a draw allocates nothing.
    /// </summary>
    internal sealed class PendingFrame
    {
        private readonly FrameHistory history;

        internal PendingFrame(FrameHistory history)
        {
            this.history = history;
        }

        /// <summary>Whether the draw resets every history (docs/plans/temporal-renderer.md, "Resets").</summary>
        public bool Reset => history.derived.Reset;

        /// <summary>The draw's index among the drawn frames since the last reset: 0 for a draw that resets.</summary>
        public long DrawIndex => history.derived.DrawIndex;

        /// <summary>
        /// The step in s by which the draw's adapt step moves the exposure: the clock step, or for a draw of a still its share
        /// of the still rule (docs/plans/temporal-renderer.md, "The still rule").
        /// </summary>
        public float ExposureStep => history.derived.ExposureStep;

        /// <summary>Whether the draw refreshes every face of the environment map: the first draw of a still.</summary>
        public bool RefreshesEveryFace => history.derived.RefreshesEveryFace;

        /// <summary>
        /// The frames drawn before this draw: <see cref="FrameHistory.DrawnFrames"/> at its <see cref="Begin"/>, also after
        /// its <see cref="Drew"/>, so that a read of the draw's images after its commit finds the ones that it wrote.
        /// </summary>
        public long DrawnFrames => history.derived.DrawnFrames;

        /// <summary>Which of two alternating images this draw writes: the parity of <see cref="DrawnFrames"/>. It reads the other.</summary>
        public int Parity => (int)(DrawnFrames & 1);

        /// <summary>The draw's render size.</summary>
        public Extent2D RenderSize => history.derived.RenderSize;

        /// <summary>The render size of the last drawn frame that this draw continues, or this draw's own where it continues none.</summary>
        public Extent2D PreviousRenderSize => history.derived.Continued?.RenderSize ?? RenderSize;

        /// <summary>
        /// The placement history that this draw joins: the last drawn frame's placed instances, keys rising, or none when
        /// the last frame's camera was in another frame, or no frame was drawn.
        /// </summary>
        public ReadOnlySpan<KeptPlacement> PreviousPlacements =>
            history.derived.Continued.HasValue ? history.DrawnPlacements : [];

        /// <summary>
        /// The light keys that this draw joins: the last drawn frame's keyed lights, keys rising, with their indices in its
        /// light list, or none where the draw continues no frame.
        /// </summary>
        public ReadOnlySpan<KeptLight> PreviousLights =>
            history.derived.Continued.HasValue ? CollectionsMarshal.AsSpan(history.drawnLights) : [];

        /// <summary>The count of the light list of the last drawn frame that this draw continues, or 0.</summary>
        public int PreviousLightCount => history.derived.Continued?.LightCount ?? 0;

        /// <summary>
        /// The last drawn frame's camera relative to <paramref name="current"/>, this draw's camera. Where this draw
        /// continues no frame, the last camera is this one, at this eye: a point then has no motion of the camera.
        /// </summary>
        public PreviousView PreviousViewOf(in DrawnCamera current)
        {
            DrawnCamera last = history.derived.Continued?.Camera ?? current;
            return new(
                // In doubles, then narrowed: two positions far from the frame's origin differ exactly by the eye's step.
                Eye: (last.EyeInFrame - current.EyeInFrame).ToVector3(),
                Rotation: last.Rotation,
                TanHalfX: last.TanHalfX,
                TanHalfY: last.TanHalfY,
                RootRotation: last.RootRotation,
                RenderSize: PreviousRenderSize
            );
        }

        /// <summary>Writes the draw's camera, as the batch interpolated it. Once per draw.</summary>
        public void Write(in DrawnCamera drawn)
        {
            Contract.Requires(history.armed && history.pendingCamera is null);
            history.pendingCamera = drawn;
        }

        /// <summary>Appends one placed instance of the draw. The keys rise strictly, in the order of the snapshot's instances.</summary>
        public void Place(in KeptPlacement placement)
        {
            List<KeptPlacement> placements = history.pendingPlacements;
            Contract.Requires(
                history.armed && (placements.Count == 0 || placements[^1].Key.Order < placement.Key.Order)
            );
            placements.Add(placement);
        }

        /// <summary>
        /// Writes the count of the draw's light list, and appends its keyed lights. The keys rise strictly by slot, in the
        /// order of the list. Once per draw.
        /// </summary>
        public void WriteLights(int count, ReadOnlySpan<KeptLight> keyed)
        {
            Contract.Requires(history.armed && history.pendingLights.Count == 0 && KeysRise(keyed));
            history.pendingLightCount = count;
            foreach (KeptLight light in keyed)
            {
                Contract.Requires(light.Index >= 0 && light.Index < count);
                history.pendingLights.Add(light);
            }
        }
    }
}
