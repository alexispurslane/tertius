namespace Tertius.Simulation;

/// <summary>
/// Keeps the position of every numeric frame in its tree parent small (docs/plans/numeric-frames.md, "Nesting for vast
/// distances"). A falling or resting frame farther than <see cref="NumericFrameOptions.NestDistance"/> from its parent's
/// origin moves under a nesting frame near it: a resting frame on a grid of <see cref="NumericFrameOptions.NestGrid"/> in
/// the semantic frame. Every multiple of the grid is exact in a double, so the moves between nesting frames, and their
/// jumps, change no position relative to anything else. Reads between nearby entities then compose through a nesting frame
/// that they share, and no large number enters them.
/// </summary>
/// <remarks>
/// It runs once at each sync point, after the membership rules, on one thread, over the frames in dense order. A far frame:
/// <list type="number">
/// <item>Jumps its nesting frame by whole grid steps, when every frame in that nest is then within half the nest distance
/// of it. The frames in the nest shift back by the same exact amount.</item>
/// <item>Else moves under the nearest other nesting frame of its semantic frame within half the nest distance of it.</item>
/// <item>Else moves under a new nesting frame at the grid point nearest it.</item>
/// </list>
/// A nesting frame that holds no frame goes away with the other empty numeric frames.
/// </remarks>
internal sealed class NumericNesting(NumericFrameOptions options, FrameTree frames, INumericFrameHost host)
{
    private readonly List<FrameHandle> all = new();
    private readonly List<FrameHandle> children = new();
    private static readonly List<Entity> NoMembers = new();

    public void Run()
    {
        all.Clear();
        frames.GetAllInTree(all);
        foreach (FrameHandle frame in all)
        {
            // A frame that this run made is not in the list, and an earlier step can only have moved the frames in it.
            if (frames.Numeric(frame) is not (NumericMode.Falling or NumericMode.Resting))
                continue;
            Double3 position = frames.LocalPose(frame).Position;
            if (position.Length > options.NestDistance)
                Nest(frame, position);
        }
    }

    private void Nest(FrameHandle frame, Double3 position)
    {
        FrameHandle parent = frames.TreeParent(frame);
        bool nested = frames.Numeric(parent) == NumericMode.Nesting;
        Double3 step = Snap(position);
        if (nested && AllNear(parent, step))
        {
            host.ShiftNumericFrame(parent, step, Double3.Zero, NoMembers);
            return;
        }

        // The semantic frame that holds the nesting frames, and the place of the frame's parent in it: both grid points.
        FrameHandle semantic = nested ? frames.TreeParent(parent) : parent;
        Double3 parentPlace = nested ? frames.LocalPose(parent).Position : Double3.Zero;
        FrameHandle nest = NearestNest(semantic, parent, parentPlace, position);
        if (nest.IsNull)
            nest = host.CreateNumericFrame(semantic, parentPlace + step, Double3.Zero, NumericMode.Nesting);
        // The difference of two grid points is exact, so only the last addition rounds, at the scale of the nest distance.
        frames.MoveUnderNest(frame, nest, parentPlace - frames.LocalPose(nest).Position + position);
    }

    /// <summary>True when every frame in <paramref name="nest"/> is within half the nest distance of <paramref name="step"/>.</summary>
    private bool AllNear(FrameHandle nest, Double3 step)
    {
        children.Clear();
        frames.GetTreeChildren(nest, children);
        foreach (FrameHandle child in children)
            if ((frames.LocalPose(child).Position - step).Length > options.NestDistance / 2)
                return false;
        return true;
    }

    /// <summary>
    /// The nesting frame of <paramref name="semantic"/>, other than <paramref name="parent"/>, nearest a frame at
    /// <paramref name="position"/> in <paramref name="parent"/>, which is at <paramref name="parentPlace"/>. Null when none
    /// is within half the nest distance.
    /// </summary>
    private FrameHandle NearestNest(FrameHandle semantic, FrameHandle parent, Double3 parentPlace, Double3 position)
    {
        children.Clear();
        frames.GetTreeChildren(semantic, children);
        FrameHandle nearest = FrameHandle.Null;
        double best = options.NestDistance / 2;
        foreach (FrameHandle child in children)
        {
            if (child == parent || frames.Numeric(child) != NumericMode.Nesting)
                continue;
            double distance = (parentPlace - frames.LocalPose(child).Position + position).Length;
            if (distance <= best)
                (nearest, best) = (child, distance);
        }
        return nearest;
    }

    /// <summary>The grid point nearest <paramref name="position"/>. The grid is a power of two, so this is exact.</summary>
    private Double3 Snap(Double3 position)
    {
        double grid = options.NestGrid;
        return new Double3(Math.Round(position.X / grid) * grid, Math.Round(position.Y / grid) * grid, Math.Round(position.Z / grid) * grid);
    }
}
