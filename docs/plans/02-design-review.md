# 02 Design review

Status: planned. It starts after plan 01.

## Goal

Find the design problems in every module before more code builds on them. The review uses the software-design-philosophy skill: deep modules, information hiding, no pass-through methods, no conjoined methods, and errors defined out of existence.

## How

1. Reviewers look at each module from several angles: correctness, determinism, thread safety, performance and interface design.
2. Independent skeptics try to refute each finding. Only the findings that survive go to the user.
3. Each accepted finding gets a failing test first, then the fix.

## Known items

- `EntityTable.IntentEntities` is a property getter that changes state: it removes dead entities from the list on its first read after a death.
- `EntityTable.Activate` takes seven parameters.
- Physics: JoltPhysicsSharp 2.23.0 corrupts the stack in its `ContactManifold` wrapper, and the backend works around it. Review the workaround. Do not report the bug upstream without the user's approval.
- If plan 01 did not fix the slow Stryker runs, find the cause here.
- `Blueprints.Coerce` converts field values with `Convert.ChangeType`. So a blueprint value of 1.5 for an integer field silently becomes 2. Reject a fractional value for an integer field instead.
- `EntityTable.Activate` and `Release` accept a handle to a reserved slot that still sits in a chunk's spawn pool and was never handed out. This breaks the count of reservations that are out. Only a forged handle with the slot's current generation can do this.
- JoltPhysicsSharp keeps a wrapper object for every body that it ever created, in a static dictionary that never shrinks. After 3,000 created bodies it holds about 3,025 entries. A long session that creates and destroys many bodies leaks memory slowly. Measure it, and find a way to release the wrappers.
- `World.Frames` gives the game the whole mutable `FrameTree`. Some changes there bypass the World, so a row that mirrors the tree can disagree with it until the next tick. Decide which tree changes go through `World` methods.
- `Attachments.Attach` overwrites the node of an entity that already holds attached children, and those children lose their place in the tree. The public `World` API cannot do this today, because only new entities get attached. If `Attach` gets another caller, add a contract.
- A frame owner cannot have a character body. The backend refuses it in `JoltPhysicsBackend.Bodies.cs`, and round eight of the physics work moves the refusal into `World.Spawn`. Nobody decided this. The first implementation wrote the hull code against Jolt's rigid-body calls on the owner's body ID, for example to read its velocity and rotation. A character has no such body, so the code refused it in one line. The user wants walking vehicles with an interior, such as a mech with a cockpit that people walk around in. Give the hull code one interface for the owner's pose, velocity and acceleration in the parent frame. A rigid body or a character can stand behind it. Then remove the refusal.
- Search the World and the backend for other refusals that no design decision backs, and list them for the user.

## Report

For each finding: the module, the problem, the evidence, the skeptics' verdict and the proposed fix.
