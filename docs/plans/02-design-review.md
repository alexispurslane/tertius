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
- `Attachments.Attach` would overwrite the node of an entity that already has attached children, and those children would lose their place in the tree. The public `World` API cannot do this today, because only new entities get attached. Add a contract if `Attach` gets another caller.

## Report

For each finding: the module, the problem, the evidence, the skeptics' verdict and the proposed fix.
