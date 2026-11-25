---
id: ter-9wwx
status: in_progress
deps: []
links: []
created: 2026-11-24T23:56:27Z
type: feature
priority: 2
assignee: Alexis Purslane
---
# Fit Blue Danube with a kilometre-scale inhabited wheel and licensed scene models

## Design

Keep the station orbit and docking port on axis. Size the inhabited floor at 2,000 m radius and derive the angular velocity from standard gravity. Use the same floor and rotation for the visual ring, frame, character route and docking. Preserve the smaller spin-station test scene. Dress the station, shuttle, passenger, and inhabited rooms with identified freely licensed model parts at human scale through the demo rendering path; retain physics hulls where they represent the finished art. Prefer a coherent industrial passenger terminal and readable silhouettes. Keep sources, licenses and adaptation notes with the assets.

## Acceptance Criteria

The normal Blue Danube sequence launches, docks and reaches the usable rim. Real renderer captures show the ring, fitted shuttle, character and furnished spaces. Verify actual rim gravity/rate, clearances, credits and normal entry from a fresh checkout.


## Notes

**2026-11-25T00:19:36Z**

The art direction is a working orbital port. The selected exterior models are the Pioneer Lunar Shuttle and the orbital station by the Pioneer developers (CC BY-SA 3.0). The new habitat layer adds four-deck cabin galleries, lit concourses and windowless service bays to both axial faces of the ring. Its Blender check exports and reloads the GLB and verifies outward window faces, embedded panel normals and lighting levels. Removing outward faces, window emission, panel normals or raised arcades each fails the check. These are art-build checks. The integrated film and the normal launch still need verification.
