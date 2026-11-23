namespace Tertius.Rendering;

/// <summary>
/// Owns the window's measured render scale, one per cost kind of a draw (<see cref="DrawCost"/>). The renderer asks for
/// the scale of the next draw's kind, which the frame history derives, and draws at the largest exact-ratio size at or
/// below it, or at the smallest inside the limits when none is. Drew records the scale that the draw realized and its
/// kind under its frame number. Measure consumes that finished frame's timings: it fits the cost model of the frame's own
/// kind, so that the model describes the area that was drawn, not the request, and it moves only that kind's request.
/// So the dear catch-up draws after a reset never move the ordinary request, and the draw after them returns to it at
/// once (ter-bcwr). A kind without a fitted model asks for the scale of the kind before it: a catch-up draw asks for the
/// ordinary scale, as at the first reset of a run, and an ordinary draw for the target's largest scale. Missing timings
/// leave the scales unchanged (docs/plans/temporal-renderer.md, section 4).
/// </summary>
/// <remarks>
/// Invariants, checked on demand by AssertInvariants: each kind's request stays in the target's range, and its fitted
/// costs are finite and nonnegative, with positive cost per render area. Each recorded frame occupies its frame-number
/// slot, with a scale in the target's range and a defined cost kind. The last accepted measurement is -1 before any
/// measurement, then a nonnegative frame number.
/// </remarks>
internal sealed class RenderScaleController
{
    private readonly FrameTimeTarget target;
    private readonly (long Frame, float Scale, DrawCost Cost)?[] drawn = new (long Frame, float Scale, DrawCost Cost)?[8];

    // Each cost kind's fitted model and request, at the kind's index; none before the kind's first measurement.
    private readonly Fit?[] fits = new Fit?[Enum.GetValues<DrawCost>().Length];
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
    }

    /// <summary>
    /// The scale that a draw of the cost kind <paramref name="next"/> asks for, from the target's smallest to its largest
    /// scale: the kind's request, or the scale of the kind before it while the kind has no fitted model.
    /// </summary>
    public float RequestedScaleFor(DrawCost next)
    {
        if (fits[(int)next] is { } fit)
            return fit.Request;
        return next == DrawCost.CatchUp ? RequestedScaleFor(DrawCost.Ordinary) : target.MaxScale;
    }

    /// <summary>
    /// Records that draw <paramref name="frame"/>, of the cost kind <paramref name="cost"/>, realized
    /// <paramref name="realizedScale"/>: its render width over its output width, which the exact-ratio size can put below
    /// <see cref="RequestedScaleFor"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The frame is negative, or the scale is not a number inside the target's limits.
    /// </exception>
    public void Drew(long frame, float realizedScale, DrawCost cost)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        if (!(realizedScale >= target.MinScale && realizedScale <= target.MaxScale))
            throw new ArgumentOutOfRangeException(
                nameof(realizedScale),
                $"A draw realized the scale {realizedScale}, outside {target.MinScale} to {target.MaxScale}."
            );
        drawn[frame % drawn.Length] = (frame, realizedScale, cost);
    }

    /// <summary>
    /// Consumes the timings of a finished frame: fits the cost model of the frame's kind, and moves that kind's request
    /// toward the scale that fits the budget under the model. A frame that was not recorded, was measured before, or has
    /// timings without a valid span changes nothing.
    /// </summary>
    public void Measure(GpuTimings timings)
    {
        if (timings.Frame <= measured || timings.Frame < 0)
            return;
        if (drawn[timings.Frame % drawn.Length] is not { } frame || frame.Frame != timings.Frame)
            return;
        if (Costs(timings.Passes.Span) is not { } costs)
            return;
        double perArea = costs.Scaling / (frame.Scale * frame.Scale);
        (double fixedTime, double renderTime) = fits[(int)frame.Cost] is { } previous
            ? (
                previous.Fixed + 0.2 * (costs.Fixed - previous.Fixed),
                previous.PerArea + 0.2 * (perArea - previous.PerArea)
            )
            : (costs.Fixed, perArea);
        float request = RequestedScaleFor(frame.Cost);
        measured = timings.Frame;
        fits[(int)frame.Cost] = new(
            Fixed: fixedTime,
            PerArea: renderTime,
            Request: Moved(request: request, fixedTime: fixedTime, renderTime: renderTime)
        );
    }

    /// <summary>
    /// <paramref name="request"/> moved toward the scale whose cost under the model of <paramref name="fixedTime"/> ms and
    /// <paramref name="renderTime"/> ms per unit of render area fits 90 % of the target: a quarter of the way down, a
    /// twentieth up, and not at all while its own cost lies within 3 % of the budget.
    /// </summary>
    private float Moved(float request, double fixedTime, double renderTime)
    {
        double budget = target.Milliseconds * 0.9;
        double current = fixedTime + renderTime * request * request;
        if (Math.Abs(current - budget) < budget * 0.03)
            return request;
        double wanted = Math.Sqrt(Math.Max(0, (budget - fixedTime) / renderTime));
        wanted = Math.Clamp(wanted, target.MinScale, target.MaxScale);
        double weight = wanted < request ? 0.25 : 0.05;
        float moved = (float)Math.Clamp(request + weight * (wanted - request), target.MinScale, target.MaxScale);
        Contract.Ensures(moved >= target.MinScale && moved <= target.MaxScale);
        return moved;
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
        Contract.Invariant(measured >= -1);
        foreach (Fit? kind in fits)
            if (kind is { } fit)
            {
                Contract.Invariant(fit.Request >= target.MinScale && fit.Request <= target.MaxScale);
                Contract.Invariant(double.IsFinite(fit.Fixed) && fit.Fixed >= 0);
                Contract.Invariant(double.IsFinite(fit.PerArea) && fit.PerArea > 0);
            }
        for (int slot = 0; slot < drawn.Length; slot++)
            if (drawn[slot] is { } frame)
            {
                Contract.Invariant(frame.Frame >= 0 && frame.Frame % drawn.Length == slot);
                Contract.Invariant(frame.Scale >= target.MinScale && frame.Scale <= target.MaxScale);
                Contract.Invariant(Enum.IsDefined(frame.Cost));
            }
    }

    /// <summary>
    /// A cost kind's fitted model, <paramref name="Fixed"/> ms of work that does not grow with the render area and
    /// <paramref name="PerArea"/> ms per unit of render area, and the scale that the kind's next draw asks for.
    /// </summary>
    private readonly record struct Fit(double Fixed, double PerArea, float Request);
}
