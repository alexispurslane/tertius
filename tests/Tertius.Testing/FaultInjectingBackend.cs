using Tertius.Physics;

namespace Tertius.Testing;

/// <summary>One physics step, as the backend saw it. It was recorded on the physics thread.</summary>
/// <param name="Tick">The number of the tick the step belongs to.</param>
/// <param name="Time">The simulation time at the start of the step, in seconds.</param>
/// <param name="ThreadId">The managed id of the thread that ran the step.</param>
/// <param name="OnPoolThread">Whether that thread is a thread-pool thread.</param>
public readonly record struct StepRecord(long Tick, double Time, int ThreadId, bool OnPoolThread);

/// <summary>The intent that one entity had written when a step began.</summary>
/// <param name="Tick">The number of the tick the step belongs to.</param>
/// <param name="Entity">The entity that wrote the intent during the previous tick.</param>
/// <param name="Intent">A copy of what it wrote.</param>
public readonly record struct IntentRecord(long Tick, Entity Entity, PhysicsIntent Intent);

/// <summary>
/// The real Jolt backend plus the power to edit what the World sends it, edit what it sends back, or make a call throw.
/// It is the only test double for physics. It stands in for a bug, for the tests of what the World does with a state that
/// only a bug produces, and it records what passed through it. It forwards every call to <see cref="Jolt"/>, so it is not
/// a second model of Jolt and cannot drift from it. With no edit and no exception set, a World on the wrapper ends in
/// the same state as a World on bare Jolt.
/// <para>
/// The wrapper does not lock anything. <see cref="ApplyChanges"/> runs at the sync point and <see cref="Step"/> runs on the
/// physics thread, and the World never runs them at the same time. A test reads the recorded lists after
/// <see cref="Simulation.World.Tick"/> has returned.
/// </para>
/// </summary>
public sealed class FaultInjectingBackend(JoltPhysicsBackend jolt) : IPhysicsBackend
{
    /// <summary>The real backend. Tests read its internals through it, for example <c>HasGhostIn</c>.</summary>
    public JoltPhysicsBackend Jolt { get; } = jolt;

    /// <summary>
    /// A copy of every change batch that reached Jolt, in order: what the World sent, after <see cref="EditNextChanges"/>
    /// ran. The World clears and reuses its own batch right after the call, so a test reads these copies, not that object.
    /// Each copy also holds the <see cref="PhysicsChanges.Errors"/> that Jolt reported while it applied the batch. A call
    /// that <see cref="ThrowFromApplyChanges"/> cut short has no entry.
    /// </summary>
    public List<PhysicsChanges> AppliedChanges { get; } = [];

    /// <summary>
    /// A copy of every step output that the World received, in order, after <see cref="EditNextOutput"/> and
    /// <see cref="EditOutput"/> ran. The World clears and reuses its own output right after it applies it. A step that
    /// <see cref="ThrowFromStep"/> cut short has no entry.
    /// </summary>
    public List<PhysicsStepOutput> Outputs { get; } = [];

    /// <summary>
    /// What each step received from the World, in order: its tick, its simulation time and the thread that ran it. A step
    /// that <see cref="ThrowFromStep"/> cut short has an entry, because the World started it.
    /// </summary>
    public List<StepRecord> Steps { get; } = [];

    /// <summary>
    /// The intent of each entity that wrote one during the previous tick, as every step received it, in the order of the
    /// steps. The World hands the backend one table of intents and reuses it, so these are copies.
    /// </summary>
    public List<IntentRecord> Intents { get; } = [];

    /// <summary>Edits the next change batch before Jolt applies it, once. Null edits nothing.</summary>
    public Action<PhysicsChanges>? EditNextChanges { get; set; }

    /// <summary>Edits the output of the next step after Jolt wrote it, once. Null edits nothing. It runs before <see cref="EditOutput"/>.</summary>
    public Action<PhysicsStepOutput>? EditNextOutput { get; set; }

    /// <summary>Edits the output of every step after Jolt wrote it, until a test sets it to null. Null edits nothing.</summary>
    public Action<PhysicsStepOutput>? EditOutput { get; set; }

    /// <summary>
    /// An exception that the next <see cref="Step"/> throws instead of calling Jolt, once. The World sees it as a failed
    /// step. Null throws nothing.
    /// </summary>
    public Exception? ThrowFromStep { get; set; }

    /// <summary>
    /// An exception that the next <see cref="ApplyChanges"/> throws instead of calling Jolt, once. The World sees it as
    /// changes that the backend failed to apply. Null throws nothing.
    /// </summary>
    public Exception? ThrowFromApplyChanges { get; set; }

    public void ApplyChanges(PhysicsChanges changes, FrameTree frames)
    {
        if (ThrowFromApplyChanges is { } fault)
        {
            ThrowFromApplyChanges = null;
            throw fault;
        }
        if (EditNextChanges is { } edit)
        {
            EditNextChanges = null;
            edit(changes);
        }
        PhysicsChanges applied = Copy(changes);
        AppliedChanges.Add(applied);
        Jolt.ApplyChanges(changes, frames);
        applied.Errors.AddRange(changes.Errors);
    }

    public void Step(in PhysicsStepContext context, PhysicsStepOutput output)
    {
        Steps.Add(new StepRecord(context.Tick, context.Time, Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread));
        ReadOnlySpan<PhysicsIntent> intents = context.Intents.Span;
        foreach (Entity entity in context.IntentEntities.Span)
            Intents.Add(new IntentRecord(context.Tick, entity, intents[(int)entity.Index]));
        if (ThrowFromStep is { } fault)
        {
            ThrowFromStep = null;
            throw fault;
        }
        Jolt.Step(context, output);
        if (EditNextOutput is { } editNext)
        {
            EditNextOutput = null;
            editNext(output);
        }
        EditOutput?.Invoke(output);
        Outputs.Add(Copy(output));
    }

    public float BoundingRadius(ShapeDesc shape) => Jolt.BoundingRadius(shape);

    public string? ShapeProblem(ShapeDesc shape) => Jolt.ShapeProblem(shape);

    public void Dispose() => Jolt.Dispose();

    private static PhysicsChanges Copy(PhysicsChanges changes)
    {
        var copy = new PhysicsChanges();
        copy.BodiesDestroyed.AddRange(changes.BodiesDestroyed);
        copy.FramesDestroyed.AddRange(changes.FramesDestroyed);
        copy.BodiesCreated.AddRange(changes.BodiesCreated);
        copy.FramesCreated.AddRange(changes.FramesCreated);
        copy.BodiesMoved.AddRange(changes.BodiesMoved);
        copy.FramesShifted.AddRange(changes.FramesShifted);
        copy.Errors.AddRange(changes.Errors);
        return copy;
    }

    private static PhysicsStepOutput Copy(PhysicsStepOutput output)
    {
        var copy = new PhysicsStepOutput();
        copy.Bodies.AddRange(output.Bodies);
        copy.HandOffs.AddRange(output.HandOffs);
        copy.Contacts.AddRange(output.Contacts);
        copy.ContactsEnded.AddRange(output.ContactsEnded);
        copy.Frames.AddRange(output.Frames);
        copy.Characters.AddRange(output.Characters);
        copy.Errors.AddRange(output.Errors);
        return copy;
    }
}
