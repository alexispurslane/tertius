---
id: ter-tiei
status: closed
deps: []
links: []
created: 2026-11-25T15:25:33Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-qdn8
---
# Fit the playable Lunar Shuttle cabin to its hull, glazing and hatch

Use the existing model pipeline. Give the exterior and walkable cabin the same actual window and doorway openings, opaque wall backs, plausible textured materials, and camera views in both directions. Keep the working flight, entry and seat.

## Notes

**2026-11-25T16:58:00Z**

Fitted the Lunar Shuttle cabin, glazing, boarding doorway, stowable hatch door, PBR masks and tangent-space normal relief on branch `t3/fitted-shuttle-cabin`.

- Root cause of sight through opaque exterior: the legacy 40-pane `LoftedHull` tram sat inside an uncut `lunar_shuttle_hi.dae` whose materials had backface culling enabled (`doubleSided: false`), so interior cameras saw straight through the solid exterior hull.
- Asset pipeline (`tools/blue-danube-assets/shuttle_cabin.py`, `finish.py`, `convert.py`, `lunarshuttle.model.import.json`): cuts all 21 measured window openings (5 cockpit, 16 side cabin) and the starboard boarding doorway (`Z = [2.20, 3.40]`, `Y = [-1.40, 0.80]` in shuttle axes), sets opaque hull backs (`doubleSided: true`) on `shuttle` and separate stowable `hatch_door`, builds the watertight single-sided fitted cabin interior, clears painted window glow, and generates material-zone metallic/roughness and tangent-space normal maps for hull and interior surfaces.
- Runtime and contact contract (`src/Tertius.Demo/DemoScenes.Shuttle.cs`, `DemoScenes.DemoSurface.cs`, `ShuttleHatchControl.cs`, `BlueDanubeArt.cs`, `DemoScenes.StationExit.cs`, `BlueDanube.cs`):
  - Envelope preserved: `ArtScale = 1.6`, `ArtPlace = (0, 0, 1)`, `ArtBounds = (-4.325, -3.063, -10.013)..(4.325, 2.731, 8.166)`, `Hull = (-6.92, -4.9008, -15.0208)..(6.92, 4.3696, 14.0656)`.
  - Floors: `AftFloorY = -1.50` (`Z = [-3.00, 4.00]`), 10-degree center ramp (`Z = [-8.70, -3.00]`), `CockpitFloorY = -0.50` (`Z = [-13.60, -8.70]`), `AftBulkheadZ = 4.00`, `CeilingY = 3.10`.
  - Seat and stand: `Seat = (-0.30, -0.50, -11.20)..(0.30, 0.00, -10.60)`, `SeatStand = (0, 0.40, -10.15)`, eye at `(0, 1.15, -10.15)` facing `-Z` through `pane-nose`.
  - Pencil clip: `PenClip = (-2.534, 0.60, -6.40)` on the port rib, `PenHalf = (0.006, 0.006, 0.07)`, `PenPush = (0.2, 0.03, 0)`, `PenSpin = (0.3, 0.4, 0)`.
  - Starboard boarding doorway and porch: `Hatch = (4.85, -1.40, 2.20)..(5.95, 0.80, 3.40)`, outer hull sill `X = [5.654, 5.927]`, outward wall normal `(+0.9448, +0.2473, -0.2150)`, `Walkway` stair lip `X = [7.220, 7.520]`, `Y_top = -1.400`, `Y_bot = -1.430`, `Z = [1.90, 3.70]`.
  - Mapped station contact via `Layout.ParkedPose`: `StationExit.StairPlace = (-98.7342, +24.2344, +3.220)` (`3.4658 m` above `Deck = -102.2`), 10-degree ramp `Run = 19.6555 m`, ramp foot at `Y_st = +3.779`, `AtFoot` at `Y_st = +3.379` (`1.33 m` clear of `LiftHole.Max.Y = +2.05`, `Z_st = +4.02` in lift lane). Existing `StationExit.StairPlace` formula stays valid without station changes.
  - Clear dielectric glazing: 21 panes in `Shuttle.Glass` with `BaseColor.W = 0`, `GlassAlpha`, `AntiReflective = 0.18`, smooth with no normal map.
  - Flight hatch and bridge control (`ShuttleHatchControl`): hides `shuttle-hatch-door` and shows `porch` when `BodyMotion.Static`, reverses when `BodyMotion.Dynamic`.
