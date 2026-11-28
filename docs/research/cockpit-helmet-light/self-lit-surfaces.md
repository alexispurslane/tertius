# Self-lit surfaces that send their glow into the light volume

The user decided on 2026-11-28 (#42) that a self-lit surface sends its full glow into the light volume, gray or
colored. Today `restrainNeutral` scales the glow by its saturation: a saturation under 0.58 keeps 2%, a saturation over
0.78 keeps 100%, and a smooth step lies between. This list shows which surfaces of the cockpit, the hangar deck and the
hallway change with that decision.

## Which surfaces send glow

`glowSent` (`src/Tertius.Rendering/Shaders/frame.glsl`) returns a material's emission only when its entity holds no
light. `volume_surface.frag` captures that value. An entity that holds a light sends no glow, because its light
already lights the scene. So the demo's lamp panels are not in the list:

| Lamp | Source | Holds a light |
|---|---|---|
| Shuttle ceiling panels `lamp-ceiling-*` (5) and wall sconces `lamp-{port,starboard}-{fore,aft}` (4) | `DemoScenes.Shuttle.cs`, `CeilingLamps`, `Lamps`, `SpawnLamps` | yes |
| Station bay lamps `bay-lamp-*` | `DemoScenes.DemoStation.cs`, `SpawnBayLamps` | yes |
| Station caged sconces (`industrial_caged_sconce.gltf`, glow0 to glow3, white) | `DemoScenes.StationProps.cs`, `LampLight` on each sconce | yes |
| Orbit cabin lamps `cabin-lamp-0` to `cabin-lamp-4`, `airlock-lamp` | `DemoScenes.cs`, `SpawnCabinLamps`, `Orbit` | yes |

The demo code has no other emissive material in these three scenes. The self-lit surfaces without a light all come from
the cooked art models. Their emission is `emissiveFactor` times `KHR_materials_emissive_strength`, as
`GlbReader.Cooked` reads it, in scene-linear radiance (W/(m²·sr)). The art model holds no light of its own.

## The list

Saturation is (largest channel − smallest channel) ÷ largest channel. "Kept today" is the share of the glow that
`restrainNeutral` lets into the volume. With the decision, every row keeps 100%.

| Model | Material | Nodes | Emission (R, G, B) | Saturation | Kept today |
|---|---|---|---|---|---|
| lunarshuttle.model | Cabin_Display | Cabin_Details | (0.045, 0.155, 0.195) | 0.77 | 99% |
| lunarshuttle.model | lunarshuttle_fuselage.glow0 | shuttle | (4.45, 4.08, 2.47) | 0.45 | 2% |
| lunarshuttle.model | lunarshuttle_fuselage.glow1 | shuttle | (11.3, 10.7, 6.63) | 0.41 | 2% |
| lunarshuttle.model | lunarshuttle_fuselage.glow2 | shuttle | (17.8, 16.8, 11) | 0.38 | 2% |
| lunarshuttle.model | lunarshuttle_fuselage.glow3 | shuttle | (51.6, 49.5, 34.5) | 0.33 | 2% |
| orbital_station_2-2k.model | tunnels.glow0 | hub | (3.18, 3.21, 3.18) | 0.01 | 2% |
| orbital_station_2-2k.model | tunnels.glow1 | hub | (4.32, 4.41, 4.32) | 0.02 | 2% |
| orbital_station_2-2k.model | tunnels.glow2 | hub | (5.32, 5.37, 5.32) | 0.01 | 2% |
| orbital_station_2-2k.model | tunnels.glow3 | hub | (7.54, 7.55, 7.54) | 0.00 | 2% |
| orbital_station_2-2k.model | facade.glow0 | hub | (4.96, 5.09, 4.92) | 0.03 | 2% |
| orbital_station_2-2k.model | facade.glow1 | hub | (12, 12.2, 11.7) | 0.04 | 2% |
| orbital_station_2-2k.model | facade.glow2 | hub | (20.3, 21.5, 17.9) | 0.17 | 2% |
| orbital_station_2-2k.model | facade.glow3 | hub | (42.2, 42.1, 42.2) | 0.00 | 2% |
| orbital_station_2-2k.model | floor.glow0 | hub | (6.54, 6.71, 4.71) | 0.30 | 2% |
| orbital_station_2-2k.model | floor.glow1 | hub | (14.5, 14.5, 5.17) | 0.64 | 26% |
| orbital_station_2-2k.model | floor.glow2 | hub | (30.1, 30.3, 9.37) | 0.69 | 59% |
| orbital_station_2-2k.model | floor.glow3 | hub | (53, 53.1, 52.9) | 0.00 | 2% |
| orbital_station_2-2k.model | facade_window.glow0 | hub | (17.2, 17.3, 17.2) | 0.01 | 2% |
| orbital_station_2-2k.model | facade_window.glow1 | hub | (20.8, 20.6, 20.8) | 0.01 | 2% |
| orbital_station_2-2k.model | facade_window.glow2 | hub | (23.3, 23, 23.3) | 0.01 | 2% |
| orbital_station_2-2k.model | facade_window.glow3 | hub | (26.9, 26.6, 26.9) | 0.01 | 2% |
| orbital_station_2-2k.model | Backlit lettering | 14   /   RIM LIFT, HUB   /   14, RIM   /   ARRIVALS | (0.149, 0.164, 0.14) | 0.14 | 2% |
| orbital_station_2-2k.model | Transit blue enamel | 14   /   RIM LIFT fascia, HUB   /   14 fascia, RIM   /   ARRIVALS fascia | (0.000375, 0.00135, 0.00195) | 0.81 | 100% |
| orbital_station_2-2k.model | Window Curtained | Cabin glazing | (10.8, 7.2, 3.9) | 0.64 | 22% |
| orbital_station_2-2k.model | Window Cabin | Cabin glazing | (55.2, 39, 21.6) | 0.61 | 7% |
| orbital_station_2-2k.model | Window Galley | Cabin glazing | (40.2, 48, 49.8) | 0.19 | 2% |

## By scene

- **Cockpit** (the 1:41 shot, scene `demo-surface`, the shuttle art of `DemoScenes.Shuttle` through `art: models?.Shuttle`).
  The cyan `Cabin_Display` already keeps 99%, so the decision does not change it. The fuselage glows `glow0` to
  `glow3` are warm gray (saturation 0.33 to 0.45) and go from 2% to 100%, which is 50 times more. They sit on the
  node `shuttle`, the hull. Whether they lie inside the cockpit's 16 m volume and face the cabin needs a render: case 2
  of the cockpit set ("today with unrestrained glow") shows it.
- **Hangar deck** (the station of the demo film, `DemoScenes.DemoStation`, art `orbital_station_2-2k`). All glow is on
  the node `hub` and the cabin glazing. The tunnel, facade and window glows and the floor's `glow0` and `glow3` are
  white or near white and go from 2% to 100%. The floor's `glow1` and `glow2` (yellow) go from 26% and 59% to 100%.
  The warm windows `Window Curtained` and `Window Cabin` go from 22% and 7% to 100%. The `Backlit lettering` of the
  signs goes from 2% to 100%. The blue enamel of the sign fascias keeps 100% and is dim.
- **Hallway** (`scripts/hallway-walk.json`, scene `orbit`, gray box). It has no self-lit surface without a light. The
  decision does not change it.

Tool: `tools/glow_list.py <model.glb>...` prints the table rows from the cooked models in
`content/tertius.demo/bin/assets/blue-danube/`.
