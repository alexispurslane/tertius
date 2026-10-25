# Test style for Tertius

This guide covers the code in `tests`. The rules of [STYLE.md](../STYLE.md) for `src` apply here too, with the changes below. The style analyzer checks the named tuple elements (tool: TS0001) and the named arguments past 3 parameters (tool: TS0002) in `tests` as in `src`. Each is a warning, an error through `TreatWarningsAsErrors`, so a hit fails the build. The rule against `!` (tool: TS0003) holds for `src` only: a test may use `!` on a value that it knows. The other rules of the style analyzer, TS0004 to TS0015, hold in `tests` as in `src`, such as sealed test classes (tool: TS0006), with one exception: the rule against a method that returns `bool` and gives its result in an `out` parameter (tool: TS0009) holds for `src` only, because a test helper or a fake may keep the shape that the Try methods of .NET have. The workflow around tests, such as hand mutations, coverage and the review of snapshot images, is in CLAUDE.md.

## Names

A test name is a statement of one behavior, in the words of the domain, written as a sentence in PascalCase. A reader who sees only the list of names learns what the system does.

```csharp
public void TryGetValueOfAnotherCaseIsFalseAndGivesTheDefault()
public void TwoCasesWhoseSharedSlotHoldsTheSameVectorAreNotEqual()
public void ABaseWithAnAbstractMemberIsNotReported()
```

- Name the behavior, not the method under test: not `TryGetValue_WrongCase_ReturnsFalse`.
- A name that needs "and" for two unrelated facts is two tests.
- A test class holds the tests of one type or one behavior, and its doc comment says what they pin.

## Shape of a test

- One test pins one behavior. It sets up, acts once, then asserts.
- Assert with AwesomeAssertions, and give a reason where the expected value is not obvious:

```csharp
up.TryGetValue(out AwayFrom away).Should().BeFalse();
away.Should().Be(default(AwayFrom), "the slot that the two cases share holds the direction, not a center");
```

- A test spells out its own setup. When the next test repeats that setup, keep the repetition, so that a reader understands each test alone. The commit report checks neither duplicates nor complexity in `tests` for this reason, but it still reports a test method longer than 60 lines.
- A setup that many tests share goes into a helper in `tests/Tertius.Testing`, such as `Scenes` or `WorldTesting`. The name of the helper says what it builds.
- A test that locks in a decision tests the behavior that depends on the decision, not the constant that encodes it.

## Long tests

xUnit runs the test classes at the same time, but the tests of one class one after another. So a test or a theory that runs for seconds, such as ten minutes of ticks, goes into a nested class of its own inside its class. The nested class keeps the outer class's private helpers, and it runs beside the rest of the class. Its doc comment names what it holds and points here.

```csharp
/// <summary>The ten minutes of the fast hull on rails (tests/STYLE.md, "Long tests").</summary>
public sealed class FastHullForTenMinutes
{
    [Fact]
    public void
        AFastKinematicHullStandsExactlyOnItsOrbitInsideItsFollowingFrameAtEverySyncPointFromTheSecondAfterItsSpawn()
    ...
}
```

## Test doubles

- Every test of physics runs the real World on the real Jolt backend. Never write a fake backend: a fake drifts from the code that it copies.
- The one physics test double is `FaultInjectingBackend` in `tests/Tertius.Testing`. It wraps real Jolt, edits its input and output, and throws on request. A test of a state that only a bug produces edits the output of one step with `EditOutput`.
- A test of a valid hand-off flies a ball into a bubble with `Scenes.ApproachToBubble`.
- `IComponent` methods take ref struct parameters, which no mocking library can substitute. Write a small component by hand for each test need:

```csharp
/// <summary>A flag: the entity burns.</summary>
public sealed class Flammable : Flag;
```

## Determinism

- Most tests run on one thread, so that they are deterministic and fast. A separate test covers parallel determinism.
- A renderer test turns on deterministic rendering with the one toggle in `RendererOptions`.
- A test never depends on the time of day, a random seed that it does not set, or the order of a hash set.
