# Union tooling

Epic ticket: `ter-nn6z`.

The code holds 32 hand-written custom `[Union]` structs. Each one repeats the same members by hand: a constructor per case, `HasValue`, `Value`, a `TryGetValue` per case, the equality members and `ToString`. This epic adds a source generator that writes those members, and converts every hand-written union to it. It also adds an analyzer that steers each new closed set to the right form.

A closed set is a set of alternatives that the code knows in full: an enum, a union or a `closed` record hierarchy. ARCHITECTURE.md, "Switches over closed sets", has the rule that makes the build fail at a switch that misses one.

## Decisions

The user chose these on 2026-10-22, from two designs.

1. The form of a closed set follows from the shape of its cases, not from how hot the code is that uses it.
   - Cases that are structs give a generated union struct. A built-in `union` declaration boxes a struct case: a measurement on SDK 11.0.100-rc.1 found 24 bytes per value.
   - Cases that are classes give a built-in `union` declaration, such as `public union MessageTarget(ToEntity, ToTopic, ToFrame, ToEveryone);`. It is an 8-byte struct around one reference, and it allocates nothing beyond the case.
   - Nothing marks hot code by hand, so no mark can be missing or wrong.
2. The generator stores the cases by shared fields. A positional record struct case is stored as its fields. A positional record struct is a record struct with a parameter list, such as `readonly record struct PointGravity(double Mu)`. Fields with the same name and the same type in several cases share one slot. 17 hand-written unions share fields so by hand today. Every slot is a plain field, so the JIT can keep a union in registers. The other design overlapped whole cases with an explicit layout. That design is never larger for cases without references, but the JIT does not split a struct with overlapped fields into registers.
3. The epic converts all 32 unions, one ticket per module. The union tests of each module guard its conversion.

## The generator

A union declares its cases in kind order with one attribute on a partial struct:

```csharp
[GeneratedUnion(typeof(PointRenderLight), typeof(SpotRenderLight))]
public readonly partial struct RenderLight;
```

`GeneratedUnionAttribute` lives in Tertius.Core, in the namespace `Tertius`. The generator finds it by its full name. The generator is the project `analyzers/Tertius.Unions`, a Roslyn incremental generator for netstandard2.0. Every project in `src` and `tests` references it as an analyzer from `Directory.Build.props`.

### Storage

| Case type | How the union stores it |
|---|---|
| A positional record struct whose properties are its parameters, with no other instance state | As its fields. A field shares a slot with every field of the same name and type in another case. |
| Any other struct: a plain struct, a record with more state, a record whose property initializers change a parameter, another union | As one whole field of the case type, shared with no other case. |
| A class | Not in a generated union. Decision 1 gives class cases a built-in `union`. |
| A ref struct | Not possible. No field can hold a ref struct. |

The generator rebuilds a field-stored case with its constructor, so a case must come back with the same bits. A property initializer that changes a parameter, such as a normalized direction, breaks that, so the generator stores such a case whole. Today every struct case is a positional record, except three plain event structs of the demo: `Dock`, `LeaveShip` and `PlayWithBoxes`. They take their full size.

The kind byte is the last field. The slots come before it, the largest first, so that the struct has the least padding.

### Generated members

| Member | What it does |
|---|---|
| A constructor per case | Stores the case and its kind. |
| An implicit conversion per case | `RenderLight light = new PointRenderLight(...)`. |
| `HasValue` | False only for the empty value. |
| `Value` | The held case, boxed, or null. For debugging only. |
| `TryGetValue(out T)` per case | If the union holds the case, true and the case. Otherwise false and `default`. |
| `IEquatable<T>`, `Equals`, `GetHashCode`, `==`, `!=` | The kind and the fields of the held case. Each field compares with its own `Equals`, so NaN equals NaN, as the records compare. |
| `ToString` | `RenderLight(PointRenderLight { ... })`, or `RenderLight(none)`. |

Hand-written members stay in the partial struct: factories such as `GravityField.Point`, properties such as `IsValid`, and attributes such as `[JsonConverter]`.

### The empty value

The empty value is the all-zero default. It holds no case, and `HasValue` is false. A switch handles it with a `null` arm. The compiler never demands that arm, so a switch without it throws for the empty value.

A union whose all-zero value must be a real case names that case: `[GeneratedUnion(typeof(Parked), typeof(Ramp), ..., ZeroCase = typeof(Parked))]`. The demo's state machines use this, so that a new component starts in its first state. Kind 0 is then that case, `HasValue` is always true, and the union has no empty value. Its switches need no `null` arm.

## The analyzer

The analyzer lives in the same project as the generator. Its rules are errors, so it lands last, after every union is converted.

| Rule | Reports | Fix |
|---|---|---|
| `TU0001` | A hand-written `[Union]` struct | `[GeneratedUnion]` |
| `TU0002` | A built-in `union` with a struct case, which boxes the case | `[GeneratedUnion]` |
| `TU0003` | A `[GeneratedUnion]` with a class case | A built-in `union` |
| `TU0004` | An abstract record or class whose subtypes in the assembly are all sealed, and which is not `closed` | `closed record`, or a union |
| `TU0005` | A struct or class with an enum field or property named `Kind`, beside other instance fields | A union of one case per kind |

`TU0005` is a heuristic. A type that it reports by mistake suppresses it with a reason.

## Conversion

Each module gets one ticket. A union's tests stay as they are. The exception is a test of a member that the generator writes in another way, such as the text of `ToString`.

| Module | Unions |
|---|---|
| Core | Target, GravityField, MediumField, HemisphereUp, RenderLight, QueryShape, MotionIntent, PuffShading, InputEvent, FrameOutline |
| Physics | CharacterStartVelocity, VelocityShadow |
| Simulation | QueryFilter, PhysicsQuestion, LightData, SpatialIndex.Placement |
| Rendering | TransparentRun |
| Host | CaptureTarget, ScriptStep, MessageTarget, ScriptRunner.ReadyStep, as built-in unions |
| Demo | AscentState, RendezvousState, DockingState, DockingEvent, DisembarkState, DisembarkEvent, ClipState, BoxPlayState, BoxPlayEvent, PassengerState, LoftedHull.PaneCut |

The size of a union must not grow where a comment says that its size matters: `HemisphereUp` (16 bytes, one per rendered instance), `PuffShading` (16), `RenderLight` (56), `FrameOutline` (48), and `InputEvent`, which must fit the event payload. Each conversion asserts the size in a test.

`Target` packs every case into one 64-bit number, and every event carries one. Shared fields can make it larger. The Core ticket measures the generated size. If it grows, `Target` stays hand-written, with a suppression of `TU0001` that names the reason. It is the one exception that this design expects.

`CharacterStartVelocity` and `VelocityShadow` have no equality members today. The generated ones cost nothing that the code calls.

## Closed sets that are not unions yet

The survey found three. Each gets its own ticket after the analyzer, because each changes engine data, not only boilerplate:

- The numeric frame arrays of `FrameTree`: `numeric` holds the mode of each frame, and `stepAcceleration`, `stepPlan` and `anchor` mean something only in some modes.
- `KeplerOrbit`: `AxialTilt`, `SpinRate` and `SpinPhaseAtEpoch` mean something only for `KeplerAttitude.Spin`.
- `QueryPlan`: its `Index` decides which fields mean something. It is a ref struct that holds a span, so it cannot be a union, and it stays as it is.

## Not in this epic

- Generic unions. No union in the code is generic.
- A JSON converter from the generator. The three unions with converters keep their hand-written ones.
