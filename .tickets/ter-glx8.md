---
id: ter-glx8
status: closed
deps: [ter-uucg]
links: []
created: 2026-10-11T20:28:58Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [build, correctness]
---
# Make the build fail at every switch that misses a member of a closed set

Today no switch in src/ gets an exhaustiveness check from the compiler. All 18 switch expressions end in a discard or throw arm, and that arm turns the check off. The compiler never reports a missing case in a switch statement or an if/else-if chain. About 28 dispatch sites accept a new member of a closed set silently. Eight of them are on MotionDriver:

- AdvanceFrames (World.Sync.cs).
- HullMotion (JoltPhysicsBackend.Bodies.cs).
- FrameHullProblem (PhysicsSeam.cs).
- PredictMotion (JoltPhysicsBackend.cs).
- The apply chain and the validity tests of SetFrameDriver (World.Entities.cs).
- The validity tests of PrepareSpawn and the driver chain of MakeLive (World.Entities.cs).

The others are on BodyMotion, BodyKind, KeplerAttitude, MemoryUsage and VertexInput. More are on the unions Target, GravityField and HemisphereUp, and on the ShapeDesc and ScriptStep hierarchies.

Research on 2026-10-11 used builds on SDK 11.0.100-rc.1. It found what the RC1 compiler reports:

- Unions and closed types: a missing case fails the build with CS8509. This holds for a switch expression with no discard arm. Target, GravityField and HemisphereUp are custom [Union] structs, and closed is a C# 15 modifier. A `null` arm covers the empty union, the all-zero value with no case. A check on 2026-10-12 in GravityField.IsValid found that it compiles with a `null` arm in place of the discard arm, and that it fails with CS8509 once an arm for PointGravity is missing.
- Enums: the same, but only with CS8524 suppressed. CS8524 reports unnamed values such as (E)7. TreatWarningsAsErrors makes it an error, so every enum switch expression has a discard arm today. With CS8524 off, an unnamed value throws SwitchExpressionException at run time.
- Abstract records with a private protected constructor (ShapeDesc, ScriptStep, MessageTarget): the compiler never reports a missing case. It demands a discard arm.
- Switch statements: the compiler never reports a missing case, and no language proposal plans it.

The user chose IDE0010 for the switch statements that remain. IDE0010 (Populate switch) is a code-style analyzer that ships with the .NET SDK. If EnforceCodeStyleInBuild is true and its severity is error, it fails the build. It reports an enum switch statement that does not name every member, even one with a default case. Only every member plus a default case satisfies it. It also demands a default case on every other switch statement. That includes statements over unions and closed types, where it cannot find a missing case.

## Design

The rule for every switch and if/else-if chain over a closed set: make it a switch expression wherever that is natural. The compiler reports a missing case in a switch expression for every kind of closed set, but IDE0010 covers only enums. Two shapes are natural:

- Most arms already give a value, for example by a return or by an assignment to one variable. Make every arm give a value. Then return or assign the switch expression once. An arm that also sets an out parameter can give a tuple instead.
- The switch only makes a decision, and the code after it is the same for every arm. Make the decision a value: a switch expression gives the part that varies, as a value, a tuple or a small record. The shared code then runs once. For example, the validity tests of SetFrameDriver and PrepareSpawn can become one switch expression. It gives the problem text or null, and one throw follows.

Keep a switch statement with a default case only where statements are really the most natural form. Examples are arms that run different loops, return early from the method, or change several locals of an enclosing loop.

Steps:

1. In Directory.Build.props, add CS8524 to NoWarn. Set EnforceCodeStyleInBuild to true in the same file. Add an .editorconfig at the repository root with dotnet_diagnostic.IDE0010.severity = error. Leave IDE0072 off. It demands a discard arm on enum switch expressions, and that conflicts with the CS8524 approach. If EnforceCodeStyleInBuild turns on other rules that fail the build, set those rules back to their default severity.
2. Remove the discard arms from the enum switch expressions that give a value, and name every member instead. The sites are:
   - ToMotionType and HullMotion (JoltPhysicsBackend.Bodies.cs).
   - The BodyKind switch (JoltPhysicsBackend.Growth.cs).
   - MemoryUsage (DeviceAllocator.cs) and VertexInput (GraphicsPipelineDesc.cs).
   - ImageUse and BufferUse (ResourceState.cs).

   Keep the Enum.IsDefined guards on public inputs. Do not change the flags enums (RenderFlags, IntentFlags, SlotFlags), because code tests them bitwise.
3. Give ShapeDesc, ScriptStep, MessageTarget and ReadyStep the closed modifier. ShapeDesc is in PhysicsSeam.cs, ScriptStep and MessageTarget are in Script.cs, and ReadyStep is in ScriptRunner.cs. Write closed record, not closed abstract record, which gives error CS9384. Remove their private protected constructors. Seal every leaf record, because closed does not pass down to grandchildren. Remove the discard arms of the switch expressions over them. Keep the JsonDerivedType attributes.
4. In Target.cs, replace the discard arm of ToString with a null arm. The null arm matches default(Target), the state with no value.
   Do the same for the unions GravityField and HemisphereUp (src/Tertius.Core/Frames/GravityField.cs and src/Tertius.Core/Seams/HemisphereUp.cs). Their empty values are no gravity and the even mix. Each switch expression over their case types ends in a discard arm for the empty value today:
   - GravityField.IsValid (`_ => true`) and GravityField.ToString.
   - HemisphereUp.ToString.
   - The switch in GpuInstance.From (GpuTypes.cs) over a HemisphereUp.

   Replace each discard arm with a null arm. Two more switches have guards or property patterns, which do not count toward exhaustiveness: HemisphereOfFrame (RenderSnapshotBuilder.cs, over the frame's GravityField) and the choice of the up in ImpostorManager.cs (over a HemisphereUp). Give each an unguarded arm for the rest of each case, and a null arm.
5. Apply the rule above to every switch statement and if/else-if chain that dispatches on an enum. These are candidates for switch expressions:
   - GravityField.At (GravityField.cs), where every arm returns a vector. Its default case is the empty field, so the switch expression gets a null arm.
   - ContactEntity (JoltPhysicsBackend.HandOff.cs). Its arms set an out parameter and return, so they can give a tuple.
   - PredictMotion, where every branch returns a FrameMotion.
   - The validity tests of SetFrameDriver and PrepareSpawn.
   - The BodyMotion chains in JoltPhysicsBackend.Step.cs and Bodies.cs that only choose a value.

   Merge the MotionDriver chains into as few switches as the code allows. Give FrameHullProblem one arm per driver, without a guard clause. Guard clauses do not count toward exhaustiveness. IDE0010 covers every enum switch that stays a statement.
6. Apply the same rule to the switch statements over unions and closed types. A statement that stays gets a default case that throws, as IDE0010 demands. No tool finds a missing case in these statements at build time, so keep them few. Today the likely ones are:
   - Router.Expand (Router.cs, over Target). Each arm runs its own loop. It has no default case now, so a Target with no value goes to nobody.
   - ScriptRunner.Run and MakeReady. They return early from the method and change a loop variable.
   - Script.CheckSteps, which is partial on purpose.
   - DeviceAllocator.Free, over an object owner.

   Before you leave one as a statement, make sure that a statement really is more natural.
7. Make sure that each new build error works. Add one member at a time to MotionDriver, BodyMotion, BodyKind and KeplerAttitude. Add one case at a time to GravityField and to HemisphereUp: a constructor with a new case type, and its TryGetValue method. Add one subtype at a time to each closed hierarchy. Each time, see the build fail at every dispatch site, and then revert. Record the result in the commit message.

Not in scope:

- About 30 single-member tests such as Motion != BodyMotion.Static. They mean "anything but this one" on purpose.
- The hand-kept copies of sets: Layers.Count = 4 and its pair table, the seven key reads in FlyCamera, and the JsonDerivedType lists. A test can guard these copies, but a switch rule cannot.
- The private byte tag inside GravityField and HemisphereUp (`kind`), and the switches on it in Value, Equals and GetHashCode. A byte is not a closed set, so no compiler rule covers it. The tests of both unions cover each case.

Speed: statements and expressions go through the same lowering code in the compiler. A switch on types tests the types in arm order, so put the hot arms first. A sealed leaf type makes each type test cheaper. A switch over a union calls TryGetValue once per arm.

## Acceptance Criteria

A new member or subtype fails the build at every switch that dispatches on it. This holds for MotionDriver, BodyMotion, BodyKind and KeplerAttitude. It also holds for a new case of GravityField or HemisphereUp, and for ShapeDesc, ScriptStep, MessageTarget and ReadyStep. The only exceptions are the statement switches over unions and closed types in step 6, which throw at run time. No switch expression over a closed set has a discard arm. Every suite passes.

## Notes

**2026-10-18T21:23:39Z**

ter-ru5u removed World.SetFrameDriver and BodyDesc.FrameHullProblem, with the MotionDriver enum and BodyDesc.FrameHullMotion. A change of motion is now the intent Self.Body.SetMotion, which World.WritableHullMotionIntent guards. The hull rules live in BodyDesc.Problem(BodyRole), with the roles OwnsNoFrame, Hull and OwnsRootFrame. So the items of this ticket on FrameHullProblem and the MotionDriver chains have no code left to change.

**2026-10-19T11:39:20Z**

Change to step 3 (2026-10-19): ter-q8ge makes ScriptStep, MessageTarget and the runner's ReadyStep custom [Union] structs, not record hierarchies. So step 3 applies only to ShapeDesc. A switch expression over a union already gets the compiler's check for every case, with a null arm for the empty union (as step 4 says for Target). The runner's switch statements over the step union keep a throw for the empty union.

**2026-10-22T12:43:25Z**

Done on 2026-10-22. CS8524 off, EnforceCodeStyleInBuild on with AnalysisModeStyle=None, IDE0010 at error and no other IDE rule in the build (code-quality-tooling decision 14, ARCHITECTURE.md 'Switches over closed sets'). Check of step 7: one new member or case at a time fails the build at every switch expression over it (CS8509) and every enum switch statement (IDE0010). BodyMotion: 6 sites. BodyLayer: 1. FollowKind: 3. NumericMode: 2. KeplerAttitude: 4 (its equality tests became switch expressions). MotionIntentRefusal: 1. BodyKind: 3. QueryIndex: 1. MemoryUsage, VertexInput, BlendMode, ImageUse, BufferUse: 1 each. GravityField: 4. HemisphereUp: 3. Target: 3 (Router.Expand stays a statement that throws). MediumField: 5. PhysicsQuestion: 11. ShapeDesc (closed record): 3. ShapeKind has no switch. Union switch statements that stay name their quiet cases and throw UnreachableException in the default case. Open sets keep a discard arm: strings, ints, Silk.NET keys (PersonControl.Hold suppresses IDE0010 with a reason), flat state machine transitions, and the private kind tag inside each union struct, which the union generator (ter-nn6z) will write.
