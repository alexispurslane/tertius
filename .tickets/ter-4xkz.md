---
id: ter-4xkz
status: open
deps: [ter-tzf6]
links: []
created: 2026-10-20T17:00:58Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo]
---
# Demo shuttle: a beveled tram shape with a glass canopy over the sides, the nose and the roof

The user asked on 2026-10-20 for huge panoramic windows all around the shuttle, so that a viewer sees what goes on outside and that the shuttle moves while things move inside it, and for a nicer shape than a box. The user chose the two looks below from two designs.

Settled by the user:
- Shape: the tram. Flat sides and a flat belly, beveled long edges, and a nose that narrows and slopes over its last 3 m like a train's cab, with a short tail.
- Glass: a canopy. A band of glass along both sides and around the nose, from about 0.6 m above the cabin floor, that goes on over the beveled roof edges and the roof, with thin pillars and ribs about every 2 m. The cabin lamps move from the roof to the walls.

Constraints from the run so far:
- The shape stays within today's outer size, 6 by 4 by 16 m (Shuttle.Hull), because the station's slot (DemoStation, 1 m to spare on each side), the launch ramp, the pad and the boarding are built around it.
- The belly stays flat: the shuttle rides the ramp on it, and lands on the deck on it in step 5.
- The panes are glass with no body, as on the orbit scene's ship. The cabin's collision walls stay the solid box of Shuttle.Interior, so the walk, the seat, the pen and the float do not change.
- The hatch stays on the starboard wall, and the glass breaks around it. The pen's clip sits on the port wall at head height (Clip.cs), which becomes glass: move the clip to a pillar or say why not.
- The outer collision shape becomes the convex hull of the new outer shape. The mass stays. Shuttle.Inertia must equal what physics derives for that hull.
- A demo mesh generator lofts cross-sections along the length, with the glass cut out and drawn as panes, and its tests (a closed mesh, normals out, the glass where the design says).
- The cameras in the shuttle (cabin, pen, window, port window, cockpit) and the chase camera's framing are placed again, the window tests and the station-in-window run tests change on purpose, scripts/blue-danube.json is written again, and the launch, boarding and run tests stay green.
- Screenshots: the person inside seen from outside through the glass, and the outside seen from inside, on the ground, in orbit and at the station.

The ticket writes its design as a section of docs/plans/blue-danube-demo.md first. It replaces the small windows that ter-tzf6 built.


## Notes

**2026-10-20T18:46:20Z**

The live world design (docs/plans/live-world.md, part 3) needs the hull to stand on its tail on a flat pad. So the tail is a flat face across the whole cross-section of the hull, or it has landing feet at its corners. The engine's nozzle is recessed into that face, and the center of mass stays on the long axis.
