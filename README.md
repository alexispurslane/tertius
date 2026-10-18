# Tertius

Tertius is a game engine in C# for space-scale scenes and dense immersive-sim interactions at once. It runs on Jolt Physics in double precision and draws with a deferred Vulkan renderer. It is an engine, not a game. It is unfinished, and it changes fast.

This README says what is different about the physics of Tertius. Then it tells you which code and which tests to read, so that you can make sure that each claim is real.

## What is different

These terms have the same meaning here and in the code:

- A frame is a coordinate system that moves. Every position and velocity in Tertius is relative to a frame.
- A bubble is the sphere or ellipsoid of a frame. An object inside the bubble belongs to the frame.
- A semantic frame is a frame that the game places, such as a star system, a planet, a ship or a room.
- A numeric frame is a frame that the engine makes and removes by itself, only for precision. Gameplay never sees it.
- A group is a set of bodies that the merge test links. The merge test links two bodies that can touch within 2 s.
- A Jolt system is one Jolt physics world. Each frame has its own.
- The sync point is the step between two ticks, where the engine applies changes.

### 1. Large worlds

A 32-bit float holds about seven digits. Far from the origin of a world, small steps round away, so objects shake and contacts fail. Tertius runs Jolt in double precision, and it also keeps the numbers small.

The engine measures the center of each slow group from the origin of its semantic frame. At 2^23 m (about 8,389 km) or more, the group gets a resting frame at its center. A resting frame is a numeric frame that does not move. Each group gets its own frame, so this works at every place in the world at once.

A double at 2^23 m steps by 2^-29 m, about 2 nm. That number is the resolution of a position, not a measured error. The measurements found no error from distance up to 9,000 km in thrust and ballistic drift.

Frames that lie very far apart nest. A numeric frame more than 10^9 m from the origin of its parent moves under a nesting frame on a grid of 2^20 m. The game places only the frames that mean something, such as a star system. The engine makes the rest. A test keeps millimeter steps one light-year out. The design doc states that the grid stays exact to about a million light-years. By that number, two levels of frames that a game places cover the whole observable universe. One frame holds the universe. One frame holds each region up to about a million light-years across, such as a galaxy. The engine makes all the frames inside.

### 2. Ships in ships

A planet, a ship, a station and a room can each be a frame. A frame is an entity with an extra handle into the frame tree, so it moves with the object that owns it. A frame is not a separate level. Frames nest. For example, a fighter that flies into the bubble of a carrier becomes a child of the carrier.

A body moves into a frame on the first touch of its bubble. When it is fully outside the bubble plus a margin, it leaves. That move is a hand-off, and the entity keeps its handle.

A planet that turns has two frames. One frame goes round the Sun and does not turn. The other frame turns and holds the ground. A room is a frame with no hull. Tests cover rooms, but no demo scene has one yet.

Numeric frames carry semantic frames too. A ship that falls at orbit speed hangs in a falling frame, and its cargo rides along. A falling frame falls freely in the gravity of its parent, and the ship falls with it. Only an orbit frame follows a path: the orbit of a frame on rails (see claim 6).

### 3. Objects across an edge

A body can stick out of the bubble of a ship. Then the frame on the other side holds a ghost of the body. A ghost is a kinematic copy that follows the real body. Bodies on the other side collide with the ghost, so the real body touches objects on both sides at once. The ghost does not push back on its real body. The engine reports a contact that spans two Jolt systems one time.

The merge test for numeric frames also counts the places of ghosts. So a fast body that can hit a ghost joins the Jolt system that holds the ghost.

### 4. Fast objects

Jolt keeps velocities in 32-bit floats, also in double-precision mode. At 7,672 m/s, a speed in low orbit, a float velocity steps by 0.000488 m/s. So small pushes round away. With numeric frames off, a person who thrusts for one minute at that speed gains 355 mm/s of velocity error. That is in the harder of the two directions that the measurements tried. The error grows about linearly, so the position error after the minute is about 10 m, an estimate and not a measurement.

Bodies that move fast together share a falling frame. A falling frame is a numeric frame that falls freely, with its motion computed in doubles. Inside it, the velocities are small, so their floats keep the small pushes. A group of free bodies gets a falling frame from 1,000 m/s. A group with a body that thrusts gets one from 50 m/s.

A test turns numeric frames on and lets a person thrust for a minute near a ship in orbit. The error of the person's velocity relative to the ship stays below 1 mm/s.

### 5. Thrust that you feel

The linear acceleration of a frame acts on everything inside it. This is the proper acceleration, the value that an accelerometer reads. A ship that thrusts pushes its loose cargo toward the stern. A ship in orbit falls freely, so its proper acceleration is zero and its cargo floats. No special code makes the cargo float. Tertius applies no Coriolis or centrifugal force.

The demo has no ship engine yet. The tests prove the weight from thrust with loose crates, not with a person who stands on the deck.

### 6. Orbits and turns

A frame can follow a Kepler orbit, or it can turn at a steady rate. A Kepler orbit is the oval path of a body round another body under gravity. The engine computes both in doubles, as functions of time, so they do not drift. A frame that moves this way is on rails: a formula gives its motion, not Jolt. In the demo, Earth goes round the Sun this way, and its ground turns once per sidereal day.

A frame that moves fast on an orbit hangs in an orbit frame. An example is the station of the demo, 400 km up at 7.67 km/s. At each sync point, the engine puts the orbit frame exactly on the orbit. During a step, the orbit frame falls with the acceleration of the orbit. So the bodies near the station move with small velocities, as in a falling frame.

### 7. Spin gravity is not complete

A spin frame turns and carries what rests on it by friction. No centrifugal, Coriolis or Euler force acts on its contents yet. So a station that spins gives no gravity today. The spin gravity epic plans these forces, in [spin-gravity.md](docs/plans/spin-gravity.md).

### Determinism

The same inputs give the same world, bit for bit, whether gameplay runs on one worker thread or eight. The tests compare runs on one machine with one build. Tertius makes no claim about determinism across machines.

## Check it yourself

Each part below lists the code to read, in order, and then the tests that prove the claim. The line numbers are from the commit that added this README. Every physics test runs the real World on the real Jolt backend. The only test double wraps real Jolt to inject faults, as [real-physics-in-tests.md](docs/plans/real-physics-in-tests.md) explains.

[ARCHITECTURE.md](ARCHITECTURE.md) is the binding design. Its section "Reference frames" covers claims 1 to 6. The design doc [numeric-frames.md](docs/plans/numeric-frames.md) gives the reasons for every threshold.

### 1. Large worlds

Read this code in this order:

1. [NumericFrameOptions.cs](src/Tertius.Core/Frames/NumericFrameOptions.cs), lines 60 to 94. `CreateDistance` is `1 << 23` m and `LeaveDistance` is `1 << 22` m. `NestDistance` is `1e9` m and `NestGrid` is `1 << 20` m.
2. `Mode` at line 171 of [NumericFrameRules.cs](src/Tertius.Core/Frames/NumericFrameRules.cs). A fast group gets `Falling`. A slow group at the create distance or farther gets `Resting`.
3. `Summarize` at line 404 of the same file. It gives the mass-weighted center of a group. Static bodies count as infinitely heavy.
4. `UpdateNumericFrames` at line 305 of [FrameTree.Numeric.cs](src/Tertius.Core/Frames/FrameTree.Numeric.cs). Only this method makes, moves and removes numeric frames, once per sync point.
5. `MoveHome` at line 877 of the same file. It makes the new frame at the center of the group.
6. `NestFarFrames` at line 933 and `Nest` at line 948 of the same file. Look at the comment at line 983 on what rounds.
7. Line 44 of [joltc.nix](native/joltc.nix). It builds Jolt with `DOUBLE_PRECISION` on.
8. Line 28 of [JoltRuntime.cs](src/Tertius.Physics/Internal/JoltRuntime.cs). The engine does not start without the double-precision library.

Look for these limits:

- The rule measures the center of a group, not each body.
- Below 2^22 m, the resting frame goes away.
- If a body is in the Jolt system of its semantic frame, it waits 1 s before it moves to a new resting frame.
- Anchors, such as static bodies, can hold a group in that system farther out.
- The place of a frame in its nest rounds at about 10^-7 m.
- Reads between nearby bodies do not round, because they go through their nearest common frame.

These tests prove it:

- `A_group_at_the_create_distance_gets_a_resting_frame` and `A_slow_near_group_needs_no_frame` in [NumericFrameRulesTests.cs](tests/Tertius.Core.Tests/NumericFrameRulesTests.cs). A group at 8,388,608 m gets `Resting`. A group at 8,388,607 m gets no frame.
- `ACrateAtRestOnAFloor10000KilometersOutSharesARestingFrameWithTheFloorAndStaysAtRestOnIt` in [NumericFrameThresholdTests.cs](tests/Tertius.Physics.Tests/NumericFrameThresholdTests.cs). The crate stays within 1 mm of its place and below 1 mm/s.
- `ACrateALightYearOutKeepsMillimeterStepsRelativeToAPodNextToIt` in [WorldNumericNestingTests.cs](tests/Tertius.Physics.Tests/WorldNumericNestingTests.cs). Each step of about 1.04 mm reads true within 0.1 mm, in the world, the snapshot and the render reads.
- `ANestingFrameJumpsByWholeGridStepsAndTheFramesBelowItKeepMillimeterSteps` in the same file.
- `A_position_in_a_nested_frame_a_light_year_out_rounds_once_in_the_semantic_frame` in [FrameTreeNumericUpdateTests.cs](tests/Tertius.Core.Tests/FrameTreeNumericUpdateTests.cs). At one light-year a double steps by 2 m, so a read in a far frame rounds at that distance.

The measurements with numeric frames off are in [numeric-frames.md](docs/plans/numeric-frames.md), section "The measurements". Thrust drift and ballistic drift did not change at any distance up to 9×10^6 m. The gap error between two boxes grew by 0.42 µm from 1.5×10^6 m. Contacts changed by at most 18 nm up to 6×10^5 m. The tests are in [Precision](tests/Tertius.Physics.Tests/Precision). No test pins the double-precision build mode directly, but a float build cannot pass these runs.

### 2. Ships in ships

Read this code in this order:

1. [Entity.cs](src/Tertius.Core/Handles/Entity.cs), lines 24 to 28. A `FrameHandle` is an entity plus a row in the frame tree.
2. `AsFrame` at line 35 and `FrameSpec` at line 66 of [SpawnDesc.cs](src/Tertius.Simulation/Entities/SpawnDesc.cs). Any spawn can be a frame.
3. `Create` at line 88 and `Reparent` at line 219 of [FrameTree.cs](src/Tertius.Core/Frames/FrameTree.cs).
4. [FrameTypes.cs](src/Tertius.Core/Frames/FrameTypes.cs). Read `MotionDriver` at line 7, then `FullyOutside`, `ExitMargin` and `Fits` at lines 55 to 61.
5. `HandOffBodies` at line 105 of [JoltPhysicsBackend.HandOff.cs](src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs). The exit test is at line 132, the fit test at 159 and the entry test at 162.
6. `ExecuteHandOff` at line 258 of the same file. The body moves to the other Jolt system, and the entity keeps its handle.
7. [DemoScenes.cs](src/Tertius.Demo/DemoScenes.cs): the ship at line 129, Earth at line 259 and the ground of Earth at line 269.
8. [DemoScenes.Station.cs](src/Tertius.Demo/DemoScenes.Station.cs): the station at line 209 and its pods at line 264.

These are the hand-off rules. The exit margin is the larger of 2 % of the largest radius of the bubble and the radius of the body. If the largest radius of a frame is less than half of the smallest radius of another frame, it can enter that frame. Otherwise the two frames stay siblings.

These tests prove it:

- `BallEntersChildBubbleOnFirstTouchAndDoesNotFlipBack` in [HandOffTests.cs](tests/Tertius.Physics.Tests/HandOffTests.cs). The ball enters on its first touch and leaves only past the margin. Real Jolt sends exactly two hand-offs.
- `SmallFrameEntersBigFrameButSimilarFramesStaySiblingsAndCollide` in the same file. A fighter frame nests under a carrier, and its pilot stays in the fighter.
- `ThePassengersOfAFrameOwnerThatWasHandedOffStayInTheFrameAndRideAlong` in [PhysicsHandOffTests.cs](tests/Tertius.Simulation.Tests/PhysicsHandOffTests.cs).
- `AShipFrameUnderAFallingNumericFrameCarriesItsContentsWithIt` in [WorldNumericFrameTests.cs](tests/Tertius.Physics.Tests/WorldNumericFrameTests.cs). The ship moves more than 15 km in 2 s, and its cargo keeps its place within 1 mm.
- `ARoomWithoutAHullInAShipThatDiesInAFallingFrameMovesToSpaceAndHoldsItsPlaceAsWithoutNumericFrames` in [GhostPlaceTests.cs](tests/Tertius.Physics.Tests/GhostPlaceTests.cs). This is the room case.
- `ThePersonDriftsOutOfTheShipsFrameIntoEarthAndBack` in [SpacewalkSliceTests.cs](tests/Tertius.Host.Tests/SpacewalkSliceTests.cs). It runs the real demo script [spacewalk.json](scripts/spacewalk.json).

### 3. Objects across an edge

Read this code in this order:

1. `UpdateGhosts` at line 341 of [JoltPhysicsBackend.HandOff.cs](src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs). The test for a body that sticks out is at line 397.
2. `WantGhost` at line 533 and `GhostAllowed` at line 592 of the same file.
3. The remarks at lines 9 to 34 of [JoltPhysicsBackend.Contacts.cs](src/Tertius.Physics/JoltPhysicsBackend.Contacts.cs). Then read `contactCounts` at line 57 and `CollectContacts` at line 73.
4. `GhostPlace` at line 128, `MayTouch` at lines 230 to 243 and `MeetsGhost` at line 309 of [NumericFrameRules.cs](src/Tertius.Core/Frames/NumericFrameRules.cs).
5. `CollectGhostPlaces` at line 552 and `LinkGhostPlaces` at line 709 of [FrameTree.Numeric.cs](src/Tertius.Core/Frames/FrameTree.Numeric.cs).

Look for the count per pair of entities. At the change of the count from 0 to 1, the engine sends one `Collision`. At the change from 1 to 0, it sends one `CollisionEnded`. The ghost does not push back on its real body, so momentum does not balance across an edge. If an outside body also touches the bubble, it has its own ghost there. Only then does it push the real body.

These tests prove it:

- `GhostExistsOnlyWhileTheBodyStraddlesTheBubble` in [GhostTests.cs](tests/Tertius.Physics.Tests/GhostTests.cs).
- `ABodyInTheParentAndABodyThatSticksOutOfTheChildEachHearTheirContactThroughTheGhost` in the same file. The ball stops at the ghost, and the real block moves less than 10^-6 m. Each side gets one `Collision`. After the ghost goes, each side gets one `CollisionEnded`.
- `AMirrorPairIsReportedOnceAndEndsOnceAfterTheLastSystemDropsIt` in [GhostContactTests.cs](tests/Tertius.Physics.Tests/GhostContactTests.cs). The contact is in two Jolt systems but starts and ends one time.
- `ACrateThatSticksOutOfAFallingShipPushesARockThatRestsInSpaceInTheShipsFallingFrameAsItDoesWithoutNumericFrames` in [GhostPlaceTests.cs](tests/Tertius.Physics.Tests/GhostPlaceTests.cs).
- [FrameTreeGhostPlaceTests.cs](tests/Tertius.Core.Tests/FrameTreeGhostPlaceTests.cs) holds 43 tests of the merge test with ghost places, with no physics.

### 4. Fast objects

Read this code in this order:

1. [NumericFrameOptions.cs](src/Tertius.Core/Frames/NumericFrameOptions.cs), lines 22 to 70. `CreateSpeed` is 1,000 m/s and `LeaveSpeed` is 400 m/s. `ThrustCreateSpeed` is 50 m/s and `ThrustLeaveSpeed` is 20 m/s.
2. `Mode` at line 171 of [NumericFrameRules.cs](src/Tertius.Core/Frames/NumericFrameRules.cs). Lines 177 and 178 choose between the speeds for free groups and the speeds for groups that thrust.
3. `MayTouch` at line 230 of the same file. This is the merge test: two bounds grow by their relative speed times 2 s.
4. `ThrustClock` at line 342 of the same file. A body thrusts after 0.5 s of thrust ticks. It keeps thrusting for 20 s after its last thrust tick.
5. `Thrusts` at line 380 of [FrameTree.Numeric.cs](src/Tertius.Core/Frames/FrameTree.Numeric.cs). Every character thrusts.
6. `AdvanceNumericFrames` at line 1173 of the same file. A falling frame moves in doubles.
7. `ThrustAcceleration` at line 104 of [World.NumericFrames.cs](src/Tertius.Simulation/World.NumericFrames.cs).

The speed of a group is its common speed relative to its semantic frame. The section "Limits" of [numeric-frames.md](docs/plans/numeric-frames.md) lists the cases where precision drops. An example is two bodies that pass each other at kilometers per second.

These tests prove it:

- `A_group_of_free_bodies_gets_a_falling_frame_only_at_the_create_speed` in [NumericFrameRulesTests.cs](tests/Tertius.Core.Tests/NumericFrameRulesTests.cs). 999 m/s gives no frame, and 1,000 m/s gives `Falling`.
- `A_group_with_a_body_that_thrusts_gets_a_falling_frame_at_the_thrust_create_speed` in the same file. 49.99 m/s gives no frame, and 50 m/s gives `Falling`.
- `ACrateThatCoastsThroughEmptySpaceFallsInANumericFrameFromTheCreateSpeedOfAGroupOfFreeBodies` in [NumericFrameThresholdTests.cs](tests/Tertius.Physics.Tests/NumericFrameThresholdTests.cs). It tests the same threshold with real Jolt: 1,000 m/s gives a frame, and 999 m/s does not.
- `APersonWhoThrustsForAMinuteNearTheShipsOrbitGainsLessThanAMillimeterPerSecondOfErrorRelativeToTheShip` in [WorldNumericClusteringTests.cs](tests/Tertius.Physics.Tests/WorldNumericClusteringTests.cs). The person thrusts with 105 N for 60 s at orbit speed and gains more than 80 m/s. The velocity of the person relative to the ship stays within 1 mm/s of the same motion computed in doubles.
- `AGroupThatStartsToFallAtOrbitalSpeedKeepsTheVelocityOfEachBodyToAMicrometerPerSecond` in [WorldNumericFrameTests.cs](tests/Tertius.Physics.Tests/WorldNumericFrameTests.cs).
- `TheSpacewalkerIsWithin5CmOfADoublePrecisionIntegrationOfItsThrust` and `TheCratesAboveAndBelowTheShipDriftAsTheClohessyWiltshireEquationsSay` in [OrbitClusterTests.cs](tests/Tertius.Host.Tests/OrbitClusterTests.cs). The crates stay within 1 cm of the Clohessy-Wiltshire prediction. These tests run the script [orbit-cluster.json](scripts/orbit-cluster.json).

The 355 mm/s comes from the table "Thrust drift" in [numeric-frames.md](docs/plans/numeric-frames.md), with numeric frames off. A character of 70 kg thrusts with 105 N, so at 1.5 m/s². At 7,672 m/s the error is 29, 117 and 355 mm/s after 5, 20 and 60 s. At 1,000 m/s it is 85 mm/s after 60 s, and at 50 m/s it is 2.1 mm/s.

All of these are in the hard direction, where the common velocity points along the diagonal (1, 1, 1). If the common velocity is at right angles to the thrust, speed adds no error. The "about 10 m" is half of 0.355 m/s times 60 s. No test asserts it.

To run the measurements yourself, run this command in the repository root. The runs write CSV files into the folder that `TERTIUS_PRECISION_OUT` names.

```sh
nix develop --command bash -c 'dotnet test --project tests/Tertius.Physics.Tests -c Release -- --filter-namespace Tertius.Physics.Tests.Precision --explicit only'
```

### 5. Thrust that you feel

Read this code in this order:

1. Lines 290 to 295 of [JoltPhysicsBackend.cs](src/Tertius.Physics/JoltPhysicsBackend.cs). Each step applies gravity, steps the Jolt systems, and then computes the proper accelerations.
2. `ComputeProperAccelerations` at line 599 of [JoltPhysicsBackend.Step.cs](src/Tertius.Physics/JoltPhysicsBackend.Step.cs). The change of velocity over the step, minus the gravity that the step applied, gives the value. A dead band hides float noise.
3. `ApplyGravity` at line 299 of the same file. It pushes each awake dynamic body with its mass times the effective gravity.
4. `UpdateCharacter` at line 433 of the same file. Below an effective gravity of 0.05 m/s², a character does not stand.
5. Line 202 of [World.Sync.cs](src/Tertius.Simulation/World.Sync.cs). The sync point copies the proper acceleration into the frame tree.
6. [FrameTree.cs](src/Tertius.Core/Frames/FrameTree.cs): `ProperAcceleration` at line 593, `ZeroGravityThreshold` at line 626, and `EffectiveGravity` at line 635.
7. `FieldOf` at line 656 of the same file. The field of a frame minus its proper acceleration is the gravity that its contents feel.

A Kepler frame reads zero, because it falls freely. A static frame and a spin frame keep the value that they had, so a parked ship keeps the weight of its cargo. In the demo ship, a uniform field of 9.81 m/s² gives the weight in the cabin. No test stands a character on a deck that thrusts. Characters use the same `EffectiveGravity`.

These tests prove it:

- `ThrustingShipPushesLooseCrateTowardStern` in [GravityTests.cs](tests/Tertius.Physics.Tests/GravityTests.cs). The accelerometer of a ship of 1,000 kg under 2,000 N reads 2 m/s² within 0.001 m/s². The crate inside feels −2 m/s² along the ship.
- `LandedShipPassesPlanetGravityToItsInterior` in the same file. The ship reads between 9.7 and 9.95 m/s² up, and the crate rests on the deck.
- `ShipInOrbitIsInFreeFallAndItsCargoFloats` in the same file. On an orbit with a radius of 7,000 km, the proper acceleration is exactly zero for 180 ticks. The cargo moves less than 10^-9 m.
- `InEarthThePersonFallsFreelySoItDoesNotStand` in [SpacewalkSliceTests.cs](tests/Tertius.Host.Tests/SpacewalkSliceTests.cs).

### 6. Orbits and turns

Read this code in this order:

1. [KeplerOrbit.cs](src/Tertius.Core/Frames/Rails/KeplerOrbit.cs): `StateAt` at line 87, `Sample` at line 143 and `Chord` at line 177. The doc comments state what is exact.
2. `MuFrom` at line 66 of the same file. An orbit holds no G·M. It takes G·M from the point gravity of the parent frame.
3. [FrameSpin.cs](src/Tertius.Core/Frames/Rails/FrameSpin.cs): `RotationAt` at line 25 and `Sample` at line 38. Then read [MotionSample.cs](src/Tertius.Core/Frames/Rails/MotionSample.cs).
4. `MotionDriver` at line 7 of [FrameTypes.cs](src/Tertius.Core/Frames/FrameTypes.cs). Today this enum chooses the motion of a frame: `Static`, `Kepler`, `Physics` or `Spin`.
5. [World.Entities.cs](src/Tertius.Simulation/World.Entities.cs): `SetFrameDriver` at line 154, `SetFrameSpin` at line 215 and `SetFrameOrbit` at line 245.
6. `PlaceOnOrbit` at line 49, `CreateOrbitFrame` at line 143 and `LeaveOrbit` at line 167 of [FrameTree.Numeric.cs](src/Tertius.Core/Frames/FrameTree.Numeric.cs).
7. `Orbiting` at line 39 of [NumericMode.cs](src/Tertius.Core/Frames/NumericMode.cs).

The orbit frame takes the place of the orbit and the chord of the orbit over one step as its velocity. The chord is the change of place over the step, divided by the step. With the instant velocity instead, a crate in the orbit frame drifts by half a step of gravity, 7 cm/s in low orbit. The orbit frame never turns. The Kepler frame hangs at its origin and keeps the rotation.

Two demo scenes show this. In the scene `orbit`, Earth is a Kepler frame round the Sun, and the ground of Earth is a spin frame. The ship of that scene is not on rails. It is a `Physics` frame that Jolt lets fall freely. In the scene `station`, the station is a Kepler hull on rails in an orbit frame.

These tests prove it:

- `A_sample_holds_the_place_rotation_velocity_and_angular_velocity_of_the_orbit_at_the_end_of_its_step` in [KeplerOrbitTests.cs](tests/Tertius.Core.Tests/Rails/KeplerOrbitTests.cs). The sample equals the orbit exactly.
- `The_chord_of_a_sample_is_the_change_of_place_over_its_step_divided_by_the_step` in the same file, within 2×10^-7 m.
- `TheFrameTurnsAboutItsPoleByItsRateTimesTheTimeThatPassed` in [FrameSpinTests.cs](tests/Tertius.Core.Tests/Rails/FrameSpinTests.cs).
- `ACircularKeplerFrameIsAtTheOrbitPointForTheTimeThatHasPassed` and `AMoonRidesItsPlanetAlongTheMoonsOwnOrbit` in [KeplerFrameTests.cs](tests/Tertius.Simulation.Tests/KeplerFrameTests.cs).
- `During_a_step_the_orbit_frame_falls_with_the_orbits_acceleration_and_ends_on_the_orbit` in [FrameTreeOrbitFrameTests.cs](tests/Tertius.Core.Tests/FrameTreeOrbitFrameTests.cs).
- `AKeplerStationsHullAloneAtOrbitalSpeedRestsInAnOrbitFrameAndEveryReadInEarthIsTheOrbitsState` in [NumericFrameOrbitTests.cs](tests/Tertius.Physics.Tests/NumericFrameOrbitTests.cs). After 36,000 ticks (10 minutes), the place of the hull equals the orbit to the last bit. Its velocity is within 10^-8 m/s.
- `ThePodsDriftOutOfTheAirlockAndOnInAStraightLineRelativeToTheStationToWithinACentimeterUntilTheyBumpTheRadiator` in [StationSliceTests.cs](tests/Tertius.Host.Tests/StationSliceTests.cs). The design doc reports 2 mm in the run, and 3 cm without the orbit frame.
- `EarthsSurfaceTurnsOncePerSiderealDayAboutEarthsAxis` in [OrbitSliceTests.cs](tests/Tertius.Host.Tests/OrbitSliceTests.cs).

### 7. Spin gravity

Read this code in this order:

1. `ApplySpin` at line 371 of [World.Sync.cs](src/Tertius.Simulation/World.Sync.cs). The spin driver sets only the pose and the angular velocity.
2. The section "Scope" of [ARCHITECTURE.md](ARCHITECTURE.md). It lists Coriolis and centrifugal forces as out of scope today.
3. The comment at line 42 of [DemoScenes.SurfaceYard.cs](src/Tertius.Demo/DemoScenes.SurfaceYard.cs). That scene leaves out the spin of Earth for this reason.
4. [spin-gravity.md](docs/plans/spin-gravity.md), the plan. It adds the terms −2ω×v − ω×(ω×r) − α×r in every frame that turns.

The test `AShipWhoseHullRestsOnTheHullOfASpinFrameIsCarriedRoundByIt` in [WorldSpinFrameTests.cs](tests/Tertius.Physics.Tests/WorldSpinFrameTests.cs) shows what works today. No test of spin gravity exists yet. The numbers in the plan come from a spike on a branch that is not in main. They are not tests of this code. The epic is the ticket `ter-s9m0` in `.tickets`.

### Determinism

Read this code in this order:

1. `RunGameplay` at line 119 of [World.Tick.cs](src/Tertius.Simulation/World.Tick.cs). Gameplay runs in chunks of entities with `Parallel.For`.
2. [Router.cs](src/Tertius.Simulation/Internal/Messaging/Router.cs). Read `Delivery` at line 33. The router sorts deliveries with a stable counting sort.
3. `BeginTick` at line 357 of [EntityTable.cs](src/Tertius.Simulation/Internal/Entities/EntityTable.cs). Each chunk has its own pool of spawn slots, so handles do not depend on thread timing.
4. [ARCHITECTURE.md](ARCHITECTURE.md), lines 148 to 158. Outboxes belong to chunks and merge in chunk order.

These tests prove it:

- `ARunOnEightWorkersEndsInTheWorldOfARunOnOne` in [DeterminismTests.cs](tests/Tertius.Simulation.Tests/DeterminismTests.cs). The run has 160 entities and 200 ticks. The hash, the statistics and the counts are equal.
- `TheRunOnEightWorkersReallyRunsGameplayOnSeveralThreads` and `TheWorkloadRunsBodiesInFallingAndInRestingNumericFrames` in the same file.
- [WorldHash.cs](tests/Tertius.Testing/WorldHash.cs) lists what the hash covers. It covers each handle, pose, velocity, frame and component state, the render snapshot and the error count.
- `TheStationSceneGivesTheSameEventsAndStatesInEveryRunWithOneOrFourThreads` in [StationSliceTests.cs](tests/Tertius.Host.Tests/StationSliceTests.cs).

The test with eight workers keeps Jolt at two job threads. The other tests of this kind compare one and four threads. The Jolt build sets no flag for determinism across platforms.

## Build and run

Every command runs in the Nix dev shell from [flake.nix](flake.nix). The flake defines the shell for aarch64-darwin, x86_64-linux and aarch64-linux. The project is developed on macOS with Apple silicon.

You need these things:

- Nix with flakes turned on.
- Network access the first time. The dev shell builds the double-precision Jolt library from pinned sources.
- A Vulkan device for the renderer, for image captures, and for the Rendering and Host tests. On macOS, the dev shell supplies MoltenVK.

Do these steps in the repository root:

1. Build the solution.

   ```sh
   nix develop --command bash -c 'dotnet build Tertius.slnx'
   ```

2. Run the tests of one module. The modules are Core, Simulation, Physics, Rendering and Host.

   ```sh
   nix develop --command bash -c 'dotnet test --project tests/Tertius.Core.Tests'
   ```

3. Run a demo script with no window. This run takes 120 ticks. It writes 3 PNG images and 2 JSON state dumps into `captures/drop-box`.

   ```sh
   nix develop --command bash -c 'dotnet run --project src/Tertius.Demo -- --script scripts/drop-box.json --headless --out captures/drop-box'
   ```

4. Run a scene live in a window. W, A, S and D move. V changes the view, F1 starts a fly camera, and Esc quits.

   ```sh
   nix develop --command bash -c 'dotnet run --project src/Tertius.Demo -- --scene station'
   ```

If a script captures images and no Vulkan renderer starts, the run stops with exit code 5. The folder [scripts](scripts) holds 13 scripts. [tools/README.md](tools/README.md) gives the commands for coverage, mutation testing and JetBrains Rider. [native/README.md](native/README.md) tells how the Jolt library is built.

## Repository layout

The source projects are in `src`:

- [Tertius.Core](src/Tertius.Core) holds the math, handles, messages, the frame tree and gravity. It also holds the rules for numeric frames, the rails library and the interfaces to physics and the renderer. It depends on nothing.
- [Tertius.Simulation](src/Tertius.Simulation) holds entities, components, events, queries, blueprints, the `World` and the sync point.
- [Tertius.Physics](src/Tertius.Physics) is the Jolt backend.
- [Tertius.Rendering](src/Tertius.Rendering) is the deferred Vulkan renderer, with GLSL shaders.
- [Tertius.Host](src/Tertius.Host) holds the window, the threads and the script runner.
- [Tertius.Demo](src/Tertius.Demo) holds the demo scenes and the entry point.

Simulation, Physics and Rendering each depend only on Core, never on each other. The other folders are these:

- [tests](tests) has one xUnit project per module. [Tertius.Testing](tests/Tertius.Testing) holds the shared helpers, such as `FaultInjectingBackend`.
- [benchmarks](benchmarks) holds BenchmarkDotNet benchmarks.
- [ARCHITECTURE.md](ARCHITECTURE.md) is the binding design. Each module must follow it.
- [docs/plans](docs/plans) holds the design docs. Its [README.md](docs/plans/README.md) maps each doc to its epic ticket.
- [scripts](scripts) holds the JSON scripts for the demo scenes.
- [tools](tools) holds the helpers for coverage, mutation testing and Rider.
- [native](native) holds the build of the Jolt library.
- `.tickets` holds the work tickets, one Markdown file each, ordered by dependencies.

The tickets use the `tk` command-line tool, which is the Homebrew formula `ticket`. `tk ready` lists the tickets whose dependencies are closed. `tk show <id>` shows one ticket.

## Third-party code

The repository holds none of this code. NuGet and Nix fetch it at build time. The licenses below come from the package metadata and from [joltc.nix](native/joltc.nix). The license of Jolt Physics comes from its own repository.

| Component | Version | License | Use |
|---|---|---|---|
| Jolt Physics | v5.6.0 | MIT | Physics |
| joltc | commit 886e088 | MIT | C interface to Jolt |
| JoltPhysicsSharp | 2.23.0 | MIT | C# bindings to joltc |
| Silk.NET.Vulkan, Vulkan.Extensions.KHR, Vulkan.Extensions.EXT, Windowing, Input | 2.23.0 | MIT | Vulkan, window and input |
| StbImageSharp | 2.30.16 | Unlicense or MIT | Image reading |
| StbImageWriteSharp | 1.16.7 | None stated in its package | Image writing |
| Vulkan loader, MoltenVK | from nixpkgs | Their own licenses | Vulkan on the host |
| xunit.v3 | 4.0.2 | Apache-2.0 | Tests |
| AwesomeAssertions | 9.6.0 | Apache-2.0 | Tests |
| Microsoft.Testing.Extensions.CodeCoverage | 18.12.0 | Microsoft .NET Library license | Test coverage |
| BenchmarkDotNet | 0.15.8 | MIT | Benchmarks |
| dotnet-stryker | 5.0.0 | Apache-2.0 | Mutation testing |
| dotnet-reportgenerator-globaltool | 5.5.11 | Apache-2.0 | Coverage reports |

## License

Tertius is under the 0BSD license. You can use, copy, change and share it, with or without a fee. See [LICENSE](LICENSE) for the full text.
