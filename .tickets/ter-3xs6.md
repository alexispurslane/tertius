---
id: ter-3xs6
status: open
deps: [ter-dhkf]
links: []
created: 2026-10-17T14:34:17Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-uu1x
tags: [physics, frames]
---
# Set the numeric frame thresholds from the precision measurements

Part of the epic on fewer, more reliable numeric frames. Read the epic first.

- Two create speeds. A group with a body that thrusts keeps CreateSpeed 50 m/s and LeaveSpeed 20 m/s. A group of free bodies only gets about 1 km/s. Keep it just below a power of two, so that one float spacing covers the whole range below it (1,000 m/s is below 1,024), with the same leave ratio, 400 m/s.
- What counts as a body that thrusts is an open design point. Design it twice when the ticket starts. Candidates: a member that is a character, a member that got a force intent in a recent window, or a flag that a component sets. The drift grows with the time that the thrust lasts and not with its size, so a single impulse does not count.
- Distance thresholds. The measurements found no error from distance up to 9,000 km. Raise CreateDistance from 10 km to the measured range, for example 2^23 m (8,389 km), and LeaveDistance to half of it. Check the interaction with the nesting frames for vast distances.
- Replace "These values are guesses. Tests tune them." in the doc with the measured table and the float model (rules A, B and C) that explains it.
- Tests that throw at 60 m/s to get a falling frame must throw faster, or use a character.

