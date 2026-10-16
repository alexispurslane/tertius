---
id: ter-8wmm
status: open
deps: [ter-ziae]
links: []
created: 2026-10-12T00:10:14Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-1yed
tags: [simulation]
---
# Give the inbox the shared query words, and replace its special methods

Design: docs/plans/queries-and-sensors.md, section Event queries.

The user chose one set of query words for an inbox (EventList) and for entities (User). The inbox gets the words now, and the entity query of ter-gj55 uses the same ones.

Words that the user chose:

- Pick by type: Of<T>() on an inbox. An entity query uses With<T>() and Without<T>(), because an entity has many component types and an event has one.
- Filter by value: Where(predicate), with a static lambda, and Where(state, predicate), which passes one value without a capture.
- Filter by sender: From(sender).
- Endings: foreach over ref T, Count(), Any(), TryFirst(out T), and Remove(), which removes the matches from the inbox and returns their number.
- Order: inbox order.

The user chose to replace the special methods with the chain, so each question has one way to ask it: Contains<T>() becomes Of<T>().Any(), CountOf<T>() becomes Of<T>().Count(), RemoveAll<T>(predicate) becomes Of<T>().Where(predicate).Remove(), and RemoveAll(predicate) becomes Where(predicate).Remove(). IndexOf<T>() goes away. Its callers are in src/Tertius.Simulation/Events/EventList.cs, src/Tertius.Simulation/World.Tick.cs and the Simulation and Core tests.

## Acceptance Criteria

EventList and EventsOf<T> offer the words above, and the special methods are gone. No query allocates, which the allocation tests show. Every caller uses the chain. The early exit on the event mask stays, so a type that the inbox does not hold costs no scan.


## Notes

**2026-10-12T00:18:00Z**

The query has a stored form too (see the note of ter-ziae): EventQuery<T>, run with events.Find(query). events.Of<T>() stays as the shorthand chain, and both forms share one filter implementation.

**2026-10-12T00:26:52Z**

Events keep only Of<T> (User): no With, Without or event type mask in an event query. EventQuery<T> holds From(sender) and Where, and the payload type is its type parameter.
