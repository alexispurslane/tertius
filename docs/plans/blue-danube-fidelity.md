# Blue Danube, second film

The shuttle and station must each have one layout for the surfaces seen from both sides, the surfaces reached by a person, and the openings that the route passes through. Measure those openings in the source models. A dark window decal needs an opening and glazing before a person can look through it.

1. Fit the shuttle cabin to the hull. Keep the existing source model and preserve its silhouette. Test the sightline from the seat and the doorway from the boarding stairs. Opaque panels must block the view in both directions.
2. Fly through an existing hub port and land on its interior deck. Carry the passenger from that deck through one of the actual spokes in a working elevator to a furnished rim landing. Draw one floor wherever there is one physical surface.
3. Identify the blue wash and expanding circles in the first film. Use matched captures with one change at a time. Separate diagnostic overlays, surface depth, lighting and postprocessing before choosing a fix.
4. Use material references appropriate to each surface on the hull, in the cabin and station, and on the planet. Preserve asset provenance and the normal demo entry point.
5. Use the pilot's rig for a walk cycle and for the hand that takes the pencil. Verify the contacts in moving shots.

Review short matched clips first. The full release film uses the established Runpod route, readouts and Marine Band edit. Retain the first cut for comparison.

## Berth 14 and the rim lift

Pioneer's `station_docking_B.dae` places berth 14 at native coordinates (−104.6094, 0.0822, −3.3508) m. With the station art in engine coordinates the marker lies at (−104.6094, 11.2492, −0.0822) m. The four adjacent floor triangles are at x = −104.611 m. They extend from y = −78.99 to 99.76 m and z = −20.81 to 20.81 m. The marked point sits between the shuttle's wheels and within its parked footprint. The approach follows the common axial entrance and turns toward this floor. The source, revision and licence are recorded in the station import sidecar.

The shuttle wheels touch the imported floor. Four static boxes support it around the lift opening. The car travels down the negative-X spoke inside the original exterior tube. Conversion cuts the 4.10 m deck opening and the 15 m shaft through the back of the hub and the near side of the rim. The outer rim remains solid. The Blender check in `tools/blue-danube-assets/check_dock.py` reads the cooked triangles and checks the flight path, the deck edges around the opening and clearance down to the rim.

Each landing has a sliding gate on the station. The lift closes the deck gate on departure and opens the rim gate at rest. Its glazed walls, roof, handrail and floor form one kinematic body. The station's floor radius is 2,000 m. At the deck, the spin gives 0.513 m/s², rising to 9.80665 m/s² at the rim.
