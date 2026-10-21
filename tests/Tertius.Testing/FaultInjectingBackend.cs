using System.Numerics;
using Tertius.Frames.Rails;
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

/// <summary>The motion intent of one entity that a step applied (see <see cref="PhysicsIntent.Move"/>).</summary>
/// <param name="Tick">The number of the tick the step belongs to.</param>
/// <param name="Entity">The entity that the motion intent moves.</param>
/// <param name="Frame">The frame that the motion intent is relative to: the entity's frame when it wrote it.</param>
/// <param name="Move">A copy of the motion intent, which the entity wrote during the previous tick, or its spawn gave it.</param>
public readonly record struct MoveRecord(long Tick, Entity Entity, FrameHandle Frame, MotionIntent Move)
{
    /// <summary>The full sample of <see cref="Move"/>. Throws <see cref="InvalidOperationException"/> for another case.</summary>
    public MotionSample Sample => Move.TryGetValue(out MotionSample sample) ? sample
        : throw new InvalidOperationException($"The motion intent of {Entity} in tick {Tick} is {Move}, not a full sample.");
}

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

    /// <summary>
    /// The motion intent of each entity that had one when a step began, in the order of the steps, and in each step in the
    /// order of the intents (see <see cref="Intents"/>). These are copies.
    /// </summary>
    public List<MoveRecord> Moves { get; } = [];

    /// <summary>Edits the next change batch before Jolt applies it, once. Null edits nothing.</summary>
    public Action<PhysicsChanges>? EditNextChanges { get; set; }

    /// <summary>
    /// Edits the intents of the next step before Jolt applies them, once. The edit gets each entity whose intent the step
    /// applies, in the order of the step, with a copy of its intent, and can change, add or remove entries: an intent that the
    /// World never sends stands in for a bug. <see cref="Intents"/> records the intents after the edit. Null edits nothing.
    /// </summary>
    public Action<List<(Entity Entity, PhysicsIntent Intent)>>? EditNextIntents { get; set; }

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
        PhysicsStepContext step = EditNextIntents is { } editIntents ? EditIntents(context, editIntents) : context;
        ReadOnlySpan<PhysicsIntent> intents = step.Intents.Span;
        foreach (Entity entity in step.IntentEntities.Span)
        {
            ref readonly PhysicsIntent intent = ref intents[(int)entity.Index];
            Intents.Add(new IntentRecord(step.Tick, entity, intent));
            if (intent.Move.HasValue)
                Moves.Add(new MoveRecord(step.Tick, entity, intent.Frame, intent.Move));
        }
        if (ThrowFromStep is { } fault)
        {
            ThrowFromStep = null;
            throw fault;
        }
        Jolt.Step(step, output);
        if (EditNextOutput is { } editNext)
        {
            EditNextOutput = null;
            editNext(output);
        }
        EditOutput?.Invoke(output);
        Outputs.Add(Copy(output));
    }

    /// <summary>The step context of <paramref name="context"/> with its intents after <paramref name="edit"/>, in tables of its own.</summary>
    private PhysicsStepContext EditIntents(in PhysicsStepContext context, Action<List<(Entity Entity, PhysicsIntent Intent)>> edit)
    {
        EditNextIntents = null;
        ReadOnlySpan<PhysicsIntent> intents = context.Intents.Span;
        var edited = new List<(Entity Entity, PhysicsIntent Intent)>();
        foreach (Entity entity in context.IntentEntities.Span)
            edited.Add((entity, intents[(int)entity.Index]));
        edit(edited);
        var table = new PhysicsIntent[edited.Count == 0 ? 0 : (int)edited.Max(entry => entry.Entity.Index) + 1];
        var entities = new Entity[edited.Count];
        for (int i = 0; i < edited.Count; i++)
        {
            table[edited[i].Entity.Index] = edited[i].Intent;
            entities[i] = edited[i].Entity;
        }
        return new PhysicsStepContext(context.Tick, context.DeltaTime, context.Time, context.Frames, table, entities);
    }

    public void CastRay(FrameHandle frame, Double3 origin, Double3 direction, double start, double end, IPhysicsQueryFilter filter,
        RayCastKind kind, List<RayCastHit> hits) =>
        Jolt.CastRay(frame, origin, direction, start, end, filter, kind, hits);

    public void CastShape(FrameHandle frame, in QueryShape shape, in DPose start, Double3 direction, double startDistance, double endDistance,
        IPhysicsQueryFilter filter, List<ShapeCastHit> hits) =>
        Jolt.CastShape(frame, shape, start, direction, startDistance, endDistance, filter, hits);

    public void Overlap(FrameHandle frame, in QueryShape shape, in DPose pose, IPhysicsQueryFilter filter, List<Entity> entities) =>
        Jolt.Overlap(frame, shape, pose, filter, entities);

    public string? ShapeProblem(ShapeDesc shape) => Jolt.ShapeProblem(shape);

    public Vector3 ShapeCenterOfMass(ShapeDesc shape) => Jolt.ShapeCenterOfMass(shape);

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
