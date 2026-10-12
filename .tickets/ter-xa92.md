---
id: ter-xa92
status: open
deps: [ter-7e55]
links: []
created: 2026-10-11T16:50:25Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-tanj
tags: [design]
---
# Generate the create and box code of each event payload type, instead of reflection in the script runner

The script runner (ter-hynj) turns an event name such as InputEvent into an event, and an event back into a boxed payload for state dumps. Today src/Tertius.Host/Scripting/EventPayloads.cs does this with reflection: it scans the loaded assemblies for structs that implement IEventPayload, binds CreateEvent<T> and BoxPayload<T> with MakeGenericMethod, and checks each found type's layout (ProblemOf) because the compiler never checked it. The user decided on 2026-10-11 to replace this with a source generator.

## Design

Decision (User): a source generator writes the per-type code. A payload struct carries an attribute, for example [EventPayload]. For each one, the generator writes a registration into a [ModuleInitializer] of the same assembly: the C# name, a create function (target, sender, boxed payload to Event) and a box function (Event to boxed payload). The Host maps the name to that entry and calls it, with no scan and no MakeGenericMethod. The generated code names the type, so the compiler checks the unmanaged constraint, and ProblemOf goes away. The size check stays in EventTypeRegistry.Register.

Considered and not chosen: (1) EventType<T> hands the registry the two functions in its initializer, which needs no generator, but a script could name only types that running code had used. (2) Keep the reflection.

Open points: Stryker 5.0.0 bundles Roslyn 5.9 and cannot load generators built for a newer compiler (project memory, tertius-testing-stack), so the generator must reference an older Microsoft.CodeAnalysis. A payload without the attribute is invisible to scripts: decide whether the generator takes every struct that implements IEventPayload instead, or an analyzer warns. System.Text.Json still finds a payload's fields by reflection inside the library.

## Acceptance Criteria

The Host has no assembly scan and no MakeGenericMethod. Scripts and dumps behave as before, and the Host tests pass. A payload type with a reference field fails to compile. Stryker runs on the projects that use the generator.

