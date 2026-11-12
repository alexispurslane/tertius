# Tertius code guide

This guide names the code and the tests behind each row of the two tables in [README.md](README.md). Each part lists the code to read, in order, and then the tests that prove the row. For each test, the guide says what the test measures. The line numbers are from commit 1be71d9. The line links point to commit 1be71d9, so they show the lines that the guide describes even after the code changes.

## Terms

These terms have the same meaning here and in the code:

- A frame is a coordinate system that moves. Every position and velocity in Tertius is relative to a frame.
- A bubble is the sphere or ellipsoid of a frame. An object inside the bubble belongs to the frame.
- A semantic frame is a frame that the game places, such as a star system, a planet, a ship or a room.
- A numeric frame is a frame that the engine makes and removes by itself, only for precision. Gameplay never sees it.
- A group is a set of bodies that the merge test links. The merge test links two bodies that can touch within 2 s.
- A Jolt system is one Jolt physics world. Each frame has its own.
- The sync point is the step between two ticks, where the engine applies changes.
- A resting frame is a numeric frame that does not move. A slow group gets one at 2^23 m (about 8,389 km) or more from the origin of its semantic frame.
- A falling frame is a numeric frame that falls freely, with its motion computed in doubles. Bodies that move fast together share one.
- A nesting frame is a numeric frame on a grid of 2^20 m. A numeric frame more than 10^9 m from the origin of its parent moves under one.
- A hand-off moves a body from one frame into another. The entity keeps its handle.
- A ghost is a kinematic copy of a body that sticks out of a bubble. It lives in the frame on the other side and follows the real body.
- The owner of a frame is the entity that is the frame. Every frame follows its owner, and the owner's components move it. A frame has no driver of its own.
- A hull is the body of a frame's owner in the parent frame. Its motion is its body's `BodyMotion`: static, kinematic or dynamic. Physics moves a dynamic hull.
- A motion intent says how an entity moves in the next step. It is an end pose, a full sample or a steer target.
- A rails component writes a full sample of its entity's motion in each tick, from a formula. `KeplerMotion` follows a Kepler orbit, the oval path of a body round another body under gravity. `SpinMotion` turns its entity in place at a steady rate.
- A step record is the motion of a frame over the last finished step. At each sync point, every frame takes its step record.
- A following frame is a numeric frame that holds one fast or far kinematic body, its anchor, at its origin. At each sync point, the frame lands exactly on its anchor's step plan, the step record of the anchor's motion intent.
- The proper acceleration of a frame is its linear acceleration, the value that an accelerometer reads. It acts on everything inside the frame.
- A turning frame is a frame whose axes turn against inertial space. The rotation terms are the Coriolis term −2 ω × v, the centrifugal term −ω × (ω × r) and the Euler term −α × r, where ω is the angular velocity of the frame, α its angular acceleration, r the place of a body and v its velocity.

## Physics

Each part below matches a row of the physics table in the README. Every physics test runs the real World on the real Jolt backend. The only test double wraps real Jolt to inject faults, as [real-physics-in-tests.md](docs/plans/real-physics-in-tests.md) explains.

[ARCHITECTURE.md](ARCHITECTURE.md) is the binding design. Its section "Reference frames" covers rows 1 to 6, and its section "Turning frames", under "Physics", covers row 7. The design doc [numeric-frames.md](docs/plans/numeric-frames.md) gives the reasons for every threshold, and [spin-gravity.md](docs/plans/spin-gravity.md) gives the design of row 7.

### 1. Large worlds

Read this code in this order:

1. [NumericFrameOptions.cs](src/Tertius.Core/Frames/NumericFrameOptions.cs), [lines 64 to 94](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/NumericFrameOptions.cs#L64-L94). `CreateDistance` is `1 << 23` m and `LeaveDistance` is `1 << 22` m. `NestDistance` is `1e9` m and `NestGrid` is `1 << 20` m.
2. `Mode` at [line 213](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/NumericFrameRules.cs#L213) of [NumericFrameRules.cs](src/Tertius.Core/Frames/NumericFrameRules.cs). A fast group gets `Falling`. A slow group at the create distance or farther gets `Resting`.
3. `Summarize` at [line 471](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/NumericFrameRules.cs#L471) of the same file. It gives the mass-weighted center of a group. Static bodies count as infinitely heavy.
4. `UpdateNumericFrames` at [line 334](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L334) of [FrameTree.Numeric.cs](src/Tertius.Core/Frames/FrameTree.Numeric.cs). Only this method makes, moves and removes numeric frames, once per sync point.
5. `MoveHome` at [line 959](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L959) of the same file. At [line 976](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L976) it makes the new frame at the center of the group.
6. `NestFarFrames` at [line 1028](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L1028) and `Nest` at [line 1043](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L1043) of the same file. Look at the comment at [line 1078](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L1078) on what rounds.
7. [Line 44](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/native/joltc.nix#L44) of [joltc.nix](native/joltc.nix). It builds Jolt with `DOUBLE_PRECISION` on.
8. [Line 28](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/Internal/JoltRuntime.cs#L28) of [JoltRuntime.cs](src/Tertius.Physics/Internal/JoltRuntime.cs). The engine does not start without the double-precision library.

Look for these limits:

- The rule measures the center of a group, not each body.
- Below 2^22 m, the resting frame goes away.
- If a body is in the Jolt system of its semantic frame, it waits 1 s before it moves to a new resting frame.
- Anchors, such as static bodies, can hold a group in that system farther out.
- The place of a frame in its nest rounds at about 10^-7 m.
- Reads between nearby bodies do not round, because they go through their nearest common frame.

These tests prove it:

- `A_group_at_the_create_distance_gets_a_resting_frame` and `A_slow_near_group_needs_no_frame` in [NumericFrameRulesTests.cs](tests/Tertius.Core.Tests/NumericFrameRulesTests.cs). A group at 8,388,608 m gets `Resting`. A group at 999.9 m/s and 8,388,607 m gets no frame.
- `ACrateAtRestOnAFloor10000KilometersOutSharesARestingFrameWithTheFloorAndStaysAtRestOnIt` in [NumericFrameThresholdTests.cs](tests/Tertius.Physics.Tests/NumericFrameThresholdTests.cs). For 120 ticks, the crate is in a resting frame, in one group with the floor. It stays within 1 mm of its place along the floor, within 2 cm of its height on the floor, and below 1 mm/s.
- `ACrateALightYearOutKeepsMillimeterStepsRelativeToAPodNextToIt` in [WorldNumericNestingTests.cs](tests/Tertius.Physics.Tests/WorldNumericNestingTests.cs). A crate and a pod move at 2,048 m/s one light-year out, and the crate drifts from the pod at 1/16 m/s. For 60 ticks, each step of about 1.04 mm reads true within 0.1 mm, in the world, the snapshot and the render reads.
- `ANestingFrameJumpsByWholeGridStepsAndTheFramesBelowItKeepMillimeterSteps` in the same file. Over 330 ticks, the nesting frame stays on the grid and jumps after its frames, and the steps of the crate still read true within 0.1 mm.
- `A_position_in_a_nested_frame_a_light_year_out_rounds_once_in_the_semantic_frame` in [FrameTreeNumericUpdateTests.cs](tests/Tertius.Core.Tests/FrameTreeNumericUpdateTests.cs). At one light-year a double steps by 2 m, so a read in a far frame rounds at that distance. The test shows that the read rounds once, not twice.

The measurements with numeric frames off are in [numeric-frames.md](docs/plans/numeric-frames.md), section "The measurements". Thrust drift and ballistic drift did not change at any distance up to 9×10^6 m. The gap error between two boxes grew by 0.42 µm from 1.5×10^6 m. Contacts changed by at most 18 nm up to 6×10^5 m. The tests are in [Precision](tests/Tertius.Physics.Tests/Precision). No test pins the double-precision build mode directly, but a float build cannot pass these runs.

### 2. Ships in ships

Read this code in this order:

1. [Entity.cs](src/Tertius.Core/Handles/Entity.cs), [lines 24 to 28](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Handles/Entity.cs#L24-L28). A `FrameHandle` is an entity plus a row in the frame tree.
2. `AsFrame` at [line 53](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/SpawnDesc.cs#L53) and `FrameSpec` at [line 86](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/SpawnDesc.cs#L86) of [SpawnDesc.cs](src/Tertius.Simulation/Entities/SpawnDesc.cs). Any spawn can be a frame.
3. `Create` at [line 128](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.cs#L128) and `Reparent` at [line 267](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.cs#L267) of [FrameTree.cs](src/Tertius.Core/Frames/FrameTree.cs).
4. `FullyOutside`, `ExitMargin` and `Fits` at [lines 38 to 44](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTypes.cs#L38-L44) of [FrameTypes.cs](src/Tertius.Core/Frames/FrameTypes.cs).
5. `HandOffBodies` at [line 143](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs#L143) of [JoltPhysicsBackend.HandOff.cs](src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs). The exit test is at [line 170](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs#L170), the fit test at [201](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs#L201) and the entry test at [204](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs#L204).
6. `ExecuteHandOff` at [line 310](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs#L310) of the same file. The body moves to the other Jolt system, and the entity keeps its handle.
7. [DemoScenes.cs](src/Tertius.Demo/DemoScenes.cs): the ship at [line 123](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Demo/DemoScenes.cs#L123), Earth at [line 271](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Demo/DemoScenes.cs#L271) and the ground of Earth at [line 285](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Demo/DemoScenes.cs#L285).
8. [DemoScenes.Station.cs](src/Tertius.Demo/DemoScenes.Station.cs): the station at [line 203](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Demo/DemoScenes.Station.cs#L203) and its pods at [line 267](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Demo/DemoScenes.Station.cs#L267).

These are the hand-off rules. The exit margin is the larger of 2 % of the largest radius of the bubble and the radius of the body. If the largest radius of a frame is less than half of the smallest radius of another frame, it can enter that frame. Otherwise the two frames stay siblings.

These tests prove it:

- `BallEntersChildBubbleOnFirstTouchAndDoesNotFlipBack` in [HandOffTests.cs](tests/Tertius.Physics.Tests/HandOffTests.cs). The ball enters on the first step that ends with its sphere touching the bubble, and leaves only past the margin. Real Jolt sends exactly two hand-offs.
- `SmallFrameEntersBigFrameButSimilarFramesStaySiblingsAndCollide` in the same file. A fighter frame nests under a carrier, and its pilot stays in the fighter. Two frames of the same size stay siblings, and their hulls collide and share momentum.
- `ThePassengersOfAFrameOwnerThatWasHandedOffStayInTheFrameAndRideAlong` in [PhysicsHandOffTests.cs](tests/Tertius.Simulation.Tests/PhysicsHandOffTests.cs). The fighter enters the carrier, and its pilot stays in the fighter's frame at its place.
- `AShipFrameUnderAFallingNumericFrameCarriesItsContentsWithIt` in [WorldNumericFrameTests.cs](tests/Tertius.Physics.Tests/WorldNumericFrameTests.cs). The ship moves more than 15 km in 2 s, and its cargo keeps its place within 1 mm.
- `ARoomWithoutAHullInAShipThatDiesInAFallingFrameMovesToSpaceAndHoldsItsPlaceAsWithoutNumericFrames` in [GhostPlaceTests.cs](tests/Tertius.Physics.Tests/GhostPlaceTests.cs). This is the room case. The room moves less than 10^-6 m, with numeric frames as without.
- `ThePersonDriftsOutOfTheShipsFrameIntoEarthAndBack` in [SpacewalkSliceTests.cs](tests/Tertius.Host.Tests/SpacewalkSliceTests.cs). It runs the real demo script [spacewalk.json](scripts/spacewalk.json). The person leaves the ship's frame past the margin and comes back on its first touch of the bubble, within 25 m of the ship's center.

### 3. Objects across an edge

Read this code in this order:

1. `UpdateGhosts` at [line 449](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs#L449) of [JoltPhysicsBackend.HandOff.cs](src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs). The test for a body that sticks out is at [line 517](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs#L517).
2. `WantGhost` at [line 723](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs#L723) and `GhostAllowed` at [line 794](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs#L794) of the same file.
3. The remarks at [lines 8 to 41](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.Contacts.cs#L8-L41) of [JoltPhysicsBackend.Contacts.cs](src/Tertius.Physics/JoltPhysicsBackend.Contacts.cs). Then read `contactCounts` at [line 62](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.Contacts.cs#L62) and `CollectContacts` at [line 80](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.Contacts.cs#L80).
4. `GhostPlace` at [line 156](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/NumericFrameRules.cs#L156), `MayTouch` at [lines 274 to 292](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/NumericFrameRules.cs#L274-L292) and `MeetsGhost` at [line 373](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/NumericFrameRules.cs#L373) of [NumericFrameRules.cs](src/Tertius.Core/Frames/NumericFrameRules.cs).
5. `CollectGhostPlaces` at [line 613](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L613) and `LinkGhostPlaces` at [line 789](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L789) of [FrameTree.Numeric.cs](src/Tertius.Core/Frames/FrameTree.Numeric.cs).

Look for the count per pair of entities. At the change of the count from 0 to 1, the engine sends one `Collision`. At the change from 1 to 0, it sends one `CollisionEnded`. The ghost does not push back on its real body, so momentum does not balance across an edge. If an outside body also touches the bubble, it has its own ghost there. Only then does it push the real body.

These tests prove it:

- `GhostExistsOnlyWhileTheBodyStraddlesTheBubble` in [GhostTests.cs](tests/Tertius.Physics.Tests/GhostTests.cs). The ball has a ghost in the parent only while part of it is outside the bubble, on the way in and on the way out.
- `ABodyInTheParentAndABodyThatSticksOutOfTheChildEachHearTheirContactThroughTheGhost` in the same file. The ball stops at the ghost, and the real block moves less than 10^-6 m. The impulse is 5 N·s within 0.25 N·s, the mass of the ball times its speed. Each side gets one `Collision`. After the ghost goes, each side gets one `CollisionEnded`.
- `AMirrorPairIsReportedOnceAndEndsOnceAfterTheLastSystemDropsIt` in [GhostContactTests.cs](tests/Tertius.Physics.Tests/GhostContactTests.cs). The contact is in two Jolt systems but starts and ends one time.
- `ACrateThatSticksOutOfAFallingShipPushesARockThatRestsInSpaceInTheShipsFallingFrameAsItDoesWithoutNumericFrames` in [GhostPlaceTests.cs](tests/Tertius.Physics.Tests/GhostPlaceTests.cs). The rock joins the ship's falling frame and is hit within one tick of the run without numeric frames. Its velocity differs from that run by less than 0.1 m/s, and the contact news are the same.
- [FrameTreeGhostPlaceTests.cs](tests/Tertius.Core.Tests/FrameTreeGhostPlaceTests.cs) holds 53 test methods of the merge test with ghost places, with no physics.

### 4. Fast objects

Read this code in this order:

1. [NumericFrameOptions.cs](src/Tertius.Core/Frames/NumericFrameOptions.cs), [lines 22 to 70](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/NumericFrameOptions.cs#L22-L70). `CreateSpeed` is 1,000 m/s and `LeaveSpeed` is 400 m/s. `ThrustCreateSpeed` is 50 m/s and `ThrustLeaveSpeed` is 20 m/s.
2. `Mode` at [line 213](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/NumericFrameRules.cs#L213) of [NumericFrameRules.cs](src/Tertius.Core/Frames/NumericFrameRules.cs). [Lines 218 and 219](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/NumericFrameRules.cs#L218-L219) choose between the speeds for free groups and the speeds for groups that thrust.
3. `MayTouch` at [line 274](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/NumericFrameRules.cs#L274) of the same file. This is the merge test: two bounds grow by their relative speed times 2 s.
4. `ThrustClock` at [line 406](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/NumericFrameRules.cs#L406) of the same file. A body thrusts after 0.5 s of thrust ticks. It keeps thrusting for 20 s after its last thrust tick.
5. `Thrusts` at [line 434](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L434) of [FrameTree.Numeric.cs](src/Tertius.Core/Frames/FrameTree.Numeric.cs). Every character thrusts.
6. `AdvanceNumericFrames` at [line 1385](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L1385) of the same file. A falling frame moves in doubles.
7. `ThrustAcceleration` at [line 111](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.NumericFrames.cs#L111) of [World.NumericFrames.cs](src/Tertius.Simulation/World.NumericFrames.cs).

The speed of a group is its common speed relative to its semantic frame. The section "Limits" of [numeric-frames.md](docs/plans/numeric-frames.md) lists the cases where precision drops. An example is two bodies that pass each other at kilometers per second.

These tests prove it:

- `A_group_of_free_bodies_gets_a_falling_frame_only_at_the_create_speed` in [NumericFrameRulesTests.cs](tests/Tertius.Core.Tests/NumericFrameRulesTests.cs). 999 m/s gives no frame, and 1,000 m/s gives `Falling`.
- `A_group_with_a_body_that_thrusts_gets_a_falling_frame_at_the_thrust_create_speed` in the same file. 49.99 m/s gives no frame, and 50 m/s gives `Falling`.
- `ACrateThatCoastsThroughEmptySpaceFallsInANumericFrameFromTheCreateSpeedOfAGroupOfFreeBodies` in [NumericFrameThresholdTests.cs](tests/Tertius.Physics.Tests/NumericFrameThresholdTests.cs). It tests the same threshold with real Jolt: for 60 ticks, 1,000 m/s gives a falling frame, and 999 m/s does not.
- `APersonWhoThrustsForAMinuteNearTheShipsOrbitGainsLessThanAMillimeterPerSecondOfErrorRelativeToTheShip` in [WorldNumericClusteringTests.cs](tests/Tertius.Physics.Tests/WorldNumericClusteringTests.cs). The person thrusts with 105 N for 60 s at orbit speed and gains more than 80 m/s. The velocity of the person relative to the ship stays within 1 mm/s of the same motion computed in doubles.
- `AGroupThatStartsToFallAtOrbitalSpeedKeepsTheVelocityOfEachBodyToAMicrometerPerSecond` in [WorldNumericFrameTests.cs](tests/Tertius.Physics.Tests/WorldNumericFrameTests.cs). Two crates in a resting frame speed up to 7,672 m/s, and their frame starts to fall with their mean velocity. In the next tick, the velocity of a crate in the planet's frame changes by less than 10^-6 m/s.
- `TheSpacewalkerIsWithin5CmOfADoublePrecisionIntegrationOfItsThrust` and `TheCratesAboveAndBelowTheShipDriftAsTheClohessyWiltshireEquationsSay` in [OrbitClusterTests.cs](tests/Tertius.Host.Tests/OrbitClusterTests.cs). The spacewalker stays within 5 cm of an integration of its thrust in doubles. The crates stay within 1 cm of the Clohessy-Wiltshire prediction. These tests run the script [orbit-cluster.json](scripts/orbit-cluster.json).

The 355 mm/s comes from the table "Thrust drift" in [numeric-frames.md](docs/plans/numeric-frames.md), section "Thresholds from measured precision", with numeric frames off. A character of 70 kg thrusts with 105 N, so at 1.5 m/s². At 7,672 m/s the error is 29, 117 and 355 mm/s after 5, 20 and 60 s. At 1,000 m/s it is 85 mm/s after 60 s, and at 50 m/s it is 2.1 mm/s. In the table "Relative drift" of the same section, two free boxes at 1,000 m/s keep their relative velocity within 0.024 mm/s, and their gap within 1.3 mm after 60 s. These numbers set the speeds: bodies that move together keep their gaps nearly exact up to 1,000 m/s, and a body that thrusts drifts by about 2 mm/s per minute of thrust at 50 m/s.

All of these are in the hard direction, where the common velocity points along the diagonal (1, 1, 1). If the common velocity is at right angles to the thrust, speed adds no error. The "about 10 m" in the README is half of 0.355 m/s times 60 s. No test asserts it.

To run the measurements yourself, run this command in the repository root. The runs write CSV files into the folder that `TERTIUS_PRECISION_OUT` names.

```sh
nix develop --command bash -c 'dotnet test --project tests/Tertius.Physics.Tests -c Release -- --filter-namespace Tertius.Physics.Tests.Precision --explicit only'
```

### 5. Thrust that you feel

Read this code in this order:

1. [Lines 309 to 314](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.cs#L309-L314) of [JoltPhysicsBackend.cs](src/Tertius.Physics/JoltPhysicsBackend.cs). Each step applies gravity, steps the Jolt systems, and then computes the proper accelerations.
2. `ComputeProperAccelerations` at [line 1178](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.Step.cs#L1178) of [JoltPhysicsBackend.Step.cs](src/Tertius.Physics/JoltPhysicsBackend.Step.cs). For a frame with a dynamic hull, the change of velocity of the hull over the step, minus the whole acceleration that the parent frame gave it, gives the value. A dead band hides float noise.
3. `ApplyGravity` at [line 481](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.Step.cs#L481) and at [line 619](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.Step.cs#L619) of the same file. It pushes each awake dynamic body with its mass times the linear gravity of its frame.
4. `UpdateCharacter` at [line 840](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.Step.cs#L840) of the same file. Below an effective gravity of 0.05 m/s², a character does not stand.
5. `FollowOwners` at [line 313](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Sync.cs#L313) of [World.Sync.cs](src/Tertius.Simulation/World.Sync.cs). In step 2 of the sync point, every frame takes its step record and its proper acceleration.
6. `TakeStepRecords` at [line 86](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.StepRecords.cs#L86) and `ProperAccelerationFrom` at [line 209](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.StepRecords.cs#L209) of [FrameTree.StepRecords.cs](src/Tertius.Core/Frames/FrameTree.StepRecords.cs). This is the rule for every frame: its acceleration over the step minus the gravity at its place where the step started. A dynamic hull takes the value that physics measured instead.
7. [FrameTree.cs](src/Tertius.Core/Frames/FrameTree.cs): `ProperAcceleration` at [line 765](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.cs#L765), `FeltGravity` at [line 784](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.cs#L784), `ZeroGravityThreshold` at [line 827](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.cs#L827) and `LinearGravity` at [line 840](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.cs#L840). The field of a frame minus its proper acceleration is the gravity that its contents feel.

A frame on a Kepler orbit in a parent that falls freely and does not turn reads exactly zero, because it falls freely. A frame that holds still in a gravity field reads that gravity reversed, so a parked ship gives its cargo the weight of the ground, and a turntable that holds its place on the ground does too. A frame at rest in a ship that thrusts reads the ship's value, one step later. In the demo ship, a uniform field of 9.81 m/s² gives the weight in the cabin. No test stands a character on a deck that thrusts. Characters use the same gravity, as the effective gravity of their frame.

These tests prove it:

- `ThrustingShipPushesLooseCrateTowardStern` in [GravityTests.cs](tests/Tertius.Physics.Tests/GravityTests.cs). The accelerometer of a ship of 1,000 kg under 2,000 N reads 2 m/s² within 0.001 m/s². The crate inside feels −2 m/s² along the ship, within 5 mm/s on its velocity.
- `LandedShipPassesPlanetGravityToItsInterior` in the same file. The ship reads between 9.7 and 9.95 m/s² up, and the crate rests on the deck.
- `ShipInOrbitIsInFreeFallAndItsCargoFloats` in the same file. On an orbit with a radius of 7,000 km, the proper acceleration is exactly zero for 180 ticks. The cargo moves less than 10^-9 m.
- `CargoAsleepOnADeckSlidesTowardTheSternWhenTheShipsThrustGrows` in [SleepTests.cs](tests/Tertius.Physics.Tests/SleepTests.cs). A crate sleeps on a deck under 9.81 m/s². The thrust grows to 6 m/s², also by 10 N a tick, and the crate wakes and slides toward the stern.
- `AShipThatParksFeelsTheGravityAtItsPlaceReversedAndKeepsNoThrust` and `AFrameInAnAcceleratingFrameFeelsItsParentsProperAcceleration` in [FrameProperAccelerationTests.cs](tests/Tertius.Physics.Tests/FrameProperAccelerationTests.cs). A parked ship reads the gravity of its place within 10^-6 m/s². A turntable and a fighter in a ship that thrusts at 1.5 m/s² read the ship's value in the next step.
- `InEarthThePersonFallsFreelySoItDoesNotStand` in [SpacewalkSliceTests.cs](tests/Tertius.Host.Tests/SpacewalkSliceTests.cs).
- `TheEnginesStopOnceTheShuttleIsInOrbit` and `ABallInTheCabinFloatsRelativeToTheCabinOnceTheEnginesStop` in [DemoLaunchTests.cs](tests/Tertius.Host.Tests/DemoLaunchTests.cs). They run the demo script [demo-launch.json](scripts/demo-launch.json). After the engines stop, the cabin reads less than 0.05 m/s² for more than 300 ticks, and a ball at rest in the cabin moves less than 5 mm in 5 s.

### 6. Orbits and turns

Read this code in this order:

1. [KeplerOrbit.cs](src/Tertius.Core/Frames/Rails/KeplerOrbit.cs): `StateAt` at [line 86](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/Rails/KeplerOrbit.cs#L86), `Sample` at [line 144](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/Rails/KeplerOrbit.cs#L144) and `Chord` at [line 178](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/Rails/KeplerOrbit.cs#L178). The doc comments state what is exact.
2. `MuFrom` at [line 65](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/Rails/KeplerOrbit.cs#L65) of the same file. An orbit holds no G·M. It takes G·M from the point gravity of the parent frame.
3. [FrameSpin.cs](src/Tertius.Core/Frames/Rails/FrameSpin.cs): `RotationAt` at [line 23](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/Rails/FrameSpin.cs#L23) and `Sample` at [line 35](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/Rails/FrameSpin.cs#L35). Then read [MotionSample.cs](src/Tertius.Core/Frames/Rails/MotionSample.cs).
4. The rails components. `Handle` of [KeplerMotion.cs](src/Tertius.Simulation/Components/KeplerMotion.cs) at [line 61](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Components/KeplerMotion.cs#L61) and `Handle` of [SpinMotion.cs](src/Tertius.Simulation/Components/SpinMotion.cs) at [line 51](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Components/SpinMotion.cs#L51). In each tick, a component asks the guard, and then writes the full sample of the next step with `Self.MoveOnRails`.
5. The motion intent. `MotionIntent` at [line 30](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Seams/MotionIntent.cs#L30) of [MotionIntent.cs](src/Tertius.Core/Seams/MotionIntent.cs) is a custom union of an end pose, a full sample and a steer target. It is the field `PhysicsIntent.Move` at [line 478](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Seams/PhysicsSeam.cs#L478) of [PhysicsSeam.cs](src/Tertius.Core/Seams/PhysicsSeam.cs). Its writers are `MoveOnRails` at [line 100](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/Self.cs#L100) and `MoveKinematic` at [line 110](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/Self.cs#L110) of [Self.cs](src/Tertius.Simulation/Entities/Self.cs), and `SteerTo` at [line 274](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/Self.cs#L274). One guard says which entity takes which case: `World.MotionIntentProblem` at [line 371](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Tick.cs#L371) of [World.Tick.cs](src/Tertius.Simulation/World.Tick.cs).
6. The motion of a hull. `BodyMotion` at [line 6](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Seams/PhysicsSeam.cs#L6) of PhysicsSeam.cs. A component changes it with `Self.Body.SetMotion` at [line 349](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/Self.cs#L349) of Self.cs, for example to park a ship. The frame tree stores it for the last finished step in `HullMotion` at [line 487](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.cs#L487) of FrameTree.cs. `HullMotionInStep` at [line 512](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.cs#L512) derives the motion of a step in flight.
7. `TakeStepRecords` at [line 86](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.StepRecords.cs#L86) of [FrameTree.StepRecords.cs](src/Tertius.Core/Frames/FrameTree.StepRecords.cs). At step 2 of each sync point, every frame takes its step record: a hull's record from physics, a frame without a hull the record of its owner's motion intent. A frame without a record holds still in its tree parent.
8. Following frames. `Following` at [line 45](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/NumericMode.cs#L45) of [NumericMode.cs](src/Tertius.Core/Frames/NumericMode.cs) and its comment. Then `CreateFollowingFrame` at [line 139](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L139), `PlaceFollowingFrame` at [line 46](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L46), `LeaveFollowingFrame` at [line 188](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L188) and `StepPlanOfIntent` at [line 1334](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L1334) of [FrameTree.Numeric.cs](src/Tertius.Core/Frames/FrameTree.Numeric.cs).

A fast or far kinematic body, such as the hull of a station in low orbit, gets a following frame of its own. At each sync point, the following frame takes the place of its anchor's step plan and the chord of the plan over the step as its velocity. The chord is the change of place over the step, divided by the step. With the instant velocity instead, a crate in the following frame drifts by half a step of gravity, 7 cm/s in low orbit. The following frame never turns. A hull's frame hangs at its origin and keeps the rotation. In a step without a motion intent, the plan holds the anchor still, so the frame holds still in its semantic frame.

Two demo scenes show this. In the scene `orbit`, Earth is a frame without a body that its `KeplerMotion` moves round the Sun, and the ground of Earth is a frame that its `SpinMotion` turns. The ship of that scene is not on rails. It is a dynamic hull that Jolt lets fall freely. In the scene `station`, the station is a kinematic hull that its `KeplerMotion` moves, and it rests in a following frame.

These tests prove it:

- `A_sample_holds_the_place_rotation_velocity_and_angular_velocity_of_the_orbit_at_the_end_of_its_step` in [KeplerOrbitTests.cs](tests/Tertius.Core.Tests/Rails/KeplerOrbitTests.cs). The sample equals the orbit exactly.
- `The_chord_of_a_sample_is_the_change_of_place_over_its_step_divided_by_the_step` in the same file, within 2×10^-7 m over the step.
- `TheFrameTurnsAboutItsPoleByItsRateTimesTheTimeThatPassed` in [FrameSpinTests.cs](tests/Tertius.Core.Tests/Rails/FrameSpinTests.cs).
- `KeplerMotionWritesTheLibrarysSampleForTheEndOfTheStepInWhichItsIntentActs` in [RailsComponentTests.cs](tests/Tertius.Simulation.Tests/RailsComponentTests.cs). The component writes exactly the sample of `KeplerOrbit.Sample` for the step in which its intent acts.
- `ACircularKeplerFrameIsAtTheOrbitPointForTheTimeThatHasPassed` and `AMoonRidesItsPlanetAlongTheMoonsOwnOrbit` in [KeplerFrameTests.cs](tests/Tertius.Simulation.Tests/KeplerFrameTests.cs). After one to four quarter periods, the frame is at the orbit point within 10^-6 m.
- `AFrameOnAKeplerOrbitInAFreelyFallingParentHasExactlyZeroProperAcceleration` in [FrameProperAccelerationTests.cs](tests/Tertius.Physics.Tests/FrameProperAccelerationTests.cs). For a frame without a body, a kinematic hull and a kinematic hull in a following frame, the proper acceleration is exactly zero in every tick.
- `AShipThatParksHoldsStillWhereTheStepLeftItAndItsFrameStopsAtTheNextSyncPoint` and `AKeplerHullThatBecomesDynamicCoastsOnItsOrbitAndFollowsTheOrbitAgainWhenItBecomesKinematic` in [MotionChangeTests.cs](tests/Tertius.Physics.Tests/MotionChangeTests.cs). A parked hull holds still within 10^-9 m, and its frame takes zero velocity at the next sync point. A Kepler hull whose engines come on coasts within 0.5 m of its orbit, and a new `KeplerMotion` puts it back on its orbit within 10^-6 m.
- `During_a_step_a_following_frame_falls_with_the_acceleration_that_lands_it_on_its_anchors_plan_and_ends_on_the_plan` in [FrameTreeFollowingFrameTests.cs](tests/Tertius.Core.Tests/FrameTreeFollowingFrameTests.cs). The frame takes the acceleration that lands it on the next sample, and ends the step within 10^-6 m of it.
- `AKeplerStationsHullAloneAtOrbitalSpeedRestsInAFollowingFrameAndEveryReadInEarthIsTheOrbitsState` in [NumericFrameOrbitTests.cs](tests/Tertius.Physics.Tests/NumericFrameOrbitTests.cs). After 36,000 ticks (10 minutes), the place of the hull equals the orbit to the last bit in every tick. Its velocity is within 10^-8 m/s.
- `APlainKinematicBodyOnALowKeplerOrbitStandsOnItsOrbitToTheLastBitInAFollowingFrame` in [FollowingFrameTests.cs](tests/Tertius.Physics.Tests/FollowingFrameTests.cs). A body that owns no frame anchors a following frame too, and stands on its orbit to the last bit for ten seconds.
- `ThePodsDriftOutOfTheAirlockAndOnInAStraightLineRelativeToTheStationToWithinACentimeterUntilTheyBumpTheRadiator` in [StationSliceTests.cs](tests/Tertius.Host.Tests/StationSliceTests.cs). Each pod stays within 1 cm of its straight drift relative to the station, and drifts in the following frame for more than 5 s before it bumps.
- `EarthsSurfaceTurnsOncePerSiderealDayAboutEarthsAxis` in [OrbitSliceTests.cs](tests/Tertius.Host.Tests/OrbitSliceTests.cs). The axes of the ground of Earth match the sidereal turn within 10^-9.

### 7. Spin gravity

Every frame that turns gives its contents the rotation terms. A frame turns when the thing that moves it turns it: its owner's motion intent, physics for a dynamic hull, or a teleport. It also turns with every frame above it. Physics has one rule for every turning frame: it keeps each free body on its straight line in inertial space, as the frame's axes see it. The rotation terms are the shape of that line in the turning axes.

Read this code in this order:

1. The section "Turning frames" of [ARCHITECTURE.md](ARCHITECTURE.md), under "Physics", and the design doc [spin-gravity.md](docs/plans/spin-gravity.md).
2. `BeginStep` at [line 82](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Turns.cs#L82) of [FrameTree.Turns.cs](src/Tertius.Core/Frames/FrameTree.Turns.cs), and `BeginTurn` at [line 137](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Turns.cs#L137). Before each step, the frame tree fixes two turns for every semantic frame: the turn of its axes over the step in flight, and over the step that ended.
3. `Turns` at [line 31](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTurns.cs#L31) and `FreeChange` at [line 41](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTurns.cs#L41) of [FrameTurns.cs](src/Tertius.Core/Frames/FrameTurns.cs). A frame turns when either turn is not the identity. `FreeChange` is the two-turn update, the change of a free body's chord over the step that keeps it on its inertial line.
4. `ApplyGravity` at [line 619](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.Step.cs#L619) and `ApplyTurningUpdate` at [line 678](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.Step.cs#L678) of [JoltPhysicsBackend.Step.cs](src/Tertius.Physics/JoltPhysicsBackend.Step.cs). In a turning frame, each awake dynamic body gets the two-turn update on top of the linear gravity. The velocity shadow, `VelocityShadow` at [line 52](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/Internal/VelocityShadow.cs#L52) of [VelocityShadow.cs](src/Tertius.Physics/Internal/VelocityShadow.cs), keeps the body's chord and place in doubles, so the float rounding of Jolt does not pile up.
5. `ChordBehind` at [line 38](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTurn.cs#L38) and `InstantAtEnd` at [line 52](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTurn.cs#L52) of [FrameTurn.cs](src/Tertius.Core/Frames/FrameTurn.cs). They turn an instant velocity into a chord and back in a turning frame, as exact inverses.
6. `EffectiveGravity` at [line 864](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.cs#L864), `AddRotationTerms` at [line 874](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.cs#L874) and `FeltGravity` at [line 784](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.cs#L784) of [FrameTree.cs](src/Tertius.Core/Frames/FrameTree.cs). The effective gravity adds the centrifugal and the Euler term to the linear gravity. Characters, sleep and sleeping hulls read it. Game code reads `FeltGravity`, the same value for the last finished step. `ProperAccelerationFrom` at [line 209](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.StepRecords.cs#L209) of FrameTree.StepRecords.cs adds the parent's rotation terms, so a room at rest in a spinning station reads ω² R toward the axis.
7. The air rule of a character's up. `UpdateCharacter` at [line 840](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.Step.cs#L840) of JoltPhysicsBackend.Step.cs, and [lines 863 to 866](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.Step.cs#L863-L866). Under gravity, up turns against the gravity, with the centrifugal and the Euler term. A character that touched anything in its last update turns its up at once. A character that touched nothing turns it at most at the air rate, `CharacterAirUpTurnRate` at [line 56](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltBackendOptions.cs#L56) of [JoltBackendOptions.cs](src/Tertius.Physics/JoltBackendOptions.cs), 1 rad/s by default.
8. Numeric frames inside turning frames. `FreeAccelerationAtOrigin` at [line 1290](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L1290) of [FrameTree.Numeric.cs](src/Tertius.Core/Frames/FrameTree.Numeric.cs), which `BeginNumericFrame` at [line 1262](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L1262) reads. A falling frame in a turning frame takes the two-turn update at its origin, so its members feel only the difference of their rotation terms and the origin's. A numeric frame never turns. It reads the turns of its semantic frame.
9. Following frames inside turning frames. A following frame lands on its anchor's step plan, so its origin takes no rotation term. In a step without a motion intent, the plan of `StepPlanOfIntent` at [line 1334](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Frames/FrameTree.Numeric.cs#L1334) holds the frame at rest in the turning axes. The velocity of a body in it composes two terms: `InstantVelocity` at [line 264](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.Chords.cs#L264) and `HandedOffVelocities` at [line 327](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.Chords.cs#L327) of [JoltPhysicsBackend.Chords.cs](src/Tertius.Physics/JoltPhysicsBackend.Chords.cs), and `TryHullRecord` at [line 938](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/JoltPhysicsBackend.cs#L938) of JoltPhysicsBackend.cs, add the turn's part of the instant velocity to the term of the following frame.
10. [DemoScenes.SpinStation.cs](src/Tertius.Demo/DemoScenes.SpinStation.cs), the scene `spin-station`, with the station at [line 145](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Demo/DemoScenes.SpinStation.cs#L145), and its script [spin-station.json](scripts/spin-station.json). A ring station of radius 100 m turns about its axis at 0.31321 rad/s, which gives 9.81 m/s² at the floor. A person stands, walks, jumps and throws a crate on its floor, and a dropped crate falls in a curve.

These are the measured numbers. They come from the section "Turning frames" of ARCHITECTURE.md, in the same station of radius 100 m at 0.31321 rad/s, and from [spin-gravity.md](docs/plans/spin-gravity.md):

- A crate dropped from rest relative to the station lands at 0.5607786 s and 0.176458 m antispinward, as its inertial line says.
- An undamped crate at rest in inertial space stays within 0.04 µm of its place over 10 minutes. Without the velocity shadow it moved 24 mm.
- A ship on the floor reads ω² R toward the axis. Its read across the floor is 4.4×10^-5 m/s², and 0.0507 m/s² without the turn of the contact.
- The cost: 0.27 µs per awake dynamic body per step for the two-turn update, the angular form and the shadow. `BeginStep` takes 47 µs over 1,000 turning frames. The update of a standing character costs about 0.15 µs more per step in a turning frame.
- The air rule turns up by at most 0.95 degrees in a step at 60 Hz. A person who jumps across the station at 30 m/s needs at most 0.29 rad/s, and lands 7.9×10^-4 rad from the up of its landing place (spin-gravity.md, fork F5).
- Over a walk at 3 m/s, 99.095 m from the axis, the inertial path of a person curves toward the axis with a mean of 11.69284 m/s² spinward, against 11.69135 m/s² for (ω r + v)² / r. Antispinward it is 7.93446 m/s², against 7.93283 m/s² (spin-gravity.md, "Acceptance tests").
- In the spike before this work, a person who crossed the middle at 40 m/s got a falling frame and landed 0.7 m deep in the floor. Now such a person lands on the floor, with numeric frames and without. A crate that falls in a numeric frame keeps a velocity relative to its frame below 0.33 m/s. Without the term at the frame's origin, that velocity grows by 0.46 m/s a tick (spin-gravity.md, "Numeric frames inside turning frames").
- Bodies near a following frame inside a turning frame move as without numeric frames, within the float step at each scene's speed: 10^-3 m/s at 2 km/s and 10^-5 m/s at 10 m/s.

Look for these limits. ARCHITECTURE.md lists them under "Turning frames":

- The contents of a dynamic hull that spins up feel the Euler force one step late. A crate in a ship spun up at 1 rad/s² misses its place by 5.6 mm.
- Jolt damps a velocity relative to the frame's axes, so a damped body leaves its inertial line.
- The chord rule cannot follow the jerk of a frame's origin. A crate spawned at rest in a room 50 m from the axis drifts at 7.1×10^-5 m/s.
- A frame on a Kepler orbit in a turning parent turns with the parent, so it reads no zero proper acceleration. Orbits belong in a frame that does not turn.
- A semantic frame applies no tidal gravity of its parent to its contents.

These tests prove it:

- `OverASteadySpinTheUpdateKeepsAMovingBodyOnItsInertialLine` and `WhileTheFrameSpinsUpTheUpdateKeepsABodyAtRestInInertialSpaceOnItsInertialPlace` in [FrameTurnTests.cs](tests/Tertius.Core.Tests/FrameTurnTests.cs). With no physics, the two-turn update gives the exact change of chord that the inertial line says, for a steady spin and for a spin-up.
- `AStationInAFacingOrbitGetsTheCrossTermOfItsSpinAndTheOrbitsTurnAsItsAngularAcceleration` in [FrameTreeBeginStepTests.cs](tests/Tertius.Core.Tests/FrameTreeBeginStepTests.cs). `BeginStep` composes a chain: a spinning station in a frame that faces its planet gets an angular acceleration of 3.45×10^-4 rad/s², within 10^-6 rad/s².
- `AnUndampedCrateReleasedAtRestAboveTheFloorLandsWhereItsInertialLineSays` in [SpinStationChordTests.cs](tests/Tertius.Physics.Tests/SpinStationChordTests.cs). The crate flies within 10^-5 m of its inertial line. It lands 0.5607786 s after its release within 5×10^-8 s, and 0.176458 m antispinward within 5×10^-7 m.
- `AnUndampedCrateAtRestInInertialSpaceKeepsItsPlaceAndItsRotationThereWhileTheStationTurnsUnderIt` in [SpinStationBodyTests.cs](tests/Tertius.Physics.Tests/SpinStationBodyTests.cs). Over 10 minutes the crate stays within 5×10^-7 m of its place in inertial space, and within 10^-4 rad of its rotation there.
- `AShipOnTheFloorOfTheStationFeelsTheFloorPushItTowardTheAxisAwakeAndAsleep` and `TheCargoOfAShipOnTheFloorOfTheStationWeighsOmegaSquaredTimesRPlusR` in [SpinStationFloorTests.cs](tests/Tertius.Physics.Tests/SpinStationFloorTests.cs). The ship reads ω² R within 2×10^-3 m/s² along the radius and 10^-3 m/s² across it, awake and asleep. Its cargo weighs ω² (R + r) within the same bounds.
- `SleepersWakeWhenTheStationSpinsUpAndStayAtRestInInertialSpace` and `AFrameThatFacesItsPlanetOnAnOvalOrbitLetsItsSleepersSleep` in [TurningFrameWakeTests.cs](tests/Tertius.Physics.Tests/TurningFrameWakeTests.cs). Crates that sleep in a still station wake in the first step of a spin-up at 0.01 rad/s², and stay within 1 mm of their places in inertial space. The small change of turn of a frame on an oval orbit wakes nobody.
- `TheContentsOfAShipThatPhysicsSpinsUpByASteadyTorqueFeelTheEulerForceOneStepLate` in [SpinningShipBodyTests.cs](tests/Tertius.Physics.Tests/SpinningShipBodyTests.cs). This test pins the first limit: the crate follows the predicted path within 2×10^-5 m, and misses its inertial place by α r dt².
- `APersonJustAboveTheFloorOfTheStationFallsOntoItAndStandsFor600Of600Ticks`, `APersonWhoJumpsInTheStationLandsWhereTheInertialLineOfItsTakeoffSays` and `APersonWhoWalksOnAPlankInTheStationPressesItWithTheEffectiveGravityAndTheCoriolisTermAlongItsNormal` in [SpinStationCharacterTests.cs](tests/Tertius.Physics.Tests/SpinStationCharacterTests.cs). The person stands in 600 of 600 ticks, within 1 mm of the floor. A jump lands within 7×10^-6 m of the place that the inertial line of its takeoff gives. A person who walks spinward or antispinward presses a plank with m (ω² r ± 2 ω v), within 2 %.
- `APersonWhoJumpsAcrossTheStationLandsUprightOnTheFarFloorAndStands` and `APersonNearTheHubOfTheStationTakesTheRigidForm` in the same file. A person who jumps at 30 m/s toward the axis flies for about 3.2 s, lands within 10^-3 rad of the predicted place, upright within 10^-3 rad, and stands from the tick after it lands. Near the hub, at 0.3 m from the axis, a person takes the rigid form. At 2 m it walks.
- `AFloatingCharacterWhoseGravityFlipsForOneStepTurnsItsUpByAtMostTheAirRateTimesTheStep` and `UnderASteadyGravityACharacterInTheAirTurnsItsUpAtTheAirRateUntilItIsAgainstTheGravityAndLandsUpright` in [CharacterStandingTests.cs](tests/Tertius.Physics.Tests/CharacterStandingTests.cs). They pin the air rule. When the gravity flips by 90 or 180 degrees for one step, up turns by at most the air rate times the step, and back. When the gravity turns by 20 degrees, up turns by the air rate in each step until it is aligned, and the character lands upright and stands.
- `AnUndampedCrateAtRestInInertialSpaceInAFallingFrameInsideTheStationKeepsItsPlaceThere` in [SpinStationBodyTests.cs](tests/Tertius.Physics.Tests/SpinStationBodyTests.cs). The crate falls in a numeric frame below the station, and stays within 1 mm of its place in inertial space.
- `APersonWhoLeavesTheFloorAt50MetersPerSecondAcrossTheStationLandsOnTheFarSideAndStaysOnIt` and `APersonWhoLeavesTheFloorAt50MetersPerSecondCrossesNearTheAxisAndMeetsTheFarFloorAbove50MetersPerSecond` in [SpinStationLandingTests.cs](tests/Tertius.Physics.Tests/SpinStationLandingTests.cs). This is the landing case of the spike. The person passes within 1 m of the axis and meets the floor above 50 m/s. It sinks less than 5 cm into the floor, with numeric frames and without, on slabs and on the station's interior mesh.
- `ABodyThatCrossesTheMiddleOfTheStationInAFallingFrameMovesAsInTheStationsOwnSystemAndLandsThere` in the same file. Under rules by which every group falls, a crate or a person falls in a numeric frame for most of its crossing. It lands at the same sync point as in the station's own system, within 3×10^-5 m of that run for a crate and 6×10^-5 m for a person. Its velocity relative to its falling frame changes by less than 5 mm/s in a tick.
- `ACrateThatLeavesTheBubbleOfAHullThatChangesItsDriftInATurningFrameMovesOnAsWithoutNumericFrames` in [TurningFollowingFrameTests.cs](tests/Tertius.Physics.Tests/TurningFollowingFrameTests.cs). A hull in a turning frame anchors a following frame and stops, starts or skips a step while a crate leaves its bubble. The crate moves on as without numeric frames: for the hull at 2 km/s, within 3×10^-4 m and 10^-3 m/s.
- `AFollowingFrameWithoutAMotionIntentInATurningFrameHoldsStillInItsTurningAxesWithNoTurnTerm` in [FrameTreeBeginStepTests.cs](tests/Tertius.Core.Tests/FrameTreeBeginStepTests.cs). With no physics, a following frame whose anchor has no motion intent holds still in the turning axes.
- `AStationSceneGivesTheSameEventsAndStateInEveryRunWithOneOrFourThreads` in [SpinStationDeterminismTests.cs](tests/Tertius.Physics.Tests/SpinStationDeterminismTests.cs). Bodies fall in numeric frames and hit each other, and the runs give the same lines, bit for bit.
- `OverAWalkThePersonsInertialPathCurvesTowardTheAxisWithTheSquareOfItsInertialSpeedOverItsRadius` and `TheDroppedCrateLandsWhereAndWhenItsExpectedPathSays` in [SpinStationSliceTests.cs](tests/Tertius.Host.Tests/SpinStationSliceTests.cs). They run the script `spin-station.json`. The mean curve of the walk lies within 0.03 m/s² of (ω r ± v)² / r.

The older test `AShipWhoseHullRestsOnTheHullOfASpinFrameIsCarriedRoundByIt` in [WorldSpinFrameTests.cs](tests/Tertius.Physics.Tests/WorldSpinFrameTests.cs) shows that a ship at rest on a spin frame is carried round by it.

## Entity-component system

Each part below matches a row of the entity-component table in the README. The sections "Entities", "Messages" and "Blueprints" of [ARCHITECTURE.md](ARCHITECTURE.md) give the design.

These terms have the same meaning here and in the code:

- An entity is a handle: a slot index and a generation. The generation goes up each time the table frees the slot, so an old handle stops matching.
- A slot is a row in the entity tables. Only the entity table sees slots.
- A component is a class on an entity that implements `IComponent`. It holds behavior and state.
- A message is an `Event`: a target, a sender, a type id and a payload of up to 48 bytes.
- A topic is a list of subscribers that a message can target. Every frame has a topic, and every entity in the frame subscribes to it.
- A tick is one run of gameplay. Each entity with something to handle runs once.
- A chunk is a block of slots, 64 by default. One worker runs all the entities of a chunk.
- The front tables hold the state at the end of the last tick, and every component reads them. The back tables take the writes of this tick.

### Behavior in components

Read this code in this order:

1. [IComponent.cs](src/Tertius.Simulation/Components/IComponent.cs), [lines 14 to 27](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Components/IComponent.cs#L14-L27). A component has a `Priority`, a mask `Wants` of the message types that it handles, and `Handle`.
2. [Lines 223 to 236](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Entities.cs#L223-L236) of [World.Entities.cs](src/Tertius.Simulation/World.Entities.cs). A spawn sorts the components by priority. Equal priorities keep their order from the spawn.
3. `RunChunk` at [line 160](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Tick.cs#L160) of [World.Tick.cs](src/Tertius.Simulation/World.Tick.cs). At [line 171](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Tick.cs#L171), an entity with no messages and no `Tick` or `Spawned` to handle does not run.
4. `RunEntity` at [line 177](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Tick.cs#L177) of the same file. The components run in order over one list of messages. The loop skips a component whose `Wants` do not match the list. A `Destroy` that no component removed destroys the entity.
5. `AddDelivery` at [line 371](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Messaging/Router.cs#L371) of [Router.cs](src/Tertius.Simulation/Internal/Messaging/Router.cs). An entity gets no message of a type that none of its components wants.
6. [Use.cs](src/Tertius.Demo/Use.cs), then `Handle` in [Airlock.cs](src/Tertius.Demo/Airlock.cs) at [line 41](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Demo/Airlock.cs#L41). The demo has one `Use` message, and the components of each entity decide what it means.

These tests prove it:

- `APayloadAComponentEditsInPlaceIsSeenEditedByTheComponentsAfterIt` in [PipelineEventsTests.cs](tests/Tertius.Simulation.Tests/PipelineEventsTests.cs).
- `AnEntityWithNothingToHandleDoesNotRun` and `AMessageOfATypeNoComponentWantsIsDroppedAndDoesNotWakeTheEntity` in [PipelineTests.cs](tests/Tertius.Simulation.Tests/PipelineTests.cs).
- The component `Shield` at [line 118](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/tests/Tertius.Simulation.Tests/DeterminismTests.cs#L118) of [DeterminismTests.cs](tests/Tertius.Simulation.Tests/DeterminismTests.cs). It refuses every other `Destroy` because it removes the message from the list.

### Entities change only themselves

Read this code in this order:

1. [Line 13](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/Self.cs#L13) of [Self.cs](src/Tertius.Simulation/Entities/Self.cs) and the comment above it. `Self` is a `readonly ref struct`, so a component cannot keep it after `Handle` returns. Its writes go to the rows of the running entity only.
2. `HasComponent` at [line 58](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Snapshot.cs#L58) of [Snapshot.cs](src/Tertius.Simulation/Snapshot.cs). A component can learn whether another entity has a component type. No member gives it the component of another entity.
3. `ThrowIfInTick` at [line 409](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.cs#L409) of [World.cs](src/Tertius.Simulation/World.cs). The public write methods of `World` call it, so they throw during a tick.
4. `AddForce` at [line 231](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/Self.cs#L231) and `SetVelocity` at [line 247](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/Self.cs#L247) of [Self.cs](src/Tertius.Simulation/Entities/Self.cs), in `BodyHandle`. A push on a body is an intent in the row of its own entity. Forces add up, and a velocity keeps the last value. A motion intent of part 6 of the physics is an intent of the same kind.
5. [ARCHITECTURE.md](ARCHITECTURE.md), [lines 137 to 150](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/ARCHITECTURE.md#L137-L150), on who owns a transform. There is no general command buffer.

The part "Hot paths that allocate nothing" shows how a message reaches the other entity without allocating.

These tests prove it:

- `AnIntentWrittenInOneTickReachesPhysicsInTheStepOfTheNextTickOnly`, `ForcesWrittenInOneTickAddUp` and `AVelocityWrittenTwiceInOneTickKeepsTheLastValue` in [PhysicsIntentTests.cs](tests/Tertius.Simulation.Tests/PhysicsIntentTests.cs).

No test checks that the write methods of `World` throw during a tick. Subscriptions and physics queries also wait for a later point in the tick, and a component that an entity adds to itself applies right after its run. So the rule is "no general command buffer", not "only spawn and destroy wait".

### Hot paths that allocate nothing

Tertius is written in C#, which has a garbage collector. An allocation on a hot path can start a collection that pauses the game. So the hot paths of a tick keep their data in structs and reuse their buffers. The message path is one hot path, because every message of a tick passes through it. Items 1 to 7 cover that path. Items 8 to 15 cover the other hot paths: the event buffers, the queries and the physics callbacks of a tick.

Read this code in this order:

1. [Event.cs](src/Tertius.Core/Messaging/Event.cs), [lines 7 to 31](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Messaging/Event.cs#L7-L31). A message is a struct with its payload in 48 inline bytes, so it needs no heap object. `Create` writes the payload in place. `Extra` at [line 23](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Messaging/Event.cs#L23) is an optional reference for rare managed data. A message that leaves it empty has no heap part.
2. [EventTypes.cs](src/Tertius.Core/Messaging/EventTypes.cs), [lines 11 to 15](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Messaging/EventTypes.cs#L11-L15). Each payload type gets a dense integer id on first use. So a message names its type with an integer, not an object. `MaxPayloadSize` at [line 21](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Messaging/EventTypes.cs#L21) sets the limit, and [lines 39 and 40](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Messaging/EventTypes.cs#L39-L40) refuse a larger payload, so every payload fits inline.
3. [Target.cs](src/Tertius.Core/Handles/Target.cs), [lines 10 to 46](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Handles/Target.cs#L10-L46). The target is a custom C# 15 union of `Entity`, `Topic` and `Broadcast`. A standard union boxes its struct cases, and a box is a heap allocation. `HasValue` and the `TryGetValue` methods let the compiler match this union without boxing. Only `Value` at [line 47](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Handles/Target.cs#L47) boxes, and it is for debugging.
4. `FillBlock` at [line 330](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Messaging/Router.cs#L330) of [Router.cs](src/Tertius.Simulation/Internal/Messaging/Router.cs). A `switch` turns each target into deliveries. A `Delivery` at [line 6](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Messaging/DeliveryCells.cs#L6) of [DeliveryCells.cs](src/Tertius.Simulation/Internal/Messaging/DeliveryCells.cs) holds a recipient slot and a message index. So a broadcast adds one small struct for each recipient and does not copy the message. The list `routed` at [line 67](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Messaging/Router.cs#L67) of Router.cs holds each message once.
5. `CopyInbox` at [line 170](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Messaging/Router.cs#L170) of the same file. [Line 185](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Tick.cs#L185) of [World.Tick.cs](src/Tertius.Simulation/World.Tick.cs) calls it on the worker that runs the entity. So the copy for each recipient happens on the workers, not at the sync point. The copy goes into the reused `EventBuffer` of that worker, which item 8 describes.
6. `Topic` at [line 37](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Handles/Entity.cs#L37) of [Entity.cs](src/Tertius.Core/Handles/Entity.cs). Every frame has a topic, and every entity in the frame subscribes to it. So a message to the topic is stored once, however many entities subscribe.
7. `Send` at [line 27](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Events/Outbox.cs#L27) and `SendAfter` at [line 38](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Events/Outbox.cs#L38) of [Outbox.cs](src/Tertius.Simulation/Events/Outbox.cs). A message arrives on the next tick, or after a delay. `Outbox` is a `ref struct`, and both methods write the message in place into a list of the chunk. So a send allocates nothing once that list has grown.
8. The comment at [lines 6 to 9](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Events/EventList.cs#L6-L9) of [EventList.cs](src/Tertius.Simulation/Events/EventList.cs). After an `EventBuffer` grows to its longest list, filling it again allocates nothing. [Lines 11 to 13](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Tick.cs#L11-L13) and [line 163](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Tick.cs#L163) of [World.Tick.cs](src/Tertius.Simulation/World.Tick.cs) keep one buffer for each worker thread.
9. `EventList` at [line 158](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Events/EventList.cs#L158) of the same file. It is a `ref struct`, so it lives on the stack. `Of<T>` at [line 239](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Events/EventList.cs#L239) returns a query struct.
10. `EventsOf<T>` at [line 12](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Events/EventsOf.cs#L12) of [EventsOf.cs](src/Tertius.Simulation/Events/EventsOf.cs). Its enumerator yields references at [lines 61 to 65](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Events/EventsOf.cs#L61-L65), so a `foreach` edits each payload in place.
11. `RefPredicate` at [lines 29 to 36](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Components/IComponent.cs#L29-L36) of [IComponent.cs](src/Tertius.Simulation/Components/IComponent.cs). A predicate takes the item by reference and one value of state, so a static lambda needs no capture.
12. The remarks at [lines 17 to 30](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Queries/EntityQuery.cs#L17-L30) of [EntityQuery.cs](src/Tertius.Simulation/Queries/EntityQuery.cs). The masks, one `In`, one `Near` and one `Where` are inline. If an entity's run tries to build the heap part, `Append` at [line 208](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Queries/EntityQuery.cs#L208) throws.
13. `QueryBuffers` at [line 121](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Queries/QueryPlanner.cs#L121) of [QueryPlanner.cs](src/Tertius.Simulation/Queries/QueryPlanner.cs). Each thread has a pool of buffers for the query runs that sort their candidates.
14. The comment at [lines 7 to 12](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Physics/Internal/ContactListener.cs#L7-L12) of [ContactListener.cs](src/Tertius.Physics/Internal/ContactListener.cs). A contact callback goes straight to the handler with the native pointers. It skips the lookup of managed wrappers in JoltPhysicsSharp.
15. [EventQueryBenchmarks.cs](benchmarks/Tertius.Benchmarks/EventQueryBenchmarks.cs) compares `EventList.Of<T>()` with a `for` loop and with LINQ.

These tests prove that the hot paths allocate nothing:

- `ASteadyStateTickAllocatesNothingOnTheTickingThread` in [AllocationTests.cs](tests/Tertius.Simulation.Tests/AllocationTests.cs). 2,000 entities move, send direct, topic and delayed messages, and run every form of event query. After 60 ticks of warm-up, the test expects 0 bytes per tick over 100 ticks.
- `SubscriptionChurnAllocatesNothingPerTickOnceTheWorldIsWarm` and `EntityQueryRunsAllocateNothingOnceTheWorldIsWarm` in the same file.
- `SteadyStateUseDoesNotAllocate` in [EventListTests.cs](tests/Tertius.Simulation.Tests/EventListTests.cs) and `NoQueryAllocates` in [EventQueryTests.cs](tests/Tertius.Simulation.Tests/EventQueryTests.cs).
- `StepsWithManyPersistingContactsAndAPendingPairAllocateNothingOnAnyThread` and `StepsWithContactsThroughAGhostThatStartAndEndAllocateNothingOnAnyThread` in [ContactListenerTests.cs](tests/Tertius.Physics.Tests/ContactListenerTests.cs). They count the bytes of all threads.
- `ASteadyStateTickWithEveryKindOfUpAllocatesNothing` in [GravityAllocationTests.cs](tests/Tertius.Simulation.Tests/GravityAllocationTests.cs).

The tests in AllocationTests.cs run one worker and count the bytes of the ticking thread only. The tests that tick a world turn `WorldOptions.DebugChecks` off, because its checks allocate. The physics tests take the smaller of two windows, because the runtime can compile a hot method again once.

These tests check that the message path, which the allocation tests above run, also gives correct results:

- `TargetMatchesEachCaseWithoutDefaultArm` and `PayloadOverLimitIsRejected` in [MessagingTests.cs](tests/Tertius.Core.Tests/MessagingTests.cs) of the Core tests.
- `AnEventIsStoredOnceHoweverManyRecipientsItHas` in [RouterTests.cs](tests/Tertius.Simulation.Tests/RouterTests.cs). One broadcast to 10 entities gives one routed message and 10 deliveries.
- `APayloadEditedByOneRecipientOfABroadcastIsUnchangedForTheOthers` in [PipelineEventsTests.cs](tests/Tertius.Simulation.Tests/PipelineEventsTests.cs).
- `AMessageSentOnOneTickIsHandledOnTheNext` in [MessagingTests.cs](tests/Tertius.Simulation.Tests/MessagingTests.cs) of the Simulation tests, and `ADelayedSendArrivesOneTickAfterItsDueTick` in [MessagingDelayedTests.cs](tests/Tertius.Simulation.Tests/MessagingDelayedTests.cs).
- `AFrameTopicReachesEveryEntityInThatFrameOnly` in [MessagingTopicTests.cs](tests/Tertius.Simulation.Tests/MessagingTopicTests.cs).

### Everyone reads the last tick

Read this code in this order:

1. [Lines 283 to 293](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Entities/EntityTable.cs#L283-L293) of [EntityTable.cs](src/Tertius.Simulation/Internal/Entities/EntityTable.cs). Each kind of data that changes during a tick has a front column and a back column.
2. The comment at [lines 6 to 12](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/Self.cs#L6-L12) of [Self.cs](src/Tertius.Simulation/Entities/Self.cs). Reads come from the front snapshot of the previous tick. Writes go to the back rows of the running entity, or into the lists of its chunk.
3. `WritableTransform` at [line 781](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Entities/EntityTable.cs#L781) of [EntityTable.cs](src/Tertius.Simulation/Internal/Entities/EntityTable.cs). A write marks the row of the entity dirty and returns its back row.
4. `CopyDirtyRows` at [line 1098](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Entities/EntityTable.cs#L1098) of the same file. It copies only the dirty rows from back to front. `SwapSnapshot` at [line 733](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Sync.cs#L733) of [World.Sync.cs](src/Tertius.Simulation/World.Sync.cs) calls it in step 8 of the sync point, at [lines 172 and 173](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Sync.cs#L172-L173).
5. The comment at [lines 5 to 10](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Snapshot.cs#L5-L10) of [Snapshot.cs](src/Tertius.Simulation/Snapshot.cs). The snapshot is a read-only view of the front tables, and any thread can read it during a tick. `Self.Snapshot` at [line 146](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/Self.cs#L146) of [Self.cs](src/Tertius.Simulation/Entities/Self.cs) gives it to a component, for reading other entities.
6. `Find` at [line 93](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Snapshot.cs#L93) of [Snapshot.cs](src/Tertius.Simulation/Snapshot.cs), and `Choose` at [line 50](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Queries/QueryPlanner.cs#L50) of [QueryPlanner.cs](src/Tertius.Simulation/Queries/QueryPlanner.cs). An entity query runs on the snapshot. The planner starts from the index with the fewest candidates.

These tests prove it:

- `AnEntityReadsAnotherEntitysPoseAsItWasAtTheEndOfThePreviousTick` in [TransformTests.cs](tests/Tertius.Simulation.Tests/TransformTests.cs). A watcher that runs before the mover and a watcher that runs after it see the same poses.
- `AnEntityReadsItsOwnPoseAsItWasAtTheEndOfThePreviousTickEvenAfterWritingANewOne` and `AnEntityReadsAnotherEntitysVelocityAsItWasAtTheEndOfThePreviousTick` in the same file.
- `ManyWorkersReadTheFlagsAtOnceWhileEntitiesChangeTheirOwnAndAllSeeTheEndOfThePreviousTick` in [ComponentFlagsWorldTests.cs](tests/Tertius.Simulation.Tests/ComponentFlagsWorldTests.cs). With eight workers, 200 entities count the same neighbors as with one.
- `EntitiesThatNobodyWroteKeepTheirRowsAndAreNotMoved` and `ACopyWithNothingNewToCopyChangesNothingAndMovesNobody` in [EntityTableDirtyRowTests.cs](tests/Tertius.Simulation.Tests/EntityTableDirtyRowTests.cs).

An entity can read some of its own back rows during its run. An example is `Pending` at [line 228](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/Self.cs#L228) of [Self.cs](src/Tertius.Simulation/Entities/Self.cs), in `BodyHandle`, the intent that it wrote so far. No other entity sees those rows until the next tick.

### Deterministic parallel gameplay

Read this code in this order:

1. `RunGameplay` at [line 143](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Tick.cs#L143) of [World.Tick.cs](src/Tertius.Simulation/World.Tick.cs). Gameplay runs in chunks of entities with `Parallel.For`.
2. The remarks at [lines 11 to 39](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Messaging/Router.cs#L11-L39) of [Router.cs](src/Tertius.Simulation/Internal/Messaging/Router.cs). The router sorts deliveries with a stable counting sort, in `SortRange` at [line 384](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Messaging/Router.cs#L384). A large routing runs on the workers, and it gives the same inboxes as a routing on one thread, because no order depends on the threads.
3. `BeginTick` at [line 411](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Entities/EntityTable.cs#L411) of [EntityTable.cs](src/Tertius.Simulation/Internal/Entities/EntityTable.cs). Each chunk has its own pool of spawn slots, so handles do not depend on thread timing.
4. [ARCHITECTURE.md](ARCHITECTURE.md), [lines 195 to 212](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/ARCHITECTURE.md#L195-L212). Outboxes belong to chunks and merge in chunk order.
5. [ChunkState.cs](src/Tertius.Simulation/Internal/ChunkState.cs), [lines 25 to 48](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/ChunkState.cs#L25-L48). Each chunk has its own outbox, spawn list and destroy list. Only the worker that runs the chunk writes them.
6. `PlaceOutboxes` at [line 209](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Messaging/Router.cs#L209) of [Router.cs](src/Tertius.Simulation/Internal/Messaging/Router.cs). It gives each outbox its place in the routed list, in chunk order.
7. [Lines 204 to 209](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Sync.cs#L204-L209) of [World.Sync.cs](src/Tertius.Simulation/World.Sync.cs). A chunk can run out of its spawn slots. Then the world reports that the run is no longer deterministic.
8. The comment at [lines 6 to 12](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Components/ComponentTypes.cs#L6-L12) of [ComponentTypes.cs](src/Tertius.Simulation/Components/ComponentTypes.cs). Component type ids follow the order of full names, not the order of loading.

The part "Everyone reads the last tick" shows why no chunk sees the writes of another chunk during a tick.

These tests prove it:

- `ARunOnEightWorkersEndsInTheWorldOfARunOnOne` in [DeterminismTests.cs](tests/Tertius.Simulation.Tests/DeterminismTests.cs). The run has 160 entities and 200 ticks. The hash, the statistics and the counts are equal.
- `TheRunOnEightWorkersReallyRunsGameplayOnSeveralThreads` and `TheWorkloadRunsBodiesInFallingAndInRestingNumericFrames` in the same file.
- [WorldHash.cs](tests/Tertius.Testing/WorldHash.cs) lists what the hash covers. It covers each handle, pose, velocity, frame and component state, the render snapshot and the error count.
- `TheStationSceneGivesTheSameEventsAndStatesInEveryRunWithOneOrFourThreads` in [StationSliceTests.cs](tests/Tertius.Host.Tests/StationSliceTests.cs), and `AStationSceneGivesTheSameEventsAndStateInEveryRunWithOneOrFourThreads` in [SpinStationDeterminismTests.cs](tests/Tertius.Physics.Tests/SpinStationDeterminismTests.cs).
- `AnInboxListsTheChunkOutboxesInChunkOrderWhateverOrderTheyWereFilledIn` in [RouterTests.cs](tests/Tertius.Simulation.Tests/RouterTests.cs).
- `EachChunkSpawnsFromItsOwnPoolWhicheverChunkSpawnsFirst` and `SpawnsInterleavedBetweenChunksGetTheSameHandlesAsAnyOtherOrder` in [EntityTableSpawnPoolTests.cs](tests/Tertius.Simulation.Tests/EntityTableSpawnPoolTests.cs).
- `NoSpawnTakesItsSlotFromTheGlobalFallbackBecauseThatMakesSlotsDependOnThreadTiming` in [DeterminismTests.cs](tests/Tertius.Simulation.Tests/DeterminismTests.cs).

The test with eight workers keeps Jolt at two job threads. The other tests of this kind compare one and four threads. The Jolt build sets no flag for determinism across platforms.

The tests compare runs on one machine with one build. Tertius makes no claim about determinism across machines.

### Handles that catch stale references

Read this code in this order:

1. [Entity.cs](src/Tertius.Core/Handles/Entity.cs), [lines 8 to 15](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Handles/Entity.cs#L8-L15). An `Entity` is an index and a generation in 64 bits. Generation 0 is the null handle.
2. The remarks at [lines 17 to 24](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Entities/EntityTable.cs#L17-L24) of [EntityTable.cs](src/Tertius.Simulation/Internal/Entities/EntityTable.cs). Every member checks its handle in every build, and slots appear only inside the class.
3. `IsAlive` at [line 487](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Entities/EntityTable.cs#L487) and `LiveSlot` at [line 1360](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Entities/EntityTable.cs#L1360) of the same file. `LiveSlot` throws `StaleEntityException` for a stale handle.
4. [Lines 283 to 293](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Entities/EntityTable.cs#L283-L293) of the same file. The transform, render, intent, component and collision data lie in columns indexed by slot.
5. [StaleEntityException.cs](src/Tertius.Simulation/Entities/StaleEntityException.cs), [line 13](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/StaleEntityException.cs#L13). The exception is an `InvalidOperationException`.
6. [Lines 354 to 357](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Messaging/Router.cs#L354-L357) of [Router.cs](src/Tertius.Simulation/Internal/Messaging/Router.cs). A message to a stale handle reaches nobody.

These tests prove it:

- `AMemberThatReadsOrWritesAnEntityRefusesAHandleThatNamesNoLiveEntity` and `AMemberWhoseGoalAlreadyHoldsAnswersFalseForAHandleThatNamesNoLiveEntity` in [StaleHandleWorldTests.cs](tests/Tertius.Simulation.Tests/StaleHandleWorldTests.cs). Each one runs over a list of members.
- `AMessagePostedToADestroyedEntitysHandleDoesNotReachTheEntityThatTookItsSlot` and `AMessageAlreadyWaitingForAnEntityThatIsDestroyedIsNotDeliveredToTheEntityThatTakesItsSlot` in the same file.

### Frames are entities

Read this code in this order:

1. `FrameHandle` at [lines 24 to 40](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Handles/Entity.cs#L24-L40) of [Entity.cs](src/Tertius.Core/Handles/Entity.cs). A frame handle holds the entity that is the frame, plus the index and generation of the frame's row in the frame tree. It converts to `Entity` at [line 34](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Handles/Entity.cs#L34).
2. `AsFrame` at [line 53](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/SpawnDesc.cs#L53) and `FrameSpec` at [line 86](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/SpawnDesc.cs#L86) of [SpawnDesc.cs](src/Tertius.Simulation/Entities/SpawnDesc.cs). Any spawn can make its entity a frame. `CreateRootFrame` at [line 24](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Entities.cs#L24) of [World.Entities.cs](src/Tertius.Simulation/World.Entities.cs) makes the entity of a root frame with the same `Spawn`.
3. `OwnedFrame` at [line 676](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Entities/EntityTable.cs#L676) of [EntityTable.cs](src/Tertius.Simulation/Internal/Entities/EntityTable.cs). The record of an entity that is a frame keeps the handle of that frame.
4. [Line 23](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Core/Handles/Target.cs#L23) of [Target.cs](src/Tertius.Core/Handles/Target.cs). A message to a frame handle goes to the entity that is the frame.
5. `DestroyDying` at [line 462](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Entities.cs#L462) of [World.Entities.cs](src/Tertius.Simulation/World.Entities.cs). When the entity of a frame dies, the frame ends. [Lines 482 to 501](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Entities.cs#L482-L501) move what was in it to the nearest frame that survives.
6. [TransformRow.cs](src/Tertius.Simulation/Internal/Entities/TransformRow.cs), [lines 6 to 35](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Entities/TransformRow.cs#L6-L35). Each entity row stores its frame and a pose in that frame, in doubles.
7. `PoseIn`, `VelocityIn` and `AngularVelocityIn` at [lines 55 to 61](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/Self.cs#L55-L61) of [Self.cs](src/Tertius.Simulation/Entities/Self.cs). Each read takes a frame, and `Self` has no read without one. `SetTransform` at [line 71](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Entities/Self.cs#L71) writes the pose in `Self.Frame`.
8. `PoseIn` at [line 17](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Internal/Entities/FrameReads.cs#L17) of [FrameReads.cs](src/Tertius.Simulation/Internal/Entities/FrameReads.cs). A read in another frame converts through the frame tree in doubles.
9. `TryGetPoseIn` at [line 115](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Snapshot.cs#L115) of [Snapshot.cs](src/Tertius.Simulation/Snapshot.cs). The reads of the snapshot give false for a stale entity or a stale frame.

These tests prove it:

- `AMessageSentToAFrameHandleReachesTheEntityThatOwnsTheFrameNotItsMembers` in [MessagingTests.cs](tests/Tertius.Simulation.Tests/MessagingTests.cs) of the Simulation tests. A component on the entity of the frame gets the message.
- `DestroyingAFrameEndsTheFrameButNotWhatWasInsideIt` and `TheEntitiesOfADestroyedFrameMoveToItsParentFrame` in [LifecycleFrameTests.cs](tests/Tertius.Simulation.Tests/LifecycleFrameTests.cs). The test destroys the frame with `World.Destroy`, as for any entity.
- `AnEntityInAChildFrameHasItsPoseComposedWithTheFramesInTheParentFrame`, `AnEntityInTheParentFrameHasItsPoseInAChildFrameThroughTheInverseOfTheFramesPose` and `AnEntityInASiblingFrameHasItsPoseComposedThroughTheParentFrame` in [FramePoseQueryTests.cs](tests/Tertius.Simulation.Tests/FramePoseQueryTests.cs).
- `AFrameThatHasBeenDestroyedCannotBeAskedForAPose` in the same file.

### Blueprints

Read this code in this order:

1. The comment at [lines 24 to 32](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Blueprints/Blueprints.cs#L24-L32) of [Blueprints.cs](src/Tertius.Simulation/Blueprints/Blueprints.cs). Reflection runs only in `Register`. Every instance shares the field values.
2. `Register` at [line 53](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Blueprints/Blueprints.cs#L53) of the same file. [Line 106](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Blueprints/Blueprints.cs#L106) shares one compiled factory for each component type and field set.
3. `ValidateType` at [line 162](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Blueprints/Blueprints.cs#L162) of the same file. A component type must be a concrete class with a public constructor that takes no parameters.
4. `Instantiate` at [line 127](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Blueprints/Blueprints.cs#L127) and `Compile` at [line 234](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/Blueprints/Blueprints.cs#L234) of the same file. `Compile` builds the factory from an expression tree.

These tests prove it:

- `BlueprintsThatSetTheSameFieldsShareOneCompiledFactory` and `TwoRegistriesShareTheFactoriesOfTheSameTypeAndFieldSet` in [BlueprintTests.cs](tests/Tertius.Simulation.Tests/BlueprintTests.cs).
- `EveryInstantiateMakesNewComponentInstances` and `AReferenceValueInABlueprintIsTheSameObjectInEveryInstance` in the same file.
- `AComponentCanSpawnABlueprintDuringATick` in the same file.

A spawn also looks up one type id for each component, at [line 222](https://github.com/alexispurslane/tertius/blob/1be71d99752670e477ca2fc5b6c5552e97c6b617/src/Tertius.Simulation/World.Entities.cs#L222) of [World.Entities.cs](src/Tertius.Simulation/World.Entities.cs).

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
| Vulkan loader, MoltenVK | from nixpkgs. On the Mac, MoltenVK has two patches (`native/moltenvk.nix`) | Their own licenses | Vulkan on the host |
| xunit.v3 | 4.0.2 | Apache-2.0 | Tests |
| AwesomeAssertions | 9.6.0 | Apache-2.0 | Tests |
| Microsoft.Testing.Extensions.CodeCoverage | 18.12.0 | Microsoft .NET Library license | Test coverage |
| BenchmarkDotNet | 0.15.8 | MIT | Benchmarks |
| dotnet-stryker | 5.0.0 | Apache-2.0 | Mutation testing |
| dotnet-reportgenerator-globaltool | 5.5.11 | Apache-2.0 | Coverage reports |
