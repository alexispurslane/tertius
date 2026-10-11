---
id: ter-glx8
status: open
deps: [ter-uucg]
links: []
created: 2026-10-11T20:28:58Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [build, correctness]
---
# Make the build fail at every switch that misses a member of a closed set

Today the compiler checks no switch in src/ for exhaustiveness. All 18 switch expressions end in a discard or throw arm, which turns the check off, and switch statements and if/else-if chains are never checked. About 28 dispatch sites would accept a new member of a closed set silently. Eight are on MotionDriver: AdvanceFrames (World.Sync.cs), HullMotion (JoltPhysicsBackend.Bodies.cs), FrameHullProblem (PhysicsSeam.cs), PredictMotion (JoltPhysicsBackend.cs), the apply chain and the validity checks of SetFrameDriver, the checks of PrepareSpawn, and MakeLive (World.Entities.cs). Others are on BodyMotion, BodyKind, GravityKind, KeplerAttitude, MemoryUsage, VertexInput, the Target union and the ShapeDesc and ScriptStep hierarchies.

Research on 2026-10-11, with builds on SDK 11.0.100-rc.1, found what the RC1 compiler checks:
- A switch expression with no discard arm over a C# 15 union, over the custom [Union] struct Target, or over a type with the C# 15 closed modifier: a missing case fails the build with CS8509.
- A switch expression with no discard arm over an enum: the same, once CS8524 is suppressed. CS8524 reports unnamed values such as (E)7, and TreatWarningsAsErrors makes it an error, which is why every enum switch expression has a discard arm today. With CS8524 off, an unnamed value throws SwitchExpressionException at run time.
- An abstract record with a private protected constructor (ShapeDesc, ScriptStep, MessageTarget): never checked. The compiler demands a discard arm.
- A switch statement: never checked by the compiler, and no language proposal plans it.

The user chose IDE0010 for the switch statements that remain. IDE0010 (Populate switch) fails the build, when it is set to error and EnforceCodeStyleInBuild is true, on an enum switch statement that does not name every member, even when it has a default case. It is satisfied only by every member named plus a default case. It also demands a default case on every switch statement that is not over an enum, including statements over unions and closed types, which it cannot check.

## Design

Steps:
1. In Directory.Build.props, add CS8524 to NoWarn and set EnforceCodeStyleInBuild to true. Add an .editorconfig at the repository root with dotnet_diagnostic.IDE0010.severity = error. Leave IDE0072 off: it demands a discard arm on enum switch expressions, which conflicts with the CS8524 approach. Make sure that EnforceCodeStyleInBuild adds no other build errors; set any rule that it turns on to its default severity if it does.
2. Remove the discard arms from enum switch expressions that return a value, and name every member: ToMotionType and HullMotion (JoltPhysicsBackend.Bodies.cs), the BodyKind switch (JoltPhysicsBackend.Growth.cs), MemoryUsage (DeviceAllocator.cs), VertexInput (GraphicsPipelineDesc.cs), ImageUse and BufferUse (ResourceState.cs), and GravityField.IsValid (FrameTypes.cs), which needs Enum.IsDefined first because its discard arm rejects forged values today. Keep the Enum.IsDefined guards on public inputs. Flags enums (RenderFlags, IntentFlags, SlotFlags) stay as they are, because code tests them bitwise.
3. Give ShapeDesc (PhysicsSeam.cs), ScriptStep and MessageTarget (Script.cs) and ReadyStep (ScriptRunner.cs) the closed modifier: write closed record, never closed abstract record (error CS9384). Remove their private protected constructors, seal every leaf record (closed does not pass down to grandchildren), and remove the discard arms of the switch expressions over them. Keep the JsonDerivedType attributes.
4. In Target.cs, replace the discard arm of ToString with a null arm, which matches default(Target), the state with no value.
5. Turn every if/else-if chain that dispatches on an enum into a switch. A switch expression is best where the arms give a value. A switch statement is enough elsewhere, because IDE0010 checks it. Merge the MotionDriver chains into as few switches as the code allows. FrameHullProblem needs one arm per driver with no when guard, because guards do not count toward exhaustiveness.
6. Switch statements over unions and closed types that cannot become expressions keep a default case that throws, which IDE0010 demands; the compiler does not check them. Today these are Router.Expand (Router.cs, over Target: it has no default case now, so a Target with no value delivers to nobody), ScriptRunner.Run and MakeReady (early returns and a changing loop variable), Script.CheckSteps (partial on purpose), and DeviceAllocator.Free (over an object owner). Where a statement can become an expression, convert it.
7. Make sure that each check works: add a member to MotionDriver, BodyMotion, BodyKind, GravityKind and KeplerAttitude, and a subtype to each closed hierarchy, one at a time, and see the build fail at every dispatch site. Then revert. Record the result in the commit message.

Not in scope: about 30 single-member tests such as Motion != BodyMotion.Static, which mean 'anything but this one' on purpose; and the hand-kept copies of sets (Layers.Count = 4 and its pair table, the seven key reads in FlyCamera, the JsonDerivedType lists). A test can guard those copies; a switch check cannot.

Speed: statements and expressions lower through the same compiler code. A switch on types tests the types in arm order, so put hot arms first; sealed leaf types make each test cheaper. A switch over a union calls TryGetValue once per arm.

## Acceptance Criteria

A new member of MotionDriver, BodyMotion, BodyKind, GravityKind or KeplerAttitude, or a new subtype of ShapeDesc, ScriptStep, MessageTarget or ReadyStep, fails the build at every switch that dispatches on it, except the statement switches over unions and closed types in step 6, which throw at run time. No switch expression over a closed set has a discard arm. Every suite passes.

