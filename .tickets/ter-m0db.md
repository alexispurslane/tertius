---
id: ter-m0db
status: closed
deps: [ter-qfxn]
links: []
created: 2026-10-22T12:58:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-nn6z
tags: [tooling, demo]
---
# Convert the unions of Demo to the generator

Convert AscentState, RendezvousState, DockingState, DockingEvent, DisembarkState, DisembarkEvent, ClipState, BoxPlayState, BoxPlayEvent, PassengerState and LoftedHull.PaneCut to [GeneratedUnion] (docs/plans/union-tooling.md, section Conversion). Keep hand-written members in the partial struct. Assert the size of each union whose size a comment names. The state machines use ZeroCase for their first state, and their switches lose the null arms.

## Acceptance Criteria

No hand-written union boilerplate is left in Demo. The union tests pass unchanged, except tests of ToString text. Every suite passes.


## Notes

**2026-10-24T14:20:48Z**

Converted all 11 Demo unions to [GeneratedUnion]. Every one names a ZeroCase, its first case today, so kind order, the all-zero value and HasValue stay as they were: AscentState=Parked, RendezvousState=Standby, DockingState=Standby, DisembarkState=Aboard, ClipState=Clipped, BoxPlayState=Idle, PassengerState=Boarding, PaneCut=CellPane. The event unions keep their all-zero value as their first event too: DockingEvent=Dock, DisembarkEvent=LeaveShip, BoxPlayEvent=PlayWithBoxes (BoxPlayStateTests pins default(BoxPlayEvent) == PlayWithBoxes; the other two take the same rule, no code reads their default). Sizes (Unsafe.SizeOf, before -> after): AscentState 64->64, RendezvousState 64->64, DockingState 16->16, DockingEvent 24->24, DisembarkState 24->24, DisembarkEvent 1->2 (LeaveShip is a plain struct, stored whole as a 1-byte slot), ClipState 8->8, BoxPlayState 40->40, BoxPlayEvent 12->12, PassengerState 16->16, PaneCut 24->24. Null arms removed from five switch statements (Ascent, Rendezvous, Disembark, BoxPlay, Passenger). Generator: DockingEvent's Lost.Reason hit the CS8618 of a reference slot that not every case fills; main's fix from the Simulation ticket covers it (merged in, my duplicate dropped). One generator change of this ticket: a ZeroCase union's Value is object, never null (demo tests call Value.GetType()), pinned by TheValueOfAUnionWithAZeroCaseIsNeverNull.
