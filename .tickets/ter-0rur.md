---
id: ter-0rur
status: open
deps: []
links: []
created: 2026-10-27T11:03:33Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, character]
---
# A spawned character looks up no contacts before its first update, so it does not stand there in that step

From the review of ter-1924. A spawn (and a Rehome, which is a destroy and a spawn) is the only way that a walking character appears at a new place without a look-up of its contacts (CharacterVirtual.RefreshContacts). Jolt's new character has the ground state InAir, so a person spawned at the standing height on a floor does not stand in its first step, while a person teleported to the same place stands in that step. The fix that the review proposed: list the walking characters spawned since the last step with the teleported ones, and look up their contacts in the same pass after the intents (JoltPhysicsBackend.LookUpContactsOfTeleportedCharacters).

Tried in ter-1924 and taken out, because it changes the demo. The person of demo-exit spawns at its seat with its up against the spin gravity of the parked cabin. With the look-up it touches the floor in its first update, so its up turns at once (not at the air rate) toward the effective gravity of that first step, which is not the spin gravity: the frame tree knows the station's spin only from the first tick (DemoScenes.DemoExit.FeltAtSeat). Its up went from (0.49, 0.87, 0) to (-0.71, 0.70, 0) in the cabin's axes, and the person ended 0.094 m from its seat. DisembarkShortRunTests.WithoutTheOrderThePersonStaysAtItsSeat failed (limit 0.02 m), and scripts/demo-exit.json no longer matched its generator (a wait of 399 frames became 394). So this ticket needs a decision on the first step of a spawned character in a spinning frame whose spin the tree does not know yet, or a change to the scene, before the look-up at spawn.

Test to keep: a person spawned at the standing height on a floor and a person teleported to the same place in another world both stand in that step, at the same height (it was CharacterStandingTests.ACharacterSpawnedOnGroundStandsInItsFirstStepAsOneTeleportedThereDoes in the ter-1924 work, red before the change).

