using System.Numerics;
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

/// <summary>Where a drawn frame wrote an instance's deformed copy: its first vertex in the kept positions, and its vertex count.</summary>
internal readonly record struct KeptCopy(int FirstVertex, int VertexCount);

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

/// <summary>
/// One draw's pending state: a derived read of what <see cref="FrameHistory.Begin"/> found, and the camera and the
/// placements that the batch writes for <see cref="FrameHistory.Drew"/> to commit. It holds no rule of its own. The history
/// keeps one and arms it again at each <see cref="FrameHistory.Begin"/>, so a draw allocates nothing.
/// </summary>
internal sealed class PendingFrame
{
    private readonly FrameHistory history;
    private DrawnFrame? continued;

    internal PendingFrame(FrameHistory history)
    {
        this.history = history;
    }

    /// <summary>Whether the draw resets every history (docs/plans/temporal-renderer.md, "Resets").</summary>
    public bool Reset { get; private set; }

    /// <summary>The draw's index among the drawn frames since the last reset: 0 for a draw that resets.</summary>
    public long DrawIndex { get; private set; }

    /// <summary>The frames drawn before this draw. Each owner of two images that alternate takes its parity from it.</summary>
    public long DrawnFrames { get; private set; }

    /// <summary>Which of two alternating images this draw writes: the parity of <see cref="DrawnFrames"/>. It reads the other.</summary>
    public int Parity => (int)(DrawnFrames & 1);

    /// <summary>The draw's render size.</summary>
    public Extent2D RenderSize { get; private set; }

    /// <summary>The draw's output size.</summary>
    internal Extent2D OutputSize { get; private set; }

    /// <summary>The snapshot's count of camera cuts, which <see cref="FrameHistory.Drew"/> commits.</summary>
    internal long CameraCuts { get; private set; }

    /// <summary>The render size of the last drawn frame that this draw continues, or this draw's own where it continues none.</summary>
    public Extent2D PreviousRenderSize => continued?.RenderSize ?? RenderSize;

    /// <summary>
    /// The placement history that this draw joins: the last drawn frame's placed instances, keys rising, or none when the
    /// last frame's camera was in another frame, or no frame was drawn.
    /// </summary>
    public ReadOnlySpan<KeptPlacement> PreviousPlacements => continued.HasValue ? history.DrawnPlacements : [];

    /// <summary>The placements that the batch wrote into this draw so far.</summary>
    internal List<KeptPlacement> Placements { get; private set; } = [];

    /// <summary>The camera that the batch wrote, or null before it did.</summary>
    internal DrawnCamera? Camera { get; private set; }

    /// <summary>
    /// The last drawn frame's camera relative to <paramref name="current"/>, this draw's camera. Where this draw continues
    /// no frame, the last camera is this one, at this eye: a point then has no motion of the camera.
    /// </summary>
    public PreviousView PreviousViewOf(in DrawnCamera current)
    {
        DrawnCamera last = continued?.Camera ?? current;
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
        Contract.Requires(Camera is null);
        Camera = drawn;
    }

    /// <summary>Appends one placed instance of the draw. The keys rise strictly, in the order of the snapshot's instances.</summary>
    public void Place(in KeptPlacement placement)
    {
        Contract.Requires(Placements.Count == 0 || Placements[^1].Key.Order < placement.Key.Order);
        Placements.Add(placement);
    }

    /// <summary>Arms the pending state of a new draw, from the history's derivation in <see cref="FrameHistory.Begin"/>.</summary>
    internal void Arm(
        bool reset,
        long drawIndex,
        long drawnFrames,
        Extent2D renderSize,
        Extent2D outputSize,
        long cameraCuts,
        DrawnFrame? continuing
    )
    {
        Reset = reset;
        DrawIndex = drawIndex;
        DrawnFrames = drawnFrames;
        RenderSize = renderSize;
        OutputSize = outputSize;
        CameraCuts = cameraCuts;
        continued = continuing;
        Camera = null;
        Placements.Clear();
    }

    /// <summary>
    /// Takes the placements that the batch wrote, and gives the pending state <paramref name="spare"/> to fill in its next
    /// draw. <see cref="FrameHistory.Drew"/> swaps the two lists, so a draw copies no placement.
    /// </summary>
    internal List<KeptPlacement> TakePlacements(List<KeptPlacement> spare)
    {
        List<KeptPlacement> taken = Placements;
        spare.Clear();
        Placements = spare;
        return taken;
    }
}

/// <summary>A drawn frame, as the history records it.</summary>
internal readonly record struct DrawnFrame(
    DrawnCamera Camera,
    Extent2D RenderSize,
    Extent2D OutputSize,
    long CameraCuts
);

/// <summary>
/// The record of the last drawn frame, and the rules that relate each draw to it (docs/plans/temporal-renderer.md, "The
/// frame history"). Begin derives one draw's pending state: whether it resets every history, its draw index since the last
/// reset, the parity of the images that alternate, and the record relative to this draw's eye. The batch writes the draw's
/// camera and placements into the pending state. Drew commits it after the submission. A draw that throws before Drew
/// leaves the record, the count of drawn frames and so every parity as they were. No other owner keeps a copy of these
/// values.
/// </summary>
/// <remarks>
/// Invariants, checked by <see cref="AssertInvariants"/>:
/// <list type="bullet">
/// <item>A record exists exactly when at least one frame was drawn.</item>
/// <item>The draw index of the last drawn frame is below the count of drawn frames.</item>
/// <item>The keys of the placement history, and of the pending placements, rise strictly.</item>
/// </list>
/// </remarks>
internal sealed class FrameHistory
{
    private readonly PendingFrame pending;
    private List<KeptPlacement> drawnPlacements = [];
    private DrawnFrame? drawn;
    private long drawnFrames;
    private long lastDrawIndex;
    private bool armed;

    public FrameHistory()
    {
        pending = new(this);
    }

    /// <summary>The placement history: the last drawn frame's placed instances, keys rising.</summary>
    internal ReadOnlySpan<KeptPlacement> DrawnPlacements =>
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(drawnPlacements);

    /// <summary>
    /// The one choke point of the resets: derives the pending state of a draw of <paramref name="snapshot"/> at
    /// <paramref name="outputSize"/> and <paramref name="renderSize"/>, and changes no record. The draw resets when it is
    /// the first drawn frame, when the snapshot's count of camera cuts differs from the drawn frame's, or when the output
    /// size changed. A change of the render size resets nothing.
    /// </summary>
    /// <returns>The history's one pending state, armed for this draw. The next Begin arms it again.</returns>
    public PendingFrame Begin(RenderSnapshot snapshot, Extent2D outputSize, Extent2D renderSize)
    {
        bool reset =
            drawn is not { } last
            || last.CameraCuts != snapshot.CameraCuts
            || last.OutputSize.Width != outputSize.Width
            || last.OutputSize.Height != outputSize.Height;
        pending.Arm(
            reset: reset,
            drawIndex: reset ? 0 : lastDrawIndex + 1,
            drawnFrames: drawnFrames,
            renderSize: renderSize,
            outputSize: outputSize,
            cameraCuts: snapshot.CameraCuts,
            // A position in another frame's axes means nothing in this one's, so the record then serves no motion.
            continuing: drawn is { } record && record.Camera.FrameId == snapshot.OriginFrameId ? record : null
        );
        armed = true;
        return pending;
    }

    /// <summary>
    /// Commits <paramref name="frame"/>, the pending state of the last <see cref="Begin"/>, after its submission: its
    /// camera, render and output size, cut count and placements become the record, and the count of drawn frames grows by 1.
    /// </summary>
    public void Drew(PendingFrame frame)
    {
        Contract.Requires(armed && ReferenceEquals(frame, pending));
        DrawnCamera camera =
            frame.Camera ?? throw new InvalidOperationException("The batch wrote no camera into the pending frame.");
        long framesBefore = drawnFrames;
        drawn = new(
            Camera: camera,
            RenderSize: frame.RenderSize,
            OutputSize: frame.OutputSize,
            CameraCuts: frame.CameraCuts
        );
        drawnPlacements = frame.TakePlacements(drawnPlacements);
        lastDrawIndex = frame.DrawIndex;
        drawnFrames++;
        armed = false;
        Contract.Ensures(drawnFrames == framesBefore + 1);
    }

    /// <summary>Checks the invariants in the class comment. O(placements).</summary>
    internal void AssertInvariants()
    {
        Contract.Invariant(drawn.HasValue == drawnFrames > 0);
        Contract.Invariant(drawnFrames == 0 || lastDrawIndex < drawnFrames);
        Contract.Invariant(KeysRise(DrawnPlacements));
        Contract.Invariant(KeysRise(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pending.Placements)));
    }

    private static bool KeysRise(ReadOnlySpan<KeptPlacement> placements)
    {
        for (int i = 1; i < placements.Length; i++)
            if (placements[i - 1].Key.Order >= placements[i].Key.Order)
                return false;
        return true;
    }
}
