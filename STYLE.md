# C# style for Tertius

This guide covers the code in `src`. The code in `tests` has its own guide, [tests/STYLE.md](tests/STYLE.md). The guide holds the rules that no tool enforces, and names the tool for the rules that one does. `.editorconfig` and `tools/cleanup.sh` own formatting, member layout, `using` order and the modern syntax fixes.

When a reason can beat a rule, the rule says "prefer". When nothing beats a rule, it says "always" or "never". Most examples are real code from `src`. An example that starts with the comment `// Sketch` shows the form with made-up names.

## Priorities

Slow code is never acceptable because it was easier to write. Within that, when speed and a rule of this guide pull apart, use this order:

1. Consistency beats general performance. When another form is a little faster in theory, keep the form of this guide.
2. Benchmarked performance where it matters beats consistency. Break a rule only where a profile or a benchmark shows that the spot costs real time, and say so in a comment.

"Real time" means a share of the frame budget that a profile can see: about 16.7 ms per frame at 60 frames per second.

Example: a switch over the 184-byte `MotionIntent` copies the whole union first. A measurement found 2 to 5 ns per call. Three members read the union once per step for each entity with a motion intent. For 1,000 such entities, that is at most 15 µs per step, under 0.1 percent of the frame budget. That is not real time, so the members keep their switches.

## Data

### Use pattern matching and destructuring

Prefer pattern matching and destructuring over every other way to read data: over property chains, casts, `TryGetValue` chains and `is` tests with a body. A pattern states the shape that the code expects, and the compiler makes sure that a switch covers every case.

Use a positional pattern, such as `EndPose(var pose)`, only for a type with one field. For a type with two or more fields, use a property pattern, such as `Taking { Throw: var index }`. A positional pattern binds by parameter order: a new field or a reordered one breaks every such pattern, or silently swaps two values of one type.

```csharp
internal MotionIntent Converted(in FrameMotion start, in FrameMotion end, double step) => this switch
{
    EndPose(var pose) => new(new EndPose(end.PoseToParent(pose))),
    ...
};
```

```csharp
case Taking { Throw: var index }:
    report = Use(self, outbox, index);
    break;
```

### Choose the form of a closed set by the shape of its cases

A closed set is a set of alternatives that the code knows in full.

| Cases | Form | Example |
|---|---|---|
| Structs | A `[GeneratedUnion]` struct | `MotionIntent`, `RenderLight` |
| Classes | A built-in `union` declaration | `public union ScriptStep(RunStep, PostStep, ...)` |
| Records that carry data and no behavior | A `closed record` hierarchy | `ShapeDesc` |
| Named values with no data | An `enum` | `BodyMotion` |

The union analyzer enforces the first two rows (ARCHITECTURE.md, "Unions"). Never put a `Kind` enum beside fields that only some kinds use. Write a union with one case per kind.

A union case is a positional `readonly record struct`, so that the generator stores its fields one by one:

```csharp
public readonly record struct EndPose(DPose Pose);
public readonly record struct SteerTarget(DPose Pose);

[GeneratedUnion(typeof(EndPose), typeof(MotionSample), typeof(SteerTarget))]
public readonly partial struct MotionIntent;
```

A union with invariants checks them in factory methods, because a generated constructor cannot refuse a value.

### Name a type only when it earns a name

If a group of values appears in only a few places, use a tuple with named elements instead of a struct. Never use a tuple whose elements have no names: no `Item1`, and no `(int, double)` in a signature.

```csharp
(bool recorded, record) = this switch
{
    MotionSample sample => (true, sample),
    EndPose(var end) => (true, KinematicRecord(stepStart, end, step)),
    SteerTarget => (false, default(MotionSample)),
    null => (false, default(MotionSample))
};
```

When the group travels between modules, is stored, or appears in many places, give it a `readonly record struct`. Data that is used together goes into one struct, not into parallel arrays:

```csharp
internal readonly record struct Delivery(uint Recipient, int Event);
private readonly record struct InboxRange(int Start, int Count);
```

Write a range as a start and a count, never as a prefix-sum array read at `[i]` and `[i + 1]`.

### Put a function into the type that it belongs to

A function of an orbit is an instance method of `KeplerOrbit`. Math about two bodies that takes no orbit is a static method of `KeplerOrbit`, such as `KeplerOrbit.Chord(position, velocity, mu, step)`. Never write a static helper class such as `KeplerMath`. This holds for demo code too: if the function belongs to an engine type, it goes into the engine.

### Seal types, and keep them internal

- Every class is `sealed`, unless it is the base of a hierarchy that this guide allows.
- Every type and member is `internal` or `private`, unless another module needs it. A member becomes `public` only when it is part of the engine's API for game code or mods.

## Nulls

`Nullable` is on, and warnings are errors (`Directory.Build.props`), so a nullable warning fails the build.

Treat `T?` like `Maybe` in Haskell. A value is nullable only where "no value" is a real answer, such as a stale handle or a missing row. Null lives at the outer shell of a module: the shell tests for it once, in a form that the compiler's flow analysis understands, and passes a value that is not nullable inward. The business logic never sees a nullable type.

```csharp
// Sketch
// The shell: a stale handle gives null.
if (FindBody(entity) is not { } body)
    return false;
// The core: body is not nullable here, and every call below takes a Body, not a Body?.
Push(body, impulse);
return true;
```

Never use `!` to silence the compiler in business logic. Generated code that reads a reference slot is the one place for it. Never return null for an empty collection. Return an empty one.

## Control flow

### Switches

These rules come from ARCHITECTURE.md, "Switches over closed sets". The build enforces the first one.

- A switch over a closed set has no discard arm, so a new case fails the build.
- A switch that chooses a value is a switch expression. A union switch ends in a `null` arm. The compiler never asks for that arm, and without it the empty value throws.
- A switch statement names its quiet cases and throws `UnreachableException` in its default case. The cleanup deletes an empty default case, and a missing case then fails the build.

```csharp
case MouseButtonChanged or KeyChanged or MouseMoved or Scrolled or null:
    // Every other input does nothing.
    break;
default:
    throw new UnreachableException();
```

Watch the type of a switch expression. In `Case c => c.Moved(...), null => default`, the switch takes the type of the case, so `default` becomes a real case. Name the union in an arm, such as `Case c => new U(c.Moved(...))`.

### Guards

Write a list of early returns, not a chain of nested conditional expressions. A conditional expression is for one simple choice.

```csharp
if (!f.MultiDrawIndirect) return "multiDrawIndirect";
if (!f.DrawIndirectFirstInstance) return "drawIndirectFirstInstance";
if (!f.DepthClamp) return "depthClamp";
```

### Name the parts of a complex condition

If a condition has more than one idea in it, give each idea a named local variable. Do this even if the code uses it once. If several places use the same idea, make it a method.

Not this:

```csharp
return !double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z)
    ? throw new ArgumentException($"Near needs a finite point, not {point}.", nameof(point))
    : (QueryFilter)new NearPoint(frame, point, CheckRadius(radius));
```

This:

```csharp
bool finite = double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
if (!finite)
    throw new ArgumentException($"Near needs a finite point, not {point}.", nameof(point));
return new NearPoint(frame, point, CheckRadius(radius));
```

### State machines

If a component has more than two states, or any data that belongs to only some of its states, write a flat state machine:

1. One union holds the current state, with one case per phase. Never nest one machine inside the phase of another.
2. One pure static method gives the next state: `Next(state, event)`. It reads only its two arguments, and it is the whole table of changes. Only `Next` changes the case.
3. The behavior per state dispatches by case: a switch that calls one method or overload per case.

```csharp
public static BoxPlayState? Next(BoxPlayState state, BoxPlayEvent change) => (state, change) switch
{
    (Idle, PlayWithBoxes) => new Shoving(WalkProgress.Start),
    (Shoving, WalkDone) => new ToStand(WalkProgress.Start),
    (Taking { Throw: var index }, Took { Copy: var held }) => new Aiming(index, held, 0),
    ...
    _ => null
};
```

The table of `Next` ends in a discard arm, because the pairs of state and event are an open set. A unit test pins each row of `Next`, with no World.

## Calls and construction

Write C# that reads like Swift: each value at a call names its role.

- If a constructor or a method takes more than 3 parameters, always name every argument at each call.
- If more than 3 members need values, use an object initializer, or `required` and `init` members, instead of a long positional constructor.

```csharp
// Sketch
var spec = new CrateSpec
{
    Size = new(1, 1, 1),
    Mass = 20,
    Friction = 0.6f,
    Color = Palette.Wood,
};

Body body = world.Spawn(frame: station, pose: start, shape: crateShape, motion: BodyMotion.Dynamic);
```

The cleanup keeps named arguments: the Tertius profile leaves out the ReSharper step that removes them.

## Polymorphism and composition

### Polymorphism is an interface

Always use an interface for polymorphism. The form depends on whether the code is hot:

| Where | Form |
|---|---|
| A hot path | A type parameter constrained to a struct and the interface: `where TFilter : struct, IEventFilter`. The JIT writes one copy of the code per struct type, so every call is direct, can inline, and boxes nothing. |
| Everywhere else | A plain interface type, such as a parameter of type `IComponent`. |

Use an abstract class or a virtual method only where an interface cannot do the job, and say why in a comment.

The scan of an inbox takes its filter this way. `EventQuery<T>` is a struct that implements `IEventFilter`, and the cursor holds it as a `TFilter`:

```csharp
internal interface IEventFilter
{
    bool MayMatch(in EventMask mask);
    bool Matches(ref Event e);
}

internal ref struct EventCursor<TFilter> where TFilter : struct, IEventFilter
{
    // Not readonly: a call on a readonly field of a generic struct type would copy the filter first.
    private TFilter filter;
    ...
    public bool MoveNext()
    {
        ...
        while (++index < items.Length)
            if (filter.Matches(ref items[index]))
                return true;
        ...
    }
}
```

Two rules keep this form free of cost:

- Hold the struct in a field that is not `readonly`, and pass it on as `ref`. A call on a `readonly` field or an `in` parameter of a generic struct type copies the struct first, because the compiler cannot know that the struct is `readonly`.
- Never convert the struct to its interface type, such as by passing it to a parameter of type `IEventFilter`. That conversion boxes it, and every call becomes a virtual call again.

### Composition

Always share behavior through default interface methods. A type gets behavior by implementing the interface that holds it, and it overrides a default method only where its behavior differs. Never hold a part only to pass calls through to it, and never inherit from a class to get its behavior.

```csharp
// Sketch
internal interface IEventFilter
{
    bool MayMatch(in EventMask mask);
    bool Matches(ref Event e);

    // Every filter gets this, and a filter with a faster test overrides it.
    bool MatchesAny(Span<Event> events)
    {
        foreach (ref Event e in events)
            if (Matches(ref e))
                return true;
        return false;
    }
}
```

A default method called on a struct through a type parameter boxes the struct, because the method body sees `this` as the interface. The JIT can remove the box only if it inlines the method: a measurement on .NET 11 RC 1 found no allocation per call for a small default method after warm-up, and one box per call in cold code. So an interface that a hot path calls has no default methods, unless a measurement of that call shows no allocation. Record the measurement in a comment on the method, and let an allocation test hold it.

- A type holds a part when the part is data that the type owns, not to borrow the part's behavior.
- A primary constructor keeps the parts short to declare, such as `public sealed class Carry(CapsuleShapeDesc body) : IComponent`.
- Never build an inheritance hierarchy more than 2 levels deep below `object`.
- Use a static abstract member of an interface for a factory or a constant per type, such as `T.Zero`.
- Extension members, such as a C# 14 `extension` block, are allowed for behavior that a type does not own. A default interface method fits most such needs better, so expect to use few of them.
- An abstract base with abstract or virtual members is an open set. A switch never chooses among its subtypes.

## Performance

- Hot paths allocate nothing after warm-up. They use spans, `ref`, `in` and ref structs.
- LINQ is the first choice outside hot paths. Deferred LINQ is 5 to 26 times slower than a loop on a hot path. There, build an API that reads like LINQ and allocates nothing, such as `foreach (ref Throttle each in events.Of<Throttle>())`.
- Reflection runs only at load time, such as for blueprints.
- Every expensive feature has a toggle in its options, and its cost is measured.

## Errors and contracts

- A public API tests its arguments with guards that are always on and throw `ArgumentException` or a subtype.
- Code that changes core state uses `Contract.Requires`, `Ensures` and `Assert` (`src/Tertius.Core/Contracts/Contract.cs`), which compile out of Release. Each such module lists its invariants and checks them in one `AssertInvariants` method.
- A failed contract is a bug. Never catch one.
- A state that only a bug produces fails a contract in Debug. Release skips it and records it in `World.Errors`.
- Every member that takes an `Entity` checks its generation, in every build. A stale handle gives an explicit `null`, `false` or exception. No member between modules takes or returns a bare slot.

### Expected failures

A failure that can happen in correct code, such as a file that does not open or text that does not parse, is not a bug and not an argument error. A method that can fail this way is a `Try` method: it returns `bool` and gives its result in an `out` parameter, with `[NotNullWhen(true)]` or `[MaybeNullWhen(false)]` where the result is a reference. Do not write a result union for this.

```csharp
public readonly bool TryGet<T>(out T payload) where T : unmanaged, IEventPayload
```

If the caller needs the reason for a failure, the method gives it in a second `out` parameter. A method without `Try` in its name throws on such a failure, and its doc comment names the exception.

### Disposal and ownership

Release every resource that the stack and the garbage collector do not release, as early and as visibly as the scope allows: native memory, GPU objects, files, processes and physics backends.

- Hold every `IDisposable` local in a `using` declaration. Use a `using` block only when the resource must go before the scope ends.
- The type that creates a resource owns it, and its `Dispose` releases it, in the reverse order of creation.
- Ownership never passes in silence. A hand-over is in the name of the member or in an explicit option, such as `WorldOptions.DisposePhysics`, which makes the World dispose the physics backend that it receives.
- A type that owns a resource implements `IDisposable` and is `sealed`. It has no finalizer: a resource that escapes disposal is a bug for a test to find, not for the garbage collector to clean up.

```csharp
using var world = new World(new() { DisposePhysics = true },
    ...
```

## Names

- Name a piece of state for what it holds, with its frame and units where they matter. Remove a variable that only exists as scratch for another one.
- Use one name for one thing, in the code, its comments and the docs.
- A read of a position or a velocity names its frame, such as `PoseIn(frame)`. A read about one step names that step, such as `HullMotionInStep`.

## Comments

- A doc comment says what a member means, in plain English. It points to the design doc section that gives the reason, such as `(docs/plans/rails-components.md, "The follow kind")`.
- A comment gives the reason for the code, never a description of the code under it.
- Every ordering rule has a comment that gives its reason.

## Dependencies

Prefer what ships with .NET, and libraries driven by attributes, such as System.Text.Json, over a hand-written model.
