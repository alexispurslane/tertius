namespace Tertius.Rendering;

/// <summary>
/// Owns the window's measured render scale. Next records the scale under the frame number; Measure consumes that
/// finished frame's timings. Missing timings leave the scale unchanged (docs/plans/temporal-renderer.md, section 4).
/// </summary>
/// <remarks>
/// Invariants, checked on demand by AssertInvariants: the current and recorded scales stay in the target's range,
/// each recorded frame occupies its frame-number slot, and measured costs are finite and nonnegative, with positive
/// cost per render area. The last accepted measurement is -1 before any measurement, then a nonnegative frame number.
/// </remarks>
internal sealed class RenderScaleController
{
    private readonly FrameTimeTarget target;
    private readonly (long Frame, float Scale)?[] drawn = new (long Frame, float Scale)?[8];
    private float scale;
    private (double Fixed, double PerArea)? model;
    private long measured = -1;

    /// <summary>
    /// The parts of a frame that the controller weighs: the fixed work before and after the render size, the work at the
    /// render size, and the output, which it leaves out because it holds the wait for the window's image.
    /// </summary>
    internal enum CostGroup
    {
        BeforeRenderSize,
        AtRenderSize,
        AfterRenderSize,
        Output,
    }

    public RenderScaleController(FrameTimeTarget target)
    {
        if (RenderScaleSetting.ProblemOf(target) is { } problem)
            throw new ArgumentOutOfRangeException(nameof(target), problem);
        this.target = target;
        scale = target.MaxScale;
    }

    public float Next(long frame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        drawn[frame % drawn.Length] = (frame, scale);
        Contract.Ensures(drawn[frame % drawn.Length] == (frame, scale));
        return scale;
    }

    public void Measure(GpuTimings timings)
    {
        if (timings.Frame <= measured || timings.Frame < 0)
            return;
        if (drawn[timings.Frame % drawn.Length] is not { } frame || frame.Frame != timings.Frame)
            return;
        if (Costs(timings.Passes.Span) is not { } costs)
            return;
        double perArea = costs.Scaling / (frame.Scale * frame.Scale);
        model = model is { } previous
            ? (
                previous.Fixed + 0.2 * (costs.Fixed - previous.Fixed),
                previous.PerArea + 0.2 * (perArea - previous.PerArea)
            )
            : (costs.Fixed, perArea);
        measured = timings.Frame;
        double budget = target.Milliseconds * 0.9;
        (double fixedTime, double renderTime) = model.Value;
        double current = fixedTime + renderTime * scale * scale;
        if (Math.Abs(current - budget) < budget * 0.03)
            return;
        double wanted = Math.Sqrt(Math.Max(0, (budget - fixedTime) / renderTime));
        wanted = Math.Clamp(wanted, target.MinScale, target.MaxScale);
        double weight = wanted < scale ? 0.25 : 0.05;
        scale = (float)Math.Clamp(scale + weight * (wanted - scale), target.MinScale, target.MaxScale);
        Contract.Ensures(scale >= target.MinScale && scale <= target.MaxScale);
    }

    /// <summary>
    /// The cost group of a pass of <paramref name="stage"/>: the passes from the G-buffer to the exposure draw at the
    /// render size, and the tone map starts the output.
    /// </summary>
    internal static CostGroup CostGroupOf(FrameGraph.Stage stage)
    {
        if (stage < FrameGraph.Stage.GBuffer)
            return CostGroup.BeforeRenderSize;
        if (stage <= FrameGraph.Stage.Exposure)
            return CostGroup.AtRenderSize;
        if (stage < FrameGraph.Stage.ToneMap)
            return CostGroup.AfterRenderSize;
        return CostGroup.Output;
    }

    /// <summary>The name of the timing of <paramref name="group"/> in a frame without pass timestamps.</summary>
    internal static string NameOf(CostGroup group) =>
        group switch
        {
            CostGroup.BeforeRenderSize => "before the render size",
            CostGroup.AtRenderSize => "at the render size",
            CostGroup.AfterRenderSize => "after the render size",
            CostGroup.Output => "output",
        };

    private static (double Fixed, double Scaling)? Costs(ReadOnlySpan<PassTiming> passes)
    {
        double span = 0,
            scaling = 0;
        bool reachedOutput = false;
        foreach (PassTiming pass in passes)
        {
            CostGroup group = CostGroupOf(pass.Stage);
            if (group == CostGroup.Output)
            {
                reachedOutput = true;
                break;
            }
            if (!double.IsFinite(pass.Milliseconds) || pass.Milliseconds < 0)
                return null;
            span += pass.Milliseconds;
            if (group == CostGroup.AtRenderSize)
                scaling += pass.Milliseconds;
        }
        if (!reachedOutput || scaling <= 0)
            return null;
        return (Fixed: span - scaling, Scaling: scaling);
    }

    internal void AssertInvariants()
    {
        Contract.Invariant(scale >= target.MinScale && scale <= target.MaxScale);
        Contract.Invariant(measured >= -1);
        if (model is { } costs)
        {
            Contract.Invariant(double.IsFinite(costs.Fixed) && costs.Fixed >= 0);
            Contract.Invariant(double.IsFinite(costs.PerArea) && costs.PerArea > 0);
        }
        for (int slot = 0; slot < drawn.Length; slot++)
            if (drawn[slot] is { } frame)
            {
                Contract.Invariant(frame.Frame >= 0 && frame.Frame % drawn.Length == slot);
                Contract.Invariant(frame.Scale >= target.MinScale && frame.Scale <= target.MaxScale);
            }
    }
}
