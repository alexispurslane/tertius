---
id: ter-1m9y
status: in_progress
deps: []
links: []
created: 2026-11-25T15:25:33Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-qdn8
---
# Animate the suited pilot walking, sitting and taking the pencil

Use the real rig and existing animation/deformation and attachment systems. Match foot contact and hand-to-pencil contact to the simulated movement and events.

## Implementation and film evidence

The crew converter retains the 122 Unvanquished joints and original vertex weights. The renderer deforms each part of the suit. The pilot steps at the measured walking speed, sits over the fitted cushion, and closes the right glove on the original 20 g pencil. While held, the pencil follows its grip with solid collisions disabled. Release restores its collision filter.

The three inspected H.264 clips are in `~/Movies/Blue Danube/pilot-motion/`.

| Clip | Evidence |
|---|---|
| boarding-and-grasp.mp4 | Boots alternate, knees bend as the pilot settles, then the right hand reaches and curls around the red pencil. |
| lift-contact.mp4 | The boots ride with the moving lift. In the final frame, physics reports standing while the lift moves at 5.025 m/s. This capture uses the separate station physics fix at 420855998. |
| pencil-release.mp4 | The right glove opens and the same pencil falls onto the station floor. |

A forced conversion from the pinned source produced 11,836 triangles. The focused cockpit, shuttle, pilot and passenger tests passed (63). The focused coverage run passed (29). It covered all 40 executable pencil-holder lines and 142 of 147 lines in the pilot motion type and its nested types. The five missed lines handle a missing passenger pose or singular limb directions. Four more uncovered lines select passenger states in the action publisher. The longer scene tests exercise the passenger sequence.

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| P01 | src/Tertius.Assets/GlbReader.cs:208 | ids[c] = (ushort)skin.Rem... -> ids[c] = 0; | killed | CookedModelTests.ASkinnedPrimitiveRetainsItsJointWeightsAndParentRelativeRestPose | 2.9 |
| P02 | src/Tertius.Assets/GlbReader.cs:220 | vertices.AsSpan(0, count)... -> Array.Clear(vertices, cou... | killed | CookedModelTests.ASkinnedPrimitiveRetainsItsJointWeightsAndParentRelativeRestPose | 2.9 |
| P03 | src/Tertius.Assets/GlbReader.cs:269 | Bind(matrices.AsSpan(16 *... -> Bind(matrices.AsSpan(0, 16)) | killed | CookedModelTests.ASkinnedPrimitiveRetainsItsJointWeightsAndParentRelativeRestPose | 2.9 |
| P04 | src/Tertius.Rendering/Scene/ModelExpansion.cs:66 | Instance = part -> Instance = first | killed | ModelExpansionTests.EveryPartReceivesTheModelInstancesRigPose, ModelExpansionTests.TheRigPoseOfAnInstanceAfterAPosedModelInstanceIsKept | 4.5 |
| P05 | src/Tertius.Rendering/Scene/SceneBatcher.cs:211 | need += Vertices(part.Mes... -> need = Vertices(part.Mesh... | killed | BatcherDeformTests.APosedModelReservesVerticesForEverySkinnedPart | 4.9 |
| P06 | src/Tertius.Demo/PilotMotion.cs:123 | (cycle + 0.5f) % 1 -> cycle | killed | PilotMotionTests.EachBootStaysOnTheFloorWhileTheOtherBootSwings | 5.9 |
| P07 | src/Tertius.Demo/PilotMotion.cs:140 | 0.13f * MathF.Sin -> 0.01f * MathF.Sin | killed | PilotMotionTests.EachBootStaysOnTheFloorWhileTheOtherBootSwings | 5.1 |
| P08 | src/Tertius.Demo/PilotMotion.cs:112 | DemoScenes.Shuttle.Cockpi... -> DemoScenes.Shuttle.Cockpi... | killed | PilotMotionTests.ClosingTheSeatBeltPlacesTheHipsOverTheCushionAndTheBootsOnTheCockpitFloor | 4.8 |
| P09 | src/Tertius.Demo/PilotMotion.cs:181 | -1.08f * close -> -0.01f * close | killed | PilotMotionTests.TheGloveClosesAtThePhysicalPencilAndOpensWhenThePilotLetsGo | 5.6 |
| P10 | src/Tertius.Demo/PilotMotion.cs:166 | point - Vector3.Transform... -> point + Vector3.Transform... | killed | PilotMotionTests.TheGloveClosesAtThePhysicalPencilAndOpensWhenThePilotLetsGo | 6.6 |
| P11 | src/Tertius.Demo/PencilInHand.cs:38 | self.Body.SetCollision(be... -> self.Body.SetCollision(Co... | killed | PassengerTests.OpeningTheGloveRestoresTheSamePencilsCollisionFilter | 6.2 |
| P12 | src/Tertius.Demo/PencilInHand.cs:45 | self.Body.SetCollision(Co... -> self.Body.SetCollision(Co... | killed | PassengerTests.OpeningTheGloveRestoresTheSamePencilsCollisionFilter, PassengerTests.AStrappedPassengerStaysAtItsSeatThroughABurn | 6.4 |
| P13 | src/Tertius.Demo/Passenger.cs:310 | outbox.Send(held, new Rel... -> outbox.Send(held, new Sea... | killed | PassengerTests.OpeningTheGloveRestoresTheSamePencilsCollisionFilter | 6.5 |
| P14 | src/Tertius.Demo/Crate.cs:153 | self.AddComponent(new Pen... -> self.AddComponent(new Pen... | killed | PenClipTests.ThePassengerTakesThePenWithoutItsBodyTouchingThePen, PenClipTests.ThePassengerTakesThePenThatTheClipLetGo | 5.1 |
