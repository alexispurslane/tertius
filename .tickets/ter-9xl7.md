---
id: ter-9xl7
status: closed
deps: []
links: []
created: 2026-10-24T22:41:17Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [core, style]
---
# Result<T, TProblem> in Core, and the STYLE.md rules for failures and nulls

The user decided on 2026-10-24: a method whose result can be invalid or missing returns T?, and null is that result. When the caller needs the reason, the method returns Result<T, TProblem>, a hand-written custom [Union] struct (no boxing, measured 0 bytes for 2,000,000 reads) with nullable extension properties Ok and Problem, so that a fallback reads Parse(s).Ok ?? x. Null may mean a possibly-invalid result or a valid absence, never one ordinary state among many. The core never sees a nullable type. A Try method stays only where a benchmark shows that it matters, with a comment. The union generator does not support type parameters, so Result is the one hand-written union, with TU0001 suppressed and the reason given.


## Notes

**2026-10-24T22:43:59Z**

Built: Result<T, TProblem> in src/Tertius.Core/Unions/Result.cs, a hand-written [Union] struct with TU0001 suppressed (the generator has no type parameters), extension properties Ok (class and struct forms) and Problem. 8 tests in ResultTests, including 0 bytes allocated for 10,000 reads of a struct value. Hand mutations, all killed: M1 value constructor holds a problem, M2 problem constructor holds a value, M3 HasValue always true, M4 Value gives the problem for a value, M5 Value gives the value for a problem, M6 TryGetValue(T) always true, M7 TryGetValue(TProblem) always true, M8 class Ok always null, M9 struct Ok always null, M10 Problem always null. STYLE.md: Nulls (two meanings of null) and Expected failures (T?, Result, no Try methods without a benchmark). TS0009 still checks the old Try shape until ter-h8hm.
