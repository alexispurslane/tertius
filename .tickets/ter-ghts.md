---
id: ter-ghts
status: open
deps: [ter-2ku2]
links: []
created: 2026-10-24T16:40:01Z
type: task
priority: 2
assignee: Alexis Purslane
---
# Style analyzer, second batch: the other machine-checkable rules of STYLE.md

Add to analyzers/Tertius.Style, after ter-2ku2, every rule of STYLE.md that a machine can check, as errors, each with tests, hand mutations, and fixes of the existing code in the same branch. Candidates: a positional pattern or deconstruction of a non-tuple type with more than one element (use a property pattern); a default method without [MethodImpl(AggressiveInlining)] on an interface that appears in a struct or unmanaged constraint; a class that is neither sealed, abstract nor static and has no subtype in the compilation; an inheritance chain of source types more than 2 levels below object; a finalizer; a method named Try... that does not return bool with an out parameter; an IDisposable local created with new that is not in a using declaration and is never returned, stored or passed on; a conditional expression nested in a branch of another (ticket ter-0om4 unwinds the existing ones); a readonly field or in parameter whose type is a type parameter constrained to struct and an interface; a conversion of such a type parameter to an interface type, which boxes it; a static class named *Helper(s), *Util(s) or *Extensions without extension members. Also raise IDE0063 (simple using statement) so the cleanup turns using blocks into declarations. Each rule names its STYLE.md section. Count the hits in today's code first and report them per rule.

