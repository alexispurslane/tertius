---
id: ter-ghts
status: closed
deps: [ter-2ku2]
links: []
created: 2026-10-24T16:40:01Z
type: task
priority: 2
assignee: Alexis Purslane
---
# Style analyzer, second batch: the other machine-checkable rules of STYLE.md

Add to analyzers/Tertius.Style, after ter-2ku2, every rule of STYLE.md that a machine can check, as errors, each with tests, hand mutations, and fixes of the existing code in the same branch. Candidates: a positional pattern or deconstruction of a non-tuple type with more than one element (use a property pattern); a default method without [MethodImpl(AggressiveInlining)] on an interface that appears in a struct or unmanaged constraint; a class that is neither sealed, abstract nor static and has no subtype in the compilation; an inheritance chain of source types more than 2 levels below object; a finalizer; a method named Try... that does not return bool with an out parameter; an IDisposable local created with new that is not in a using declaration and is never returned, stored or passed on; a conditional expression nested in a branch of another (ticket ter-0om4 unwinds the existing ones); a readonly field or in parameter whose type is a type parameter constrained to struct and an interface; a conversion of such a type parameter to an interface type, which boxes it; a static class named *Helper(s), *Util(s) or *Extensions without extension members. Also raise IDE0063 (simple using statement) so the cleanup turns using blocks into declarations. Each rule names its STYLE.md section. Count the hits in today's code first and report them per rule.


## Notes

**2026-10-24T16:49:25Z**

Severity, the user's choice on 2026-10-24: every style rule is a warning (DiagnosticSeverity.Warning), which TreatWarningsAsErrors in Directory.Build.props turns into a build error, like a compiler warning. This batch also moves the union analyzer's TU rules from Error to Warning, so that all our analyzers work one way.

**2026-10-24T19:13:59Z**

Built on branch ter-ghts-style-rules-2. Every rule below is in, as a warning that TreatWarningsAsErrors makes a build error. The hit counts are from the Checked build of main at e771003, before any fix, counted once per position.

- TS0004, positional pattern or deconstruction: 88 hits in the first form. 16 of them deconstructed a framework type (15 KeyValuePair in foreach, 1 ITuple on object). Decision: the rule reports only a type that the solution declares (the compilation, or an assembly whose name starts with Tertius), because a framework type does not reorder its Deconstruct. That left 72, which the new code fix turned into property patterns. The SemanticRange Deconstruct in FrameTree.Numeric.cs went away by hand. Tuples are out of scope (TS0001 covers their names).
- TS0005, default interface method without AggressiveInlining on an interface of a struct or unmanaged constraint, with a code fix: 0 hits.
- TS0006, a class that is neither sealed, abstract, static nor closed and has no subclass in its compilation, with a code fix: 331 hits, 307 in tests and 24 in benchmarks. The fix sealed the test classes. The benchmark classes must stay open for BenchmarkDotNet, so .editorconfig turns TS0006 off in benchmarks. The class that the compiler declares for top-level statements is not reported.
- TS0007, inheritance deeper than 2 levels: 0 hits. Decision: only solution classes count. The first base from outside the solution is the root, so a framework base is level 0.
- TS0008, finalizer: 0 hits.
- TS0009, Try method that does not return bool with an out parameter: 5 hits, all test names such as TryGetValueOfAnotherCaseIsFalseAndGivesTheDefault. .editorconfig turns TS0009 off in tests. Overrides and interface implementations are not reported.
- TS0010, IDisposable local created with new, not in a using, and never handed on: 2 hits (AdhocWorkspace in the style tests), fixed with using. Handed on means any use other than a member access, a conditional access or an index, or any use in a lambda or local function.
- TS0011, nested conditional: 62 hits (47 src, 14 tests, 1 analyzers), all rewritten as guards, if/else, a switch or small methods. This closes ter-0om4.
- TS0012, readonly field or in parameter of a type parameter constrained to struct and an interface: 9 hits in the declaration form, all false positives (marker interfaces, PoseTree.Add(in TPose)). Decision: the rule reports the call of a member on such a field or parameter, which is where the defensive copy happens. 0 hits in that form.
- TS0013, conversion of such a type parameter to an interface: 0 hits.
- TS0014, static helper class without extension members: 0 hits.
- IDE0063 raised to warning: 0 hits.
- The union analyzer's TU rules and the generator's TU1001 to TU1007 are warnings now.

**2026-10-24T19:45:39Z**

Unused code, added on 2026-10-24 at the user's request: .editorconfig raises IDE0051 (unused private member), IDE0052 (private member written but never read) and IDE0060 (unused parameter) to warnings. Hits before the fix, Debug and Release builds: IDE0051 3 (test constants FrameTreeGhostPlaceTests.Step and NumericFrameOrbitTests.Dt, the test method RenderSnapshotOutlineTests.SphereRadius), IDE0052 1 (PhysicsHandOffTests.AnyVelocity, which only the Release branch of #if TERTIUS_CONTRACTS reads, so it moved into that branch), IDE0060 4 (FrameSpin.Sample's step, which only matched KeplerOrbit.Sample and went away with its argument at 25 call sites, which then left FrameSpinTests.Tick unused; the first row of two demo check panels, whose CheckPanel delegate fixes the signature, now named _; SurfaceYardReport.Scene's measurements, removed). IDE0060 stays a warning: only the two check panels were a required shape.
