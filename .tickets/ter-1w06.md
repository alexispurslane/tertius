---
id: ter-1w06
status: in_progress
deps: []
links: [ter-kugq, ter-tftg, ter-61ds, ter-w7cw, ter-qcdo, ter-myew]
created: 2026-10-17T13:44:29Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [physics, character]
---
# A weightless character that presses into a dynamic crate never moves it, while its velocity read grows

Found by the pushed-contact precision measurement (ter-kugq, branch worktree-wf_c4566912-569-1, test tests/Tertius.Physics.Tests/Precision/PushedContactMeasurement.cs, scene character-pushes-crate).

Scene: zero gravity, numeric frames off, one static root frame. A character (capsule half height 0.6 m, radius 0.3 m, 70 kg) starts 0.02 m behind a 1 m, 20 kg dynamic box along +x. A Pilot adds 105 N along +x on every tick, for 3600 ticks at 60 Hz.

Measured at V = 0 (no common velocity), every sample time:
- Neither body changes position: the center of mass never leaves the origin, and the x-distance between the centers changes by 1.1e-16 m.
- The velocity reads grow as if the character were free: |v_character - v_crate| reaches 89.98 m/s at 60 s, which is (105/70) x 59.98 s, and the crate reads 0.
- No contact ends.
The same frozen state holds in direction y at every common speed up to 300 km/s, and in every distance-control run (start points up to 9e6 m).

When the common velocity has a part along the push (direction (1,1,1)/sqrt(3), any V from 1 m/s up), the contact breaks and the character passes the crate: x-distance between the centers about -2600 m at 60 s, crate tilt up to 177 degrees, and at V = 1 m/s 12 contacts begin and 12 end. From V = 2000 m/s up, the slip across x at 5 s is exactly one tick of the cross velocity, V x sqrt(2/3) / 60.

Probable area: UpdateCharacter and WithoutMotionIntoContacts in src/Tertius.Physics/JoltPhysicsBackend.Step.cs. A character that does not stand keeps its velocity against a dynamic body, so it can push it, but here Jolt's CharacterVirtual never moved the crate. Also see ter-tftg (a walking character slows when it touches a light resting crate). Done means: a weightless character that presses into a free crate moves it, the pair's momentum follows the thrust, and the velocity reads agree with the positions.


## Notes

**2026-10-17T14:08:27Z**

Investigation (branch worktree-wf_c3fa9854-e4e-1). Root cause: Jolt's CharacterVirtual::ExtendedUpdate starts with CancelVelocityTowardsSteepSlopes, which removes the velocity into every contact that had a collision in the last update and whose face is steeper than MaxSlopeAngle against the character's up, whenever the ground state is OnSteepGround or NotSupported. A weightless character that touches the crate's face is OnSteepGround from its first update, so Jolt moves it with no velocity into the crate, never calls HandleContact, and never pushes. The engine then writes the full velocity back (WithoutMotionIntoContacts skips dynamic bodies), so the stored velocity grows as if free while nothing moves. Second defect: when Jolt does push (start gap 0.2 m, or with the cancel bypassed), the crate's impulse is not taken from the character, so the pair gains momentum (250 against 208 N s after 2 s). Second symptom: the cancel uses the absolute velocity, not the velocity relative to the crate, and on the first update the ValidateMovement sweep treats the co-moving crate as still at its start pose and scales the whole displacement, which is the exact one tick of cross velocity. Red tests in tests/Tertius.Physics.Tests/WeightlessCharacterPushTests.cs. The fix has design choices for the user.

**2026-10-17T14:21:29Z**

Decision (user, 2026-10-17): design B. Below FrameTree.ZeroGravityThreshold a character becomes a rigid capsule with its rotation locked, solved by Jolt, and it turns back into a CharacterVirtual above the threshold with hysteresis. A character that does not stand gets Jolt's plain Update, with no steep-slope cancel. A weightless push has no strength cap. Standing characters keep today's behavior. The four red tests on branch worktree-wf_c3fa9854-e4e-1 (commit faf2f9e, tests/Tertius.Physics.Tests/WeightlessCharacterPushTests.cs) are the acceptance tests. Rejected: design A (engine impulses from both effective masses), because it has no friction and its after-step ordering may only move the co-moving false hit from a body ahead to a body behind.

**2026-10-17T15:24:38Z**

Implemented design B on branch worktree-wf_30340cc3-24e-1. BodyRecord.Motion holds the form: Character for the walking form (CharacterVirtual), Dynamic for the rigid form (a dynamic body, AllowedDOFs translation only, no damping, the character's mass and material). JoltPhysicsBackend.SwitchCharacterForms runs at the start of each step, after the reconcile and before the spawn chords, intents and gravity, and applies the pure rule NextCharacterMotion: rigid below FrameTree.ZeroGravityThreshold (0.05 m/s2), walking again from twice it (0.1 m/s2). A switch is RemovePrimary plus CreatePrimary at the same pose and velocity, so the loose-contact machinery carries every contact (no Collision or CollisionEnded). The rigid capsule is 0.02 m wider and its cylinder 0.02 m shorter than the walking capsule, because a standing CharacterVirtual rests its capsule on a box floor but keeps its 0.02 m padding from a wall at its side (measured), so a switch moves nothing. A walking character that does not stand gets plain Update, not ExtendedUpdate. The four acceptance tests pass, plus 15 new tests in tests/Tertius.Physics.Tests/CharacterFormTests.cs. Restated tests: CharacterTests (a light crate now shares the momentum; the wall slide test uses a frictionless wall), GrowthTests (the character is rigid in the weightless space), AirlockSliceTests and SpacewalkSliceTests (a 7 mm/s upward drift from the sill's rounded edge, ticket ter-myew). Follow-ups: ter-myew (sill-edge push), ter-qcdo (a weightless character lowers a whole system's sleep threshold). Demo: scene weightless-push and scripts/weightless-push.json. Every test project passes in Debug and Release.

**2026-10-17T16:09:16Z**

Review fixes on branch worktree-wf_30340cc3-24e-1, each confirmed first by a test in tests/Tertius.Physics.Tests/CharacterFormTests.cs that failed for the finding's reason.
1. The band between 0.05 and 0.1 m/s2 trapped a character that came from below: a rigid character on a floor never stood or walked. NextCharacterMotion now takes a third input, touches (the rigid body holds a started pair in its system, FrameWorld.HasStartedPair). A rigid character that touches something walks again from 0.05 m/s2 on. The band still holds a floating character, where both forms move alike. Test: ACharacterThatLiesOnAFloorWhenAWeakGravityComesStandsAndWalksAsOneThatSpawnedInThatGravity.
2. A switch to the rigid form ended each contact with a body between 0.02 and 0.1 m away that the walking form still held. The rigid form now gets the walking form's contact list from a CollideShape query (walking capsule, max separation padding + 0.1 m) after each update of its system (NoteRigidCharacterContacts), and HeldPairEnded ends its held pairs by that list. Test: ASwitchToTheRigidFormEndsNoContactWithAWallThatTheWalkingFormStillHeld.
3. Rigid capsules were cached by BodyDesc reference, so a new BodyDesc per spawn made a new native capsule. The cache is now keyed by the walking CapsuleShapeDesc reference. Test: CharactersOfTwoDescriptionsWithOneCapsuleShareOneNativeCapsuleInTheRigidForm.
