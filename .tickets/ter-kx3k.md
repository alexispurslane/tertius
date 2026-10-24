---
id: ter-kx3k
status: closed
deps: []
links: []
created: 2026-10-22T12:58:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-nn6z
tags: [tooling, host]
---
# Convert the class unions of Host to built-in unions

CaptureTarget, ScriptStep, MessageTarget and ScriptRunner.ReadyStep have class cases, so they become built-in union declarations (docs/plans/union-tooling.md, decision 1). Keep the JSON converters. Check that ScriptUnionTests still means what it says.

## Acceptance Criteria

The four are built-in unions. Every suite passes.


## Notes

**2026-10-22T13:11:55Z**

Built-in unions on SDK 11.0.100-rc.1 take a body with members, attributes ([JsonConverter] works), doc comments, interfaces (IEquatable<T>) and private or internal nesting. The compiler writes one constructor per case, object? Value, [Union] and IUnion; no HasValue, no TryGetValue, ValueType equality with no ==, and a ToString that gives the type name. A hand-written one-parameter public constructor is error CS9374, so a null case cannot be refused: it makes the default value, the nullable analysis reports a maybe-null case at build time, and CheckSteps and HeadlessPlayback refuse the default value at run time. Kept: equality members and ToString. Removed: constructors, Value, HasValue, TryGetValue (callers use is-patterns). A switch expression without a null arm compiles silently, as it did for the hand-written unions.
